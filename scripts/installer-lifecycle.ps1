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
    [string] $LocalAppDataRoot,
    [string] $LegacyBackupPath,
    [string] $LegacyShellStatePath,
    [string] $DesignerBackupPath,
    [switch] $InternalTestMode,
    [string] $InternalProcessSnapshotPath,
    [string] $InternalActionLogPath,
    [switch] $InternalSkipShutdownSignal,
    [int] $InternalRollbackCopyFailureAfterItemCount,
    [string] $InternalPrepareBackupFailureReparseTargetPath,
    [string] $InternalDesignerFailureStage,
    [string] $InternalCleanupFailureStage,
    [string] $InternalShellRootPath,
    [string] $InternalErrorDiagnosticPath,
    [switch] $InternalCurrentRunValueExists,
    [AllowEmptyString()]
    [string] $InternalCurrentRunValue
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:TestActions = [System.Collections.ArrayList]::new()
$script:ValidatedInternalProcessSnapshotPath = $null
$script:ValidatedInternalActionLogPath = $null
$script:ValidatedBackupFailureTarget = $null
$script:ValidatedInternalShellRootPath = $null
$script:ValidatedInternalErrorDiagnosticPath = $null
$script:ValidatedInternalTestRoot = $null

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

function Test-PathIsStrictDescendant {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Boundary)

    $candidate = Get-NormalizedPath $Path
    $parent = Get-NormalizedPath $Boundary
    $prefix = $parent +
        [System.IO.Path]::DirectorySeparatorChar
    return $candidate.StartsWith(
        $prefix,
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

function Remove-DirectoryTreeWithoutFollowingReparsePoints {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Boundary)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Assert-NoReparsePoint -Path $Path -Boundary $Boundary
    foreach ($item in @(
        Get-ChildItem -LiteralPath $Path -Force)) {
        if (($item.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            if ($item.PSIsContainer) {
                [System.IO.Directory]::Delete($item.FullName)
            }
            else {
                [System.IO.File]::Delete($item.FullName)
            }
        }
        elseif ($item.PSIsContainer) {
            Remove-DirectoryTreeWithoutFollowingReparsePoints `
                -Path $item.FullName `
                -Boundary $Boundary
        }
        else {
            Remove-Item -LiteralPath $item.FullName -Force
        }
    }

    Remove-Item -LiteralPath $Path -Force
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

function Get-ValidatedLegacyShellStateTarget {
    param(
        [Parameter(Mandatory = $true)][string] $StatePath,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    $localRoot = Get-NormalizedPath $LocalAppDataRoot
    $programs = Get-NormalizedPath (Join-Path $localRoot 'Programs')
    $target = Get-NormalizedPath $StatePath
    $parent = Get-NormalizedPath (Split-Path -Path $target -Parent)
    if (-not (Test-PathEquals $parent $programs)) {
        throw "Legacy shell state must stay directly under Programs: $programs"
    }

    $prefix = 'CodexQuotaHud.legacy-shell-state.'
    $leaf = Split-Path -Path $target -Leaf
    if (-not $leaf.StartsWith(
            $prefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Legacy shell state must use the exact $prefix prefix."
    }

    $suffix = $leaf.Substring($prefix.Length)
    try {
        [void][Guid]::Parse($suffix)
    }
    catch {
        throw 'Legacy shell state suffix must be a GUID.'
    }

    Assert-NoReparsePointTree -Path $target -Boundary $localRoot
    Assert-ResolvedPathEqualsExpected -Path $target
    return $target
}

function Get-ValidatedDesignerBackupTarget {
    param(
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    $localRoot = Get-NormalizedPath $LocalAppDataRoot
    $programs = Get-NormalizedPath (Join-Path $localRoot 'Programs')
    $target = Get-NormalizedPath $BackupPath
    $parent = Get-NormalizedPath (Split-Path -Path $target -Parent)
    if (-not (Test-PathEquals $parent $programs)) {
        throw "Designer removal backup must stay directly under Programs: $programs"
    }

    $prefix = 'CodexQuotaHud.designer-removal-backup.'
    $leaf = Split-Path -Path $target -Leaf
    if (-not $leaf.StartsWith(
            $prefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Designer removal backup must use the exact $prefix prefix."
    }

    try {
        [void][Guid]::Parse($leaf.Substring($prefix.Length))
    }
    catch {
        throw 'Designer removal backup suffix must be a GUID.'
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
        if ($InternalTestMode -and
            -not [string]::IsNullOrWhiteSpace(
                $script:ValidatedBackupFailureTarget)) {
            $injectedPath = Join-Path $BackupPath 'injected-reparse'
            New-Item `
                -ItemType Junction `
                -Path $injectedPath `
                -Target $script:ValidatedBackupFailureTarget |
                Out-Null
            Add-TestAction -Action 'InjectBackupReparse' -Properties @{
                Source = $script:ValidatedBackupFailureTarget
                Destination = $injectedPath
            }
            throw 'Injected legacy backup copy failure.'
        }

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
            Remove-DirectoryTreeWithoutFollowingReparsePoints `
                -Path $BackupPath `
                -Boundary (Split-Path -Path $BackupPath -Parent)
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

function Get-ValidatedRollbackSiblingTarget {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot,
        [Parameter(Mandatory = $true)][string] $Prefix)

    $localRoot = Get-NormalizedPath $LocalAppDataRoot
    $programs = Get-NormalizedPath (Join-Path $localRoot 'Programs')
    $target = Get-NormalizedPath $Path
    $parent = Get-NormalizedPath (Split-Path -Path $target -Parent)
    if (-not (Test-PathEquals $parent $programs)) {
        throw "Rollback path must stay directly under Programs: $programs"
    }

    $leaf = Split-Path -Path $target -Leaf
    if (-not $leaf.StartsWith(
            $Prefix,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Rollback path must use the exact $Prefix prefix."
    }

    $suffix = $leaf.Substring($Prefix.Length)
    try {
        [void][Guid]::Parse($suffix)
    }
    catch {
        throw 'Rollback path suffix must be a GUID.'
    }

    Assert-NoReparsePointTree -Path $target -Boundary $localRoot
    Assert-ResolvedPathEqualsExpected -Path $target
    return $target
}

function Assert-DirectoryCopiesMatch {
    param(
        [Parameter(Mandatory = $true)][string] $Source,
        [Parameter(Mandatory = $true)][string] $Destination)

    Assert-NoReparsePointTree `
        -Path $Source `
        -Boundary (Split-Path -Path $Source -Parent)
    Assert-NoReparsePointTree `
        -Path $Destination `
        -Boundary (Split-Path -Path $Destination -Parent)

    $sourceRoot = Get-NormalizedPath $Source
    $destinationRoot = Get-NormalizedPath $Destination
    $sourceItems = @(
        Get-ChildItem -LiteralPath $sourceRoot -Force -Recurse)
    $destinationItems = @(
        Get-ChildItem -LiteralPath $destinationRoot -Force -Recurse)
    if ($sourceItems.Count -ne $destinationItems.Count) {
        throw 'Rollback staging copy item count does not match the backup.'
    }

    foreach ($sourceItem in $sourceItems) {
        $relativePath = $sourceItem.FullName.Substring(
            $sourceRoot.Length).TrimStart(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar)
        $destinationPath = Join-Path $destinationRoot $relativePath
        if (-not (Test-Path -LiteralPath $destinationPath)) {
            throw "Rollback staging copy is missing: $relativePath"
        }

        $destinationItem = Get-Item -LiteralPath $destinationPath -Force
        if ([bool]$sourceItem.PSIsContainer -ne
            [bool]$destinationItem.PSIsContainer) {
            throw "Rollback staging item type differs: $relativePath"
        }

        if (-not $sourceItem.PSIsContainer) {
            if ($sourceItem.Length -ne $destinationItem.Length) {
                throw "Rollback staging file length differs: $relativePath"
            }

            $sourceHash = (Get-FileHash `
                -LiteralPath $sourceItem.FullName `
                -Algorithm SHA256).Hash
            $destinationHash = (Get-FileHash `
                -LiteralPath $destinationPath `
                -Algorithm SHA256).Hash
            if (-not [string]::Equals(
                    $sourceHash,
                    $destinationHash,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Rollback staging file content differs: $relativePath"
            }
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
    $programs = Get-NormalizedPath (Split-Path -Path $InstallPath -Parent)
    $suffix = [Guid]::NewGuid().ToString('N')
    $staging = Get-ValidatedRollbackSiblingTarget `
        -Path (Join-Path `
            $programs `
            "CodexQuotaHud.rollback-staging.$suffix") `
        -LocalAppDataRoot $LocalAppDataRoot `
        -Prefix 'CodexQuotaHud.rollback-staging.'
    $displaced = Get-ValidatedRollbackSiblingTarget `
        -Path (Join-Path `
            $programs `
            "CodexQuotaHud.rollback-displaced.$suffix") `
        -LocalAppDataRoot $LocalAppDataRoot `
        -Prefix 'CodexQuotaHud.rollback-displaced.'
    $stagingExists = $false
    $targetDisplaced = $false
    $stagingActivated = $false

    try {
        New-Item -ItemType Directory -Path $staging | Out-Null
        $stagingExists = $true
        $copiedItemCount = 0
        foreach ($item in @(
            Get-ChildItem -LiteralPath $BackupPath -Force |
                Sort-Object -Property Name)) {
            Copy-Item `
                -LiteralPath $item.FullName `
                -Destination $staging `
                -Recurse `
                -Force
            $copiedItemCount++
            if ($InternalTestMode) {
                Add-TestAction -Action 'StageRollbackCopy' -Properties @{
                    Source = $item.FullName
                    Destination = $staging
                    ItemCount = $copiedItemCount
                }
            }

            if ($InternalTestMode -and
                $InternalRollbackCopyFailureAfterItemCount -gt 0 -and
                $copiedItemCount -ge
                    $InternalRollbackCopyFailureAfterItemCount) {
                throw 'Injected rollback copy failure.'
            }
        }

        Assert-DirectoryCopiesMatch `
            -Source $BackupPath `
            -Destination $staging
        Remove-Item `
            -LiteralPath (Join-Path `
                $staging `
                'CodexQuotaHud.LegacyBackup.json') `
            -Force

        if (Test-Path -LiteralPath $InstallPath) {
            [void](Get-ValidatedInstallTarget `
                -InstallPath $InstallPath `
                -LocalAppDataRoot $LocalAppDataRoot)
            Move-Item -LiteralPath $InstallPath -Destination $displaced
            $targetDisplaced = $true
        }

        Move-Item -LiteralPath $staging -Destination $InstallPath
        $stagingExists = $false
        $stagingActivated = $true

        if ($targetDisplaced) {
            Assert-NoReparsePointTree `
                -Path $displaced `
                -Boundary $programs
            Remove-Item -LiteralPath $displaced -Recurse -Force
            $targetDisplaced = $false
        }

        Assert-NoReparsePointTree `
            -Path $BackupPath `
            -Boundary $programs
        Remove-Item -LiteralPath $BackupPath -Recurse -Force
    }
    catch {
        if ($targetDisplaced -and
            -not $stagingActivated -and
            -not (Test-Path -LiteralPath $InstallPath) -and
            (Test-Path -LiteralPath $displaced)) {
            Move-Item -LiteralPath $displaced -Destination $InstallPath
            $targetDisplaced = $false
        }

        if ($stagingExists -and (Test-Path -LiteralPath $staging)) {
            Assert-NoReparsePointTree `
                -Path $staging `
                -Boundary $programs
            Remove-Item -LiteralPath $staging -Recurse -Force
        }

        throw
    }

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
    if ($InternalCleanupFailureStage -eq 'LegacyCommit') {
        if ($InternalTestMode) {
            Add-TestAction -Action 'LegacyCleanupFailureAfterValidation' `
                -Properties @{ Destination = $BackupPath }
        }
        throw 'Injected legacy cleanup failure after marker validation.'
    }
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

function Get-ValidatedDesignerRemovalMarker {
    param(
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $DesignerPath,
        [Parameter(Mandatory = $true)][string] $ShortcutPath)

    $markerPath = Join-Path `
        $BackupPath `
        'CodexQuotaHud.DesignerRemoval.json'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "Designer removal marker is missing: $markerPath"
    }
    $markerItem = Get-Item -LiteralPath $markerPath -Force
    if (($markerItem.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing reparse-point Designer removal marker: $markerPath"
    }

    $marker = Get-Content `
        -LiteralPath $markerPath `
        -Raw `
        -Encoding UTF8 |
        ConvertFrom-Json
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
        -not (Test-PathEquals ([string]$marker.Source) $DesignerPath) -or
        -not (Test-PathEquals ([string]$marker.Destination) $BackupPath) -or
        -not (Test-PathEquals ([string]$marker.ShortcutSource) $ShortcutPath)) {
        throw 'Designer removal marker does not match the exact managed paths.'
    }

    return $marker
}

function Set-DesignerRemovalMarkerState {
    param(
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)] $Marker,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Prepared', 'RestoreVerified')]
        [string] $State)

    $markerPath = Join-Path `
        $BackupPath `
        'CodexQuotaHud.DesignerRemoval.json'
    $temporaryPath = Join-Path `
        $BackupPath `
        'CodexQuotaHud.DesignerRemoval.json.pending'
    $previousPath = Join-Path `
        $BackupPath `
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
            (ConvertTo-Json -InputObject $Marker -Compress),
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

function Remove-DesignerPayloadTreeDeterministically {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Boundary)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    Assert-NoReparsePointTree -Path $Path -Boundary $Boundary
    $removedFileCount = 0
    foreach ($file in @(
        Get-ChildItem -LiteralPath $Path -File -Force -Recurse |
            Sort-Object -Property FullName)) {
        Remove-Item -LiteralPath $file.FullName -Force
        $removedFileCount++
        if ($InternalCleanupFailureStage -eq
            'DesignerAfterPayloadDelete') {
            if ($InternalTestMode) {
                Add-TestAction `
                    -Action 'DesignerCleanupFailureAfterPayloadDelete' `
                    -Properties @{
                        Destination = $file.FullName
                        RemovedFileCount = $removedFileCount
                    }
            }
            throw (
                'Injected Designer cleanup failure after payload deletion.')
        }
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

function Remove-DesignerRemovalBackupPayloads {
    param(
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)] $Marker)

    $boundary = Split-Path -Path $BackupPath -Parent
    Assert-NoReparsePointTree -Path $BackupPath -Boundary $boundary
    $allowedNames = @(
        'designer',
        'DesignerStartMenu.lnk',
        'CodexQuotaHud.DesignerRemoval.json',
        'CodexQuotaHud.DesignerRemoval.json.pending',
        'CodexQuotaHud.DesignerRemoval.json.previous')
    foreach ($item in @(Get-ChildItem -LiteralPath $BackupPath -Force)) {
        if ($item.Name -notin $allowedNames) {
            throw "Unexpected Designer removal backup item: $($item.Name)"
        }
    }

    $backupDesigner = Join-Path $BackupPath 'designer'
    if (Test-Path -LiteralPath $backupDesigner) {
        if (-not (Test-Path -LiteralPath $backupDesigner -PathType Container)) {
            throw 'Designer backup payload is not a directory.'
        }
        Remove-DesignerPayloadTreeDeterministically `
            -Path $backupDesigner `
            -Boundary $BackupPath
    }
    if (Test-Path -LiteralPath $backupDesigner) {
        throw 'Designer backup payload cleanup postcondition failed.'
    }

    $backupShortcut = Join-Path $BackupPath 'DesignerStartMenu.lnk'
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
        $transactionPath = Join-Path $BackupPath $transactionLeaf
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

    $markerPath = Join-Path `
        $BackupPath `
        'CodexQuotaHud.DesignerRemoval.json'
    Remove-Item -LiteralPath $markerPath -Force
    if (Test-Path -LiteralPath $markerPath) {
        throw 'Designer removal marker cleanup postcondition failed.'
    }
    if (@(Get-ChildItem -LiteralPath $BackupPath -Force).Count -ne 0) {
        throw 'Designer removal backup was not empty after payload cleanup.'
    }
    Remove-Item -LiteralPath $BackupPath -Force
}

function Assert-DesignerShortcutSafe {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or ($item.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing unsafe Designer shortcut: $Path"
    }
}

function Assert-DesignerFilesMatch {
    param(
        [Parameter(Mandatory = $true)][string] $Source,
        [Parameter(Mandatory = $true)][string] $Destination)

    if (-not (Test-Path -LiteralPath $Source -PathType Leaf) -or
        -not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
        throw 'Designer restoration file is missing.'
    }
    $sourceItem = Get-Item -LiteralPath $Source -Force
    $destinationItem = Get-Item -LiteralPath $Destination -Force
    $sourceHash = (Get-FileHash `
        -LiteralPath $Source `
        -Algorithm SHA256).Hash
    $destinationHash = (Get-FileHash `
        -LiteralPath $Destination `
        -Algorithm SHA256).Hash
    if ($sourceItem.Length -ne $destinationItem.Length -or
        $sourceItem.LastWriteTimeUtc.Ticks -ne
            $destinationItem.LastWriteTimeUtc.Ticks -or
        $sourceItem.Attributes -ne $destinationItem.Attributes -or
        -not [string]::Equals(
            $sourceHash,
            $destinationHash,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Designer restoration file bytes or metadata differ.'
    }
}

function Assert-DesignerDirectoriesMatch {
    param(
        [Parameter(Mandatory = $true)][string] $Source,
        [Parameter(Mandatory = $true)][string] $Destination)

    Assert-DirectoryCopiesMatch -Source $Source -Destination $Destination
    $sourceRoot = Get-NormalizedPath $Source
    $destinationRoot = Get-NormalizedPath $Destination
    foreach ($sourceItem in @(
        Get-ChildItem -LiteralPath $sourceRoot -File -Force -Recurse)) {
        $relative = $sourceItem.FullName.Substring($sourceRoot.Length).TrimStart(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar)
        Assert-DesignerFilesMatch `
            -Source $sourceItem.FullName `
            -Destination (Join-Path $destinationRoot $relative)
    }
}

function Prepare-DesignerComponentRemoval {
    param(
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $ShortcutPath,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    $designer = Get-NormalizedPath (Join-Path $InstallPath 'designer')
    Assert-NoReparsePointTree -Path $designer -Boundary $LocalAppDataRoot
    Assert-DesignerShortcutSafe -Path $ShortcutPath
    $sourceExists = Test-Path -LiteralPath $designer -PathType Container
    $shortcutExists = Test-Path -LiteralPath $ShortcutPath -PathType Leaf

    if (Test-Path -LiteralPath $BackupPath) {
        $marker = Get-ValidatedDesignerRemovalMarker `
            -BackupPath $BackupPath `
            -DesignerPath $designer `
            -ShortcutPath $ShortcutPath
    }
    else {
        if (-not $sourceExists -and -not $shortcutExists) {
            return
        }
        New-Item -ItemType Directory -Path $BackupPath | Out-Null
        $marker = [ordered]@{
            Source = $designer
            Destination = $BackupPath
            ShortcutSource = $ShortcutPath
            ShortcutExisted = [bool]$shortcutExists
            DesignerExisted = [bool]$sourceExists
            State = 'Prepared'
        }
        [System.IO.File]::WriteAllText(
            (Join-Path $BackupPath 'CodexQuotaHud.DesignerRemoval.json'),
            (ConvertTo-Json -InputObject $marker -Compress),
            [System.Text.UTF8Encoding]::new($false))
    }

    $backupDesigner = Join-Path $BackupPath 'designer'
    if ($sourceExists) {
        if (-not [bool]$marker.DesignerExisted) {
            throw 'Designer payload appeared after the removal snapshot.'
        }
        if (Test-Path -LiteralPath $backupDesigner) {
            throw 'Designer removal found both source and backup payloads.'
        }
        Move-Item -LiteralPath $designer -Destination $backupDesigner
        if ($InternalDesignerFailureStage -eq 'PrepareAfterDesignerMove') {
            throw 'Injected Designer removal move failure.'
        }
    }

    $backupShortcut = Join-Path $BackupPath 'DesignerStartMenu.lnk'
    if ([bool]$marker.ShortcutExisted -and $shortcutExists) {
        if (Test-Path -LiteralPath $backupShortcut) {
            throw 'Designer removal found both source and backup shortcuts.'
        }
        Move-Item -LiteralPath $ShortcutPath -Destination $backupShortcut
        if ($InternalDesignerFailureStage -eq 'PrepareAfterShortcutMove') {
            throw 'Injected Designer shortcut move failure.'
        }
    }
    elseif (-not [bool]$marker.ShortcutExisted -and $shortcutExists) {
        throw 'Designer shortcut appeared after the removal snapshot.'
    }

    Assert-NoReparsePointTree `
        -Path $BackupPath `
        -Boundary (Split-Path -Path $BackupPath -Parent)
    if ((Test-Path -LiteralPath $designer) -or
        (Test-Path -LiteralPath $ShortcutPath)) {
        throw 'Designer removal postcondition failed.'
    }
}

function Restore-DesignerComponentRemoval {
    param(
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $ShortcutPath,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        return
    }
    $designer = Get-NormalizedPath (Join-Path $InstallPath 'designer')
    $marker = Get-ValidatedDesignerRemovalMarker `
        -BackupPath $BackupPath `
        -DesignerPath $designer `
        -ShortcutPath $ShortcutPath
    Assert-NoReparsePointTree `
        -Path $BackupPath `
        -Boundary (Split-Path -Path $BackupPath -Parent)

    $backupDesigner = Join-Path $BackupPath 'designer'
    $programs = Split-Path -Path $InstallPath -Parent
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
                -not (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
                throw 'Verified Designer restoration shortcut is missing.'
            }
            if (-not [bool]$marker.ShortcutExisted -and
                (Test-Path -LiteralPath $ShortcutPath)) {
                throw 'Unexpected Designer shortcut appeared after restoration.'
            }
            Assert-NoReparsePointTree `
                -Path $designer `
                -Boundary $LocalAppDataRoot
            Assert-DesignerShortcutSafe -Path $ShortcutPath
            Remove-DesignerRemovalBackupPayloads `
                -BackupPath $BackupPath `
                -Marker $marker
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
                Assert-DesignerDirectoriesMatch `
                    -Source $backupDesigner `
                    -Destination $designer
            }
            else {
                $staging = Get-ValidatedRollbackSiblingTarget `
                    -Path (Join-Path $programs (
                        'CodexQuotaHud.designer-rollback-staging.' +
                        [Guid]::NewGuid().ToString('N'))) `
                    -LocalAppDataRoot $LocalAppDataRoot `
                    -Prefix 'CodexQuotaHud.designer-rollback-staging.'
                New-Item -ItemType Directory -Path $staging | Out-Null
                foreach ($item in @(
                    Get-ChildItem -LiteralPath $backupDesigner -Force)) {
                    Copy-Item `
                        -LiteralPath $item.FullName `
                        -Destination $staging `
                        -Recurse `
                        -Force
                }
                foreach ($sourceFile in @(
                    Get-ChildItem `
                        -LiteralPath $backupDesigner `
                        -File `
                        -Force `
                        -Recurse)) {
                    $relative = $sourceFile.FullName.Substring(
                        $backupDesigner.Length).TrimStart(
                            [System.IO.Path]::DirectorySeparatorChar,
                            [System.IO.Path]::AltDirectorySeparatorChar)
                    $stagedFile = Join-Path $staging $relative
                    [System.IO.File]::SetAttributes(
                        $stagedFile,
                        [System.IO.FileAttributes]::Normal)
                    [System.IO.File]::SetLastWriteTimeUtc(
                        $stagedFile,
                        $sourceFile.LastWriteTimeUtc)
                    [System.IO.File]::SetAttributes(
                        $stagedFile,
                        $sourceFile.Attributes)
                }
                if ($InternalDesignerFailureStage -eq 'RollbackCopy') {
                    throw 'Injected Designer rollback copy failure.'
                }
                Assert-DesignerDirectoriesMatch `
                    -Source $backupDesigner `
                    -Destination $staging
                if ($InternalDesignerFailureStage -eq 'RollbackMove') {
                    throw 'Injected Designer rollback move failure.'
                }
                Move-Item -LiteralPath $staging -Destination $designer
                $staging = $null
            }
        }

        $backupShortcut = Join-Path $BackupPath 'DesignerStartMenu.lnk'
        if ([bool]$marker.ShortcutExisted) {
            if (-not (Test-Path -LiteralPath $backupShortcut -PathType Leaf)) {
                if (-not (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
                    throw 'Designer shortcut backup is missing.'
                }
                Assert-DesignerShortcutSafe -Path $ShortcutPath
            }
            elseif (Test-Path -LiteralPath $ShortcutPath) {
                Assert-DesignerShortcutSafe -Path $backupShortcut
                Assert-DesignerShortcutSafe -Path $ShortcutPath
                Assert-DesignerFilesMatch `
                    -Source $backupShortcut `
                    -Destination $ShortcutPath
            }
            else {
                Assert-DesignerShortcutSafe -Path $backupShortcut
                New-Item `
                    -ItemType Directory `
                    -Path (Split-Path -Path $ShortcutPath -Parent) `
                    -Force |
                    Out-Null
                Copy-Item `
                    -LiteralPath $backupShortcut `
                    -Destination $ShortcutPath `
                    -Force
                $backupItem = Get-Item -LiteralPath $backupShortcut -Force
                [System.IO.File]::SetLastWriteTimeUtc(
                    $ShortcutPath,
                    $backupItem.LastWriteTimeUtc)
                [System.IO.File]::SetAttributes(
                    $ShortcutPath,
                    $backupItem.Attributes)
                Assert-DesignerFilesMatch `
                    -Source $backupShortcut `
                    -Destination $ShortcutPath
            }
        }
        elseif (Test-Path -LiteralPath $ShortcutPath) {
            throw 'Unexpected Designer shortcut appeared during restoration.'
        }

        if ([bool]$marker.DesignerExisted -and
            -not (Test-Path -LiteralPath $designer -PathType Container)) {
            throw 'Designer restoration payload postcondition failed.'
        }
        if ([bool]$marker.ShortcutExisted -and
            -not (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
            throw 'Designer restoration shortcut postcondition failed.'
        }
        Set-DesignerRemovalMarkerState `
            -BackupPath $BackupPath `
            -Marker $marker `
            -State 'RestoreVerified'
        Remove-DesignerRemovalBackupPayloads `
            -BackupPath $BackupPath `
            -Marker $marker
    }
    finally {
        if ($null -ne $staging -and
            (Test-Path -LiteralPath $staging)) {
            Remove-DirectoryTreeWithoutFollowingReparsePoints `
                -Path $staging `
                -Boundary $programs
        }
    }
}

function Remove-DesignerComponentRemovalBackup {
    param(
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $ShortcutPath)

    if (-not (Test-Path -LiteralPath $BackupPath)) {
        return
    }
    $marker = Get-ValidatedDesignerRemovalMarker `
        -BackupPath $BackupPath `
        -DesignerPath (Join-Path $InstallPath 'designer') `
        -ShortcutPath $ShortcutPath
    Assert-NoReparsePointTree `
        -Path $BackupPath `
        -Boundary (Split-Path -Path $BackupPath -Parent)
    if ($InternalDesignerFailureStage -eq 'CommitDelete') {
        throw 'Injected Designer backup delete failure.'
    }
    if (Test-Path -LiteralPath (Join-Path $InstallPath 'designer')) {
        throw 'Designer removal commit found a restored payload.'
    }
    if (Test-Path -LiteralPath $ShortcutPath) {
        throw 'Designer removal commit found a restored shortcut.'
    }
    Remove-DesignerRemovalBackupPayloads `
        -BackupPath $BackupPath `
        -Marker $marker
}

function Get-ManagedShellPaths {
    if ($InternalTestMode) {
        $desktop = Join-Path $script:ValidatedInternalShellRootPath 'Desktop'
        $programs = Join-Path `
            $script:ValidatedInternalShellRootPath `
            'StartMenu\Programs'
    }
    else {
        $desktop = [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::DesktopDirectory)
        $programs = Join-Path `
            ([Environment]::GetFolderPath(
                [Environment+SpecialFolder]::ApplicationData)) `
            'Microsoft\Windows\Start Menu\Programs'
    }

    # Windows PowerShell 5 treats UTF-8 scripts without a BOM as ANSI. Build
    # the localized filename from code points so the helper remains portable.
    $previewSuffix = -join @(
        [char]0x5F00,
        [char]0x53D1,
        [char]0x9884,
        [char]0x89C8)
    $designerSuffix = -join @(
        [char]0x76AE,
        [char]0x80A4,
        [char]0x8BBE,
        [char]0x8BA1,
        [char]0x5668)

    return [pscustomobject]@{
        NormalDesktop = Join-Path $desktop 'Codex Quota HUD.lnk'
        PreviewDesktop = Join-Path `
            $desktop `
            ("Codex Quota HUD $previewSuffix.lnk")
        StartMenu = Join-Path $programs 'Codex Quota HUD.lnk'
        DesignerStartMenu = Join-Path `
            $programs `
            ("Codex Quota HUD $designerSuffix.lnk")
    }
}

function Get-CurrentStartupRunState {
    if ($InternalTestMode) {
        return [pscustomobject]@{
            Exists = [bool]$InternalCurrentRunValueExists
            Value = if ($InternalCurrentRunValueExists) {
                [string]$InternalCurrentRunValue
            }
            else {
                $null
            }
        }
    }

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Run',
        $false)
    if ($null -eq $key) {
        return [pscustomobject]@{ Exists = $false; Value = $null }
    }

    try {
        $exists = @($key.GetValueNames()) -contains 'CodexQuotaHud'
        $value = if ($exists) {
            [string]$key.GetValue(
                'CodexQuotaHud',
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }
        else {
            $null
        }
        return [pscustomobject]@{ Exists = $exists; Value = $value }
    }
    finally {
        $key.Dispose()
    }
}

function Assert-SnapshotSourceFileSafe {
    param([Parameter(Mandatory = $true)][string] $Path)

    $item = Get-Item -LiteralPath $Path -Force
    if ($item.PSIsContainer -or
        ($item.Attributes -band
            [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Legacy shell snapshot source must be a regular file: $Path"
    }
}

function Snapshot-LegacyShellState {
    param([Parameter(Mandatory = $true)][string] $StatePath)

    if (Test-Path -LiteralPath $StatePath) {
        throw "Legacy shell state already exists: $StatePath"
    }

    $shell = Get-ManagedShellPaths
    $run = Get-CurrentStartupRunState
    $entries = @(
        [pscustomobject]@{
            Property = 'NormalDesktopExists'
            Source = $shell.NormalDesktop
            Backup = 'NormalDesktop.lnk'
        },
        [pscustomobject]@{
            Property = 'PreviewDesktopExists'
            Source = $shell.PreviewDesktop
            Backup = 'PreviewDesktop.lnk'
        },
        [pscustomobject]@{
            Property = 'StartMenuExists'
            Source = $shell.StartMenu
            Backup = 'StartMenu.lnk'
        }
    )

    try {
        New-Item -ItemType Directory -Path $StatePath | Out-Null
        $manifest = [ordered]@{
            Version = 1
            RunValueExists = [bool]$run.Exists
            RunValue = $run.Value
        }
        foreach ($entry in $entries) {
            $exists = Test-Path -LiteralPath $entry.Source -PathType Leaf
            $manifest[$entry.Property] = [bool]$exists
            if ($exists) {
                Assert-SnapshotSourceFileSafe -Path $entry.Source
                Copy-Item `
                    -LiteralPath $entry.Source `
                    -Destination (Join-Path $StatePath $entry.Backup)
            }
        }

        $json = ConvertTo-Json -InputObject $manifest -Compress
        [System.IO.File]::WriteAllText(
            (Join-Path $StatePath 'CodexQuotaHud.LegacyShellState.json'),
            $json,
            [System.Text.UTF8Encoding]::new($false))
    }
    catch {
        if (Test-Path -LiteralPath $StatePath) {
            Remove-DirectoryTreeWithoutFollowingReparsePoints `
                -Path $StatePath `
                -Boundary (Split-Path -Path $StatePath -Parent)
        }
        throw
    }

    if ($InternalTestMode) {
        Add-TestAction -Action 'SnapshotLegacyState' -Properties @{
            Destination = $StatePath
        }
    }
}

function Remove-ExactStartupRunValue {
    if ($InternalTestMode) {
        Add-TestAction -Action 'RemoveRunValue' -Properties @{
            Name = 'CodexQuotaHud'
        }
        return
    }

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Run',
        $true)
    if ($null -eq $key) {
        return
    }

    try {
        $key.DeleteValue('CodexQuotaHud', $false)
    }
    finally {
        $key.Dispose()
    }
}

function Set-ExactStartupRunValue {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string] $Value)

    if ($InternalTestMode) {
        Add-TestAction -Action 'SetRunValue' -Properties @{
            Name = 'CodexQuotaHud'
            Value = $Value
        }
        return
    }

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey(
        'Software\Microsoft\Windows\CurrentVersion\Run')
    try {
        $key.SetValue(
            'CodexQuotaHud',
            $Value,
            [Microsoft.Win32.RegistryValueKind]::String)
    }
    finally {
        $key.Dispose()
    }
}

function Remove-ExactNewUninstallRegistration {
    $name = '{7F6E38C7-5928-4A18-9C9B-9B6D9B90D314}_is1'
    if ($InternalTestMode) {
        Add-TestAction -Action 'RemoveUninstallKey' -Properties @{
            Name = $name
        }
        return
    }

    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree(
        "Software\Microsoft\Windows\CurrentVersion\Uninstall\$name",
        $false)
}

function Remove-ExactNewUninstallerFiles {
    param([Parameter(Mandatory = $true)][string] $InstallPath)

    if (-not (Test-Path -LiteralPath $InstallPath -PathType Container)) {
        return
    }

    foreach ($file in @(Get-ChildItem -LiteralPath $InstallPath -File -Force)) {
        if ($file.Name -match '^unins\d{3}\.(exe|dat|msg)$') {
            Remove-Item -LiteralPath $file.FullName -Force
            if ($InternalTestMode) {
                Add-TestAction -Action 'RemoveUninstallerFile' -Properties @{
                    Destination = $file.FullName
                }
            }
        }
    }
}

function Restore-ManagedShortcut {
    param(
        [Parameter(Mandatory = $true)][bool] $Existed,
        [Parameter(Mandatory = $true)][string] $BackupPath,
        [Parameter(Mandatory = $true)][string] $Destination)

    if (-not $Existed) {
        if ($InternalTestMode) {
            Add-TestAction -Action 'RemoveManagedShortcut' -Properties @{
                Destination = $Destination
            }
        }
        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Force
        }
        return
    }

    if (-not (Test-Path -LiteralPath $BackupPath -PathType Leaf)) {
        throw "Legacy shell shortcut backup is missing: $BackupPath"
    }
    Assert-SnapshotSourceFileSafe -Path $BackupPath
    $parent = Split-Path -Path $Destination -Parent
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Copy-Item -LiteralPath $BackupPath -Destination $Destination -Force
}

function Compensate-LegacyInstall {
    param(
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $StatePath)

    if (-not (Test-Path -LiteralPath $StatePath)) {
        return
    }

    Assert-NoReparsePointTree `
        -Path $StatePath `
        -Boundary (Split-Path -Path $StatePath -Parent)
    $markerPath = Join-Path `
        $StatePath `
        'CodexQuotaHud.LegacyShellState.json'
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf)) {
        throw "Legacy shell state marker is missing: $markerPath"
    }
    $marker = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 |
        ConvertFrom-Json
    if ([int]$marker.Version -ne 1) {
        throw 'Legacy shell state marker version is invalid.'
    }

    $shell = Get-ManagedShellPaths
    foreach ($path in @(
        $shell.NormalDesktop,
        $shell.PreviewDesktop,
        $shell.StartMenu)) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }
    Remove-ExactStartupRunValue

    Restore-ManagedShortcut `
        -Existed ([bool]$marker.NormalDesktopExists) `
        -BackupPath (Join-Path $StatePath 'NormalDesktop.lnk') `
        -Destination $shell.NormalDesktop
    Restore-ManagedShortcut `
        -Existed ([bool]$marker.PreviewDesktopExists) `
        -BackupPath (Join-Path $StatePath 'PreviewDesktop.lnk') `
        -Destination $shell.PreviewDesktop
    Restore-ManagedShortcut `
        -Existed ([bool]$marker.StartMenuExists) `
        -BackupPath (Join-Path $StatePath 'StartMenu.lnk') `
        -Destination $shell.StartMenu
    if ([bool]$marker.RunValueExists) {
        Set-ExactStartupRunValue -Value ([string]$marker.RunValue)
    }

    Remove-ExactNewUninstallRegistration
    Remove-ExactNewUninstallerFiles -InstallPath $InstallPath
    Remove-DirectoryTreeWithoutFollowingReparsePoints `
        -Path $StatePath `
        -Boundary (Split-Path -Path $StatePath -Parent)
}

function Discard-LegacyShellState {
    param([Parameter(Mandatory = $true)][string] $StatePath)

    if (-not (Test-Path -LiteralPath $StatePath)) {
        return
    }
    Assert-NoReparsePointTree `
        -Path $StatePath `
        -Boundary (Split-Path -Path $StatePath -Parent)
    Remove-DirectoryTreeWithoutFollowingReparsePoints `
        -Path $StatePath `
        -Boundary (Split-Path -Path $StatePath -Parent)
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

function Get-ValidatedInternalHookPath {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $TestRoot,
        [switch] $MustExist)

    $target = Get-NormalizedPath $Path
    if (-not (Test-PathIsStrictDescendant `
            -Path $target `
            -Boundary $TestRoot)) {
        throw "Internal hook must stay inside the unique test directory: $TestRoot"
    }

    Assert-NoReparsePoint -Path $target -Boundary $TestRoot
    $parent = Split-Path -Path $target -Parent
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        throw "Internal hook parent directory does not exist: $parent"
    }

    if ($MustExist -and
        -not (Test-Path -LiteralPath $target -PathType Leaf)) {
        throw "Internal hook file does not exist: $target"
    }

    return $target
}

function Get-ValidatedInternalErrorDiagnosticPath {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $TestRoot)

    $target = Get-NormalizedPath $Path
    $expected = Get-NormalizedPath (
        Join-Path $TestRoot 'lifecycle-error.json')
    if (-not (Test-PathEquals $target $expected)) {
        throw (
            'Internal lifecycle diagnostic must use the fixed path inside ' +
            "the unique test directory: $expected")
    }

    Assert-NoReparsePoint -Path $target -Boundary $TestRoot
    if (Test-Path -LiteralPath $target) {
        $item = Get-Item -LiteralPath $target -Force
        if ($item.PSIsContainer) {
            throw "Internal lifecycle diagnostic must be a file: $target"
        }
    }

    return $target
}

function Write-InternalErrorDiagnostic {
    param([Parameter(Mandatory = $true)] $ErrorRecord)

    if (-not $InternalTestMode -or
        [string]::IsNullOrWhiteSpace(
            $script:ValidatedInternalErrorDiagnosticPath)) {
        return
    }

    $diagnosticPath = $script:ValidatedInternalErrorDiagnosticPath
    $testRoot = $script:ValidatedInternalTestRoot
    $systemTemp = Get-NormalizedPath ([System.IO.Path]::GetTempPath())
    Assert-NoReparsePoint -Path $testRoot -Boundary $systemTemp
    [void][System.IO.Directory]::CreateDirectory($testRoot)
    Assert-NoReparsePoint -Path $diagnosticPath -Boundary $testRoot

    $invocationInfo = ''
    if ($null -ne $ErrorRecord.InvocationInfo) {
        $invocationInfo = [string]$ErrorRecord.InvocationInfo.PositionMessage
    }
    $diagnostic = [ordered]@{
        Action = $Action
        InstallPath = $InstallPath
        LocalAppDataRoot = $LocalAppDataRoot
        InternalShellRootPath = $InternalShellRootPath
        ExceptionType = $ErrorRecord.Exception.GetType().FullName
        ExceptionMessage = $ErrorRecord.Exception.Message
        ScriptStackTrace = [string]$ErrorRecord.ScriptStackTrace
        InvocationInfo = $invocationInfo
    }
    $json = ConvertTo-Json -InputObject $diagnostic -Depth 4
    [System.IO.File]::WriteAllText(
        $diagnosticPath,
        $json,
        [System.Text.UTF8Encoding]::new($false))
}

function Get-ValidatedInternalDirectoryPath {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $TestRoot)

    $target = Get-NormalizedPath $Path
    if (-not (Test-PathIsStrictDescendant `
            -Path $target `
            -Boundary $TestRoot)) {
        throw (
            'Internal directory hook must stay inside the unique test ' +
            "directory: $TestRoot")
    }

    Assert-NoReparsePointTree -Path $target -Boundary $TestRoot
    if (-not (Test-Path -LiteralPath $target -PathType Container)) {
        throw "Internal directory hook does not exist: $target"
    }

    return $target
}

function Write-TestActionLog {
    if (-not $InternalTestMode -or
        [string]::IsNullOrWhiteSpace(
            $script:ValidatedInternalActionLogPath)) {
        return
    }

    $json = ConvertTo-Json -InputObject @($script:TestActions) -Compress
    [System.IO.File]::WriteAllText(
        $script:ValidatedInternalActionLogPath,
        $json,
        [System.Text.UTF8Encoding]::new($false))
}

function Get-CodexQuotaHudProcesses {
    if ($InternalTestMode) {
        if ([string]::IsNullOrWhiteSpace(
            $script:ValidatedInternalProcessSnapshotPath)) {
            return @()
        }

        $json = Get-Content `
            -LiteralPath $script:ValidatedInternalProcessSnapshotPath `
            -Raw `
            -Encoding UTF8
        $parsed = ConvertFrom-Json -InputObject $json
        return @($parsed | ForEach-Object { $_ })
    }

    return @(Get-Process `
        -Name 'CodexQuotaHud.App' `
        -ErrorAction SilentlyContinue)
}

