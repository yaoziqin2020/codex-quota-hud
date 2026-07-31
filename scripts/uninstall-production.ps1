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

$localRoot = Get-NormalizedPath ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData))
$target = Get-NormalizedPath (Join-Path $localRoot 'Programs\CodexQuotaHud')
$expected = Get-NormalizedPath (Join-Path $localRoot 'Programs\CodexQuotaHud')
if (-not (Test-PathEquals $target $expected)) {
    throw 'Uninstall target validation failed.'
}
$item = if (Test-Path -LiteralPath $target) {
    Get-Item -LiteralPath $target -Force
}
if ($null -ne $item -and ($item.Attributes -band
    [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Refusing reparse-point uninstall target: $target"
}

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
