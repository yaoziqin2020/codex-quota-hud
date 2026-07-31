[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'PrepareInstall',
        'CommitInstall',
        'RollbackInstall',
        'PrepareUninstall',
        'PurgeSettings')]
    [string] $Action,
    [Parameter(Mandatory = $true)]
    [string] $InstallPath,
    [string] $LocalAppDataRoot,
    [string] $LegacyBackupPath,
    [switch] $InternalTestMode,
    [string] $InternalProcessSnapshotPath,
    [string] $InternalActionLogPath,
    [switch] $InternalSkipShutdownSignal
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestActions = [System.Collections.ArrayList]::new()

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    $suppliedRoot = [System.IO.Path]::GetPathRoot($Path)
    if (-not [System.IO.Path]::IsPathRooted($Path) -or
        [string]::IsNullOrWhiteSpace($suppliedRoot) -or
        $suppliedRoot.Length -le 1) {
        throw "Path must be absolute: $Path"
    }

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

function Assert-NoReparsePointTree {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Boundary)

    Assert-NoReparsePoint -Path $Path -Boundary $Boundary
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        return
    }

    $pending = [System.Collections.Queue]::new()
    $pending.Enqueue((Get-NormalizedPath $Path))
    while ($pending.Count -gt 0) {
        $directory = [string]$pending.Dequeue()
        foreach ($item in @(
            Get-ChildItem -LiteralPath $directory -Force)) {
            if (($item.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing a reparse-point path component: $($item.FullName)"
            }

            if ($item.PSIsContainer) {
                $pending.Enqueue($item.FullName)
            }
        }
    }
}

function Assert-ResolvedPathEqualsExpected {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolved = Get-NormalizedPath (
        (Resolve-Path -LiteralPath $Path).ProviderPath)
    if (-not (Test-PathEquals $resolved $Path)) {
        throw "Path resolves outside its expected location: $Path"
    }
}

function Get-ValidatedInstallTarget {
    param(
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    $localRoot = Get-NormalizedPath $LocalAppDataRoot
    $target = Get-NormalizedPath $InstallPath
    $programs = Get-NormalizedPath (Join-Path $localRoot 'Programs')
    $expected = Get-NormalizedPath (Join-Path $programs 'CodexQuotaHud')
    $fileSystemRoot = [System.IO.Path]::GetPathRoot($target)
    $userProfile = Get-NormalizedPath (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::UserProfile))

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

    Assert-NoReparsePointTree -Path $target -Boundary $localRoot
    Assert-ResolvedPathEqualsExpected -Path $target
    return $target
}

function Get-ValidatedSettingsTarget {
    param(
        [Parameter(Mandatory = $true)][string] $SettingsPath,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    $localRoot = Get-NormalizedPath $LocalAppDataRoot
    $target = Get-NormalizedPath $SettingsPath
    $expected = Get-NormalizedPath (Join-Path $localRoot 'CodexQuotaHud')
    $fileSystemRoot = [System.IO.Path]::GetPathRoot($target)
    $userProfile = Get-NormalizedPath (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::UserProfile))

    if (-not (Test-PathEquals $target $expected)) {
        throw "Settings target must be exactly: $expected"
    }

    foreach ($forbidden in @(
        $fileSystemRoot,
        $userProfile,
        $localRoot)) {
        if (Test-PathEquals $target $forbidden) {
            throw "Refusing unsafe settings target: $target"
        }
    }

    Assert-NoReparsePointTree -Path $target -Boundary $localRoot
    Assert-ResolvedPathEqualsExpected -Path $target
    return $target
}

