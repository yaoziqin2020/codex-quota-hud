[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.1.1',
    [Parameter(Mandatory = $true)]
    [string] $InstallerPath,
    [ValidateSet(
        '',
        'fresh-default',
        'fresh-designer',
        'add-designer',
        'remove-designer',
        'upgrade-selected',
        'uninstall-preserve',
        'uninstall-purge',
        'cleanup-legacy-failure',
        'cleanup-designer-failure')]
    [AllowEmptyString()]
    [string] $InternalScenario = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-NormalizedPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        $Path = Join-Path (Get-Location).Path $Path
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

function Test-StrictDescendant {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Boundary)

    $candidate = Get-NormalizedPath $Path
    $parent = (Get-NormalizedPath $Boundary) +
        [System.IO.Path]::DirectorySeparatorChar
    return $candidate.StartsWith(
        $parent,
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Assert-NoReparsePoint {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Boundary)

    $current = Get-NormalizedPath $Path
    $stop = Get-NormalizedPath $Boundary
    while ($true) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (($item.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing reparse-point smoke path: $current"
            }
        }

        if (Test-PathEquals $current $stop) {
            break
        }

        $parent = Split-Path -Path $current -Parent
        if ([string]::IsNullOrWhiteSpace($parent) -or
            (Test-PathEquals $parent $current)) {
            throw "Smoke path escaped validation boundary: $Path"
        }
        $current = $parent
    }
}

function Assert-NoProductionOverlap {
    param(
        [Parameter(Mandatory = $true)][string] $TestPath,
        [Parameter(Mandatory = $true)][string[]] $ProductionPaths)

    foreach ($productionPath in $ProductionPaths) {
        if ((Test-PathEquals $TestPath $productionPath) -or
            (Test-StrictDescendant $TestPath $productionPath) -or
            (Test-StrictDescendant $productionPath $TestPath)) {
            throw (
                "Refusing smoke target that overlaps production path: " +
                "$TestPath / $productionPath")
        }
    }
}

function Assert-Exists {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Description does not exist: $Path"
    }
}

function Assert-Missing {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Description)

    if (Test-Path -LiteralPath $Path) {
        throw "$Description still exists: $Path"
    }
}

function Assert-DesktopShortcut {
    param(
        [Parameter(Mandatory = $true)][string] $ShortcutPath,
        [Parameter(Mandatory = $true)][string] $ExpectedTarget,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $ExpectedArguments)

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $null
    try {
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        $target = [string]$shortcut.TargetPath
        $arguments = [string]$shortcut.Arguments
    }
    finally {
        if ($null -ne $shortcut) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject(
                $shortcut)
        }
        if ($null -ne $shell) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject(
                $shell)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($target)) {
        if (-not (Test-PathEquals $target $ExpectedTarget) -or
            -not [string]::Equals(
                $arguments.Trim(),
                $ExpectedArguments,
                [System.StringComparison]::Ordinal)) {
            throw 'Desktop link has an unexpected target or arguments.'
        }
        return
    }

    $asciiShortcutPath = Join-Path `
        ([System.IO.Path]::GetTempPath()) `
        ('codex-quota-hud-shortcut-' +
            [System.Guid]::NewGuid().ToString('N') + '.lnk')
    $fallbackShell = $null
    $fallbackShortcut = $null
    try {
        Copy-Item -LiteralPath $ShortcutPath -Destination $asciiShortcutPath
        $fallbackShell = New-Object -ComObject WScript.Shell
        $fallbackShortcut = $fallbackShell.CreateShortcut($asciiShortcutPath)
        $target = [string]$fallbackShortcut.TargetPath
        $arguments = [string]$fallbackShortcut.Arguments
    }
    finally {
        if ($null -ne $fallbackShortcut) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject(
                $fallbackShortcut)
        }
        if ($null -ne $fallbackShell) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject(
                $fallbackShell)
        }
        if (Test-Path -LiteralPath $asciiShortcutPath) {
            Remove-Item -LiteralPath $asciiShortcutPath -Force
        }
    }

    if ([string]::IsNullOrWhiteSpace($target) -or
        -not (Test-PathEquals $target $ExpectedTarget) -or
        -not [string]::Equals(
            $arguments.Trim(),
            $ExpectedArguments,
            [System.StringComparison]::Ordinal)) {
        throw 'Desktop link has an unexpected target or arguments.'
    }
    Write-Host 'Desktop link verified through an ASCII-path copy.'
}

function Invoke-SetupProcess {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Arguments,
        [Parameter(Mandatory = $true)][string] $Description,
        [Parameter(Mandatory = $true)][string] $LogPath)

    $process = Start-Process `
        -FilePath $Path `
        -ArgumentList "$Arguments /LOG=`"$LogPath`"" `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        $signedExitCode = [int]$process.ExitCode
        $unsignedExitCode = [System.BitConverter]::ToUInt32(
            [System.BitConverter]::GetBytes($signedExitCode), 0)
        $exitCodeHex = $unsignedExitCode.ToString('X8')
        $setupFullPath = Get-NormalizedPath $Path
        $logFullPath = Get-NormalizedPath $LogPath
        $logExists = Test-Path -LiteralPath $logFullPath -PathType Leaf
        $logLength = if ($logExists) {
            [int64](Get-Item -LiteralPath $logFullPath -Force).Length
        }
        else {
            [int64]-1
        }
        if ($logExists) {
            Write-Host "Inno log tail for $Description`:"
            Get-Content -LiteralPath $logFullPath -Tail 80
        }
        throw (
            "$Description failed. " +
            "ExitCodeSigned=$signedExitCode; " +
            "ExitCodeUnsigned=$unsignedExitCode; " +
            "ExitCodeHex=0x$exitCodeHex; " +
            "SetupPath=$setupFullPath; " +
            "InternalRoot=$internalRoot; " +
            "LogPath=$logFullPath; " +
            "LogExists=$logExists; " +
            "LogLength=$logLength.")
    }
    return $process.ExitCode
}

function Get-InternalDefine {
    param(
        [Parameter(Mandatory = $true)][string[]] $Arguments,
        [Parameter(Mandatory = $true)][string] $Name)

    $prefix = "/D$Name="
    $matches = @($Arguments | Where-Object {
        $_.StartsWith($prefix, [System.StringComparison]::Ordinal)
    })
    if ($matches.Count -ne 1) {
        throw "Expected exactly one internal define: $Name"
    }
    return $matches[0].Substring($prefix.Length)
}

