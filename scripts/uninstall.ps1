[CmdletBinding()]
param(
    [switch] $InternalTestMode,
    [string] $InternalLocalAppDataRoot,
    [string] $InternalTargetPath,
    [string] $InternalProcessSnapshotPath,
    [string] $InternalActionLogPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestActions = [System.Collections.ArrayList]::new()

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals(
        $fullPath,
        $root,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath
    }

    return $fullPath.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Test-PathEquals {
    param(
        [Parameter(Mandatory = $true)][string] $Left,
        [Parameter(Mandatory = $true)][string] $Right)

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
    $boundaryFull = Get-NormalizedPath $Boundary
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a reparse-point path component: $current"
            }
        }

        if (Test-PathEquals $current $boundaryFull) {
            break
        }

        $parent = Split-Path -Path $current -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or
            (Test-PathEquals $parent $current)) {
            throw "Path escaped its validation boundary: $Path"
        }

        $current = $parent
    }
}

function Get-ValidatedInstallTarget {
    param(
        [Parameter(Mandatory = $true)][string] $TargetPath,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    $localRoot = Get-NormalizedPath $LocalAppDataRoot
    $target = Get-NormalizedPath $TargetPath
    $expected = Get-NormalizedPath (
        Join-Path $localRoot 'Programs\CodexQuotaHud')
    $programs = Get-NormalizedPath (Join-Path $localRoot 'Programs')
    $fileSystemRoot = [System.IO.Path]::GetPathRoot($target)
    $userProfile = Get-NormalizedPath $env:USERPROFILE

    if (-not (Test-PathEquals $target $expected)) {
        throw "Uninstall target must be exactly: $expected"
    }

    foreach ($forbidden in @(
        $fileSystemRoot,
        $userProfile,
        $localRoot,
        $programs)) {
        if (Test-PathEquals $target $forbidden) {
            throw "Refusing unsafe uninstall target: $target"
        }
    }

    Assert-NoReparsePoint -Path $target -Boundary $localRoot
    if (Test-Path -LiteralPath $target) {
        $resolved = Get-NormalizedPath (
            (Resolve-Path -LiteralPath $target).ProviderPath)
        if (-not (Test-PathEquals $resolved $target)) {
            throw "Uninstall target resolves outside its expected path: $target"
        }
    }

    return $target
}

function Add-TestAction {
    param(
        [Parameter(Mandatory = $true)][string] $Action,
        [hashtable] $Properties = @{})

    $entry = [ordered]@{ Action = $Action }
    foreach ($property in $Properties.GetEnumerator()) {
        $entry[$property.Key] = $property.Value
    }

    [void]$script:TestActions.Add([pscustomobject]$entry)
}

function Write-TestActionLog {
    if (-not $InternalTestMode -or
        [string]::IsNullOrWhiteSpace($InternalActionLogPath)) {
        return
    }

    $json = ConvertTo-Json -InputObject @($script:TestActions) -Compress
    [System.IO.File]::WriteAllText(
        (Get-NormalizedPath $InternalActionLogPath),
        $json,
        [System.Text.UTF8Encoding]::new($false))
}

function Get-CodexQuotaHudProcesses {
    if ($InternalTestMode) {
        if ([string]::IsNullOrWhiteSpace($InternalProcessSnapshotPath)) {
            return @()
        }

        $json = Get-Content `
            -LiteralPath $InternalProcessSnapshotPath `
            -Raw `
            -Encoding UTF8
        $parsed = ConvertFrom-Json -InputObject $json
        return @($parsed | ForEach-Object { $_ })
    }

    return @(Get-CimInstance `
        -ClassName Win32_Process `
        -Filter "Name = 'CodexQuotaHud.App.exe'" `
        -ErrorAction SilentlyContinue)
}

function Stop-InstalledInstance {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    foreach ($process in @(Get-CodexQuotaHudProcesses)) {
        if ($null -eq $process -or
            -not [string]::Equals(
                [string]$process.Name,
                'CodexQuotaHud.App.exe',
                [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::IsNullOrWhiteSpace(
                [string]$process.ExecutablePath) -or
            -not (Test-PathEquals `
                ([string]$process.ExecutablePath) `
                $ExecutablePath)) {
            continue
        }

        $processId = [int]$process.ProcessId
        if ($InternalTestMode) {
            Add-TestAction -Action 'StopProcess' -Properties @{
                ProcessId = $processId
                ExecutablePath = [string]$process.ExecutablePath
            }
            continue
        }

        Stop-Process -Id $processId -Force -ErrorAction Stop
        Wait-Process -Id $processId -Timeout 10 -ErrorAction SilentlyContinue
    }
}

function Remove-StartupRegistration {
    if ($InternalTestMode) {
        Add-TestAction -Action 'RemoveRunValue' -Properties @{
            Name = 'CodexQuotaHud'
        }
        return
    }

    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (Test-Path -LiteralPath $runKey) {
        Remove-ItemProperty `
            -Path $runKey `
            -Name 'CodexQuotaHud' `
            -ErrorAction SilentlyContinue
    }
}

try {
    if ($InternalTestMode) {
        if ([string]::IsNullOrWhiteSpace($InternalLocalAppDataRoot)) {
            throw 'Internal test mode requires -InternalLocalAppDataRoot.'
        }

        $localAppDataRoot = Get-NormalizedPath $InternalLocalAppDataRoot
        $systemTemp = Get-NormalizedPath ([System.IO.Path]::GetTempPath())
        if (-not $localAppDataRoot.StartsWith(
            $systemTemp,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Internal test LocalAppData must stay inside the system temporary directory.'
        }
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($InternalLocalAppDataRoot) -or
            -not [string]::IsNullOrWhiteSpace($InternalTargetPath) -or
            -not [string]::IsNullOrWhiteSpace($InternalProcessSnapshotPath) -or
            -not [string]::IsNullOrWhiteSpace($InternalActionLogPath)) {
            throw 'Internal uninstall hooks require -InternalTestMode.'
        }

        $localAppDataRoot = Get-NormalizedPath $env:LOCALAPPDATA
    }

    $expectedTarget = Join-Path $localAppDataRoot 'Programs\CodexQuotaHud'
    $requestedTarget = if ($InternalTestMode -and
        -not [string]::IsNullOrWhiteSpace($InternalTargetPath)) {
        $InternalTargetPath
    }
    else {
        $expectedTarget
    }
    $target = Get-ValidatedInstallTarget `
        -TargetPath $requestedTarget `
        -LocalAppDataRoot $localAppDataRoot
    $targetExecutable = Join-Path $target 'CodexQuotaHud.App.exe'

    Stop-InstalledInstance -ExecutablePath $targetExecutable
    Remove-StartupRegistration

    if (Test-Path -LiteralPath $target) {
        $targetItem = Get-Item -LiteralPath $target -Force
        if (($targetItem.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to remove a reparse-point target: $target"
        }

        Remove-Item -LiteralPath $target -Recurse -Force
    }

    Write-Host "Uninstalled CodexQuotaHud from: $target"
}
finally {
    Write-TestActionLog
}