function Get-ValidatedLegacyBackupTarget {
    param(
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    $localRoot = Get-NormalizedPath $LocalAppDataRoot
    $programs = Get-NormalizedPath (Join-Path $localRoot 'Programs')
    $target = Get-NormalizedPath $BackupPath
    $parent = Get-NormalizedPath (Split-Path -Path $target -Parent)
    if (-not (Test-PathEquals $parent $programs)) {
        throw "Legacy backup must stay directly under Programs: $programs"
    }

    $prefix = 'CodexQuotaHud.legacy-backup.'
    $leaf = Split-Path -Path $target -Leaf
    if (-not $leaf.StartsWith(
            $prefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Legacy backup must use the exact $prefix prefix."
    }

    $suffix = $leaf.Substring($prefix.Length)
    try {
        [void][Guid]::Parse($suffix)
    }
    catch {
        throw 'Legacy backup suffix must be a GUID.'
    }

    Assert-NoReparsePointTree -Path $target -Boundary $localRoot
    Assert-ResolvedPathEqualsExpected -Path $target
    return $target
}

function Get-ValidatedLegacyMarker {
    param(
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $InstallPath)

    $markerPath = Join-Path $BackupPath 'CodexQuotaHud.LegacyBackup.json'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "Legacy backup marker is missing: $markerPath"
    }

    $marker = Get-Content `
        -LiteralPath $markerPath `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json
    $properties = @($marker.PSObject.Properties.Name)
    if ($properties.Count -ne 2 -or
        $properties -notcontains 'Source' -or
        $properties -notcontains 'Destination' -or
        [string]::IsNullOrWhiteSpace([string]$marker.Source) -or
        [string]::IsNullOrWhiteSpace([string]$marker.Destination) -or
        -not (Test-PathEquals ([string]$marker.Source) $InstallPath) -or
        -not (Test-PathEquals ([string]$marker.Destination) $BackupPath)) {
        throw 'Legacy backup marker does not match the exact source and destination.'
    }

    return $markerPath
}

function Copy-LegacyInstallToBackup {
    param(
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $BackupPath)

    $executable = Join-Path $InstallPath 'CodexQuotaHud.App.exe'
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        return
    }

    if (Test-Path -LiteralPath $BackupPath) {
        throw "Legacy backup already exists: $BackupPath"
    }

    try {
        New-Item -ItemType Directory -Path $BackupPath | Out-Null
        Get-ChildItem -LiteralPath $InstallPath -Force |
            Copy-Item -Destination $BackupPath -Recurse -Force

        $marker = [ordered]@{
            Source = $InstallPath
            Destination = $BackupPath
        }
        $markerJson = ConvertTo-Json -InputObject $marker -Compress
        [System.IO.File]::WriteAllText(
            (Join-Path $BackupPath 'CodexQuotaHud.LegacyBackup.json'),
            $markerJson,
            [System.Text.UTF8Encoding]::new($false))
    }
    catch {
        if (Test-Path -LiteralPath $BackupPath) {
            Assert-NoReparsePoint `
                -Path $BackupPath `
                -Boundary (Split-Path -Path $BackupPath -Parent)
            Remove-Item -LiteralPath $BackupPath -Recurse -Force
        }

        throw
    }

    if ($InternalTestMode) {
        Add-TestAction -Action 'BackupLegacy' -Properties @{
            Source = $InstallPath
            Destination = $BackupPath
        }
    }
}

function Restore-LegacyInstallBackup {
    param(
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        return
    }

    [void](Get-ValidatedLegacyMarker `
        -BackupPath $BackupPath `
        -InstallPath $InstallPath)
    Assert-NoReparsePointTree `
        -Path $BackupPath `
        -Boundary (Split-Path -Path $BackupPath -Parent)
    if (Test-Path -LiteralPath $InstallPath) {
        [void](Get-ValidatedInstallTarget `
            -InstallPath $InstallPath `
            -LocalAppDataRoot $LocalAppDataRoot)
        Remove-Item -LiteralPath $InstallPath -Recurse -Force
    }

    Copy-Item -LiteralPath $BackupPath -Destination $InstallPath -Recurse
    $restoredMarker = Join-Path `
        $InstallPath `
        'CodexQuotaHud.LegacyBackup.json'
    Remove-Item -LiteralPath $restoredMarker -Force
    Remove-Item -LiteralPath $BackupPath -Recurse -Force

    if ($InternalTestMode) {
        Add-TestAction -Action 'RollbackLegacy' -Properties @{
            Source = $BackupPath
            Destination = $InstallPath
        }
    }
}

function Remove-LegacyInstallBackup {
    param(
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $BackupPath)

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        return
    }

    [void](Get-ValidatedLegacyMarker `
        -BackupPath $BackupPath `
        -InstallPath $InstallPath)
    Assert-NoReparsePointTree `
        -Path $BackupPath `
        -Boundary (Split-Path -Path $BackupPath -Parent)
    Remove-Item -LiteralPath $BackupPath -Recurse -Force

    if ($InternalTestMode) {
        Add-TestAction -Action 'CommitLegacy' -Properties @{
            Destination = $BackupPath
        }
    }
}

function Remove-ValidatedSettingsDirectory {
    param(
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    $settings = Get-ValidatedSettingsTarget `
        -SettingsPath (Join-Path $LocalAppDataRoot 'CodexQuotaHud') `
        -LocalAppDataRoot $LocalAppDataRoot
    if (Test-Path -LiteralPath $settings) {
        Remove-Item -LiteralPath $settings -Recurse -Force
    }

    if ($InternalTestMode) {
        Add-TestAction -Action 'PurgeSettings' -Properties @{
            Destination = $settings
        }
    }
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
        -ErrorAction Stop)
}

