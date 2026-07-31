[CmdletBinding()]
param()

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
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Boundary)

    $current = Get-NormalizedPath $Path
    $stop = Get-NormalizedPath $Boundary
    while ($true) {
        if (Test-Path -LiteralPath $current -ErrorAction Stop) {
            $item = Get-Item -LiteralPath $current -Force -ErrorAction Stop
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

$localRoot = Get-NormalizedPath ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData))
$target = Get-NormalizedPath (Join-Path $localRoot 'Programs\CodexQuotaHud')
$expected = Get-NormalizedPath (Join-Path $localRoot 'Programs\CodexQuotaHud')
if (-not (Test-PathEquals $target $expected)) {
    throw 'Uninstall target validation failed.'
}
$volumeRoot = Get-NormalizedPath ([System.IO.Path]::GetPathRoot($localRoot))
Assert-NoReparsePoint -Path $target -Boundary $volumeRoot

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

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'CodexQuotaHud' `
    -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}
Write-Host "Uninstalled CodexQuotaHud from: $target"
