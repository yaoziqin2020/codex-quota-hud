[CmdletBinding()]
param(
    [string] $PublishedPath,
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
        throw "Install target must be exactly: $expected"
    }

    foreach ($forbidden in @(
        $fileSystemRoot,
        $userProfile,
        $localRoot,
        $programs)) {
        if (Test-PathEquals $target $forbidden) {
            throw "Refusing unsafe install target: $target"
        }
    }

    Assert-NoReparsePoint -Path $target -Boundary $localRoot
    if (Test-Path -LiteralPath $target) {
        $resolved = Get-NormalizedPath (
            (Resolve-Path -LiteralPath $target).ProviderPath)
        if (-not (Test-PathEquals $resolved $target)) {
            throw "Install target resolves outside its expected path: $target"
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

function Set-StartupRegistration {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $value = "`"$ExecutablePath`" --background"
    if ($InternalTestMode) {
        Add-TestAction -Action 'SetRunValue' -Properties @{
            Name = 'CodexQuotaHud'
            Value = $value
        }
        return
    }

    $runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    if (-not (Test-Path -LiteralPath $runKey)) {
        New-Item -Path $runKey -Force | Out-Null
    }

    New-ItemProperty `
        -Path $runKey `
        -Name 'CodexQuotaHud' `
        -Value $value `
        -PropertyType String `
        -Force | Out-Null
}

function Start-InstalledInstance {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    if ($InternalTestMode) {
        Add-TestAction -Action 'StartProcess' -Properties @{
            FilePath = $ExecutablePath
            Arguments = '--background'
            WindowStyle = 'Hidden'
        }
        return
    }

    Start-Process `
        -FilePath $ExecutablePath `
        -ArgumentList '--background' `
        -WindowStyle Hidden | Out-Null
}

try {
    $repositoryRoot = Get-NormalizedPath (Join-Path $PSScriptRoot '..')
    if ([string]::IsNullOrWhiteSpace($PublishedPath)) {
        $PublishedPath = Join-Path `
            $repositoryRoot `
            'artifacts\CodexQuotaHud-win-x64'
    }

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
            throw 'Internal install hooks require -InternalTestMode.'
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

    $source = Get-NormalizedPath $PublishedPath
    $sourceExecutable = Join-Path $source 'CodexQuotaHud.App.exe'
    if (-not (Test-Path -LiteralPath $source -PathType Container) -or
        -not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
        throw "Published payload is incomplete: $sourceExecutable"
    }

    $targetParent = Split-Path -Path $target -Parent
    New-Item -ItemType Directory -Path $targetParent -Force | Out-Null
    Assert-NoReparsePoint -Path $targetParent -Boundary $localAppDataRoot

    $suffix = [Guid]::NewGuid().ToString('N')
    $stage = Join-Path $targetParent "CodexQuotaHud.staging.$suffix"
    $backup = Join-Path $targetParent "CodexQuotaHud.backup.$suffix"
    $targetExecutable = Join-Path $target 'CodexQuotaHud.App.exe'
    $movedExistingTarget = $false
    $installedNewTarget = $false

    try {
        New-Item -ItemType Directory -Path $stage | Out-Null
        Get-ChildItem -LiteralPath $source -Force |
            Copy-Item -Destination $stage -Recurse -Force

        $stagedExecutable = Join-Path $stage 'CodexQuotaHud.App.exe'
        if (-not (Test-Path -LiteralPath $stagedExecutable -PathType Leaf)) {
            throw "Staged executable is missing: $stagedExecutable"
        }

        Stop-InstalledInstance -ExecutablePath $targetExecutable

        if (Test-Path -LiteralPath $target) {
            Move-Item -LiteralPath $target -Destination $backup
            $movedExistingTarget = $true
        }

        Move-Item -LiteralPath $stage -Destination $target
        $installedNewTarget = $true
        Set-StartupRegistration -ExecutablePath $targetExecutable
        Start-InstalledInstance -ExecutablePath $targetExecutable

        if ($movedExistingTarget -and (Test-Path -LiteralPath $backup)) {
            Remove-Item -LiteralPath $backup -Recurse -Force
            $movedExistingTarget = $false
        }
    }
    catch {
        if ($installedNewTarget -and (Test-Path -LiteralPath $target)) {
            $targetItem = Get-Item -LiteralPath $target -Force
            if (($targetItem.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Install rollback encountered an unsafe reparse point.'
            }

            Remove-Item -LiteralPath $target -Recurse -Force
        }

        if ($movedExistingTarget -and (Test-Path -LiteralPath $backup)) {
            Move-Item -LiteralPath $backup -Destination $target
        }

        throw
    }
    finally {
        if (Test-Path -LiteralPath $stage) {
            $stageItem = Get-Item -LiteralPath $stage -Force
            if (($stageItem.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -eq 0) {
                Remove-Item -LiteralPath $stage -Recurse -Force
            }
        }
    }

    Write-Host "Installed CodexQuotaHud to: $target"
}
finally {
    Write-TestActionLog
}