function Get-ExactInstalledProcesses {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $matches = [System.Collections.ArrayList]::new()
    foreach ($process in @(Get-CodexQuotaHudProcesses)) {
        if ($InternalTestMode) {
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
                    "Executable path cannot be inspected for matching " +
                    "process $([int]$process.ProcessId).")
            }

            $processPath = try {
                Get-NormalizedPath ([string]$process.ExecutablePath)
            }
            catch {
                throw (
                    "Executable path cannot be inspected for matching " +
                    "process $([int]$process.ProcessId).")
            }

            if (Test-PathEquals $processPath $ExecutablePath) {
                if ([string]::IsNullOrWhiteSpace(
                    [string]$process.ProcessIdentity)) {
                    throw (
                        "Stable process identity cannot be inspected for " +
                        "matching process $([int]$process.ProcessId).")
                }

                [void]$matches.Add([pscustomobject]@{
                    ProcessId = [int]$process.ProcessId
                    ProcessIdentity = [string]$process.ProcessIdentity
                    ExecutablePath = $processPath
                    ProcessObject = $null
                })
            }

            continue
        }

        $safeHandle = $null
        $processPath = $null
        try {
            $safeHandle = $process.SafeHandle
            if ($null -eq $safeHandle -or
                $safeHandle.IsInvalid -or
                $safeHandle.IsClosed) {
                throw 'Process handle is unavailable.'
            }

            $processPath = Get-NormalizedPath (
                [string]$process.MainModule.FileName)
        }
        catch {
            $process.Dispose()
            throw (
                "Executable path and stable handle cannot be inspected for " +
                "matching process $([int]$process.Id).")
        }

        if (Test-PathEquals $processPath $ExecutablePath) {
            [void]$matches.Add([pscustomobject]@{
                ProcessId = [int]$process.Id
                ProcessIdentity = (
                    $safeHandle.DangerousGetHandle().ToInt64().ToString())
                ExecutablePath = $processPath
                ProcessObject = $process
            })
        }
        else {
            $process.Dispose()
        }
    }

    return @($matches)
}

