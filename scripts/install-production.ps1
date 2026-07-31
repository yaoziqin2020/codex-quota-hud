[CmdletBinding()]
param([string] $PublishedPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($full)
    if ([string]::Equals($full, $root,
        [System.StringComparison]::OrdinalIgnoreCase)) { return $full }
    return $full.TrimEnd('\', '/')
}

function Test-PathEquals {
    param([string] $Left, [string] $Right)
    return [string]::Equals(
        (Get-NormalizedPath $Left),
        (Get-NormalizedPath $Right),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparsePoint {
    param([string] $Path, [string] $Boundary)
    $current = Get-NormalizedPath $Path
    $stop = Get-NormalizedPath $Boundary
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing reparse-point path: $current"
            }
        }
        if (Test-PathEquals $current $stop) { break }
        $parent = Split-Path -Path $current -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or
            (Test-PathEquals $parent $current)) {
            throw "Path escaped validation boundary: $Path"
        }
        $current = $parent
    }
}

$repositoryRoot = Get-NormalizedPath (Join-Path $PSScriptRoot '..')
if ([string]::IsNullOrWhiteSpace($PublishedPath)) {
    $PublishedPath = Join-Path $repositoryRoot 'artifacts\CodexQuotaHud-win-x64'
}
$source = Get-NormalizedPath $PublishedPath
$sourceExecutable = Join-Path $source 'CodexQuotaHud.App.exe'
if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
    throw "Published payload is incomplete: $sourceExecutable"
}

$localRoot = Get-NormalizedPath ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData))
$programs = Join-Path $localRoot 'Programs'
$target = Get-NormalizedPath (Join-Path $programs 'CodexQuotaHud')
if (-not (Test-PathEquals $target (Join-Path $localRoot 'Programs\CodexQuotaHud'))) {
    throw 'Install target validation failed.'
}
Assert-NoReparsePoint -Path $target -Boundary $localRoot

$targetExecutable = Join-Path $target 'CodexQuotaHud.App.exe'
foreach ($process in @(Get-CimInstance -ClassName Win32_Process `
    -Filter "Name = 'CodexQuotaHud.App.exe'" -ErrorAction SilentlyContinue)) {
    if (-not [string]::IsNullOrWhiteSpace([string]$process.ExecutablePath) -and
        (Test-PathEquals ([string]$process.ExecutablePath) $targetExecutable)) {
        Stop-Process -Id ([int]$process.ProcessId) -Force -ErrorAction Stop
        Wait-Process -Id ([int]$process.ProcessId) -Timeout 10 `
            -ErrorAction SilentlyContinue
    }
}

$targetParent = Split-Path -Path $target -Parent
New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
$suffix = [Guid]::NewGuid().ToString('N')
$stage = Join-Path $targetParent "CodexQuotaHud.staging.$suffix"
$backup = Join-Path $targetParent "CodexQuotaHud.backup.$suffix"
$movedExisting = $false
$installedNew = $false
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    Get-ChildItem -LiteralPath $source -Force |
        Copy-Item -Destination $stage -Recurse -Force
    if (-not (Test-Path -LiteralPath (
        Join-Path $stage 'CodexQuotaHud.App.exe') -PathType Leaf)) {
        throw 'Staged executable is missing.'
    }
    if (Test-Path -LiteralPath $target) {
        Move-Item -LiteralPath $target -Destination $backup
        $movedExisting = $true
    }
    Move-Item -LiteralPath $stage -Destination $target
    $installedNew = $true
    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runKey -Force | Out-Null
    New-ItemProperty -Path $runKey -Name 'CodexQuotaHud' `
        -Value "`"$targetExecutable`" --background" `
        -PropertyType String -Force | Out-Null
    Start-Process -FilePath $targetExecutable -ArgumentList '--background' `
        -WindowStyle Hidden | Out-Null
    if ($movedExisting -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force
        $movedExisting = $false
    }
}
catch {
    if ($installedNew -and (Test-Path -LiteralPath $target)) {
        Assert-NoReparsePoint -Path $target -Boundary $localRoot
        Remove-Item -LiteralPath $target -Recurse -Force
    }
    if ($movedExisting -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $backup -Destination $target
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $stage) {
        Assert-NoReparsePoint -Path $stage -Boundary $localRoot
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
}

Write-Host "Installed CodexQuotaHud to: $target"
