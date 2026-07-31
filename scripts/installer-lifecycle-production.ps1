[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet(
        'PrepareInstall',
        'SnapshotLegacyState',
        'CommitInstall',
        'DiscardLegacyState',
        'CompensateLegacyInstall',
        'RollbackInstall',
        'PrepareUninstall',
        'PurgeSettings')]
    [string] $Action,
    [Parameter(Mandatory = $true)]
    [string] $InstallPath,
    [string] $LegacyBackupPath,
    [string] $LegacyShellStatePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string] $Path)
    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        throw "Path must be absolute: $Path"
    }
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

function Test-StrictDescendant {
    param([string] $Path, [string] $Boundary)
    return (Get-NormalizedPath $Path).StartsWith(
        (Get-NormalizedPath $Boundary) + '\',
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

function Assert-SafeTree {
    param([string] $Path, [string] $Boundary)
    Assert-NoReparsePoint -Path $Path -Boundary $Boundary
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return }
    $pending = [System.Collections.Queue]::new()
    $pending.Enqueue((Get-NormalizedPath $Path))
    while ($pending.Count -gt 0) {
        $directory = [string]$pending.Dequeue()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force)) {
            if (($item.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing reparse-point tree item: $($item.FullName)"
            }
            if ($item.PSIsContainer) { $pending.Enqueue($item.FullName) }
        }
    }
}

function Remove-SafeTree {
    param([string] $Path, [string] $Boundary)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Assert-SafeTree -Path $Path -Boundary $Boundary
    Remove-Item -LiteralPath $Path -Recurse -Force
}

function Get-GuidSibling {
    param([string] $Path, [string] $Prefix, [string] $Programs)
    $target = Get-NormalizedPath $Path
    if (-not (Test-PathEquals (Split-Path $target -Parent) $Programs)) {
        throw "Lifecycle path must stay directly under Programs: $Programs"
    }
    $leaf = Split-Path $target -Leaf
    if (-not $leaf.StartsWith($Prefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Lifecycle path must use prefix $Prefix"
    }
    [void][Guid]::Parse($leaf.Substring($Prefix.Length))
    return $target
}

function Get-ShellPaths {
    $previewTitle = 'Codex Quota HUD ' +
        [string]([char[]](0x5F00, 0x53D1, 0x9884, 0x89C8))
    $desktop = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::DesktopDirectory)
    $programsMenu = Join-Path ([Environment]::GetFolderPath(
        [Environment+SpecialFolder]::StartMenu)) 'Programs'
    return [pscustomobject]@{
        NormalDesktop = Join-Path $desktop 'Codex Quota HUD.lnk'
        PreviewDesktop = Join-Path $desktop "$previewTitle.lnk"
        StartMenu = Join-Path $programsMenu 'Codex Quota HUD.lnk'
    }
}

