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
        'PrepareDesignerComponentRemoval',
        'CommitDesignerComponentRemoval',
        'RollbackDesignerComponentRemoval',
        'PrepareUninstall',
        'FinalizeUninstall',
        'PurgeSettings')]
    [string] $Action,
    [Parameter(Mandatory = $true)]
    [string] $InstallPath,
    [string] $LegacyBackupPath,
    [string] $LegacyShellStatePath,
    [string] $DesignerBackupPath
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
    $designerTitle = 'Codex Quota HUD ' +
        [string]([char[]](0x76AE, 0x80A4, 0x8BBE, 0x8BA1, 0x5668))
    $desktop = [Environment]::GetFolderPath(
        [Environment+SpecialFolder]::DesktopDirectory)
    $programsMenu = Join-Path ([Environment]::GetFolderPath(
        [Environment+SpecialFolder]::StartMenu)) 'Programs'
    return [pscustomobject]@{
        NormalDesktop = Join-Path $desktop 'Codex Quota HUD.lnk'
        PreviewDesktop = Join-Path $desktop "$previewTitle.lnk"
        StartMenu = Join-Path $programsMenu 'Codex Quota HUD.lnk'
        DesignerStartMenu = Join-Path $programsMenu "$designerTitle.lnk"
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

function Close-ExactDesignerProcess {
    param([string] $Executable)

    $matches = [System.Collections.ArrayList]::new()
    foreach ($process in @(Get-Process `
        -Name 'CodexQuotaHud.SkinDesigner' `
        -ErrorAction SilentlyContinue)) {
        try {
            $handle = $process.SafeHandle
            if ($null -eq $handle -or $handle.IsInvalid -or $handle.IsClosed) {
                throw 'Process handle is unavailable.'
            }
            $path = Get-NormalizedPath $process.MainModule.FileName
        }
        catch {
            $process.Dispose()
            throw (
                'Executable path and stable handle cannot be inspected for ' +
                "matching Designer process $($process.Id).")
        }
        if (Test-PathEquals $path $Executable) {
            [void]$matches.Add($process)
        }
        else { $process.Dispose() }
    }
    try {
        foreach ($process in @($matches)) {
            [void]$process.CloseMainWindow()
        }
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        $running = [System.Collections.ArrayList]::new()
        foreach ($process in @($matches)) {
            $remaining = [Math]::Max(
                0,
                [int][Math]::Ceiling(
                    ($deadline - [DateTime]::UtcNow).TotalMilliseconds))
            if (-not $process.WaitForExit($remaining)) {
                [void]$running.Add($process.Id)
            }
        }
        if ($running.Count -gt 0) {
            throw (
                'Exact Skin Designer process is still running after a ' +
                'normal close request: ' + ($running -join ', '))
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

function Assert-DesignerShortcutSafe {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or ($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing unsafe Designer shortcut: $Path"
    }
}

function Get-DesignerMarker {
    param(
        [string] $Backup,
        [string] $Designer,
        [string] $Shortcut)
    $path = Join-Path $Backup 'CodexQuotaHud.DesignerRemoval.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Designer removal marker is missing: $path"
    }
    $item = Get-Item -LiteralPath $path -Force
    if (($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing reparse-point Designer removal marker: $path"
    }
    $marker = ConvertFrom-Json (Get-Content `
        -LiteralPath $path -Raw -Encoding UTF8)
    $properties = @($marker.PSObject.Properties.Name)
    if ($properties.Count -ne 6 -or
        $properties -notcontains 'Source' -or
        $properties -notcontains 'Destination' -or
        $properties -notcontains 'ShortcutSource' -or
        $properties -notcontains 'ShortcutExisted' -or
        $properties -notcontains 'DesignerExisted' -or
        $properties -notcontains 'State' -or
        -not ($marker.ShortcutExisted -is [bool]) -or
        -not ($marker.DesignerExisted -is [bool]) -or
        [string]$marker.State -notin @('Prepared', 'RestoreVerified') -or
        -not (Test-PathEquals ([string]$marker.Source) $Designer) -or
        -not (Test-PathEquals ([string]$marker.Destination) $Backup) -or
        -not (Test-PathEquals ([string]$marker.ShortcutSource) $Shortcut)) {
        throw 'Designer removal marker does not match the exact managed paths.'
    }
    return $marker
}

function Set-DesignerMarkerState {
    param(
        [string] $Backup,
        $Marker,
        [ValidateSet('Prepared', 'RestoreVerified')]
        [string] $State)
    $markerPath = Join-Path $Backup 'CodexQuotaHud.DesignerRemoval.json'
    $temporaryPath = Join-Path `
        $Backup `
        'CodexQuotaHud.DesignerRemoval.json.pending'
    $previousPath = Join-Path `
        $Backup `
        'CodexQuotaHud.DesignerRemoval.json.previous'
    foreach ($transactionPath in @($temporaryPath, $previousPath)) {
        if (Test-Path -LiteralPath $transactionPath) {
            $transactionItem = Get-Item `
                -LiteralPath $transactionPath `
                -Force
            if ($transactionItem.PSIsContainer -or
                ($transactionItem.Attributes -band
                    [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing unsafe Designer marker transaction: $transactionPath"
            }
        }
    }
    if (Test-Path -LiteralPath $previousPath) {
        throw 'Previous Designer removal marker transaction is unresolved.'
    }
    $Marker.State = $State
    try {
        [System.IO.File]::WriteAllText(
            $temporaryPath,
            (ConvertTo-Json $Marker -Compress),
            [System.Text.UTF8Encoding]::new($false))
        [System.IO.File]::Replace(
            $temporaryPath,
            $markerPath,
            $previousPath)
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
        if (Test-Path -LiteralPath $previousPath) {
            Remove-Item -LiteralPath $previousPath -Force
        }
    }
}

function Remove-DesignerPayloadTree {
    param([string] $Path, [string] $Boundary)
    if (-not (Test-Path -LiteralPath $Path)) { return }
    Assert-SafeTree -Path $Path -Boundary $Boundary
    foreach ($file in @(
        Get-ChildItem -LiteralPath $Path -File -Force -Recurse |
            Sort-Object -Property FullName)) {
        Remove-Item -LiteralPath $file.FullName -Force
    }
    foreach ($directory in @(
        Get-ChildItem -LiteralPath $Path -Directory -Force -Recurse |
            Sort-Object `
                -Property @{ Expression = { $_.FullName.Length } }, FullName `
                -Descending)) {
        Remove-Item -LiteralPath $directory.FullName -Force
    }
    Remove-Item -LiteralPath $Path -Force
}

function Remove-DesignerBackupPayloads {
    param([string] $Backup, [string] $Programs)
    Assert-SafeTree -Path $Backup -Boundary $Programs
    $allowedNames = @(
        'designer',
        'DesignerStartMenu.lnk',
        'CodexQuotaHud.DesignerRemoval.json',
        'CodexQuotaHud.DesignerRemoval.json.pending',
        'CodexQuotaHud.DesignerRemoval.json.previous')
    foreach ($item in @(Get-ChildItem -LiteralPath $Backup -Force)) {
        if ($item.Name -notin $allowedNames) {
            throw "Unexpected Designer removal backup item: $($item.Name)"
        }
    }

    $backupDesigner = Join-Path $Backup 'designer'
    if (Test-Path -LiteralPath $backupDesigner) {
        if (-not (Test-Path -LiteralPath $backupDesigner -PathType Container)) {
            throw 'Designer backup payload is not a directory.'
        }
        Remove-DesignerPayloadTree `
            -Path $backupDesigner `
            -Boundary $Backup
    }
    if (Test-Path -LiteralPath $backupDesigner) {
        throw 'Designer backup payload cleanup postcondition failed.'
    }

    $backupShortcut = Join-Path $Backup 'DesignerStartMenu.lnk'
    if (Test-Path -LiteralPath $backupShortcut) {
        Assert-DesignerShortcutSafe -Path $backupShortcut
        Remove-Item -LiteralPath $backupShortcut -Force
    }
    if (Test-Path -LiteralPath $backupShortcut) {
        throw 'Designer backup shortcut cleanup postcondition failed.'
    }

    foreach ($transactionLeaf in @(
        'CodexQuotaHud.DesignerRemoval.json.pending',
        'CodexQuotaHud.DesignerRemoval.json.previous')) {
        $transactionPath = Join-Path $Backup $transactionLeaf
        if (Test-Path -LiteralPath $transactionPath) {
            $transactionItem = Get-Item `
                -LiteralPath $transactionPath `
                -Force
            if ($transactionItem.PSIsContainer -or
                ($transactionItem.Attributes -band
                    [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing unsafe Designer marker transaction: $transactionPath"
            }
            Remove-Item -LiteralPath $transactionPath -Force
        }
    }

    $markerPath = Join-Path $Backup 'CodexQuotaHud.DesignerRemoval.json'
    Remove-Item -LiteralPath $markerPath -Force
    if (Test-Path -LiteralPath $markerPath) {
        throw 'Designer removal marker cleanup postcondition failed.'
    }
    if (@(Get-ChildItem -LiteralPath $Backup -Force).Count -ne 0) {
        throw 'Designer removal backup was not empty after payload cleanup.'
    }
    Remove-Item -LiteralPath $Backup -Force
}

function Assert-DesignerFileMatch {
    param([string] $Source, [string] $Destination)
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf) -or
        -not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
        throw 'Designer restoration file is missing.'
    }
    $left = Get-Item -LiteralPath $Source -Force
    $right = Get-Item -LiteralPath $Destination -Force
    $leftHash = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash
    $rightHash = (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
    if ($left.Length -ne $right.Length -or
        $left.LastWriteTimeUtc.Ticks -ne $right.LastWriteTimeUtc.Ticks -or
        $left.Attributes -ne $right.Attributes -or
        -not [string]::Equals($leftHash, $rightHash,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Designer restoration file bytes or metadata differ.'
    }
}

function Assert-DesignerDirectoryMatch {
    param([string] $Source, [string] $Destination)
    Assert-SafeTree -Path $Source -Boundary (Split-Path $Source -Parent)
    Assert-SafeTree `
        -Path $Destination `
        -Boundary (Split-Path $Destination -Parent)
    $sourceRoot = Get-NormalizedPath $Source
    $destinationRoot = Get-NormalizedPath $Destination
    $sourceItems = @(Get-ChildItem -LiteralPath $sourceRoot -Force -Recurse)
    $destinationItems = @(
        Get-ChildItem -LiteralPath $destinationRoot -Force -Recurse)
    if ($sourceItems.Count -ne $destinationItems.Count) {
        throw 'Designer restoration item count differs.'
    }
    foreach ($sourceItem in $sourceItems) {
        $relative = $sourceItem.FullName.Substring($sourceRoot.Length).TrimStart(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
        $destinationPath = Join-Path $destinationRoot $relative
        if (-not (Test-Path -LiteralPath $destinationPath)) {
            throw "Designer restoration item is missing: $relative"
        }
        $destinationItem = Get-Item -LiteralPath $destinationPath -Force
        if ([bool]$sourceItem.PSIsContainer -ne
            [bool]$destinationItem.PSIsContainer) {
            throw "Designer restoration item type differs: $relative"
        }
        if (-not $sourceItem.PSIsContainer) {
            Assert-DesignerFileMatch `
                -Source $sourceItem.FullName `
                -Destination $destinationPath
        }
    }
}

function Prepare-DesignerRemoval {
    param(
        [string] $Install,
        [string] $Backup,
        [string] $Shortcut,
        [string] $Programs)
    $designer = Get-NormalizedPath (Join-Path $Install 'designer')
    Assert-SafeTree -Path $designer -Boundary $Programs
    Assert-DesignerShortcutSafe -Path $Shortcut
    $sourceExists = Test-Path -LiteralPath $designer -PathType Container
    $shortcutExists = Test-Path -LiteralPath $Shortcut -PathType Leaf
    if (Test-Path -LiteralPath $Backup) {
        $marker = Get-DesignerMarker `
            -Backup $Backup -Designer $designer -Shortcut $Shortcut
    }
    else {
        if (-not $sourceExists -and -not $shortcutExists) { return }
        New-Item -ItemType Directory -Path $Backup | Out-Null
        $marker = [ordered]@{
            Source = $designer
            Destination = $Backup
            ShortcutSource = $Shortcut
            ShortcutExisted = [bool]$shortcutExists
            DesignerExisted = [bool]$sourceExists
            State = 'Prepared'
        }
        [System.IO.File]::WriteAllText(
            (Join-Path $Backup 'CodexQuotaHud.DesignerRemoval.json'),
            (ConvertTo-Json $marker -Compress),
            [System.Text.UTF8Encoding]::new($false))
    }
    $backupDesigner = Join-Path $Backup 'designer'
    if ($sourceExists) {
        if (-not [bool]$marker.DesignerExisted) {
            throw 'Designer payload appeared after the removal snapshot.'
        }
        if (Test-Path -LiteralPath $backupDesigner) {
            throw 'Designer removal found both source and backup payloads.'
        }
        Move-Item -LiteralPath $designer -Destination $backupDesigner
    }
    $backupShortcut = Join-Path $Backup 'DesignerStartMenu.lnk'
    if ([bool]$marker.ShortcutExisted -and $shortcutExists) {
        if (Test-Path -LiteralPath $backupShortcut) {
            throw 'Designer removal found both source and backup shortcuts.'
        }
        Move-Item -LiteralPath $Shortcut -Destination $backupShortcut
    }
    elseif (-not [bool]$marker.ShortcutExisted -and $shortcutExists) {
        throw 'Designer shortcut appeared after the removal snapshot.'
    }
    Assert-SafeTree -Path $Backup -Boundary $Programs
    if ((Test-Path -LiteralPath $designer) -or
        (Test-Path -LiteralPath $Shortcut)) {
        throw 'Designer removal postcondition failed.'
    }
}

function Restore-DesignerRemoval {
    param(
        [string] $Install,
        [string] $Backup,
        [string] $Shortcut,
        [string] $Programs)
    if (-not (Test-Path -LiteralPath $Backup)) { return }
    $designer = Get-NormalizedPath (Join-Path $Install 'designer')
    $marker = Get-DesignerMarker `
        -Backup $Backup -Designer $designer -Shortcut $Shortcut
    Assert-SafeTree -Path $Backup -Boundary $Programs
    $backupDesigner = Join-Path $Backup 'designer'
    $staging = $null
    try {
        if ([string]$marker.State -eq 'RestoreVerified') {
            if ([bool]$marker.DesignerExisted -and
                -not (Test-Path -LiteralPath $designer -PathType Container)) {
                throw 'Verified Designer restoration payload is missing.'
            }
            if (-not [bool]$marker.DesignerExisted -and
                (Test-Path -LiteralPath $designer)) {
                throw 'Unexpected Designer payload appeared after restoration.'
            }
            if ([bool]$marker.ShortcutExisted -and
                -not (Test-Path -LiteralPath $Shortcut -PathType Leaf)) {
                throw 'Verified Designer restoration shortcut is missing.'
            }
            if (-not [bool]$marker.ShortcutExisted -and
                (Test-Path -LiteralPath $Shortcut)) {
                throw 'Unexpected Designer shortcut appeared after restoration.'
            }
            Assert-SafeTree -Path $designer -Boundary $Programs
            Assert-DesignerShortcutSafe -Path $Shortcut
            Remove-DesignerBackupPayloads `
                -Backup $Backup `
                -Programs $Programs
            return
        }

        if ([bool]$marker.DesignerExisted -and
            -not (Test-Path -LiteralPath $backupDesigner -PathType Container)) {
            throw 'Designer backup payload is missing.'
        }
        if (-not [bool]$marker.DesignerExisted -and
            (Test-Path -LiteralPath $backupDesigner)) {
            throw 'Unexpected Designer backup payload was found.'
        }
        if (Test-Path -LiteralPath $backupDesigner -PathType Container) {
            if (Test-Path -LiteralPath $designer) {
                Assert-DesignerDirectoryMatch `
                    -Source $backupDesigner -Destination $designer
            }
            else {
                $staging = Get-GuidSibling `
                    -Path (Join-Path $Programs (
                        'CodexQuotaHud.designer-rollback-staging.' +
                        [Guid]::NewGuid().ToString('N'))) `
                    -Prefix 'CodexQuotaHud.designer-rollback-staging.' `
                    -Programs $Programs
                New-Item -ItemType Directory -Path $staging | Out-Null
                foreach ($item in @(
                    Get-ChildItem -LiteralPath $backupDesigner -Force)) {
                    Copy-Item -LiteralPath $item.FullName `
                        -Destination $staging -Recurse -Force
                }
                foreach ($sourceFile in @(
                    Get-ChildItem -LiteralPath $backupDesigner `
                        -File -Force -Recurse)) {
                    $relative = $sourceFile.FullName.Substring(
                        $backupDesigner.Length).TrimStart(
                            [System.IO.Path]::DirectorySeparatorChar,
                            [System.IO.Path]::AltDirectorySeparatorChar)
                    $stagedFile = Join-Path $staging $relative
                    [System.IO.File]::SetAttributes(
                        $stagedFile, [System.IO.FileAttributes]::Normal)
                    [System.IO.File]::SetLastWriteTimeUtc(
                        $stagedFile, $sourceFile.LastWriteTimeUtc)
                    [System.IO.File]::SetAttributes(
                        $stagedFile, $sourceFile.Attributes)
                }
                Assert-DesignerDirectoryMatch `
                    -Source $backupDesigner -Destination $staging
                Move-Item -LiteralPath $staging -Destination $designer
                $staging = $null
            }
        }
        $backupShortcut = Join-Path $Backup 'DesignerStartMenu.lnk'
        if ([bool]$marker.ShortcutExisted) {
            if (-not (Test-Path -LiteralPath $backupShortcut -PathType Leaf)) {
                if (-not (Test-Path -LiteralPath $Shortcut -PathType Leaf)) {
                    throw 'Designer shortcut backup is missing.'
                }
                Assert-DesignerShortcutSafe -Path $Shortcut
            }
            elseif (Test-Path -LiteralPath $Shortcut) {
                Assert-DesignerShortcutSafe -Path $backupShortcut
                Assert-DesignerShortcutSafe -Path $Shortcut
                Assert-DesignerFileMatch `
                    -Source $backupShortcut -Destination $Shortcut
            }
            else {
                Assert-DesignerShortcutSafe -Path $backupShortcut
                New-Item -ItemType Directory `
                    -Path (Split-Path $Shortcut -Parent) -Force | Out-Null
                Copy-Item -LiteralPath $backupShortcut `
                    -Destination $Shortcut -Force
                $backupItem = Get-Item -LiteralPath $backupShortcut -Force
                [System.IO.File]::SetAttributes(
                    $Shortcut, [System.IO.FileAttributes]::Normal)
                [System.IO.File]::SetLastWriteTimeUtc(
                    $Shortcut, $backupItem.LastWriteTimeUtc)
                [System.IO.File]::SetAttributes(
                    $Shortcut, $backupItem.Attributes)
                Assert-DesignerFileMatch `
                    -Source $backupShortcut -Destination $Shortcut
            }
        }
        elseif (Test-Path -LiteralPath $Shortcut) {
            throw 'Unexpected Designer shortcut appeared during restoration.'
        }

        if ([bool]$marker.DesignerExisted -and
            -not (Test-Path -LiteralPath $designer -PathType Container)) {
            throw 'Designer restoration payload postcondition failed.'
        }
        if ([bool]$marker.ShortcutExisted -and
            -not (Test-Path -LiteralPath $Shortcut -PathType Leaf)) {
            throw 'Designer restoration shortcut postcondition failed.'
        }
        Set-DesignerMarkerState `
            -Backup $Backup `
            -Marker $marker `
            -State 'RestoreVerified'
        Remove-DesignerBackupPayloads `
            -Backup $Backup `
            -Programs $Programs
    }
    finally {
        if ($null -ne $staging -and (Test-Path -LiteralPath $staging)) {
            Remove-SafeTree -Path $staging -Boundary $Programs
        }
    }
}

function Commit-DesignerRemoval {
    param(
        [string] $Install,
        [string] $Backup,
        [string] $Shortcut,
        [string] $Programs)
    if (-not (Test-Path -LiteralPath $Backup)) { return }
    $marker = Get-DesignerMarker `
        -Backup $Backup `
        -Designer (Join-Path $Install 'designer') `
        -Shortcut $Shortcut
    if (Test-Path -LiteralPath (Join-Path $Install 'designer')) {
        throw 'Designer removal commit found a restored payload.'
    }
    if (Test-Path -LiteralPath $Shortcut) {
        throw 'Designer removal commit found a restored shortcut.'
    }
    Remove-DesignerBackupPayloads -Backup $Backup -Programs $Programs
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
$designerExecutable = Join-Path `
    $install `
    'designer\CodexQuotaHud.SkinDesigner.exe'

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
$designerBackup = $null
if (-not [string]::IsNullOrWhiteSpace($DesignerBackupPath)) {
    $designerBackup = Get-GuidSibling `
        -Path $DesignerBackupPath `
        -Prefix 'CodexQuotaHud.designer-removal-backup.' `
        -Programs $programs
    Assert-SafeTree -Path $designerBackup -Boundary $localRoot
}
$designerActions = @(
    'PrepareDesignerComponentRemoval',
    'CommitDesignerComponentRemoval',
    'RollbackDesignerComponentRemoval')
if ($Action -in $designerActions -and $null -eq $designerBackup) {
    throw "$Action requires -DesignerBackupPath."
}
$designerShortcut = if ($Action -in $designerActions) {
    (Get-ShellPaths).DesignerStartMenu
}
else { $null }

switch ($Action) {
    'PrepareInstall' {
        Stop-ExactProcess -Executable $executable
        Close-ExactDesignerProcess -Executable $designerExecutable
        if ($null -ne $backup) {
            Copy-Backup -Install $install -Backup $backup -Programs $programs
        }
    }
    'PrepareDesignerComponentRemoval' {
        Close-ExactDesignerProcess -Executable $designerExecutable
        Prepare-DesignerRemoval `
            -Install $install `
            -Backup $designerBackup `
            -Shortcut $designerShortcut `
            -Programs $programs
    }
    'CommitDesignerComponentRemoval' {
        Commit-DesignerRemoval `
            -Install $install `
            -Backup $designerBackup `
            -Shortcut $designerShortcut `
            -Programs $programs
    }
    'RollbackDesignerComponentRemoval' {
        Restore-DesignerRemoval `
            -Install $install `
            -Backup $designerBackup `
            -Shortcut $designerShortcut `
            -Programs $programs
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
    'PrepareUninstall' {
        Stop-ExactProcess -Executable $executable
        Close-ExactDesignerProcess -Executable $designerExecutable
    }
    'FinalizeUninstall' {
        Remove-SafeTree -Path $install -Boundary $localRoot
    }
    'PurgeSettings' {
        $settings = Get-NormalizedPath (Join-Path $localRoot 'CodexQuotaHud')
        if (-not (Test-StrictDescendant $settings $localRoot)) {
            throw 'Settings path validation failed.'
        }
        Remove-SafeTree -Path $settings -Boundary $localRoot
    }
}