function Wait-ForExactInstalledInstanceExit {
    param(
        [AllowEmptyCollection()]
        [Parameter(Mandatory = $true)][object[]] $Processes,
        [Parameter(Mandatory = $true)][string] $ExecutablePath,
        [Parameter(Mandatory = $true)][int] $TimeoutSeconds)

    if ($InternalTestMode) {
        Add-TestAction -Action 'WaitForExit' -Properties @{
            ExecutablePath = $ExecutablePath
            TimeoutSeconds = $TimeoutSeconds
        }
        return @($Processes)
    }

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $remaining = [System.Collections.ArrayList]::new()
    foreach ($entry in @($Processes)) {
        $milliseconds = [Math]::Max(
            0,
            [int][Math]::Ceiling(
                ($deadline - [DateTime]::UtcNow).TotalMilliseconds))
        $exited = try {
            $entry.ProcessObject.WaitForExit($milliseconds)
        }
        catch {
            throw (
                "Cannot wait on the validated process handle for " +
                "$($entry.ProcessId).")
        }

        if (-not $exited) {
            [void]$remaining.Add($entry)
        }
    }

    return @($remaining)
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

    $validatedProcesses = @(
        Get-ExactInstalledProcesses -ExecutablePath $ExecutablePath)
    try {
        $remaining = @(
            Wait-ForExactInstalledInstanceExit `
                -Processes $validatedProcesses `
                -ExecutablePath $ExecutablePath `
                -TimeoutSeconds 2)
        foreach ($process in $remaining) {
            if ($InternalTestMode) {
                Add-TestAction -Action 'StopProcess' -Properties @{
                    ProcessId = $process.ProcessId
                    ProcessIdentity = $process.ProcessIdentity
                    ExecutablePath = $process.ExecutablePath
                }
            }
            else {
                try {
                    $process.ProcessObject.Kill()
                }
                catch [System.InvalidOperationException] {
                    if (-not $process.ProcessObject.HasExited) {
                        throw
                    }
                }
            }
        }

        $stillRunning = @(
            Wait-ForExactInstalledInstanceExit `
                -Processes $remaining `
                -ExecutablePath $ExecutablePath `
                -TimeoutSeconds 10)
        if (-not $InternalTestMode -and $stillRunning.Count -gt 0) {
            $processIds = $stillRunning.ProcessId -join ', '
            throw (
                'Timed out waiting for exact installed process handle to ' +
                "exit: $processIds")
        }
    }
    finally {
        if (-not $InternalTestMode) {
            foreach ($process in $validatedProcesses) {
                $process.ProcessObject.Dispose()
            }
        }
    }
}

