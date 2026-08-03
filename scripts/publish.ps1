[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.1.1',
    [string] $AppProjectPath,
    [string] $DesignerProjectPath,
    [string] $OutputPath,
    [string] $DotNetExecutable = 'dotnet',
    [switch] $InternalTestMode,
    [string] $InternalArgumentCapturePath,
    [int] $InternalPublisherExitCode,
    [switch] $InternalSkipFakeExecutable,
    [switch] $InternalFailBackupCleanupAfterFirstFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$defaultAppProjectPath = Join-Path `
    $repositoryRoot `
    'src\CodexQuotaHud.App\CodexQuotaHud.App.csproj'
$defaultDesignerProjectPath = Join-Path `
    $repositoryRoot `
    'src\CodexQuotaHud.SkinDesigner\CodexQuotaHud.SkinDesigner.csproj'
$defaultOutputPath = Join-Path `
    $repositoryRoot `
    'artifacts\CodexQuotaHud-win-x64'

if ([string]::IsNullOrWhiteSpace($AppProjectPath)) {
    $AppProjectPath = $defaultAppProjectPath
}
if ([string]::IsNullOrWhiteSpace($DesignerProjectPath)) {
    $DesignerProjectPath = $defaultDesignerProjectPath
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = $defaultOutputPath
}

$appProjectFullPath = [System.IO.Path]::GetFullPath($AppProjectPath)
$designerProjectFullPath = [System.IO.Path]::GetFullPath($DesignerProjectPath)
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$expectedOutputPath = [System.IO.Path]::GetFullPath($defaultOutputPath)
$fourPartVersion = "$Version.0"
$systemTemp = [System.IO.Path]::GetFullPath(
    [System.IO.Path]::GetTempPath())
$systemTempPrefix = $systemTemp.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar

function Assert-RegularFile {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description file does not exist: $Path"
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or ($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Description must be a regular file: $Path"
    }
}

function Assert-NoReparsePoint {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing reparse-point $Description path: $Path"
    }
}

