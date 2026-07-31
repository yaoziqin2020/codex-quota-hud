[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.1.1',
    [Parameter(Mandatory = $true)]
    [string] $InstallerPath
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
        if (Test-Path -LiteralPath $LogPath -PathType Leaf) {
            Write-Host "Inno log tail for $Description`:"
            Get-Content -LiteralPath $LogPath -Tail 80
        }
        throw "$Description failed with exit code $($process.ExitCode)."
    }
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

$systemTemp = Get-NormalizedPath ([System.IO.Path]::GetTempPath())
$smokeId = [Guid]::NewGuid().ToString('D')
$smokeRoot = Get-NormalizedPath (
    (Join-Path $systemTemp "CodexQuotaHud.InstallerSmoke.$smokeId"))
$cleanupAuthorized = $false
$internalTestId = $null
$runRegistryPath =
    'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run'
$uninstallRegistryPath = $null
$uninstallRegistryRelativeKey = $null
$internalRoot = $null
$normalDesktop = $null
$previewDesktop = $null
$normalStartMenu = $null
$isolatedShortcutPaths = [System.Collections.ArrayList]::new()
$productionRunRelativeKey =
    'Software\Microsoft\Windows\CurrentVersion\Run'
$productionUninstallRelativeKey =
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
    '{7F6E38C7-5928-4A18-9C9B-9B6D9B90D314}_is1'
$productionRunSnapshot = Get-RegistryValueSnapshot `
    -RelativeKey $productionRunRelativeKey `
    -ValueName 'CodexQuotaHud'
