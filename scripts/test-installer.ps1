[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.1.0',
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

$repositoryRoot = Get-NormalizedPath (Join-Path $PSScriptRoot '..')
$expectedInstallerName = "CodexQuotaHud-Setup-v$Version.exe"
$formalInstaller = Get-NormalizedPath $InstallerPath
if (-not (Test-Path -LiteralPath $formalInstaller -PathType Leaf)) {
    throw "Production installer does not exist: $formalInstaller"
}
if (-not [string]::Equals(
    [System.IO.Path]::GetFileName($formalInstaller),
    $expectedInstallerName,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Installer filename must be $expectedInstallerName."
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
    $normalStartMenu = Join-Path $startMenu 'Codex Quota HUD.lnk'
    $runValueName = "CodexQuotaHud.InternalTest.$internalTestId"
    $uninstallRegistryPath =
        'Registry::HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall\' +
        "CQH.Test.$internalTestId`_is1"

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
    $initialDesktopLinks = @(
        Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -File)
    if ($initialDesktopLinks.Count -ne 1) {
        throw 'Preview desktop link unexpectedly exists after clean install.'
    }
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
    Write-Host 'Smoke scenario passed: clean isolated install.'

    New-Item -ItemType Directory -Path $settings -Force | Out-Null
    $settingsMarker = Join-Path $settings 'settings.json'
    $previewMarker = Join-Path $settings 'preview-window.json'
    [System.IO.File]::WriteAllText($settingsMarker, 'settings marker')
    [System.IO.File]::WriteAllText($previewMarker, 'preview marker')

    Invoke-SetupProcess `
        -Path $isolatedInstaller `
        -Arguments (
            '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART ' +
            '/TASKS="previewdesktopicon"') `
        -Description 'Isolated upgrade' `
        -LogPath (Join-Path $smokeRoot 'upgrade.log')
    Assert-Exists -Path $normalStartMenu -Description 'Normal Start Menu link'
    Assert-Missing -Path $normalDesktop -Description 'Normal desktop link'
    $desktopLinksAfterUpgrade = @(
        Get-ChildItem -LiteralPath $desktop -Filter '*.lnk' -File)
    $previewLinks = @($desktopLinksAfterUpgrade | Where-Object {
        -not (Test-PathEquals $_.FullName $normalDesktop)
    })
    if ($previewLinks.Count -ne 1) {
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
            'Expected exactly one non-normal desktop link after upgrade; ' +
            "found $($previewLinks.Count).")
    }
    $previewDesktop = $previewLinks[0].FullName
    $shell = New-Object -ComObject WScript.Shell
    try {
        $previewShortcut = $shell.CreateShortcut($previewDesktop)
        if (-not (Test-PathEquals `
                $previewShortcut.TargetPath `
                $installedExecutable) -or
            -not [string]::Equals(
                $previewShortcut.Arguments.Trim(),
                '--preview',
                [System.StringComparison]::Ordinal)) {
            throw 'Preview desktop link has an unexpected target or arguments.'
        }
    }
    finally {
        if ($null -ne $shell) {
            [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject(
                $shell)
        }
    }
    $runValueAfterUpgrade = Get-ItemProperty `
        -LiteralPath $runRegistryPath `
        -Name $runValueName `
        -ErrorAction SilentlyContinue
    if ($null -ne $runValueAfterUpgrade) {
        throw 'Isolated startup value still exists after task deselection.'
    }
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
    Write-Host 'Smoke scenario passed: purge uninstall removes test settings.'
    Write-Host "Isolated installer root: $internalRoot"
    Write-Host "Isolated test ID: $internalTestId"
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($internalTestId)) {
        Remove-ItemProperty `
            -LiteralPath $runRegistryPath `
            -Name "CodexQuotaHud.InternalTest.$internalTestId" `
            -Force `
            -ErrorAction SilentlyContinue
    }
    if (-not [string]::IsNullOrWhiteSpace($uninstallRegistryPath)) {
        Remove-Item `
            -LiteralPath $uninstallRegistryPath `
            -Recurse `
            -Force `
            -ErrorAction SilentlyContinue
    }
    if ($cleanupAuthorized -and (Test-Path -LiteralPath $smokeRoot)) {
        Assert-NoReparsePoint -Path $smokeRoot -Boundary $systemTemp
        if (-not (Test-StrictDescendant $smokeRoot $systemTemp)) {
            throw 'Refusing cleanup outside system temporary.'
        }
        Remove-Item -LiteralPath $smokeRoot -Recurse -Force
    }

    if ($cleanupAuthorized) {
        Write-Host "Finally cleanup completed: $smokeRoot"
    }
}