function Assert-NoReparseAncestor {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Boundary,
        [Parameter(Mandatory = $true)][string] $Description
    )

    $current = [System.IO.Path]::GetFullPath($Path)
    $boundaryFullPath = [System.IO.Path]::GetFullPath($Boundary).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    while ($true) {
        Assert-NoReparsePoint -Path $current -Description $Description
        if ([string]::Equals(
            $current.TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar),
            $boundaryFullPath,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $parent = [System.IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or
            [string]::Equals(
                $parent,
                $current,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "$Description path escaped its safety boundary: $Path"
        }
        $current = $parent
    }
}

function Assert-SafeTree {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "$Description directory does not exist: $Path"
    }
    Assert-NoReparsePoint -Path $Path -Description $Description
    foreach ($item in @(Get-ChildItem -LiteralPath $Path -Recurse -Force)) {
        if (($item.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing reparse-point content in $Description tree: " +
                $item.FullName
        }
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

function Remove-NodeWithoutFollowingReparse {
    param([Parameter(Mandatory = $true)][string] $Path)

    $attributes = [System.IO.File]::GetAttributes($Path)
    $isDirectory = ($attributes -band
        [System.IO.FileAttributes]::Directory) -ne 0
    $isReparsePoint = ($attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0
    if ($isReparsePoint) {
        if ($isDirectory) {
            [System.IO.Directory]::Delete($Path, $false)
        }
        else {
            [System.IO.File]::Delete($Path)
        }
        return
    }

    if ($isDirectory) {
        foreach ($child in @(
            [System.IO.Directory]::EnumerateFileSystemEntries($Path))) {
            Remove-NodeWithoutFollowingReparse -Path $child
        }
        [System.IO.File]::SetAttributes(
            $Path,
            [System.IO.FileAttributes]::Normal)
        [System.IO.Directory]::Delete($Path, $false)
    }
    else {
        [System.IO.File]::SetAttributes(
            $Path,
            [System.IO.FileAttributes]::Normal)
        [System.IO.File]::Delete($Path)
    }
}

function Remove-OperationTreeSafely {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }
    Remove-NodeWithoutFollowingReparse -Path $Path
    if (Test-Path -LiteralPath $Path) {
        throw "$Description cleanup postcondition failed: $Path"
    }
}

function Remove-CheckedTree {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description,
        [switch] $FailAfterFirstFile
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }
    Assert-SafeTree -Path $Path -Description $Description
    if ($InternalTestMode -and $FailAfterFirstFile) {
        $firstFile = @(Get-ChildItem `
            -LiteralPath $Path `
            -File `
            -Recurse `
            -Force | Sort-Object FullName | Select-Object -First 1)
        if ($firstFile.Count -ne 1) {
            throw "Partial cleanup injection requires a file in: $Path"
        }
        Remove-Item -LiteralPath $firstFile[0].FullName -Force
        if (Test-Path -LiteralPath $firstFile[0].FullName) {
            throw "Partial cleanup injection could not remove: $($firstFile[0].FullName)"
        }
        throw "Simulated partial publish backup cleanup failure: $Path"
    }
    Remove-OperationTreeSafely -Path $Path -Description $Description
}

function Invoke-ApplicationPublish {
    param(
        [Parameter(Mandatory = $true)][string] $Project,
        [Parameter(Mandatory = $true)][string] $Destination,
        [switch] $DisableRuntimeConfigurationFiles
    )

    $arguments = @(
        'publish',
        $Project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=None',
        '-p:DebugSymbols=false',
        "-p:Version=$Version",
        "-p:FileVersion=$fourPartVersion",
        "-p:AssemblyVersion=$fourPartVersion",
        '-o', $Destination
    )
    if ($DisableRuntimeConfigurationFiles) {
        $arguments += '-p:GenerateRuntimeConfigurationFiles=false'
    }

    $global:LASTEXITCODE = 0
    & $DotNetExecutable @arguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "dotnet publish failed for $Project with exit code $exitCode."
    }
}

Assert-RegularFile -Path $appProjectFullPath -Description 'App project'
Assert-RegularFile -Path $designerProjectFullPath -Description 'Designer project'

if ($InternalTestMode) {
    if ([string]::Equals(
            $outputFullPath.TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar),
            $systemTemp.TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar),
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $outputFullPath.StartsWith(
            $systemTempPrefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw (
            'Internal test output must be a unique directory inside the ' +
            'system temporary directory.')
    }
}
elseif (-not [string]::Equals(
    $outputFullPath,
    $expectedOutputPath,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Production publish output must be exactly: $expectedOutputPath"
}

if (-not $InternalTestMode -and
    (-not [string]::Equals($DotNetExecutable, 'dotnet',
        [System.StringComparison]::OrdinalIgnoreCase) -or
     -not [string]::IsNullOrWhiteSpace($InternalArgumentCapturePath) -or
     $InternalPublisherExitCode -ne 0 -or
     $InternalSkipFakeExecutable -or
     $InternalFailBackupCleanupAfterFirstFile)) {
    throw 'Internal publisher hooks require -InternalTestMode.'
}

$outputBoundary = if ($InternalTestMode) { $systemTemp } else { $repositoryRoot }
Assert-NoReparseAncestor `
    -Path $outputFullPath `
    -Boundary $outputBoundary `
    -Description 'publish output'
if (Test-Path -LiteralPath $outputFullPath) {
    $outputItem = Get-Item -LiteralPath $outputFullPath -Force
    if (-not $outputItem.PSIsContainer) {
        throw "Publish output must be a directory: $outputFullPath"
    }
    Assert-SafeTree -Path $outputFullPath -Description 'existing publish output'
}

$outputParent = [System.IO.Path]::GetDirectoryName($outputFullPath)
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    throw "Publish output has no parent directory: $outputFullPath"
}
New-Item -ItemType Directory -Path $outputParent -Force | Out-Null
Assert-NoReparseAncestor `
    -Path $outputParent `
    -Boundary $outputBoundary `
    -Description 'publish output parent'

$operationId = [Guid]::NewGuid().ToString('N')
$stagePath = "$outputFullPath.stage.$operationId"
$backupPath = "$outputFullPath.backup.$operationId"
if (Test-Path -LiteralPath $stagePath) {
    throw "Operation stage already exists: $stagePath"
}
if (Test-Path -LiteralPath $backupPath) {
    throw "Operation backup already exists: $backupPath"
}

$captureFullPath = $null
if ($InternalTestMode -and
    -not [string]::IsNullOrWhiteSpace($InternalArgumentCapturePath)) {
    $captureFullPath = [System.IO.Path]::GetFullPath(
        $InternalArgumentCapturePath)
    if (-not $captureFullPath.StartsWith(
        $systemTempPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Internal argument capture must stay inside system temporary.'
    }
    if (Test-Path -LiteralPath $captureFullPath) {
        $captureItem = Get-Item -LiteralPath $captureFullPath -Force
        if ($captureItem.PSIsContainer -or ($captureItem.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Internal argument capture must be a regular file: $captureFullPath"
        }
        Remove-Item -LiteralPath $captureFullPath -Force
    }
}

$oldCapture = $env:CODEX_HUD_CAPTURE_PATH
$oldExitCode = $env:CODEX_HUD_FAKE_EXIT_CODE
$oldSkipExecutable = $env:CODEX_HUD_SKIP_FAKE_EXE
$existingMoved = $false
$newPromoted = $false
$promotionValidated = $false
try {
    New-Item -ItemType Directory -Path $stagePath | Out-Null
    Assert-SafeTree -Path $stagePath -Description 'publish stage'

    if ($InternalTestMode) {
        $env:CODEX_HUD_CAPTURE_PATH = $captureFullPath
        if ($InternalPublisherExitCode -ne 0) {
            $env:CODEX_HUD_FAKE_EXIT_CODE =
                $InternalPublisherExitCode.ToString(
                    [System.Globalization.CultureInfo]::InvariantCulture)
        }
        else {
            Remove-Item Env:\CODEX_HUD_FAKE_EXIT_CODE -ErrorAction SilentlyContinue
        }
        $env:CODEX_HUD_SKIP_FAKE_EXE =
            if ($InternalSkipFakeExecutable) { '1' } else { '0' }
    }

    Invoke-ApplicationPublish `
        -Project $appProjectFullPath `
        -Destination $stagePath
    $designerStage = Join-Path $stagePath 'designer'
    Invoke-ApplicationPublish `
        -Project $designerProjectFullPath `
        -Destination $designerStage `
        -DisableRuntimeConfigurationFiles

    Assert-SafeTree -Path $stagePath -Description 'publish stage'
    $expectedApp = Join-Path $stagePath 'CodexQuotaHud.App.exe'
    $expectedDesigner = Join-Path `
        $designerStage `
        'CodexQuotaHud.SkinDesigner.exe'
    Assert-RegularFile -Path $expectedApp -Description 'Published App executable'
    Assert-RegularFile `
        -Path $expectedDesigner `
        -Description 'Published Designer executable'

    $allowedFiles = @(
        'CodexQuotaHud.App.exe',
        'designer\CodexQuotaHud.SkinDesigner.exe'
    )
    foreach ($file in @(Get-ChildItem `
        -LiteralPath $stagePath `
        -File `
        -Recurse `
        -Force)) {
        $relative = Get-RelativeChildPath `
            -Root $stagePath `
            -Child $file.FullName
        if ($allowedFiles -notcontains $relative) {
            throw "Unexpected published file: $relative"
        }
    }
    foreach ($directory in @(Get-ChildItem `
        -LiteralPath $stagePath `
        -Directory `
        -Recurse `
        -Force)) {
        $relative = Get-RelativeChildPath `
            -Root $stagePath `
            -Child $directory.FullName
        if (-not [string]::Equals(
            $relative,
            'designer',
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unexpected published directory: $relative"
        }
    }

    if (Test-Path -LiteralPath $outputFullPath) {
        Move-Item -LiteralPath $outputFullPath -Destination $backupPath
        $existingMoved = $true
        Assert-SafeTree -Path $backupPath -Description 'publish backup'
    }
    Move-Item -LiteralPath $stagePath -Destination $outputFullPath
    $newPromoted = $true
    Assert-SafeTree -Path $outputFullPath -Description 'promoted publish output'
    $promotionValidated = $true
    if ($existingMoved) {
        Remove-CheckedTree `
            -Path $backupPath `
            -Description 'publish backup' `
            -FailAfterFirstFile:$InternalFailBackupCleanupAfterFirstFile
        $existingMoved = $false
    }
}
catch {
    $failureMessage = $_.Exception.Message
    $cleanupErrors = [System.Collections.ArrayList]::new()
    if ($promotionValidated) {
        if (Test-Path -LiteralPath $backupPath) {
            $failureMessage += (
                ' Promoted output remains active; diagnostic backup retained: ' +
                $backupPath)
        }
    }
    elseif ($existingMoved) {
        try {
            if ($newPromoted -and (Test-Path -LiteralPath $outputFullPath)) {
                Remove-CheckedTree `
                    -Path $outputFullPath `
                    -Description 'failed promoted publish output'
            }
            if (Test-Path -LiteralPath $backupPath) {
                Move-Item -LiteralPath $backupPath -Destination $outputFullPath
                $existingMoved = $false
            }
        }
        catch {
            [void]$cleanupErrors.Add(
                "Publish rollback failed: $($_.Exception.Message)")
        }
    }
    elseif ($newPromoted -and (Test-Path -LiteralPath $outputFullPath)) {
        try {
            Remove-CheckedTree `
                -Path $outputFullPath `
                -Description 'failed promoted publish output'
        }
        catch {
            [void]$cleanupErrors.Add(
                "Promoted output cleanup failed: $($_.Exception.Message)")
        }
    }
    if (Test-Path -LiteralPath $stagePath) {
        try {
            Remove-OperationTreeSafely `
                -Path $stagePath `
                -Description 'publish stage'
        }
        catch {
            [void]$cleanupErrors.Add(
                "Publish stage cleanup failed: $($_.Exception.Message)")
        }
    }
    if ($cleanupErrors.Count -gt 0) {
        $failureMessage += ' ' + ($cleanupErrors -join ' | ')
    }
    throw $failureMessage
}
finally {
    $env:CODEX_HUD_CAPTURE_PATH = $oldCapture
    $env:CODEX_HUD_FAKE_EXIT_CODE = $oldExitCode
    $env:CODEX_HUD_SKIP_FAKE_EXE = $oldSkipExecutable
}

Write-Host "Published CodexQuotaHud applications to: $outputFullPath"
