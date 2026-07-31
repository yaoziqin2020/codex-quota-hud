[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.0.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$releaseRoot = Join-Path $repositoryRoot 'artifacts\release'
$packageName = "CodexQuotaHud-v$Version-win-x64"
$stage = Join-Path $releaseRoot $packageName
$archive = Join-Path $releaseRoot "$packageName.zip"
$published = Join-Path `
    $repositoryRoot `
    'artifacts\CodexQuotaHud-win-x64'

& (Join-Path $PSScriptRoot 'publish.ps1') -Version $Version

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
if (Test-Path -LiteralPath $archive) {
    Remove-Item -LiteralPath $archive -Force
}

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

Compress-Archive `
    -Path (Join-Path $stage '*') `
    -DestinationPath $archive `
    -CompressionLevel Optimal

Write-Host "Release package created: $archive"
