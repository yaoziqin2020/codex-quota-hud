[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version = '1.1.0',
    [string] $PublishedPath,
    [string] $OutputPath,
    [string] $InnoCompilerPath,
    [switch] $InternalTestMode,
    [string] $InternalArgumentCapturePath,
    [int] $InternalCompilerExitCode,
    [switch] $InternalSkipFakeSetup
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$defaultPublishedPath = Join-Path `
    $repositoryRoot `
    'artifacts\CodexQuotaHud-win-x64'
$defaultOutputPath = Join-Path $repositoryRoot 'artifacts\release'
$definitionPath = Join-Path `
    $repositoryRoot `
    'installer\CodexQuotaHud.iss'
$chineseLanguageFile = Join-Path `
    $repositoryRoot `
    'installer\Languages\ChineseSimplified.isl'

if ([string]::IsNullOrWhiteSpace($PublishedPath)) {
    $PublishedPath = $defaultPublishedPath
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = $defaultOutputPath
}

$publishedFullPath = [System.IO.Path]::GetFullPath($PublishedPath)
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$expectedProductionOutput = [System.IO.Path]::GetFullPath(
    $defaultOutputPath)
$publishedExecutable = Join-Path `
    $publishedFullPath `
    'CodexQuotaHud.App.exe'
$expectedSetup = Join-Path `
    $outputFullPath `
    "CodexQuotaHud-Setup-v$Version.exe"

if (-not (Test-Path -LiteralPath $definitionPath -PathType Leaf)) {
    throw "Inno Setup definition does not exist: $definitionPath"
}

if (-not (Test-Path -LiteralPath $chineseLanguageFile -PathType Leaf)) {
    throw "Chinese language file does not exist: $chineseLanguageFile"
}

if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable does not exist: $publishedExecutable"
}

if (-not $InternalTestMode -and
    (-not [string]::IsNullOrWhiteSpace($InnoCompilerPath) -or
     -not [string]::IsNullOrWhiteSpace($InternalArgumentCapturePath) -or
     $InternalCompilerExitCode -ne 0 -or
     $InternalSkipFakeSetup)) {
    throw 'Internal installer builder hooks require -InternalTestMode.'
}

if (-not $InternalTestMode -and
    -not [string]::Equals(
        $outputFullPath,
        $expectedProductionOutput,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Production installer output must be exactly: $expectedProductionOutput"
}

if ($InternalTestMode) {
    $systemTemp = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath())
    if (-not $outputFullPath.StartsWith(
            $systemTemp,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        [string]::Equals(
            $outputFullPath,
            $systemTemp,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw (
            'Internal installer output must be a unique directory inside ' +
            'the system temporary directory.')
    }
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath)) {
    $compilerCandidates = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe'
    )
    $InnoCompilerPath = $compilerCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InnoCompilerPath) -or
    -not (Test-Path -LiteralPath $InnoCompilerPath -PathType Leaf)) {
    throw 'Inno Setup 6 compiler was not found.'
}

$compilerFullPath = [System.IO.Path]::GetFullPath($InnoCompilerPath)
New-Item -ItemType Directory -Path $outputFullPath -Force | Out-Null
if (Test-Path -LiteralPath $expectedSetup) {
    Remove-Item -LiteralPath $expectedSetup -Force
}

$compilerArguments = @(
    "/DAppVersion=$Version",
    "/DPublishedDir=$publishedFullPath",
    "/DRepositoryRoot=$repositoryRoot",
    "/DChineseLanguageFile=$chineseLanguageFile",
    "/O$outputFullPath",
    'installer\CodexQuotaHud.iss'
)

if ($InternalTestMode) {
    $internalTestId = [Guid]::NewGuid().ToString('D')
    $internalTestRoot = Join-Path `
        $outputFullPath `
        "isolated-$internalTestId"
    $compilerArguments = @(
        $compilerArguments[0..3]
        "/DInternalTestId=$internalTestId"
        "/DInternalTestRoot=$internalTestRoot"
        $compilerArguments[4..($compilerArguments.Count - 1)]
    )

    if (-not [string]::IsNullOrWhiteSpace(
        $InternalArgumentCapturePath)) {
        $captureFullPath = [System.IO.Path]::GetFullPath(
            $InternalArgumentCapturePath)
        $captureParent = Split-Path -Path $captureFullPath -Parent
        if (-not $captureFullPath.StartsWith(
                $systemTemp,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Internal argument capture must stay inside system temporary.'
        }

        New-Item -ItemType Directory -Path $captureParent -Force | Out-Null
        $captureJson = ConvertTo-Json `
            -InputObject @($compilerArguments) `
            -Compress
        [System.IO.File]::WriteAllText(
            $captureFullPath,
            $captureJson,
            [System.Text.UTF8Encoding]::new($false))
    }
}

$oldCapture = $env:CODEX_HUD_INSTALLER_CAPTURE_PATH
$oldExitCode = $env:CODEX_HUD_INSTALLER_FAKE_EXIT_CODE
$oldSkipSetup = $env:CODEX_HUD_INSTALLER_SKIP_FAKE_SETUP
try {
    if ($InternalTestMode) {
        $env:CODEX_HUD_INSTALLER_CAPTURE_PATH =
            $InternalArgumentCapturePath
        if ($InternalCompilerExitCode -ne 0) {
            $env:CODEX_HUD_INSTALLER_FAKE_EXIT_CODE =
                $InternalCompilerExitCode.ToString(
                    [System.Globalization.CultureInfo]::InvariantCulture)
        }
        else {
            Remove-Item `
                Env:\CODEX_HUD_INSTALLER_FAKE_EXIT_CODE `
                -ErrorAction SilentlyContinue
        }

        $env:CODEX_HUD_INSTALLER_SKIP_FAKE_SETUP =
            if ($InternalSkipFakeSetup) { '1' } else { '0' }
    }

    Push-Location $repositoryRoot
    try {
        $global:LASTEXITCODE = 0
        if ($InternalTestMode -and
            [string]::Equals(
                [System.IO.Path]::GetExtension($compilerFullPath),
                '.ps1',
                [System.StringComparison]::OrdinalIgnoreCase)) {
            & powershell.exe `
                -NoProfile `
                -NonInteractive `
                -ExecutionPolicy Bypass `
                -File $compilerFullPath `
                @compilerArguments
        }
        else {
            & $compilerFullPath @compilerArguments
        }

        $compilerExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
}
finally {
    $env:CODEX_HUD_INSTALLER_CAPTURE_PATH = $oldCapture
    $env:CODEX_HUD_INSTALLER_FAKE_EXIT_CODE = $oldExitCode
    $env:CODEX_HUD_INSTALLER_SKIP_FAKE_SETUP = $oldSkipSetup
}

if ($compilerExitCode -ne 0) {
    throw "ISCC.exe failed with exit code $compilerExitCode."
}

if (-not (Test-Path -LiteralPath $expectedSetup -PathType Leaf)) {
    throw "Expected installer was not created: $expectedSetup"
}

Write-Output "Installer created: $expectedSetup"