function Get-RegistryValueSnapshot {
    param(
        [Parameter(Mandatory = $true)][string] $RelativeKey,
        [Parameter(Mandatory = $true)][string] $ValueName)

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        $RelativeKey,
        $false)
    try {
        if ($null -eq $key -or $ValueName -notin $key.GetValueNames()) {
            return '{"Exists":false}'
        }
        return ConvertTo-Json ([ordered]@{
            Exists = $true
            Kind = [string]$key.GetValueKind($ValueName)
            Value = [string]$key.GetValue(
                $ValueName,
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        }) -Compress
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Get-RegistryKeySnapshot {
    param([Parameter(Mandatory = $true)][string] $RelativeKey)

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        $RelativeKey,
        $false)
    try {
        if ($null -eq $key) { return '{"Exists":false}' }
        $values = [ordered]@{}
        foreach ($name in @($key.GetValueNames() | Sort-Object)) {
            $values[$name] = [ordered]@{
                Kind = [string]$key.GetValueKind($name)
                Value = [string]$key.GetValue(
                    $name,
                    $null,
                    [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            }
        }
        return ConvertTo-Json ([ordered]@{
            Exists = $true
            Values = $values
        }) -Compress
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Get-RegistryValuePresenceChecked {
    param(
        [Parameter(Mandatory = $true)][string] $RelativeKey,
        [Parameter(Mandatory = $true)][string] $ValueName)

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        $RelativeKey,
        $false)
    try {
        if ($null -eq $key) { return $false }
        return $ValueName -in $key.GetValueNames()
    }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Get-RegistryKeyPresenceChecked {
    param([Parameter(Mandatory = $true)][string] $RelativeKey)

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
        $RelativeKey,
        $false)
    try { return $null -ne $key }
    finally {
        if ($null -ne $key) { $key.Dispose() }
    }
}

function Remove-ExactInternalRegistryTree {
    param(
        [Parameter(Mandatory = $true)][string] $InternalTestId,
        [Parameter(Mandatory = $true)][string] $RunRelativeKey)

    $parsedId = [Guid]::Empty
    if (-not [Guid]::TryParse($InternalTestId, [ref]$parsedId)) {
        throw 'Internal registry cleanup ID is not a GUID.'
    }
    $testRoot = 'Software\CodexQuotaHud.Tests\' + $InternalTestId
    $expectedRun = $testRoot + '\Run'
    if (-not [string]::Equals(
            $RunRelativeKey,
            $expectedRun,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Internal registry cleanup path is not the exact GUID subtree.'
    }

    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree(
        $testRoot,
        $false)
    if (Get-RegistryKeyPresenceChecked -RelativeKey $testRoot) {
        throw 'Internal registry GUID subtree cleanup postcondition failed.'
    }
}

function Get-PathPresenceChecked {
    param([Parameter(Mandatory = $true)][string] $Path)
    return [bool](Test-Path -LiteralPath $Path -ErrorAction Stop)
}

function Assert-InternalArtifactsAbsent {
    param(
        [Parameter(Mandatory = $true)][string] $RunRelativeKey,
        [Parameter(Mandatory = $true)][string] $RunValueName,
        [Parameter(Mandatory = $true)][string] $UninstallRelativeKey,
        [Parameter(Mandatory = $true)][string[]] $ShortcutPaths,
        [Parameter(Mandatory = $true)][string] $Description)

    if (Get-RegistryValuePresenceChecked `
        -RelativeKey $RunRelativeKey `
        -ValueName $RunValueName) {
        throw "$Description left the exact internal Run value."
    }
    if (Get-RegistryKeyPresenceChecked -RelativeKey $UninstallRelativeKey) {
        throw "$Description left the exact internal uninstall key."
    }
    foreach ($shortcut in $ShortcutPaths) {
        if (Get-PathPresenceChecked -Path $shortcut) {
            throw "$Description left managed shortcut: $shortcut"
        }
    }
}

function Assert-InternalUninstallRegistration {
    param(
        [Parameter(Mandatory = $true)][string] $RegistryPath,
        [Parameter(Mandatory = $true)][string] $InstallPath,
        [Parameter(Mandatory = $true)][string] $TestRoot)

    if (-not (Test-Path -LiteralPath $RegistryPath)) {
        throw "Internal uninstall key does not exist: $RegistryPath"
    }
    $installLocation = [string](Get-ItemPropertyValue `
        -LiteralPath $RegistryPath `
        -Name 'InstallLocation' `
        -ErrorAction Stop)
    if (-not (Test-PathEquals $installLocation $InstallPath)) {
        throw 'Internal uninstall key InstallLocation escaped isolated install.'
    }
    $uninstallString = [string](Get-ItemPropertyValue `
        -LiteralPath $RegistryPath `
        -Name 'UninstallString' `
        -ErrorAction Stop)
    if ($uninstallString.IndexOf(
        $TestRoot,
        [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        throw 'Internal uninstall key UninstallString escaped isolated root.'
    }
}

function Assert-IsolatedScenarioBoundary {
    param(
        [Parameter(Mandatory = $true)][string] $Scenario,
        [Parameter(Mandatory = $true)][string] $ScenarioRoot,
        [Parameter(Mandatory = $true)][string] $InternalRoot,
        [Parameter(Mandatory = $true)][string[]] $TestPaths,
        [Parameter(Mandatory = $true)][string[]] $ProductionPaths,
        [Parameter(Mandatory = $true)][string] $InternalRunRelativeKey,
        [Parameter(Mandatory = $true)][string] $InternalRunValueName,
        [Parameter(Mandatory = $true)][string] $InternalUninstallRelativeKey,
        [Parameter(Mandatory = $true)][string] $ProductionRunValueName,
        [Parameter(Mandatory = $true)][string] $ProductionUninstallRelativeKey)

    if (-not (Test-StrictDescendant $InternalRoot $ScenarioRoot)) {
        throw "Scenario $Scenario internal root escaped its generated root."
    }
    foreach ($path in $TestPaths) {
        if (-not (Test-StrictDescendant $path $ScenarioRoot)) {
            throw "Scenario $Scenario path escaped its generated root: $path"
        }
        Assert-NoProductionOverlap `
            -TestPath $path `
            -ProductionPaths $ProductionPaths
    }
    if ([string]::Equals(
            $InternalRunRelativeKey,
            'Software\Microsoft\Windows\CurrentVersion\Run',
            [System.StringComparison]::OrdinalIgnoreCase) -or
        -not $InternalRunRelativeKey.StartsWith(
            'Software\CodexQuotaHud.Tests\',
            [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals(
            $InternalRunValueName,
            $ProductionRunValueName,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals(
            $InternalUninstallRelativeKey,
            $ProductionUninstallRelativeKey,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Scenario $Scenario registry identity overlaps production."
    }
}

function New-SentinelSnapshot {
    param(
        [Parameter(Mandatory = $true)][string] $SettingsRoot,
        [Parameter(Mandatory = $true)][string] $LocalAppDataRoot)

    $paths = [ordered]@{
        Settings = Join-Path $SettingsRoot 'settings.json'
        Skin = Join-Path $SettingsRoot (
            'skins\11111111-1111-1111-1111-111111111111\skin.json')
        Draft = Join-Path $SettingsRoot (
            'designer\drafts\22222222-2222-2222-2222-222222222222\draft.json')
        Recovery = Join-Path $SettingsRoot 'designer\recovery\recovery.json'
        Import = Join-Path $SettingsRoot 'imports\import.cqskin'
    }
    foreach ($entry in $paths.GetEnumerator()) {
        New-Item `
            -ItemType Directory `
            -Path (Split-Path -Path $entry.Value -Parent) `
            -Force |
            Out-Null
        [System.IO.File]::WriteAllText(
            $entry.Value,
            "sentinel:$($entry.Key)",
            [System.Text.UTF8Encoding]::new($false))
    }
    $unrelated = Join-Path $LocalAppDataRoot 'CodexQuotaHud.unrelated.keep'
    [System.IO.File]::WriteAllText(
        $unrelated,
        'unrelated sentinel',
        [System.Text.UTF8Encoding]::new($false))
    $hashes = [ordered]@{}
    foreach ($entry in $paths.GetEnumerator()) {
        $hashes[$entry.Value] = (Get-FileHash `
            -LiteralPath $entry.Value `
            -Algorithm SHA256).Hash
    }
    return [pscustomobject]@{
        Paths = $paths
        Hashes = $hashes
        Unrelated = $unrelated
    }
}

function Assert-SentinelHashesUnchanged {
    param([Parameter(Mandatory = $true)][psobject] $Snapshot)
    foreach ($entry in $Snapshot.Hashes.GetEnumerator()) {
        Assert-Exists -Path $entry.Key -Description 'User-data sentinel'
        $actual = (Get-FileHash `
            -LiteralPath $entry.Key `
            -Algorithm SHA256).Hash
        if (-not [string]::Equals(
                $actual,
                $entry.Value,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "User-data sentinel hash changed: $($entry.Key)"
        }
    }
    Assert-Exists `
        -Path $Snapshot.Unrelated `
        -Description 'Unrelated Local App Data sentinel'
}

function Assert-FileMatchesPublished {
    param(
        [Parameter(Mandatory = $true)][string] $Installed,
        [Parameter(Mandatory = $true)][string] $Published,
        [Parameter(Mandatory = $true)][string] $Description)
    Assert-Exists -Path $Installed -Description $Description
    $installedHash = (Get-FileHash `
        -LiteralPath $Installed `
        -Algorithm SHA256).Hash
    $publishedHash = (Get-FileHash `
        -LiteralPath $Published `
        -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $installedHash,
            $publishedHash,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description does not match the published payload."
    }
}

function Assert-NoOperationResidue {
    param(
        [Parameter(Mandatory = $true)][string] $ProgramsRoot,
        [Parameter(Mandatory = $true)][string] $InternalRoot)
    if (Test-Path -LiteralPath $ProgramsRoot -PathType Container) {
        $residue = @(Get-ChildItem -LiteralPath $ProgramsRoot -Force |
            Where-Object {
                $_.Name -like 'CodexQuotaHud.designer-removal-backup.*' -or
                $_.Name -like 'CodexQuotaHud.designer-rollback-staging.*' -or
                $_.Name -like 'CodexQuotaHud.legacy-backup.*' -or
                $_.Name -like 'CodexQuotaHud.legacy-shell-state.*' -or
                $_.Name -like 'CodexQuotaHud.rollback-*'
            })
        if ($residue.Count -ne 0) {
            throw ('Operation residue remains: ' +
                (($residue.FullName) -join ', '))
        }
    }
    $running = @(Get-CimInstance Win32_Process -Filter (
        "Name='CodexQuotaHud.App.exe' OR " +
        "Name='CodexQuotaHud.SkinDesigner.exe'") |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace([string]$_.ExecutablePath) -and
            ([string]$_.ExecutablePath).StartsWith(
                (Get-NormalizedPath $InternalRoot) +
                    [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)
        })
    if ($running.Count -ne 0) {
        throw 'An isolated installer process is still running.'
    }
}

function Assert-NormalInstalledState {
    param(
        [string] $InstalledExecutable,
        [string] $PublishedExecutable,
        [string] $NormalStartMenu,
        [string] $NormalDesktop,
        [string] $RunRegistryPath,
        [string] $RunValueName)
    Assert-FileMatchesPublished `
        -Installed $InstalledExecutable `
        -Published $PublishedExecutable `
        -Description 'Installed normal executable'
    Assert-Exists -Path $NormalStartMenu -Description 'Normal Start Menu link'
    Assert-Exists -Path $NormalDesktop -Description 'Normal desktop link'
    Assert-DesktopShortcut `
        -ShortcutPath $NormalStartMenu `
        -ExpectedTarget $InstalledExecutable `
        -ExpectedArguments ''
    Assert-DesktopShortcut `
        -ShortcutPath $NormalDesktop `
        -ExpectedTarget $InstalledExecutable `
        -ExpectedArguments ''
    $startup = Get-ItemPropertyValue `
        -LiteralPath $RunRegistryPath `
        -Name $RunValueName `
        -ErrorAction Stop
    $expected = "`"$InstalledExecutable`" --background"
    if (-not [string]::Equals(
            [string]$startup,
            $expected,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Normal startup value has an unexpected target or arguments.'
    }
}

function Assert-DesignerInstalledState {
    param(
        [string] $InstalledDesignerExecutable,
        [string] $PublishedDesignerExecutable,
        [string] $DesignerStartMenu,
        [string] $DesignerDesktop,
        [string] $DesignerRunValue,
        [string] $RunRegistryPath,
        [string] $PreviewDesktop,
        [string] $PreviewStartMenu)
    Assert-FileMatchesPublished `
        -Installed $InstalledDesignerExecutable `
        -Published $PublishedDesignerExecutable `
        -Description 'Installed Designer executable'
    Assert-Exists -Path $DesignerStartMenu -Description 'Designer Start link'
    Assert-DesktopShortcut `
        -ShortcutPath $DesignerStartMenu `
        -ExpectedTarget $installedDesignerExecutable `
        -ExpectedArguments ''
    Assert-Missing -Path $DesignerDesktop -Description 'Designer desktop link'
    Assert-Missing -Path $PreviewDesktop -Description 'Preview desktop link'
    Assert-Missing -Path $PreviewStartMenu -Description 'Preview Start link'
    if (Get-RegistryValuePresenceChecked `
        -RelativeKey 'Software\Microsoft\Windows\CurrentVersion\Run' `
        -ValueName $DesignerRunValue) {
        throw 'DesignerRunValue unexpectedly exists.'
    }
}

function Assert-DesignerMissingState {
    param(
        [string] $DesignerDirectory,
        [string] $DesignerStartMenu,
        [string] $DesignerDesktop,
        [string] $DesignerRunValue,
        [string] $PreviewDesktop,
        [string] $PreviewStartMenu)
    Assert-Missing -Path $DesignerDirectory -Description 'Designer directory'
    Assert-Missing -Path $DesignerStartMenu -Description 'Designer Start link'
    Assert-Missing -Path $DesignerDesktop -Description 'Designer desktop link'
    Assert-Missing -Path $PreviewDesktop -Description 'Preview desktop link'
    Assert-Missing -Path $PreviewStartMenu -Description 'Preview Start link'
    if (Get-RegistryValuePresenceChecked `
        -RelativeKey 'Software\Microsoft\Windows\CurrentVersion\Run' `
        -ValueName $DesignerRunValue) {
        throw 'DesignerRunValue unexpectedly exists.'
    }
}

function Invoke-IsolatedScenario {
    param([Parameter(Mandatory = $true)][string] $Name)
    & powershell.exe `
        -NoProfile `
        -NonInteractive `
        -ExecutionPolicy Bypass `
        -File $PSCommandPath `
        -Version $Version `
        -InstallerPath $formalInstaller `
        -InternalScenario $Name
    if ($LASTEXITCODE -ne 0) {
        throw "Isolated scenario $Name failed with exit code $LASTEXITCODE."
    }
}

function Invoke-CleanupFailureScenario {
    param([Parameter(Mandatory = $true)][string] $Name)
    Invoke-IsolatedScenario -Name $Name
}

$repositoryRoot = Get-NormalizedPath (Join-Path $PSScriptRoot '..')
$expectedInstallerName = "CodexQuotaHud-Setup-v$Version.exe"
$formalInstaller = Get-NormalizedPath $InstallerPath
if (-not [string]::Equals(
    [System.IO.Path]::GetFileName($formalInstaller),
    $expectedInstallerName,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer filename must be $expectedInstallerName."
}
$canonicalInstaller = Get-NormalizedPath (
    Join-Path $repositoryRoot "artifacts\release\$expectedInstallerName")
if (-not (Test-PathEquals $formalInstaller $canonicalInstaller)) {
    throw "InstallerPath must be exactly: $canonicalInstaller"
}
if (-not (Test-Path -LiteralPath $formalInstaller -PathType Leaf)) {
    throw "Production installer does not exist: $formalInstaller"
}
$checksumManifest = Join-Path `
    (Split-Path -Path $canonicalInstaller -Parent) `
    'SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $checksumManifest -PathType Leaf)) {
    throw "Canonical checksum manifest does not exist: $checksumManifest"
}
$manifestLines = @(
    Get-Content -LiteralPath $checksumManifest -Encoding UTF8 |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$escapedInstallerName = [Regex]::Escape($expectedInstallerName)
$matchingLines = @($manifestLines | Where-Object {
    $_ -match "^([0-9a-f]{64})  $escapedInstallerName$"
})
if ($matchingLines.Count -ne 1) {
    throw (
        'SHA256SUMS.txt must contain exactly one lowercase hash entry for ' +
        "$expectedInstallerName.")
}
$expectedInstallerHash = (
    [Regex]::Match($matchingLines[0], '^[0-9a-f]{64}')).Value
$actualInstallerHash = (Get-FileHash `
    -LiteralPath $formalInstaller `
    -Algorithm SHA256).Hash.ToLowerInvariant()
if (-not [string]::Equals(
    $actualInstallerHash,
    $expectedInstallerHash,
    [System.StringComparison]::Ordinal)) {
    throw 'Production installer hash does not match SHA256SUMS.txt.'
}

$published = Get-NormalizedPath (
    Join-Path $repositoryRoot 'artifacts\CodexQuotaHud-win-x64')
$publishedExecutable = Join-Path $published 'CodexQuotaHud.App.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published payload does not exist: $publishedExecutable"
}
$publishedDesignerExecutable = Join-Path `
    $published `
    'designer\CodexQuotaHud.SkinDesigner.exe'
if (-not (Test-Path -LiteralPath $publishedDesignerExecutable -PathType Leaf)) {
    throw "Published Designer payload does not exist: $publishedDesignerExecutable"
}

$systemTemp = Get-NormalizedPath ([System.IO.Path]::GetTempPath())
$smokeId = [Guid]::NewGuid().ToString('D')
$smokeRoot = Get-NormalizedPath (
    (Join-Path $systemTemp "CodexQuotaHud.InstallerSmoke.$smokeId"))
$cleanupAuthorized = $false
$scenarioFailed = $false
$internalTestId = $null
$runRegistryPath = $null
$internalRunRelativeKey = $null
$uninstallRegistryPath = $null
$uninstallRegistryRelativeKey = $null
$internalRoot = $null
$normalDesktop = $null
$previewDesktop = $null
$normalStartMenu = $null
$isolatedShortcutPaths = [System.Collections.ArrayList]::new()
$productionRunRelativeKey =
    'Software\Microsoft\Windows\CurrentVersion\Run'
$productionRunValueName = 'CodexQuotaHud'
$productionUninstallRelativeKey =
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
    '{7F6E38C7-5928-4A18-9C9B-9B6D9B90D314}_is1'
$productionRunSnapshot = Get-RegistryValueSnapshot `
    -RelativeKey $productionRunRelativeKey `
    -ValueName $productionRunValueName
$productionUninstallSnapshot = Get-RegistryKeySnapshot `
    -RelativeKey $productionUninstallRelativeKey

if ([string]::IsNullOrWhiteSpace($InternalScenario)) {
    Invoke-IsolatedScenario -Name 'fresh-default'
    Invoke-IsolatedScenario -Name 'fresh-designer'
    Invoke-IsolatedScenario -Name 'add-designer'
    Invoke-IsolatedScenario -Name 'remove-designer'
    Invoke-IsolatedScenario -Name 'upgrade-selected'
    Invoke-IsolatedScenario -Name 'uninstall-preserve'
    Invoke-IsolatedScenario -Name 'uninstall-purge'
    Write-Host 'All seven isolated installer scenarios passed.'
    Invoke-CleanupFailureScenario -Name 'cleanup-legacy-failure'
    Invoke-CleanupFailureScenario -Name 'cleanup-designer-failure'
    Write-Host 'Both committed cleanup failure scenarios passed.'
    return
}
$scenario = $InternalScenario

try {
    if (-not (Test-StrictDescendant $smokeRoot $systemTemp)) {
        throw 'Smoke root must be a strict descendant of system temporary.'
    }
    Assert-NoReparsePoint -Path $smokeRoot -Boundary $systemTemp
    New-Item -ItemType Directory -Path $smokeRoot | Out-Null
    $cleanupAuthorized = $true

    $buildOutput = Join-Path $smokeRoot 'Build'
    $capture = Join-Path $smokeRoot 'build-arguments.json'
    $cleanupFailureStage = switch ($scenario) {
        'cleanup-legacy-failure' { 'LegacyCommit' }
        'cleanup-designer-failure' { 'DesignerAfterPayloadDelete' }
        default { '' }
    }
    & (Join-Path $PSScriptRoot 'build-installer.ps1') `
        -Version $Version `
        -PublishedPath $published `
        -OutputPath $buildOutput `
        -InternalTestMode `
        -InternalArgumentCapturePath $capture `
        -InternalCleanupFailureStage $cleanupFailureStage

    $isolatedInstaller = Join-Path $buildOutput $expectedInstallerName
    Assert-Exists -Path $isolatedInstaller -Description 'Isolated test Setup'
    if (Test-PathEquals $isolatedInstaller $formalInstaller) {
        throw 'Isolated test Setup must be distinct from production Setup.'
    }

    $parsedCompilerArguments = ConvertFrom-Json (
        Get-Content -LiteralPath $capture -Raw -Encoding UTF8)
    $compilerArguments = @(
        $parsedCompilerArguments | ForEach-Object { [string]$_ })
    $internalTestId = Get-InternalDefine `
        -Arguments $compilerArguments `
        -Name 'InternalTestId'
    $parsedGuid = [Guid]::Empty
    if (-not [Guid]::TryParse($internalTestId, [ref]$parsedGuid)) {
        throw 'Internal test ID is not a GUID.'
    }
    $internalRunRelativeKey =
        "Software\CodexQuotaHud.Tests\$internalTestId\Run"
    $runRegistryPath =
        'Registry::HKEY_CURRENT_USER\' + $internalRunRelativeKey
    $internalRoot = Get-NormalizedPath (Get-InternalDefine `
        -Arguments $compilerArguments `
        -Name 'InternalTestRoot')
    if (-not (Test-StrictDescendant $internalRoot $smokeRoot)) {
        throw 'Internal test root escaped the unique smoke root.'
    }
    Assert-NoReparsePoint -Path $internalRoot -Boundary $systemTemp

    $localAppData = Join-Path $internalRoot 'LocalAppData'
    $install = Join-Path $localAppData 'Programs\CodexQuotaHud'
    $settings = Join-Path $localAppData 'CodexQuotaHud'
    $desktop = Join-Path $internalRoot 'Shell\Desktop'
    $startMenu = Join-Path $internalRoot 'Shell\StartMenu\Programs'
    $installedExecutable = Join-Path $install 'CodexQuotaHud.App.exe'
    $designerDirectory = Join-Path $install 'designer'
    $installedDesignerExecutable = Join-Path `
        $designerDirectory `
        'CodexQuotaHud.SkinDesigner.exe'
    $normalDesktop = Join-Path $desktop 'Codex Quota HUD.lnk'
    $previewShortcutName = 'Codex Quota HUD ' +
        [char]0x5f00 + [char]0x53d1 + [char]0x9884 + [char]0x89c8 + '.lnk'
    $previewDesktop = Join-Path $desktop $previewShortcutName
    $normalStartMenu = Join-Path $startMenu 'Codex Quota HUD.lnk'
    $designerSuffix = -join @(
        [char]0x76ae,
        [char]0x80a4,
        [char]0x8bbe,
        [char]0x8ba1,
        [char]0x5668)
    $designerLinkName = "Codex Quota HUD $designerSuffix.lnk"
    $DesignerStartMenu = Join-Path $startMenu $designerLinkName
    $DesignerDesktop = Join-Path $desktop $designerLinkName
    $DesignerRunValue = "CodexQuotaHud.SkinDesigner.InternalTest.$internalTestId"
    $previewStartMenu = Join-Path $startMenu $previewShortcutName
    $runValueName = "CodexQuotaHud.InternalTest.$internalTestId"
    $uninstallRegistryRelativeKey =
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
        "CQH.Test.$internalTestId`_is1"
    $uninstallRegistryPath =
        'Registry::HKEY_CURRENT_USER\' + $uninstallRegistryRelativeKey
    [void]$isolatedShortcutPaths.Add($normalDesktop)
    [void]$isolatedShortcutPaths.Add($previewDesktop)
    [void]$isolatedShortcutPaths.Add($normalStartMenu)
    [void]$isolatedShortcutPaths.Add($DesignerStartMenu)
    [void]$isolatedShortcutPaths.Add($DesignerDesktop)
    [void]$isolatedShortcutPaths.Add($previewStartMenu)

    $productionInstall = Join-Path `
        ([Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) `
        'Programs\CodexQuotaHud'
    $productionSettings = Join-Path `
        ([Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) `
        'CodexQuotaHud'
    $productionDesktop = Get-NormalizedPath (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::DesktopDirectory))
    $productionStartMenu = Get-NormalizedPath (Join-Path (
        [Environment]::GetFolderPath(
            [Environment+SpecialFolder]::StartMenu)) 'Programs')
    $productionPaths = @(
        $productionInstall,
        $productionSettings,
        $productionDesktop,
        $productionStartMenu)
    $scenarioPaths = @(
        $internalRoot,
        $localAppData,
        $install,
        $settings,
        $desktop,
        $startMenu)
    Assert-IsolatedScenarioBoundary -Scenario $scenario `
        -ScenarioRoot $smokeRoot `
        -InternalRoot $internalRoot `
        -TestPaths $scenarioPaths `
        -ProductionPaths $productionPaths `
        -InternalRunRelativeKey $internalRunRelativeKey `
        -InternalRunValueName $runValueName `
        -InternalUninstallRelativeKey $uninstallRegistryRelativeKey `
        -ProductionRunValueName $productionRunValueName `
        -ProductionUninstallRelativeKey $productionUninstallRelativeKey

    foreach ($shellDirectory in @($desktop, $startMenu)) {
        if (-not (Test-StrictDescendant $shellDirectory $internalRoot)) {
            throw "Internal shell directory escaped GUID root: $shellDirectory"
        }
        Assert-NoReparsePoint -Path $shellDirectory -Boundary $internalRoot
        New-Item -ItemType Directory -Path $shellDirectory -Force | Out-Null
        Assert-NoReparsePoint -Path $shellDirectory -Boundary $internalRoot
    }

    $quiet = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART ' +
        '/TASKS="startup,desktopicon"'
    $initialDesignerSelected = $scenario -notin @(
        'fresh-default',
        'add-designer',
        'cleanup-legacy-failure')
    $initialSelection = if ($scenario -in @(
        'fresh-default',
        'cleanup-legacy-failure')) {
        $quiet
    }
    elseif ($initialDesignerSelected) {
        "$quiet /TYPE=custom /COMPONENTS=designer"
    }
    else {
        "$quiet /TYPE=normal"
    }
    $setupExitCodes = [System.Collections.ArrayList]::new()
    if ($scenario -eq 'cleanup-legacy-failure') {
        New-Item -ItemType Directory -Path $designerDirectory -Force |
            Out-Null
        [System.IO.File]::WriteAllText(
            $installedExecutable,
            'legacy executable before committed cleanup failure')
        [System.IO.File]::WriteAllText(
            $installedDesignerExecutable,
            'legacy Designer before committed cleanup failure')
        [System.IO.File]::WriteAllText(
            (Join-Path $designerDirectory 'a-first-payload.bin'),
            'legacy Designer payload')
        [System.IO.File]::WriteAllText(
            $DesignerStartMenu,
            'legacy Designer shortcut')
    }
    [void]$setupExitCodes.Add((Invoke-SetupProcess `
        -Path $isolatedInstaller `
        -Arguments $initialSelection `
        -Description 'Isolated clean install' `
        -LogPath (Join-Path $smokeRoot 'clean-install.log')))
    Assert-NormalInstalledState `
        -InstalledExecutable $installedExecutable `
        -PublishedExecutable $publishedExecutable `
        -NormalStartMenu $normalStartMenu `
        -NormalDesktop $normalDesktop `
        -RunRegistryPath $runRegistryPath `
        -RunValueName $runValueName
    if ($initialDesignerSelected) {
        Assert-DesignerInstalledState `
            -InstalledDesignerExecutable $installedDesignerExecutable `
            -PublishedDesignerExecutable $publishedDesignerExecutable `
            -DesignerStartMenu $DesignerStartMenu `
            -DesignerDesktop $DesignerDesktop `
            -DesignerRunValue $DesignerRunValue `
            -RunRegistryPath $runRegistryPath `
            -PreviewDesktop $previewDesktop `
            -PreviewStartMenu $previewStartMenu
    }
    else {
        Assert-DesignerMissingState `
            -DesignerDirectory $designerDirectory `
            -DesignerStartMenu $DesignerStartMenu `
            -DesignerDesktop $DesignerDesktop `
            -DesignerRunValue $DesignerRunValue `
            -PreviewDesktop $previewDesktop `
            -PreviewStartMenu $previewStartMenu
    }
    Assert-InternalUninstallRegistration `
        -RegistryPath $uninstallRegistryPath `
        -InstallPath $install `
        -TestRoot $internalRoot
    $sentinels = New-SentinelSnapshot `
        -SettingsRoot $settings `
        -LocalAppDataRoot $localAppData

    switch ($scenario) {
        'fresh-default' {
            Assert-DesignerMissingState `
                -DesignerDirectory $designerDirectory `
                -DesignerStartMenu $DesignerStartMenu `
                -DesignerDesktop $DesignerDesktop `
                -DesignerRunValue $DesignerRunValue `
                -PreviewDesktop $previewDesktop `
                -PreviewStartMenu $previewStartMenu
            Assert-SentinelHashesUnchanged -Snapshot $sentinels
        }
        'fresh-designer' {
            Assert-DesignerInstalledState `
                -InstalledDesignerExecutable $installedDesignerExecutable `
                -PublishedDesignerExecutable $publishedDesignerExecutable `
                -DesignerStartMenu $DesignerStartMenu `
                -DesignerDesktop $DesignerDesktop `
                -DesignerRunValue $DesignerRunValue `
                -RunRegistryPath $runRegistryPath `
                -PreviewDesktop $previewDesktop `
                -PreviewStartMenu $previewStartMenu
            Assert-SentinelHashesUnchanged -Snapshot $sentinels
        }
        'add-designer' {
            foreach ($attempt in 1..2) {
                [void]$setupExitCodes.Add((Invoke-SetupProcess `
                    -Path $isolatedInstaller `
                    -Arguments (
                        "$quiet /TYPE=custom /COMPONENTS=designer") `
                    -Description "Isolated add Designer pass $attempt" `
                    -LogPath (Join-Path $smokeRoot "add-$attempt.log")))
                Assert-DesignerInstalledState `
                    -InstalledDesignerExecutable $installedDesignerExecutable `
                    -PublishedDesignerExecutable $publishedDesignerExecutable `
                    -DesignerStartMenu $DesignerStartMenu `
                    -DesignerDesktop $DesignerDesktop `
                    -DesignerRunValue $DesignerRunValue `
                    -RunRegistryPath $runRegistryPath `
                    -PreviewDesktop $previewDesktop `
                    -PreviewStartMenu $previewStartMenu
                Assert-SentinelHashesUnchanged -Snapshot $sentinels
            }
        }
        'remove-designer' {
            $removeSelection = $quiet + ' /TYPE=normal /COMPONENTS=""'
            foreach ($attempt in 1..2) {
                [void]$setupExitCodes.Add((Invoke-SetupProcess `
                    -Path $isolatedInstaller `
                    -Arguments $removeSelection `
                    -Description "Isolated remove Designer pass $attempt" `
                    -LogPath (Join-Path $smokeRoot "remove-$attempt.log")))
                Assert-NormalInstalledState `
                    -InstalledExecutable $installedExecutable `
                    -PublishedExecutable $publishedExecutable `
                    -NormalStartMenu $normalStartMenu `
                    -NormalDesktop $normalDesktop `
                    -RunRegistryPath $runRegistryPath `
                    -RunValueName $runValueName
                Assert-DesignerMissingState `
                    -DesignerDirectory $designerDirectory `
                    -DesignerStartMenu $DesignerStartMenu `
                    -DesignerDesktop $DesignerDesktop `
                    -DesignerRunValue $DesignerRunValue `
                    -PreviewDesktop $previewDesktop `
                    -PreviewStartMenu $previewStartMenu
                Assert-SentinelHashesUnchanged -Snapshot $sentinels
            }
        }
        'upgrade-selected' {
            [System.IO.File]::WriteAllText(
                $installedExecutable, 'older internal normal payload')
            [System.IO.File]::WriteAllText(
                $installedDesignerExecutable, 'older internal Designer payload')
            foreach ($attempt in 1..2) {
                [void]$setupExitCodes.Add((Invoke-SetupProcess `
                    -Path $isolatedInstaller `
                    -Arguments $quiet `
                    -Description "Isolated selected upgrade pass $attempt" `
                    -LogPath (Join-Path $smokeRoot "upgrade-$attempt.log")))
                Assert-NormalInstalledState `
                    -InstalledExecutable $installedExecutable `
                    -PublishedExecutable $publishedExecutable `
                    -NormalStartMenu $normalStartMenu `
                    -NormalDesktop $normalDesktop `
                    -RunRegistryPath $runRegistryPath `
                    -RunValueName $runValueName
                Assert-DesignerInstalledState `
                    -InstalledDesignerExecutable $installedDesignerExecutable `
                    -PublishedDesignerExecutable $publishedDesignerExecutable `
                    -DesignerStartMenu $DesignerStartMenu `
                    -DesignerDesktop $DesignerDesktop `
                    -DesignerRunValue $DesignerRunValue `
                    -RunRegistryPath $runRegistryPath `
                    -PreviewDesktop $previewDesktop `
                    -PreviewStartMenu $previewStartMenu
                Assert-SentinelHashesUnchanged -Snapshot $sentinels
            }
        }
        'cleanup-legacy-failure' {
            $processLog = Join-Path `
                $internalRoot `
                'diagnostics\lifecycle-process.log'
            $setupLog = Join-Path $smokeRoot 'clean-install.log'
            Assert-Exists `
                -Path $processLog `
                -Description 'Legacy cleanup lifecycle process log'
            Assert-Exists `
                -Path $setupLog `
                -Description 'Legacy cleanup Setup log'
            $processText = Get-Content `
                -LiteralPath $processLog `
                -Raw `
                -Encoding UTF8
            $setupText = Get-Content `
                -LiteralPath $setupLog `
                -Raw `
                -Encoding UTF8
            foreach ($requiredAction in @(
                'Action=CommitInstall',
                'Action=CommitDesignerComponentRemoval',
                'Action=DiscardLegacyState')) {
                if (-not $processText.Contains($requiredAction)) {
                    throw "Other cleanup was not attempted: $requiredAction"
                }
            }
            if (-not $setupText.Contains(
                'Legacy install backup cleanup failed:')) {
                throw 'Setup did not log the legacy cleanup warning.'
            }
            foreach ($rollbackAction in @(
                'Action=RollbackDesignerComponentRemoval',
                'Action=CompensateLegacyInstall',
                'Action=RollbackInstall')) {
                if ($processText.Contains($rollbackAction)) {
                    throw "Committed install invoked rollback: $rollbackAction"
                }
            }

            $programsRoot = Join-Path $localAppData 'Programs'
            $legacyResidue = @(Get-ChildItem `
                -LiteralPath $programsRoot `
                -Directory `
                -Force |
                Where-Object {
                    $_.Name -like 'CodexQuotaHud.legacy-backup.*'
                })
            if ($legacyResidue.Count -ne 1) {
                throw 'Legacy cleanup failure must retain one exact backup.'
            }
            $legacyMarkerPath = Join-Path `
                $legacyResidue[0].FullName `
                'CodexQuotaHud.LegacyBackup.json'
            Assert-Exists `
                -Path $legacyMarkerPath `
                -Description 'Identifiable legacy cleanup marker'
            $legacyMarker = Get-Content `
                -LiteralPath $legacyMarkerPath `
                -Raw `
                -Encoding UTF8 |
                ConvertFrom-Json
            if (-not (Test-PathEquals ([string]$legacyMarker.Source) $install) -or
                -not (Test-PathEquals `
                    ([string]$legacyMarker.Destination) `
                    $legacyResidue[0].FullName)) {
                throw 'Legacy cleanup marker paths are not identifiable.'
            }
            $designerResidue = @(Get-ChildItem `
                -LiteralPath $programsRoot `
                -Directory `
                -Force |
                Where-Object {
                    $_.Name -like
                        'CodexQuotaHud.designer-removal-backup.*'
                })
            if ($designerResidue.Count -ne 0) {
                throw 'Designer cleanup did not continue after legacy failure.'
            }
            Assert-NormalInstalledState `
                -InstalledExecutable $installedExecutable `
                -PublishedExecutable $publishedExecutable `
                -NormalStartMenu $normalStartMenu `
                -NormalDesktop $normalDesktop `
                -RunRegistryPath $runRegistryPath `
                -RunValueName $runValueName
            Assert-DesignerMissingState `
                -DesignerDirectory $designerDirectory `
                -DesignerStartMenu $DesignerStartMenu `
                -DesignerDesktop $DesignerDesktop `
                -DesignerRunValue $DesignerRunValue `
                -PreviewDesktop $previewDesktop `
                -PreviewStartMenu $previewStartMenu
            Assert-SentinelHashesUnchanged -Snapshot $sentinels

            [void]$setupExitCodes.Add((Invoke-SetupProcess `
                -Path $isolatedInstaller `
                -Arguments $initialSelection `
                -Description 'Safe retry after legacy cleanup failure' `
                -LogPath (Join-Path $smokeRoot 'legacy-safe-retry.log')))
            Assert-NormalInstalledState `
                -InstalledExecutable $installedExecutable `
                -PublishedExecutable $publishedExecutable `
                -NormalStartMenu $normalStartMenu `
                -NormalDesktop $normalDesktop `
                -RunRegistryPath $runRegistryPath `
                -RunValueName $runValueName
            Assert-DesignerMissingState `
                -DesignerDirectory $designerDirectory `
                -DesignerStartMenu $DesignerStartMenu `
                -DesignerDesktop $DesignerDesktop `
                -DesignerRunValue $DesignerRunValue `
                -PreviewDesktop $previewDesktop `
                -PreviewStartMenu $previewStartMenu
            Assert-Exists `
                -Path $legacyMarkerPath `
                -Description 'Identifiable legacy residue after safe retry'
            Assert-SentinelHashesUnchanged -Snapshot $sentinels
            Write-Host (
                'Smoke scenario passed: committed legacy cleanup failure ' +
                'does not roll back the new install.')
        }
        'cleanup-designer-failure' {
            $removeSelection = $quiet + ' /TYPE=normal /COMPONENTS=""'
            $removeLog = Join-Path $smokeRoot 'designer-cleanup-failure.log'
            [void]$setupExitCodes.Add((Invoke-SetupProcess `
                -Path $isolatedInstaller `
                -Arguments $removeSelection `
                -Description 'Committed Designer cleanup failure' `
                -LogPath $removeLog))
            $processLog = Join-Path `
                $internalRoot `
                'diagnostics\lifecycle-process.log'
            Assert-Exists `
                -Path $processLog `
                -Description 'Designer cleanup lifecycle process log'
            Assert-Exists `
                -Path $removeLog `
                -Description 'Designer cleanup Setup log'
            $processText = Get-Content `
                -LiteralPath $processLog `
                -Raw `
                -Encoding UTF8
            $setupText = Get-Content `
                -LiteralPath $removeLog `
                -Raw `
                -Encoding UTF8
            foreach ($requiredAction in @(
                'Action=CommitInstall',
                'Action=CommitDesignerComponentRemoval')) {
                if (-not $processText.Contains($requiredAction)) {
                    throw "Other cleanup was not attempted: $requiredAction"
                }
            }
            if (-not $setupText.Contains(
                'Designer component cleanup failed:')) {
                throw 'Setup did not log the Designer cleanup warning.'
            }
            foreach ($rollbackAction in @(
                'Action=RollbackDesignerComponentRemoval',
                'Action=CompensateLegacyInstall',
                'Action=RollbackInstall')) {
                if ($processText.Contains($rollbackAction)) {
                    throw "Committed install invoked rollback: $rollbackAction"
                }
            }

            $programsRoot = Join-Path $localAppData 'Programs'
            $designerResidue = @(Get-ChildItem `
                -LiteralPath $programsRoot `
                -Directory `
                -Force |
                Where-Object {
                    $_.Name -like
                        'CodexQuotaHud.designer-removal-backup.*'
                })
            if ($designerResidue.Count -ne 1) {
                throw 'Designer cleanup failure must retain one exact backup.'
            }
            $designerMarkerPath = Join-Path `
                $designerResidue[0].FullName `
                'CodexQuotaHud.DesignerRemoval.json'
            Assert-Exists `
                -Path $designerMarkerPath `
                -Description 'Identifiable Designer cleanup marker'
            $designerMarker = Get-Content `
                -LiteralPath $designerMarkerPath `
                -Raw `
                -Encoding UTF8 |
                ConvertFrom-Json
            if (-not (Test-PathEquals `
                    ([string]$designerMarker.Source) `
                    $designerDirectory) -or
                -not (Test-PathEquals `
                    ([string]$designerMarker.Destination) `
                    $designerResidue[0].FullName) -or
                -not [string]::Equals(
                    [string]$designerMarker.State,
                    'Prepared',
                    [System.StringComparison]::Ordinal)) {
                throw 'Designer cleanup marker is not identifiable.'
            }
            Assert-NormalInstalledState `
                -InstalledExecutable $installedExecutable `
                -PublishedExecutable $publishedExecutable `
                -NormalStartMenu $normalStartMenu `
                -NormalDesktop $normalDesktop `
                -RunRegistryPath $runRegistryPath `
                -RunValueName $runValueName
            Assert-DesignerMissingState `
                -DesignerDirectory $designerDirectory `
                -DesignerStartMenu $DesignerStartMenu `
                -DesignerDesktop $DesignerDesktop `
                -DesignerRunValue $DesignerRunValue `
                -PreviewDesktop $previewDesktop `
                -PreviewStartMenu $previewStartMenu
            Assert-SentinelHashesUnchanged -Snapshot $sentinels

            [void]$setupExitCodes.Add((Invoke-SetupProcess `
                -Path $isolatedInstaller `
                -Arguments $removeSelection `
                -Description 'Safe retry after Designer cleanup failure' `
                -LogPath (Join-Path $smokeRoot 'designer-safe-retry.log')))
            Assert-NormalInstalledState `
                -InstalledExecutable $installedExecutable `
                -PublishedExecutable $publishedExecutable `
                -NormalStartMenu $normalStartMenu `
                -NormalDesktop $normalDesktop `
                -RunRegistryPath $runRegistryPath `
                -RunValueName $runValueName
            Assert-DesignerMissingState `
                -DesignerDirectory $designerDirectory `
                -DesignerStartMenu $DesignerStartMenu `
                -DesignerDesktop $DesignerDesktop `
                -DesignerRunValue $DesignerRunValue `
                -PreviewDesktop $previewDesktop `
                -PreviewStartMenu $previewStartMenu
            Assert-Exists `
                -Path $designerMarkerPath `
                -Description 'Identifiable Designer residue after safe retry'
            Assert-SentinelHashesUnchanged -Snapshot $sentinels
            Write-Host (
                'Smoke scenario passed: committed Designer cleanup failure ' +
                'does not roll back the new install.')
        }
        'uninstall-preserve' {
            $uninstaller = Join-Path $install 'unins000.exe'
            Assert-Exists -Path $uninstaller -Description 'Isolated uninstaller'
            [void]$setupExitCodes.Add((Invoke-SetupProcess `
                -Path $uninstaller `
                -Arguments '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' `
                -Description 'Isolated default uninstall' `
                -LogPath (Join-Path $smokeRoot 'default-uninstall.log')))
            Assert-Missing -Path $install -Description 'Install directory'
            Assert-SentinelHashesUnchanged -Snapshot $sentinels
            Assert-InternalArtifactsAbsent `
                -RunRelativeKey $internalRunRelativeKey `
                -RunValueName $runValueName `
                -UninstallRelativeKey $uninstallRegistryRelativeKey `
                -ShortcutPaths @($isolatedShortcutPaths) `
                -Description 'Default uninstall'
            Write-Host 'Smoke scenario passed: default uninstall preserves settings.'
        }
        'uninstall-purge' {
            $uninstaller = Join-Path $install 'unins000.exe'
            Assert-Exists -Path $uninstaller -Description 'Isolated uninstaller'
            [void]$setupExitCodes.Add((Invoke-SetupProcess `
                -Path $uninstaller `
                -Arguments (
                    '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /PURGESETTINGS') `
                -Description 'Isolated purge uninstall' `
                -LogPath (Join-Path $smokeRoot 'purge-uninstall.log')))
            Assert-Missing `
                -Path $settings `
                -Description 'Purged test settings directory'
            Assert-Exists `
                -Path $sentinels.Unrelated `
                -Description 'Unrelated Local App Data sentinel'
            Assert-InternalArtifactsAbsent `
                -RunRelativeKey $internalRunRelativeKey `
                -RunValueName $runValueName `
                -UninstallRelativeKey $uninstallRegistryRelativeKey `
                -ShortcutPaths @($isolatedShortcutPaths) `
                -Description 'Purge uninstall'
            Write-Host 'Smoke scenario passed: purge uninstall removes test settings.'
        }
        default { throw "Unknown isolated scenario: $scenario" }
    }

    $desktopLinks = @(
        Get-ChildItem `
            -LiteralPath $desktop `
            -Filter '*.lnk' `
            -File `
            -ErrorAction Stop)
    $expectedDesktopCount = if ($scenario -like 'uninstall-*') { 0 } else { 1 }
    if ($desktopLinks.Count -ne $expectedDesktopCount) {
        throw "Scenario $scenario has an unexpected desktop link count."
    }
    if ($scenario -notlike 'cleanup-*-failure') {
        Assert-NoOperationResidue `
            -ProgramsRoot (Join-Path $localAppData 'Programs') `
            -InternalRoot $internalRoot
    }
    Write-Host (
        "Smoke scenario passed: $scenario; exit codes: " +
        ($setupExitCodes -join ', '))
    Write-Host "Isolated installer root: $internalRoot"
    Write-Host "Isolated test ID: $internalTestId"
}
catch {
    $scenarioFailed = $true
    $primaryError = $_
    [Console]::Error.WriteLine(
        "Smoke primary error: " + $primaryError.Exception.Message)
    [Console]::Error.WriteLine(
        "Smoke failure stack: " + $primaryError.ScriptStackTrace)
    throw
}
finally {
    $cleanupErrors = [System.Collections.ArrayList]::new()
    if (-not [string]::IsNullOrWhiteSpace($internalTestId) -and
        -not [string]::IsNullOrWhiteSpace($internalRunRelativeKey)) {
        try {
            Remove-ExactInternalRegistryTree `
                -InternalTestId $internalTestId `
                -RunRelativeKey $internalRunRelativeKey
        }
        catch { [void]$cleanupErrors.Add($_.Exception.Message) }
    }
    if (-not [string]::IsNullOrWhiteSpace($uninstallRegistryPath)) {
        try {
            if (Get-RegistryKeyPresenceChecked `
                -RelativeKey $uninstallRegistryRelativeKey) {
                Remove-Item `
                    -LiteralPath $uninstallRegistryPath `
                    -Recurse `
                    -Force `
                    -ErrorAction Stop
            }
        }
        catch { [void]$cleanupErrors.Add($_.Exception.Message) }
    }
    if ($cleanupAuthorized -and -not $scenarioFailed) {
        try {
            if (Get-PathPresenceChecked -Path $smokeRoot) {
                Assert-NoReparsePoint -Path $smokeRoot -Boundary $systemTemp
                if (-not (Test-StrictDescendant $smokeRoot $systemTemp)) {
                    throw 'Refusing cleanup outside system temporary.'
                }
                Remove-Item -LiteralPath $smokeRoot -Recurse -Force `
                    -ErrorAction Stop
            }
        }
        catch { [void]$cleanupErrors.Add($_.Exception.Message) }
    }

    if ($cleanupAuthorized) {
        try {
            if (-not $scenarioFailed -and
                (Get-PathPresenceChecked -Path $smokeRoot)) {
                [void]$cleanupErrors.Add(
                    "Cleanup postcondition failed: temp root exists: $smokeRoot")
            }
        }
        catch { [void]$cleanupErrors.Add($_.Exception.Message) }
        try {
            if (-not [string]::IsNullOrWhiteSpace($internalTestId) -and
                -not [string]::IsNullOrWhiteSpace(
                    $internalRunRelativeKey) -and
                (Get-RegistryKeyPresenceChecked `
                    -RelativeKey $internalRunRelativeKey)) {
                [void]$cleanupErrors.Add(
                    'Cleanup postcondition failed: internal Run key exists.')
            }
        }
        catch { [void]$cleanupErrors.Add($_.Exception.Message) }
        try {
            if (-not [string]::IsNullOrWhiteSpace(
                    $uninstallRegistryRelativeKey) -and
                (Get-RegistryKeyPresenceChecked `
                    -RelativeKey $uninstallRegistryRelativeKey)) {
                [void]$cleanupErrors.Add(
                    'Cleanup postcondition failed: internal uninstall key exists.')
            }
        }
        catch { [void]$cleanupErrors.Add($_.Exception.Message) }
        foreach ($shortcut in @($isolatedShortcutPaths)) {
            try {
                if (-not [string]::IsNullOrWhiteSpace([string]$shortcut) -and
                    (Get-PathPresenceChecked -Path ([string]$shortcut))) {
                    [void]$cleanupErrors.Add(
                        "Cleanup postcondition failed: shortcut exists: $shortcut")
                }
            }
            catch { [void]$cleanupErrors.Add($_.Exception.Message) }
        }
    }

    $currentProductionRunSnapshot = Get-RegistryValueSnapshot `
        -RelativeKey $productionRunRelativeKey `
        -ValueName 'CodexQuotaHud'
    $currentProductionUninstallSnapshot = Get-RegistryKeySnapshot `
        -RelativeKey $productionUninstallRelativeKey
    if (-not [string]::Equals(
            $productionRunSnapshot,
            $currentProductionRunSnapshot,
            [System.StringComparison]::Ordinal) -or
        -not [string]::Equals(
            $productionUninstallSnapshot,
            $currentProductionUninstallSnapshot,
            [System.StringComparison]::Ordinal)) {
        [void]$cleanupErrors.Add('Production registry snapshot changed.')
    }

    if ($cleanupErrors.Count -gt 0) {
        throw ('Isolated cleanup failed: ' + ($cleanupErrors -join ' | '))
    }
    if ($cleanupAuthorized -and $scenarioFailed) {
        Write-Host "Preserved failed scenario diagnostics: $smokeRoot"
    }
    elseif ($cleanupAuthorized) {
        Write-Host "Finally cleanup completed with checked postconditions: $smokeRoot"
    }
}