$productionUninstallSnapshot = Get-RegistryKeySnapshot `
    -RelativeKey $productionUninstallRelativeKey

try {
    if (-not (Test-StrictDescendant $smokeRoot $systemTemp)) {
        throw 'Smoke root must be a strict descendant of system temporary.'
    }
    Assert-NoReparsePoint -Path $smokeRoot -Boundary $systemTemp
    New-Item -ItemType Directory -Path $smokeRoot | Out-Null
    $cleanupAuthorized = $true

    $buildOutput = Join-Path $smokeRoot 'Build'
    $capture = Join-Path $smokeRoot 'build-arguments.json'
    & (Join-Path $PSScriptRoot 'build-installer.ps1') `
        -Version $Version `
        -PublishedPath $published `
        -OutputPath $buildOutput `
        -InternalTestMode `
        -InternalArgumentCapturePath $capture

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
    $normalDesktop = Join-Path $desktop 'Codex Quota HUD.lnk'
    $previewShortcutName = 'Codex Quota HUD ' +
        [char]0x5f00 + [char]0x53d1 + [char]0x9884 + [char]0x89c8 + '.lnk'
    $previewDesktop = Join-Path $desktop $previewShortcutName
    $normalStartMenu = Join-Path $startMenu 'Codex Quota HUD.lnk'
    $runValueName = "CodexQuotaHud.InternalTest.$internalTestId"
    $uninstallRegistryRelativeKey =
        'Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
        "CQH.Test.$internalTestId`_is1"
    $uninstallRegistryPath =
        'Registry::HKEY_CURRENT_USER\' + $uninstallRegistryRelativeKey
    [void]$isolatedShortcutPaths.Add($normalDesktop)
    [void]$isolatedShortcutPaths.Add($previewDesktop)
    [void]$isolatedShortcutPaths.Add($normalStartMenu)

    $productionInstall = Join-Path `
        ([Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) `
        'Programs\CodexQuotaHud'
    $productionSettings = Join-Path `
        ([Environment]::GetFolderPath(
            [Environment+SpecialFolder]::LocalApplicationData)) `
        'CodexQuotaHud'
    $productionPaths = @($productionInstall, $productionSettings)
    foreach ($testTarget in @(
        $smokeRoot,
        $internalRoot,
        $localAppData,
        $install,
        $settings,
        $desktop,
        $startMenu)) {
        Assert-NoProductionOverlap `
            -TestPath $testTarget `
            -ProductionPaths $productionPaths
    }

    Invoke-SetupProcess `
        -Path $isolatedInstaller `
        -Arguments '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' `
        -Description 'Isolated clean install' `
        -LogPath (Join-Path $smokeRoot 'clean-install.log')
    Assert-Exists -Path $installedExecutable -Description 'Installed executable'
    Assert-Exists -Path $normalStartMenu -Description 'Normal Start Menu link'
    Assert-Exists -Path $normalDesktop -Description 'Normal desktop link'
    Assert-Missing -Path $previewDesktop -Description 'Developer Preview desktop link'
    $initialDesktopLinks = @(
        Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -File)
    if ($initialDesktopLinks.Count -ne 1) {
        throw 'Clean install did not create exactly one desktop link.'
    }
    Assert-DesktopShortcut `
        -ShortcutPath $normalDesktop `
        -ExpectedTarget $installedExecutable `
        -ExpectedArguments ''
    $startupValue = Get-ItemPropertyValue `
        -LiteralPath $runRegistryPath `
        -Name $runValueName `
        -ErrorAction Stop
    $expectedStartupValue = "`"$installedExecutable`" --background"
    if (-not [string]::Equals(
        [string]$startupValue,
        $expectedStartupValue,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Isolated startup value does not target the isolated executable.'
    }
    Assert-InternalUninstallRegistration `
        -RegistryPath $uninstallRegistryPath `
        -InstallPath $install `
        -TestRoot $internalRoot
    Write-Host 'Smoke scenario passed: clean isolated install.'

    New-Item -ItemType Directory -Path $settings -Force | Out-Null
    $settingsMarker = Join-Path $settings 'settings.json'
    $previewMarker = Join-Path $settings 'preview-window.json'
    [System.IO.File]::WriteAllText($settingsMarker, 'settings marker')
    [System.IO.File]::WriteAllText($previewMarker, 'preview marker')
    $legacyPdbMarker = Join-Path $install 'CodexQuotaHud.Core.pdb'
    [System.IO.File]::WriteAllText($legacyPdbMarker, 'legacy pdb marker')
    [System.IO.File]::WriteAllText(
        $previewDesktop,
        'legacy v1.1.0 preview shortcut')

    Invoke-SetupProcess `
        -Path $isolatedInstaller `
        -Arguments (
            '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART ' +
            '/TASKS="desktopicon"') `
        -Description 'Isolated upgrade' `
        -LogPath (Join-Path $smokeRoot 'upgrade.log')
    Assert-Exists -Path $normalStartMenu -Description 'Normal Start Menu link'
    Assert-Exists -Path $normalDesktop -Description 'Normal desktop link'
    Assert-Missing -Path $previewDesktop -Description 'Legacy Developer Preview link'
    $desktopLinksAfterUpgrade = @(
        Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -File)
    if ($desktopLinksAfterUpgrade.Count -ne 1) {
        Write-Host 'Desktop contents after isolated upgrade:'
        if (Test-Path -LiteralPath $desktop -PathType Container) {
            Get-ChildItem -LiteralPath $desktop -Force |
                Select-Object Name, FullName |
                Format-List |
                Out-String |
                Write-Host
        }
        $upgradeLog = Join-Path $smokeRoot 'upgrade.log'
        if (Test-Path -LiteralPath $upgradeLog -PathType Leaf) {
            Write-Host 'Upgrade log tail:'
            Get-Content -LiteralPath $upgradeLog -Tail 120
        }
        throw (
            'Expected exactly one normal desktop link after upgrade; ' +
            "found $($desktopLinksAfterUpgrade.Count).")
    }
    Assert-DesktopShortcut `
        -ShortcutPath $normalDesktop `
        -ExpectedTarget $installedExecutable `
        -ExpectedArguments ''
    if (Get-RegistryValuePresenceChecked `
        -RelativeKey $productionRunRelativeKey `
        -ValueName $runValueName) {
        throw 'Isolated startup value still exists after task deselection.'
    }
    Assert-InternalUninstallRegistration `
        -RegistryPath $uninstallRegistryPath `
        -InstallPath $install `
        -TestRoot $internalRoot
    Assert-Exists -Path $settingsMarker -Description 'Settings marker'
    Assert-Exists -Path $previewMarker -Description 'Preview-state marker'
    Write-Host 'Smoke scenario passed: isolated upgrade and task replacement.'

    $uninstaller = Join-Path $install 'unins000.exe'
    Assert-Exists -Path $uninstaller -Description 'Isolated uninstaller'
    Invoke-SetupProcess `
        -Path $uninstaller `
        -Arguments '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' `
        -Description 'Isolated default uninstall' `
        -LogPath (Join-Path $smokeRoot 'default-uninstall.log')
    Assert-Missing -Path $install -Description 'Install directory'
    Assert-Exists -Path $settingsMarker -Description 'Preserved settings marker'
    Assert-Exists -Path $previewMarker -Description 'Preserved preview-state marker'
    Assert-InternalArtifactsAbsent `
        -RunRelativeKey $productionRunRelativeKey `
        -RunValueName $runValueName `
        -UninstallRelativeKey $uninstallRegistryRelativeKey `
        -ShortcutPaths @($isolatedShortcutPaths) `
        -Description 'Default uninstall'
    Write-Host 'Smoke scenario passed: default uninstall preserves settings.'

    Invoke-SetupProcess `
        -Path $isolatedInstaller `
        -Arguments '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART' `
        -Description 'Isolated reinstall' `
        -LogPath (Join-Path $smokeRoot 'reinstall.log')
    Assert-Exists -Path $settingsMarker -Description 'Reinstall settings marker'
    Assert-Exists -Path $previewMarker -Description 'Reinstall preview-state marker'
    $uninstaller = Join-Path $install 'unins000.exe'
    Invoke-SetupProcess `
        -Path $uninstaller `
        -Arguments (
            '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /PURGESETTINGS') `
        -Description 'Isolated purge uninstall' `
        -LogPath (Join-Path $smokeRoot 'purge-uninstall.log')
    Assert-Missing -Path $settings -Description 'Purged test settings directory'
    Assert-InternalArtifactsAbsent `
        -RunRelativeKey $productionRunRelativeKey `
        -RunValueName $runValueName `
        -UninstallRelativeKey $uninstallRegistryRelativeKey `
        -ShortcutPaths @($isolatedShortcutPaths) `
        -Description 'Purge uninstall'
    Write-Host 'Smoke scenario passed: purge uninstall removes test settings.'
    Write-Host "Isolated installer root: $internalRoot"
    Write-Host "Isolated test ID: $internalTestId"
}
catch {
    Write-Error ("Smoke failure stack: " + $_.ScriptStackTrace)
    throw
}
finally {
    $cleanupErrors = [System.Collections.ArrayList]::new()
    if (-not [string]::IsNullOrWhiteSpace($internalTestId)) {
        try {
            $testRunName = "CodexQuotaHud.InternalTest.$internalTestId"
            if (Get-RegistryValuePresenceChecked `
                -RelativeKey $productionRunRelativeKey `
                -ValueName $testRunName) {
                Remove-ItemProperty `
                    -LiteralPath $runRegistryPath `
                    -Name $testRunName `
                    -Force `
                    -ErrorAction Stop
            }
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
    if ($cleanupAuthorized) {
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
            if (Get-PathPresenceChecked -Path $smokeRoot) {
                [void]$cleanupErrors.Add(
                    "Cleanup postcondition failed: temp root exists: $smokeRoot")
            }
        }
        catch { [void]$cleanupErrors.Add($_.Exception.Message) }
        try {
            if (-not [string]::IsNullOrWhiteSpace($internalTestId) -and
                (Get-RegistryValuePresenceChecked `
                    -RelativeKey $productionRunRelativeKey `
                    -ValueName "CodexQuotaHud.InternalTest.$internalTestId")) {
                [void]$cleanupErrors.Add(
                    'Cleanup postcondition failed: internal Run value exists.')
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
    if ($cleanupAuthorized) {
        Write-Host "Finally cleanup completed with checked postconditions: $smokeRoot"
    }
}
