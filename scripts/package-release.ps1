[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.1.0',
    [string] $OutputPath,
    [string] $DotNetExecutable = 'dotnet',
    [string] $InnoCompilerPath,
    [switch] $InternalTestMode,
    [string] $InternalArgumentCapturePath,
    [int] $InternalCompilerExitCode,
    [switch] $InternalSkipFakeSetup
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
        $InternalSkipFakeSetup) {
        throw 'Internal packaging hooks require -InternalTestMode.'
    }

    $releaseRoot = $productionReleaseRoot
}

$packageName = "CodexQuotaHud-v$Version-win-x64"
$stage = Join-Path $releaseRoot $packageName
$archive = Join-Path $releaseRoot "$packageName.zip"
$setup = Join-Path $releaseRoot "CodexQuotaHud-Setup-v$Version.exe"
$checksums = Join-Path $releaseRoot 'SHA256SUMS.txt'
$published = if ($InternalTestMode) {
    Join-Path $releaseRoot ".internal-published-v$Version"
}
else {
    Join-Path $repositoryRoot 'artifacts\CodexQuotaHud-win-x64'
}

function Remove-ExactPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to remove a reparse-point packaging path: $Path"
    }

    if ($item.PSIsContainer) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
    else {
        Remove-Item -LiteralPath $Path -Force
    }
}

$packagingStage = 'initial cleanup'
try {
    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
    Remove-ExactPath -Path $stage
    Remove-ExactPath -Path $archive
    Remove-ExactPath -Path $setup
    Remove-ExactPath -Path $checksums
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
    $payload = Join-Path $stage 'artifacts\CodexQuotaHud-win-x64'
    $scripts = Join-Path $stage 'scripts'
    New-Item -ItemType Directory -Path $payload -Force | Out-Null
    New-Item -ItemType Directory -Path $scripts -Force | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $published 'CodexQuotaHud.App.exe') `
        -Destination $payload
    Copy-Item `
        -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') `
        -Destination $scripts
    Copy-Item `
        -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') `
        -Destination $scripts
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'README.md') `
        -Destination $stage
    Copy-Item `
        -LiteralPath (Join-Path $repositoryRoot 'LICENSE') `
        -Destination $stage

    $packagingStage = 'ZIP creation'
    Compress-Archive `
        -Path (Join-Path $stage '*') `
        -DestinationPath $archive `
        -CompressionLevel Optimal

    $packagingStage = 'Setup creation'
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

    $packagingStage = 'checksum creation'
    $setupHash = (Get-FileHash -LiteralPath $setup -Algorithm SHA256).
        Hash.ToLowerInvariant()
    $archiveHash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).
        Hash.ToLowerInvariant()
    $manifest = @(
        "$setupHash  $([System.IO.Path]::GetFileName($setup))",
        "$archiveHash  $([System.IO.Path]::GetFileName($archive))"
    ) -join "`n"
    [System.IO.File]::WriteAllText(
        $checksums,
        $manifest + "`n",
        [System.Text.UTF8Encoding]::new($false))

    Write-Host "Release Setup created: $setup"
    Write-Host "Release ZIP created: $archive"
    Write-Host "Checksum manifest created: $checksums"
}
catch {
    Remove-Item -LiteralPath $checksums -Force -ErrorAction SilentlyContinue
    throw "Release packaging failed during $packagingStage. $($_.Exception.Message)"
}
finally {
    Remove-ExactPath -Path $stage
    if ($InternalTestMode) {
        Remove-ExactPath -Path $published
    }
}