function Get-SkinDesignerProcesses {
    if ($InternalTestMode) {
        if ([string]::IsNullOrWhiteSpace(
            $script:ValidatedInternalProcessSnapshotPath)) {
            return @()
        }
        $parsed = ConvertFrom-Json -InputObject (Get-Content `
            -LiteralPath $script:ValidatedInternalProcessSnapshotPath `
            -Raw `
            -Encoding UTF8)
        return @($parsed | ForEach-Object { $_ })
    }

    return @(Get-Process `
        -Name 'CodexQuotaHud.SkinDesigner' `
        -ErrorAction SilentlyContinue)
}

function Get-ExactSkinDesignerProcesses {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $matches = [System.Collections.ArrayList]::new()
    foreach ($process in @(Get-SkinDesignerProcesses)) {
        if ($InternalTestMode) {
            if ($null -eq $process -or
                -not [string]::Equals(
                    [string]$process.Name,
                    'CodexQuotaHud.SkinDesigner.exe',
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            if ([string]::IsNullOrWhiteSpace(
                [string]$process.ExecutablePath)) {
                throw (
                    'Executable path cannot be inspected for matching ' +
                    "Designer process $([int]$process.ProcessId).")
            }
            $processPath = try {
                Get-NormalizedPath ([string]$process.ExecutablePath)
            }
            catch {
                throw (
                    'Executable path cannot be inspected for matching ' +
                    "Designer process $([int]$process.ProcessId).")
            }
            if (Test-PathEquals $processPath $ExecutablePath) {
                if ([string]::IsNullOrWhiteSpace(
                    [string]$process.ProcessIdentity)) {
                    throw (
                        'Stable process identity cannot be inspected for ' +
                        "matching Designer process $([int]$process.ProcessId).")
                }
                $propertyNames = @($process.PSObject.Properties.Name)
                [void]$matches.Add([pscustomobject]@{
                    ProcessId = [int]$process.ProcessId
                    ProcessIdentity = [string]$process.ProcessIdentity
                    ExecutablePath = $processPath
                    ExitAfterClose = (
                        'ExitAfterClose' -in $propertyNames -and
                        [bool]$process.ExitAfterClose)
                    ProcessObject = $null
                })
            }
            continue
        }

        $safeHandle = $null
        try {
            $safeHandle = $process.SafeHandle
            if ($null -eq $safeHandle -or
                $safeHandle.IsInvalid -or
                $safeHandle.IsClosed) {
                throw 'Process handle is unavailable.'
            }
            $processPath = Get-NormalizedPath (
                [string]$process.MainModule.FileName)
        }
        catch {
            $process.Dispose()
            throw (
                'Executable path and stable handle cannot be inspected for ' +
                "matching Designer process $([int]$process.Id).")
        }
        if (Test-PathEquals $processPath $ExecutablePath) {
            [void]$matches.Add([pscustomobject]@{
                ProcessId = [int]$process.Id
                ProcessIdentity = (
                    $safeHandle.DangerousGetHandle().ToInt64().ToString())
                ExecutablePath = $processPath
                ExitAfterClose = $false
                ProcessObject = $process
            })
        }
        else {
            $process.Dispose()
        }
    }
    return @($matches)
}

function Close-ExactSkinDesignerInstances {
    param([Parameter(Mandatory = $true)][string] $ExecutablePath)

    $validated = @(Get-ExactSkinDesignerProcesses `
        -ExecutablePath $ExecutablePath)
    if ($validated.Count -eq 0) {
        return
    }
    try {
        foreach ($process in $validated) {
            if ($InternalTestMode) {
                Add-TestAction -Action 'CloseDesignerWindow' -Properties @{
                    ProcessId = $process.ProcessId
                    ProcessIdentity = $process.ProcessIdentity
                    ExecutablePath = $process.ExecutablePath
                }
            }
            else {
                [void]$process.ProcessObject.CloseMainWindow()
            }
        }

        if ($InternalTestMode) {
            Add-TestAction -Action 'WaitForDesignerExit' -Properties @{
                ExecutablePath = $ExecutablePath
                TimeoutSeconds = 10
            }
            $stillRunning = @($validated | Where-Object {
                -not $_.ExitAfterClose
            })
        }
        else {
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            $stillRunning = [System.Collections.ArrayList]::new()
            foreach ($process in $validated) {
                $milliseconds = [Math]::Max(
                    0,
                    [int][Math]::Ceiling(
                        ($deadline - [DateTime]::UtcNow).TotalMilliseconds))
                $exited = try {
                    $process.ProcessObject.WaitForExit($milliseconds)
                }
                catch {
                    throw (
                        'Cannot wait on the validated Designer process ' +
                        "handle for $($process.ProcessId).")
                }
                if (-not $exited) {
                    [void]$stillRunning.Add($process)
                }
            }
        }

        if ($stillRunning.Count -gt 0) {
            throw (
                'Exact Skin Designer process is still running after a ' +
                'normal close request: ' +
                (($stillRunning.ProcessId) -join ', '))
        }
    }
    finally {
        if (-not $InternalTestMode) {
            foreach ($process in $validated) {
                $process.ProcessObject.Dispose()
            }
        }
    }
}

try {
    $systemLocalRoot = Get-NormalizedPath (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData))
    if ($InternalTestMode) {
        if ([string]::IsNullOrWhiteSpace($LocalAppDataRoot)) {
            throw 'Internal test mode requires -LocalAppDataRoot.'
        }

        $localRoot = Get-NormalizedPath $LocalAppDataRoot
        $systemTemp = Get-NormalizedPath ([System.IO.Path]::GetTempPath())
        $testRoot = Get-NormalizedPath (
            Split-Path -Path $localRoot -Parent)
        $script:ValidatedInternalTestRoot = $testRoot
        if (-not [string]::Equals(
                (Split-Path -Path $localRoot -Leaf),
                'LocalAppData',
                [System.StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-PathIsStrictDescendant `
                -Path $testRoot `
                -Boundary $systemTemp)) {
            throw (
                'Internal test LocalAppData must stay inside the system ' +
                'temporary directory.')
        }

        Assert-NoReparsePoint -Path $localRoot -Boundary $systemTemp
        if (-not [string]::IsNullOrWhiteSpace(
            $InternalProcessSnapshotPath)) {
            $script:ValidatedInternalProcessSnapshotPath =
                Get-ValidatedInternalHookPath `
                    -Path $InternalProcessSnapshotPath `
                    -TestRoot $testRoot `
                    -MustExist
        }

        if (-not [string]::IsNullOrWhiteSpace($InternalActionLogPath)) {
            $script:ValidatedInternalActionLogPath =
                Get-ValidatedInternalHookPath `
                    -Path $InternalActionLogPath `
                    -TestRoot $testRoot
        }

        if (-not [string]::IsNullOrWhiteSpace(
            $InternalPrepareBackupFailureReparseTargetPath)) {
            $script:ValidatedBackupFailureTarget =
                Get-ValidatedInternalDirectoryPath `
                    -Path $InternalPrepareBackupFailureReparseTargetPath `
                    -TestRoot $testRoot
        }

        if (-not [string]::IsNullOrWhiteSpace($InternalShellRootPath)) {
            $script:ValidatedInternalShellRootPath =
                Get-ValidatedInternalDirectoryPath `
                    -Path $InternalShellRootPath `
                    -TestRoot $testRoot
        }

        if (-not [string]::IsNullOrWhiteSpace(
            $InternalErrorDiagnosticPath)) {
            $script:ValidatedInternalErrorDiagnosticPath =
                Get-ValidatedInternalErrorDiagnosticPath `
                    -Path $InternalErrorDiagnosticPath `
                    -TestRoot $testRoot
            if (Test-Path -LiteralPath `
                $script:ValidatedInternalErrorDiagnosticPath) {
                Remove-Item `
                    -LiteralPath $script:ValidatedInternalErrorDiagnosticPath `
                    -Force
            }
        }

        if (-not [string]::IsNullOrWhiteSpace(
            $InternalDesignerFailureStage) -and
            $InternalDesignerFailureStage -notin @(
                'PrepareAfterDesignerMove',
                'PrepareAfterShortcutMove',
                'RollbackCopy',
                'RollbackMove',
                'CommitDelete')) {
            throw 'Unknown internal Designer failure stage.'
        }
        if (-not [string]::IsNullOrWhiteSpace(
            $InternalCleanupFailureStage) -and
            $InternalCleanupFailureStage -notin @(
                'LegacyCommit',
                'DesignerAfterPayloadDelete')) {
            throw 'Unknown internal cleanup failure stage.'
        }
    }
    else {
        if (-not [string]::IsNullOrWhiteSpace($LocalAppDataRoot) -and
            -not (Test-PathEquals $LocalAppDataRoot $systemLocalRoot)) {
            throw (
                'Production LocalAppDataRoot must equal the system ' +
                'LocalApplicationData directory.')
        }

        $localRoot = $systemLocalRoot
        if (-not [string]::IsNullOrWhiteSpace(
            $InternalProcessSnapshotPath) -or
            -not [string]::IsNullOrWhiteSpace($InternalActionLogPath) -or
            $InternalSkipShutdownSignal -or
            $InternalRollbackCopyFailureAfterItemCount -ne 0 -or
            -not [string]::IsNullOrWhiteSpace(
                $InternalPrepareBackupFailureReparseTargetPath) -or
            -not [string]::IsNullOrWhiteSpace(
                $InternalDesignerFailureStage) -or
            -not [string]::IsNullOrWhiteSpace(
                $InternalCleanupFailureStage) -or
            -not [string]::IsNullOrWhiteSpace($InternalShellRootPath) -or
            -not [string]::IsNullOrWhiteSpace(
                $InternalErrorDiagnosticPath) -or
            $InternalCurrentRunValueExists -or
            -not [string]::IsNullOrWhiteSpace($InternalCurrentRunValue)) {
            throw 'Internal lifecycle hooks require -InternalTestMode.'
        }
    }

    $target = Get-ValidatedInstallTarget `
        -InstallPath $InstallPath `
        -LocalAppDataRoot $localRoot
    $executable = Join-Path $target 'CodexQuotaHud.App.exe'
    $designerExecutable = Join-Path `
        $target `
        'designer\CodexQuotaHud.SkinDesigner.exe'
    $backup = $null
    if (-not [string]::IsNullOrWhiteSpace($LegacyBackupPath)) {
        $backup = Get-ValidatedLegacyBackupTarget `
            -BackupPath $LegacyBackupPath `
            -LocalAppDataRoot $localRoot
    }
    $shellState = $null
    if (-not [string]::IsNullOrWhiteSpace($LegacyShellStatePath)) {
        $shellState = Get-ValidatedLegacyShellStateTarget `
            -StatePath $LegacyShellStatePath `
            -LocalAppDataRoot $localRoot
    }
    $designerBackup = $null
    if (-not [string]::IsNullOrWhiteSpace($DesignerBackupPath)) {
        $designerBackup = Get-ValidatedDesignerBackupTarget `
            -BackupPath $DesignerBackupPath `
            -LocalAppDataRoot $localRoot
    }

    if ($Action -in @(
        'SnapshotLegacyState',
        'DiscardLegacyState',
        'CompensateLegacyInstall')) {
        if ($null -eq $shellState) {
            throw "$Action requires -LegacyShellStatePath."
        }
        if ($InternalTestMode -and
            [string]::IsNullOrWhiteSpace(
                $script:ValidatedInternalShellRootPath)) {
            throw "$Action internal test mode requires -InternalShellRootPath."
        }
    }

    if ($Action -in @(
        'PrepareDesignerComponentRemoval',
        'CommitDesignerComponentRemoval',
        'RollbackDesignerComponentRemoval')) {
        if ($null -eq $designerBackup) {
            throw "$Action requires -DesignerBackupPath."
        }
        if ($InternalTestMode -and
            [string]::IsNullOrWhiteSpace(
                $script:ValidatedInternalShellRootPath)) {
            throw "$Action internal test mode requires -InternalShellRootPath."
        }
    }

    $designerShortcut = $null
    if ($Action -in @(
        'PrepareDesignerComponentRemoval',
        'CommitDesignerComponentRemoval',
        'RollbackDesignerComponentRemoval')) {
        $designerShortcut = (Get-ManagedShellPaths).DesignerStartMenu
    }

    switch ($Action) {
        'PrepareInstall' {
            Stop-ExactInstalledInstance -ExecutablePath $executable
            Close-ExactSkinDesignerInstances `
                -ExecutablePath $designerExecutable
            if ($null -ne $backup) {
                Copy-LegacyInstallToBackup `
                    -InstallPath $target `
                    -BackupPath $backup
            }
        }
        'SnapshotLegacyState' {
            Snapshot-LegacyShellState -StatePath $shellState
        }
        'PrepareUninstall' {
            Stop-ExactInstalledInstance -ExecutablePath $executable
            Close-ExactSkinDesignerInstances `
                -ExecutablePath $designerExecutable
        }
        'PrepareDesignerComponentRemoval' {
            Close-ExactSkinDesignerInstances `
                -ExecutablePath $designerExecutable
            Prepare-DesignerComponentRemoval `
                -InstallPath $target `
                -BackupPath $designerBackup `
                -ShortcutPath $designerShortcut `
                -LocalAppDataRoot $localRoot
        }
        'CommitDesignerComponentRemoval' {
            Remove-DesignerComponentRemovalBackup `
                -InstallPath $target `
                -BackupPath $designerBackup `
                -ShortcutPath $designerShortcut
        }
        'RollbackDesignerComponentRemoval' {
            Restore-DesignerComponentRemoval `
                -InstallPath $target `
                -BackupPath $designerBackup `
                -ShortcutPath $designerShortcut `
                -LocalAppDataRoot $localRoot
        }
        'FinalizeUninstall' {
            Remove-DirectoryTreeWithoutFollowingReparsePoints `
                -Path $target `
                -Boundary $localRoot
        }
        'CommitInstall' {
            if ($null -ne $backup) {
                Remove-LegacyInstallBackup `
                    -InstallPath $target `
                    -BackupPath $backup
            }
        }
        'DiscardLegacyState' {
            Discard-LegacyShellState -StatePath $shellState
        }
        'CompensateLegacyInstall' {
            Compensate-LegacyInstall `
                -InstallPath $target `
                -StatePath $shellState
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
catch {
    $primaryError = $_
    if ($InternalTestMode -and
        -not [string]::IsNullOrWhiteSpace(
            $script:ValidatedInternalErrorDiagnosticPath)) {
        try {
            Write-InternalErrorDiagnostic -ErrorRecord $primaryError
        }
        catch {
            [Console]::Error.WriteLine(
                'Could not write internal lifecycle diagnostic: ' +
                $_.Exception.Message)
        }
    }
    throw
}
finally {
    Write-TestActionLog
}