function Get-ExactInstalledProcesses {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $matches = [System.Collections.ArrayList]::new()
    foreach ($process in @(Get-CodexQuotaHudProcesses)) {
        if ($null -eq $process -or
            -not [string]::Equals(
                [string]$process.Name,
                'CodexQuotaHud.App.exe',
                [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ([string]::IsNullOrWhiteSpace(
            [string]$process.ExecutablePath)) {
            throw (
                "Executable path cannot be inspected for matching process " +
                "$([int]$process.ProcessId).")
        }

        $processPath = try {
            Get-NormalizedPath ([string]$process.ExecutablePath)
        }
        catch {
            throw (
                "Executable path cannot be inspected for matching process " +
                "$([int]$process.ProcessId).")
        }

        if (Test-PathEquals $processPath $ExecutablePath) {
            [void]$matches.Add([pscustomobject]@{
                ProcessId = [int]$process.ProcessId
                ExecutablePath = $processPath
            })
        }
    }

    return @($matches)
}

function Wait-ForExactInstalledInstanceExit {
    param(
        [Parameter(Mandatory = $true)][string] $ExecutablePath,
        [Parameter(Mandatory = $true)][int] $TimeoutSeconds)

    if ($InternalTestMode) {
        Add-TestAction -Action 'WaitForExit' -Properties @{
            ExecutablePath = $ExecutablePath
            TimeoutSeconds = $TimeoutSeconds
        }
        return @(Get-ExactInstalledProcesses -ExecutablePath $ExecutablePath)
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $remaining = @(
            Get-ExactInstalledProcesses -ExecutablePath $ExecutablePath)
        if ($remaining.Count -eq 0) {
            return @()
        }

        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    return $remaining
}

function Stop-ExactInstalledInstance {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    if (-not $InternalSkipShutdownSignal) {
        if ($InternalTestMode) {
            Add-TestAction -Action 'SignalShutdown' -Properties @{
                EventName = 'Local\CodexQuotaHud.ShutdownRequested'
            }
        }
        else {
            try {
                $event = [System.Threading.EventWaitHandle]::OpenExisting(
                    'Local\CodexQuotaHud.ShutdownRequested')
                try {
                    [void]$event.Set()
                }
                finally {
                    $event.Dispose()
                }
            }
            catch [System.Threading.WaitHandleCannotBeOpenedException] {
                # Listener-incompatible legacy version; use bounded fallback.
            }
        }
    }

    $remaining = @(
        Wait-ForExactInstalledInstanceExit `
            -ExecutablePath $ExecutablePath `
            -TimeoutSeconds 2)
    foreach ($process in $remaining) {
        if ($InternalTestMode) {
            Add-TestAction -Action 'StopProcess' -Properties @{
                ProcessId = $process.ProcessId
                ExecutablePath = $process.ExecutablePath
            }
        }
        else {
            Stop-Process -Id $process.ProcessId -Force -ErrorAction Stop
        }
    }

    $stillRunning = @(
        Wait-ForExactInstalledInstanceExit `
            -ExecutablePath $ExecutablePath `
            -TimeoutSeconds 10)
    if (-not $InternalTestMode -and $stillRunning.Count -gt 0) {
        $processIds = $stillRunning.ProcessId -join ', '
        throw "Timed out waiting for exact installed process to exit: $processIds"
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($LocalAppDataRoot)) {
        $LocalAppDataRoot = [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)
    }

    $localRoot = Get-NormalizedPath $LocalAppDataRoot
    if ($InternalTestMode) {
        $systemTemp = Get-NormalizedPath ([System.IO.Path]::GetTempPath())
        if (-not $localRoot.StartsWith(
            $systemTemp,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            throw (
                'Internal test LocalAppData must stay inside the system ' +
                'temporary directory.')
        }
    }
    elseif (-not [string]::IsNullOrWhiteSpace(
            $InternalProcessSnapshotPath) -or
        -not [string]::IsNullOrWhiteSpace($InternalActionLogPath) -or
        $InternalSkipShutdownSignal) {
        throw 'Internal lifecycle hooks require -InternalTestMode.'
    }

    $target = Get-ValidatedInstallTarget `
        -InstallPath $InstallPath `
        -LocalAppDataRoot $localRoot
    $executable = Join-Path $target 'CodexQuotaHud.App.exe'
    $backup = $null
    if (-not [string]::IsNullOrWhiteSpace($LegacyBackupPath)) {
        $backup = Get-ValidatedLegacyBackupTarget `
            -BackupPath $LegacyBackupPath `
            -LocalAppDataRoot $localRoot
    }

    switch ($Action) {
        'PrepareInstall' {
            Stop-ExactInstalledInstance -ExecutablePath $executable
            if ($null -ne $backup) {
                Copy-LegacyInstallToBackup `
                    -InstallPath $target `
                    -BackupPath $backup
            }
        }
        'PrepareUninstall' {
            Stop-ExactInstalledInstance -ExecutablePath $executable
        }
        'CommitInstall' {
            if ($null -ne $backup) {
                Remove-LegacyInstallBackup `
                    -InstallPath $target `
                    -BackupPath $backup
            }
        }
        'RollbackInstall' {
            if ($null -ne $backup) {
                Restore-LegacyInstallBackup `
                    -InstallPath $target `
                    -BackupPath $backup `
                    -LocalAppDataRoot $localRoot
            }
        }
        'PurgeSettings' {
            Remove-ValidatedSettingsDirectory `
                -LocalAppDataRoot $localRoot
        }
    }
}
finally {
    Write-TestActionLog
}
