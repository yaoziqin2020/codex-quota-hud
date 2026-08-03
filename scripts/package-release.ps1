[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.1.1',
    [string] $OutputPath,
    [string] $DotNetExecutable = 'dotnet',
    [string] $InnoCompilerPath,
    [switch] $InternalTestMode,
    [string] $InternalArgumentCapturePath,
    [int] $InternalCompilerExitCode,
    [switch] $InternalSkipFakeSetup,
    [switch] $InternalFailStageCleanup,
    [switch] $InternalFailFailureCleanupStageAfterFirstFile,
    [switch] $InternalFailManifestDeleteOnce,
    [switch] $InternalRemovePublishedAppBeforeZip,
    [switch] $InternalRemovePublishedDesignerBeforeSetup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$productionReleaseRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\release'))

if ($InternalTestMode) {
    if ([string]::IsNullOrWhiteSpace($OutputPath) -or
        -not [System.IO.Path]::IsPathRooted($OutputPath)) {
        throw 'Internal packaging requires an absolute -OutputPath.'
    }

    $releaseRoot = [System.IO.Path]::GetFullPath($OutputPath)
    $systemTemp = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath())
    if (-not $releaseRoot.StartsWith(
            $systemTemp,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals(
            $releaseRoot,
            $systemTemp,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals(
            $releaseRoot,
            $productionReleaseRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw (
            'Internal packaging output must be a unique directory inside ' +
            'the system temporary directory and outside production artifacts.')
    }
}
else {
    if (-not [string]::IsNullOrWhiteSpace($OutputPath) -and
        -not [string]::Equals(
            [System.IO.Path]::GetFullPath($OutputPath),
            $productionReleaseRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Production release output must be exactly: $productionReleaseRoot"
    }

    if (-not [string]::Equals(
            $DotNetExecutable,
            'dotnet',
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::IsNullOrWhiteSpace($InnoCompilerPath) -or
        -not [string]::IsNullOrWhiteSpace($InternalArgumentCapturePath) -or
        $InternalCompilerExitCode -ne 0 -or
        $InternalSkipFakeSetup -or
        $InternalFailStageCleanup -or
        $InternalFailFailureCleanupStageAfterFirstFile -or
        $InternalFailManifestDeleteOnce -or
        $InternalRemovePublishedAppBeforeZip -or
        $InternalRemovePublishedDesignerBeforeSetup) {
        throw 'Internal packaging hooks require -InternalTestMode.'
    }

    $releaseRoot = $productionReleaseRoot
}

$packageName = "CodexQuotaHud-v$Version-win-x64"
$stage = Join-Path $releaseRoot $packageName
$archive = Join-Path $releaseRoot "$packageName.zip"
$setup = Join-Path $releaseRoot "CodexQuotaHud-Setup-v$Version.exe"
$checksums = Join-Path $releaseRoot 'SHA256SUMS.txt'
$manifestTemp = Join-Path `
    $releaseRoot `
    ('.SHA256SUMS-v{0}.{1}.tmp' -f `
        $Version, [Guid]::NewGuid().ToString('N'))
$published = if ($InternalTestMode) {
    Join-Path $releaseRoot ".internal-published-v$Version"
}
else {
    Join-Path $repositoryRoot 'artifacts\CodexQuotaHud-win-x64'
}

function Remove-ExactPath {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [switch] $FailAfterFirstFile
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to remove a reparse-point packaging path: $Path"
    }

    if ($FailAfterFirstFile) {
        if (-not $item.PSIsContainer) {
            throw "Partial removal injection requires a directory: $Path"
        }
        $firstFile = @(Get-ChildItem `
            -LiteralPath $Path `
            -Recurse `
            -File `
            -Force | Sort-Object -Property FullName)[0]
        if ($null -eq $firstFile) {
            throw "Partial removal injection found no file: $Path"
        }
        Remove-Item -LiteralPath $firstFile.FullName -Force -ErrorAction Stop
        if (Test-Path -LiteralPath $firstFile.FullName -ErrorAction Stop) {
            throw "Partial removal injection postcondition failed: $($firstFile.FullName)"
        }
        throw "Simulated partial stage removal failure after deleting: $($firstFile.FullName)"
    }

    if ($item.PSIsContainer) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    else {
        Remove-Item -LiteralPath $Path -Force
    }
}

function Assert-RegularPackagingFile {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or ($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description must be a regular file: $Path"
    }
}

function Get-RelativeChildPath {
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [Parameter(Mandatory = $true)][string] $Child
    )

    $rootPrefix = [System.IO.Path]::GetFullPath($Root).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    $childFullPath = [System.IO.Path]::GetFullPath($Child)
    if (-not $childFullPath.StartsWith(
        $rootPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the expected root: $childFullPath"
    }
    return $childFullPath.Substring($rootPrefix.Length)
}

function Assert-ExactZipStage {
    param([Parameter(Mandatory = $true)][string] $Path)

    $rootItem = Get-Item -LiteralPath $Path -Force
    if (-not $rootItem.PSIsContainer -or ($rootItem.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "ZIP stage must be a regular directory: $Path"
    }

    $allowedFiles = @(
        'artifacts\CodexQuotaHud-win-x64\CodexQuotaHud.App.exe',
        'scripts\install.ps1',
        'scripts\uninstall.ps1',
        'README.md',
        'LICENSE'
    )
    $actualFiles = @()
    foreach ($item in @(Get-ChildItem `
        -LiteralPath $Path `
        -Recurse `
        -Force)) {
        if (($item.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "ZIP stage contains a reparse point: $($item.FullName)"
        }
        if (-not $item.PSIsContainer) {
            $relative = Get-RelativeChildPath `
                -Root $Path `
                -Child $item.FullName
            $actualFiles += $relative
            if ($allowedFiles -notcontains $relative) {
                throw "Unexpected ZIP stage entry: $relative"
            }
        }
    }
    foreach ($expected in $allowedFiles) {
        if ($actualFiles -notcontains $expected) {
            throw "Required ZIP stage entry is missing: $expected"
        }
    }
    if ($actualFiles.Count -ne $allowedFiles.Count) {
        throw 'ZIP stage entry count does not match the release contract.'
    }
}

$script:ManifestDeleteFailureInjected = $false
function Remove-ManifestPathChecked {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path -ErrorAction Stop)) { return }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing unsafe manifest cleanup path: $Path"
    }
    if ($InternalTestMode -and $InternalFailManifestDeleteOnce -and
        -not $script:ManifestDeleteFailureInjected) {
        $script:ManifestDeleteFailureInjected = $true
        throw "Simulated manifest deletion failure: $Path"
    }
    Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (Test-Path -LiteralPath $Path -ErrorAction Stop) {
        throw "Manifest cleanup postcondition failed: $Path"
    }
}

function Invoke-ManifestCleanupChecked {
    param([Parameter(Mandatory = $true)][string[]] $Paths)

    $errors = [System.Collections.ArrayList]::new()
    foreach ($path in $Paths) {
        try { Remove-ManifestPathChecked -Path $path }
        catch { [void]$errors.Add($_.Exception.Message) }
    }
    return @($errors)
}

$packagingStage = 'initial cleanup'
try {
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
    $releaseRootItem = Get-Item -LiteralPath $releaseRoot -Force
    if (-not $releaseRootItem.PSIsContainer -or
        ($releaseRootItem.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Release root must be a regular directory: $releaseRoot"
    }
    Remove-ExactPath -Path $stage
    Remove-ExactPath -Path $archive
    Remove-ExactPath -Path $setup
    Remove-ExactPath -Path $checksums
    foreach ($staleManifest in @(Get-ChildItem `
        -LiteralPath $releaseRoot `
        -Filter ".SHA256SUMS-v$Version.*.tmp" `
        -File `
        -Force)) {
        Remove-ExactPath -Path $staleManifest.FullName
    }
    if ($InternalTestMode) {
        Remove-ExactPath -Path $published
    }

    $packagingStage = 'publish'
    if ($InternalTestMode) {
        & (Join-Path $PSScriptRoot 'publish.ps1') `
            -Version $Version `
            -OutputPath $published `
            -DotNetExecutable $DotNetExecutable `
            -InternalTestMode `
            -InternalArgumentCapturePath $InternalArgumentCapturePath
    }
    else {
        & (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version
    }

    $packagingStage = 'ZIP staging'
    $publishedApp = Join-Path $published 'CodexQuotaHud.App.exe'
    if ($InternalTestMode -and $InternalRemovePublishedAppBeforeZip) {
        Remove-Item -LiteralPath $publishedApp -Force -ErrorAction Stop
    }
    Assert-RegularPackagingFile `
        -Path $publishedApp `
        -Description 'Published App executable'
    $payload = Join-Path $stage 'artifacts\CodexQuotaHud-win-x64'
    $scripts = Join-Path $stage 'scripts'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    Copy-Item `
        -LiteralPath $publishedApp `
        -Destination $payload
    Copy-Item `
        -LiteralPath (Join-Path $PSScriptRoot 'install-production.ps1') `
        -Destination (Join-Path $scripts 'install.ps1')
    Copy-Item `
        -LiteralPath (Join-Path $PSScriptRoot 'uninstall-production.ps1') `
        -Destination (Join-Path $scripts 'uninstall.ps1')
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'README.md') `
        -Destination $stage
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'LICENSE') `
        -Destination $stage
    Assert-ExactZipStage -Path $stage

    $packagingStage = 'ZIP creation'
    Compress-Archive `
        -Path (Join-Path $stage '*') `
        -DestinationPath $archive `
        -CompressionLevel Optimal

    $packagingStage = 'Setup creation'
    $publishedDesigner = Join-Path `
        $published `
        'designer\CodexQuotaHud.SkinDesigner.exe'
    if ($InternalTestMode -and
        $InternalRemovePublishedDesignerBeforeSetup) {
        Remove-Item -LiteralPath $publishedDesigner -Force -ErrorAction Stop
    }
    Assert-RegularPackagingFile `
        -Path $publishedDesigner `
        -Description 'Published Designer executable'
    if ($InternalTestMode) {
        & (Join-Path $PSScriptRoot 'build-installer.ps1') `
            -Version $Version `
            -PublishedPath $published `
            -OutputPath $releaseRoot `
            -InnoCompilerPath $InnoCompilerPath `
            -InternalTestMode `
            -InternalArgumentCapturePath $InternalArgumentCapturePath `
            -InternalCompilerExitCode $InternalCompilerExitCode `
            -InternalSkipFakeSetup:$InternalSkipFakeSetup
    }
    else {
        & (Join-Path $PSScriptRoot 'build-installer.ps1') `
            -Version $Version `
            -PublishedPath $published
    }

    $packagingStage = 'release artifact validation'
    if (-not (Test-Path -LiteralPath $setup -PathType Leaf)) {
        throw "Expected Setup is missing: $setup"
    }
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        throw "Expected ZIP is missing: $archive"
    }

    $packagingStage = 'temporary checksum creation'
    $setupHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).
        Hash.ToLowerInvariant()
    $archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).
        Hash.ToLowerInvariant()
    $manifest = @(
        "$setupHash  $([System.IO.Path]::GetFileName($setup))",
        "$archiveHash  $([System.IO.Path]::GetFileName($archive))"
    ) -join "`n"
    $manifestBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
        $manifest + "`n")
    [System.IO.File]::WriteAllBytes($manifestTemp, $manifestBytes)
    $writtenBytes = [System.IO.File]::ReadAllBytes($manifestTemp)
    if (-not [string]::Equals(
        [Convert]::ToBase64String($manifestBytes),
        [Convert]::ToBase64String($writtenBytes),
        [System.StringComparison]::Ordinal)) {
        throw 'Temporary checksum manifest byte validation failed.'
    }

    $packagingStage = 'stage cleanup'
    if ($InternalTestMode -and $InternalFailStageCleanup) {
        throw 'Simulated stage cleanup failure.'
    }
    Remove-ExactPath -Path $stage
    if ($InternalTestMode) {
        Remove-ExactPath -Path $published
    }

    $packagingStage = 'atomic checksum commit'
    Move-Item `
        -LiteralPath $manifestTemp `
        -Destination $checksums

    Write-Host "Release Setup created: $setup"
    Write-Host "Release ZIP created: $archive"
    Write-Host "Checksum manifest created: $checksums"
}
catch {
    $failureMessage =
        "Release packaging failed during $packagingStage. $($_.Exception.Message)"
    $manifestCleanupErrors = @(
        Invoke-ManifestCleanupChecked -Paths @($manifestTemp, $checksums))
    if ($manifestCleanupErrors.Count -gt 0) {
        $retryErrors = @(
            Invoke-ManifestCleanupChecked -Paths @($manifestTemp, $checksums))
        if ($retryErrors.Count -gt 0) {
            $failureMessage += ' Manifest cleanup failed: ' +
                ($manifestCleanupErrors + $retryErrors -join ' | ')
        }
        else {
            $failureMessage += ' Manifest cleanup initially failed but retry ' +
                'succeeded: ' + ($manifestCleanupErrors -join ' | ')
        }
    }
    $cleanupTargets = @(
        [PSCustomObject]@{
            Description = 'ZIP stage'
            Path = $stage
            FailAfterFirstFile = [bool](
                $InternalTestMode -and
                $InternalFailFailureCleanupStageAfterFirstFile)
        }
    )
    if ($InternalTestMode) {
        $cleanupTargets += [PSCustomObject]@{
            Description = 'internal publish output'
            Path = $published
            FailAfterFirstFile = $false
        }
    }
    $cleanupTargets += @(
        [PSCustomObject]@{
            Description = 'ZIP archive'
            Path = $archive
            FailAfterFirstFile = $false
        },
        [PSCustomObject]@{
            Description = 'Setup executable'
            Path = $setup
            FailAfterFirstFile = $false
        }
    )

    $cleanupErrors = [System.Collections.ArrayList]::new()
    foreach ($cleanupTarget in $cleanupTargets) {
        try {
            Remove-ExactPath `
                -Path $cleanupTarget.Path `
                -FailAfterFirstFile:([bool] $cleanupTarget.FailAfterFirstFile)
        }
        catch {
            [void]$cleanupErrors.Add(
                "$($cleanupTarget.Description): $($_.Exception.Message)")
        }
    }
    if ($cleanupErrors.Count -gt 0) {
        $failureMessage += ' Cleanup also failed: ' +
            ($cleanupErrors -join ' | ')
    }
    throw $failureMessage
}