function Stop-ExactProcess {
    param([string] $Executable)
    try {
        $event = [System.Threading.EventWaitHandle]::OpenExisting(
            'Local\CodexQuotaHud.ShutdownRequested')
        try { [void]$event.Set() } finally { $event.Dispose() }
    }
    catch [System.Threading.WaitHandleCannotBeOpenedException] { }

    $matches = [System.Collections.ArrayList]::new()
    foreach ($process in @(Get-Process -Name 'CodexQuotaHud.App' `
        -ErrorAction SilentlyContinue)) {
        try { $path = Get-NormalizedPath $process.MainModule.FileName }
        catch { $process.Dispose(); throw }
        if (Test-PathEquals $path $Executable) {
            [void]$matches.Add($process)
        }
        else { $process.Dispose() }
    }
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(2)
        foreach ($process in @($matches)) {
            $remaining = [Math]::Max(0, [int](
                ($deadline - [DateTime]::UtcNow).TotalMilliseconds))
            if (-not $process.WaitForExit($remaining)) { $process.Kill() }
        }
        foreach ($process in @($matches)) {
            if (-not $process.WaitForExit(10000)) {
                throw "Timed out stopping process $($process.Id)."
            }
        }
    }
    finally { foreach ($process in @($matches)) { $process.Dispose() } }
}

function Copy-Backup {
    param([string] $Install, [string] $Backup, [string] $Programs)
    if (-not (Test-Path -LiteralPath $Install -PathType Container)) { return }
    if (Test-Path -LiteralPath $Backup) {
        throw "Legacy backup already exists: $Backup"
    }
    Assert-SafeTree -Path $Install -Boundary $Programs
    New-Item -ItemType Directory -Path $Backup | Out-Null
    try {
        Get-ChildItem -LiteralPath $Install -Force |
            Copy-Item -Destination $Backup -Recurse -Force
        Assert-SafeTree -Path $Backup -Boundary $Programs
    }
    catch { Remove-SafeTree -Path $Backup -Boundary $Programs; throw }
}

function Snapshot-Shell {
    param([string] $State, [string] $Programs)
    if (Test-Path -LiteralPath $State) {
        throw "Legacy shell state already exists: $State"
    }
    $shell = Get-ShellPaths
    New-Item -ItemType Directory -Path $State | Out-Null
    $marker = [ordered]@{}
    foreach ($entry in @(
        @('NormalDesktop', $shell.NormalDesktop),
        @('PreviewDesktop', $shell.PreviewDesktop),
        @('StartMenu', $shell.StartMenu))) {
        $exists = Test-Path -LiteralPath $entry[1] -PathType Leaf
        $marker["$($entry[0])Exists"] = $exists
        if ($exists) {
            $item = Get-Item -LiteralPath $entry[1] -Force
            if (($item.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing unsafe shortcut: $($entry[1])"
            }
            Copy-Item -LiteralPath $entry[1] `
                -Destination (Join-Path $State "$($entry[0]).lnk")
        }
    }
    $runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Run', $false)
    try {
        $runValue = if ($null -ne $runKey) {
            $runKey.GetValue('CodexQuotaHud', $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
        else { $null }
    }
    finally { if ($null -ne $runKey) { $runKey.Dispose() } }
    $marker.RunValueExists = $null -ne $runValue
    $marker.RunValue = if ($null -ne $runValue) { [string]$runValue } else { '' }
    [System.IO.File]::WriteAllText(
        (Join-Path $State 'state.json'),
        (ConvertTo-Json $marker -Compress),
        [System.Text.UTF8Encoding]::new($false))
    Assert-SafeTree -Path $State -Boundary $Programs
}

function Remove-ManagedShortcutChecked {
    param([Parameter(Mandatory = $true)][string] $Path)
    if (-not (Test-Path -LiteralPath $Path -ErrorAction Stop)) { return }
    $item = Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    if ($item.PSIsContainer -or ($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing unsafe managed shortcut path: $Path"
    }
    Remove-Item -LiteralPath $Path -Force -ErrorAction Stop
    if (Test-Path -LiteralPath $Path -ErrorAction Stop) {
        throw "Managed shortcut cleanup postcondition failed: $Path"
    }
}

function Remove-StartupValueChecked {
    param([Parameter(Mandatory = $true)][string] $ValueName)
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Run',
        $true)
    if ($null -eq $key) { return }
    try {
        if ($ValueName -in $key.GetValueNames()) {
            $key.DeleteValue($ValueName, $true)
        }
        if ($ValueName -in $key.GetValueNames()) {
            throw "Startup value cleanup postcondition failed: $ValueName"
        }
    }
    finally { $key.Dispose() }
}

function Set-StartupValueChecked {
    param(
        [Parameter(Mandatory = $true)][string] $ValueName,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $Value)
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        $key.SetValue($ValueName, $Value,
            [Microsoft.Win32.RegistryValueKind]::String)
        $actual = [string]$key.GetValue(
            $ValueName,
            $null,
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if (-not [string]::Equals(
            $actual,
            $Value,
            [System.StringComparison]::Ordinal)) {
            throw "Startup value restore postcondition failed: $ValueName"
        }
    }
    finally { $key.Dispose() }
}

function Remove-UninstallKeyChecked {
    param([Parameter(Mandatory = $true)][string] $SubKeyName)
    $relative =
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\' + $SubKeyName
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($relative, $false)
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($relative, $false)
    try {
        if ($null -ne $key) {
            throw "Uninstall key cleanup postcondition failed: $SubKeyName"
        }
    }
    finally { if ($null -ne $key) { $key.Dispose() } }
}

function Remove-NewUninstallerFilesChecked {
    param([Parameter(Mandatory = $true)][string] $InstallPath)
    if (-not (Test-Path -LiteralPath $InstallPath -PathType Container `
        -ErrorAction Stop)) { return }
    foreach ($file in @(Get-ChildItem -LiteralPath $InstallPath -File -Force `
        -ErrorAction Stop)) {
        if ($file.Name -match '^unins\d{3}\.(exe|dat|msg)$') {
            Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop
            if (Test-Path -LiteralPath $file.FullName -ErrorAction Stop) {
                throw "Uninstaller file cleanup postcondition failed: $($file.FullName)"
            }
        }
    }
}

function Restore-ManagedShortcutChecked {
    param(
        [Parameter(Mandatory = $true)][bool] $Existed,
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $Destination)
    if (-not $Existed) {
        Remove-ManagedShortcutChecked -Path $Destination
        return
    }
    if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf `
        -ErrorAction Stop)) {
        throw "Legacy shortcut backup is missing: $BackupPath"
    }
    $backup = Get-Item -LiteralPath $BackupPath -Force -ErrorAction Stop
    if (($backup.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing unsafe legacy shortcut backup: $BackupPath"
    }
    $parent = Split-Path -Path $Destination -Parent
    New-Item -ItemType Directory -Path $parent -Force -ErrorAction Stop |
        Out-Null
    Copy-Item -LiteralPath $BackupPath -Destination $Destination -Force `
        -ErrorAction Stop
    if (-not (Test-Path -LiteralPath $Destination -PathType Leaf `
        -ErrorAction Stop)) {
        throw "Legacy shortcut restore postcondition failed: $Destination"
    }
}

function Invoke-ProductionCompensation {
    param(
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $StatePath,
        [Parameter(Mandatory = $true)][string] $Programs,
        [Parameter(Mandatory = $true)][psobject] $Shell,
        [Parameter(Mandatory = $true)][string] $RunValueName,
        [Parameter(Mandatory = $true)][string] $UninstallSubKeyName)

    if (-not (Test-Path -LiteralPath $StatePath -PathType Container `
        -ErrorAction Stop)) { return }
    Assert-SafeTree -Path $StatePath -Boundary $Programs
    $markerPath = Join-Path $StatePath 'state.json'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf `
        -ErrorAction Stop)) {
        throw "Legacy shell marker is missing: $markerPath"
    }
    $marker = ConvertFrom-Json (Get-Content -LiteralPath $markerPath `
        -Raw -Encoding UTF8 -ErrorAction Stop)
    $errors = [System.Collections.ArrayList]::new()

    foreach ($path in @(
        $Shell.NormalDesktop,
        $Shell.PreviewDesktop,
        $Shell.StartMenu)) {
        try { Remove-ManagedShortcutChecked -Path $path }
        catch { [void]$errors.Add($_.Exception.Message) }
    }
    try { Remove-StartupValueChecked -ValueName $RunValueName }
    catch { [void]$errors.Add($_.Exception.Message) }

    foreach ($entry in @(
        @('NormalDesktop', $Shell.NormalDesktop),
        @('PreviewDesktop', $Shell.PreviewDesktop),
        @('StartMenu', $Shell.StartMenu))) {
        try {
            Restore-ManagedShortcutChecked `
                -Existed ([bool]$marker."$($entry[0])Exists") `
                -BackupPath (Join-Path $StatePath "$($entry[0]).lnk") `
                -Destination $entry[1]
        }
        catch { [void]$errors.Add($_.Exception.Message) }
    }
    if ([bool]$marker.RunValueExists) {
        try {
            Set-StartupValueChecked `
                -ValueName $RunValueName `
                -Value ([string]$marker.RunValue)
        }
        catch { [void]$errors.Add($_.Exception.Message) }
    }
    try { Remove-UninstallKeyChecked -SubKeyName $UninstallSubKeyName }
    catch { [void]$errors.Add($_.Exception.Message) }
    try { Remove-NewUninstallerFilesChecked -InstallPath $InstallPath }
    catch { [void]$errors.Add($_.Exception.Message) }

    if ($errors.Count -gt 0) {
        throw ('Legacy compensation failed; recovery snapshot retained: ' +
            ($errors -join ' | '))
    }
    Remove-SafeTree -Path $StatePath -Boundary $Programs
}

$localRoot = Get-NormalizedPath ([Environment]::GetFolderPath(
    [Environment+SpecialFolder]::LocalApplicationData))
$programs = Get-NormalizedPath (Join-Path $localRoot 'Programs')
$install = Get-NormalizedPath $InstallPath
$expectedInstall = Get-NormalizedPath (Join-Path $programs 'CodexQuotaHud')
if (-not (Test-PathEquals $install $expectedInstall)) {
    throw "Install target must be exactly: $expectedInstall"
}
Assert-NoReparsePoint -Path $install -Boundary $localRoot
$executable = Join-Path $install 'CodexQuotaHud.App.exe'

$backup = $null
if (-not [string]::IsNullOrWhiteSpace($LegacyBackupPath)) {
    $backup = Get-GuidSibling -Path $LegacyBackupPath `
        -Prefix 'CodexQuotaHud.legacy-backup.' -Programs $programs
}
$state = $null
if (-not [string]::IsNullOrWhiteSpace($LegacyShellStatePath)) {
    $state = Get-GuidSibling -Path $LegacyShellStatePath `
        -Prefix 'CodexQuotaHud.legacy-shell-state.' -Programs $programs
}

switch ($Action) {
    'PrepareInstall' {
        Stop-ExactProcess -Executable $executable
        if ($null -ne $backup) {
            Copy-Backup -Install $install -Backup $backup -Programs $programs
        }
    }
    'SnapshotLegacyState' {
        if ($null -eq $state) { throw 'Shell state path is required.' }
        Snapshot-Shell -State $state -Programs $programs
    }
    'CommitInstall' {
        if ($null -ne $backup) {
            Remove-SafeTree -Path $backup -Boundary $programs
        }
    }
    'DiscardLegacyState' {
        if ($null -eq $state) { throw 'Shell state path is required.' }
        Remove-SafeTree -Path $state -Boundary $programs
    }
    'CompensateLegacyInstall' {
        if ($null -eq $state) { throw 'Shell state path is required.' }
        Invoke-ProductionCompensation `
            -InstallPath $install `
            -StatePath $state `
            -Programs $programs `
            -Shell (Get-ShellPaths) `
            -RunValueName 'CodexQuotaHud' `
            -UninstallSubKeyName (
                '{7F6E38C7-5928-4A18-9C9B-9B6D9B90D314}_is1')
    }
    'RollbackInstall' {
        if ($null -ne $backup -and (Test-Path -LiteralPath $backup)) {
            Remove-SafeTree -Path $install -Boundary $localRoot
            Move-Item -LiteralPath $backup -Destination $install
        }
    }
    'PrepareUninstall' { Stop-ExactProcess -Executable $executable }
    'PurgeSettings' {
        $settings = Get-NormalizedPath (Join-Path $localRoot 'CodexQuotaHud')
        if (-not (Test-StrictDescendant $settings $localRoot)) {
            throw 'Settings path validation failed.'
        }
        Remove-SafeTree -Path $settings -Boundary $localRoot
    }
}
