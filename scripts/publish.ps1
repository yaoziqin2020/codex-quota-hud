[CmdletBinding()]
param(
    [string] $ProjectPath,
    [string] $OutputPath,
    [string] $DotNetExecutable = 'dotnet',
    [switch] $InternalTestMode,
    [string] $InternalArgumentCapturePath,
    [int] $InternalPublisherExitCode,
    [switch] $InternalSkipFakeExecutable
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..'))
$defaultProjectPath = Join-Path `
    $repositoryRoot `
    'src\CodexQuotaHud.App\CodexQuotaHud.App.csproj'
$defaultOutputPath = Join-Path `
    $repositoryRoot `
    'artifacts\CodexQuotaHud-win-x64'

if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    $ProjectPath = $defaultProjectPath
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = $defaultOutputPath
}

$projectFullPath = [System.IO.Path]::GetFullPath($ProjectPath)
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$expectedOutputPath = [System.IO.Path]::GetFullPath($defaultOutputPath)

if (-not (Test-Path -LiteralPath $projectFullPath -PathType Leaf)) {
    throw "Project file does not exist: $projectFullPath"
}

if ($InternalTestMode) {
    $systemTemp = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath())
    if (-not $outputFullPath.StartsWith(
        $systemTemp,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Internal test output must stay inside the system temporary directory.'
    }
}
elseif (-not [string]::Equals(
    $outputFullPath,
    $expectedOutputPath,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Production publish output must be exactly: $expectedOutputPath"
}

if (-not $InternalTestMode -and
    (-not [string]::Equals($DotNetExecutable, 'dotnet',
        [System.StringComparison]::OrdinalIgnoreCase) -or
     -not [string]::IsNullOrWhiteSpace($InternalArgumentCapturePath) -or
     $InternalPublisherExitCode -ne 0 -or
     $InternalSkipFakeExecutable)) {
    throw 'Internal publisher hooks require -InternalTestMode.'
}

if (Test-Path -LiteralPath $outputFullPath) {
    $outputItem = Get-Item -LiteralPath $outputFullPath -Force
    if (($outputItem.Attributes -band
        [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Refusing to replace a reparse-point output directory: $outputFullPath"
    }

    Remove-Item -LiteralPath $outputFullPath -Recurse -Force
}

$publishArguments = @(
    'publish',
    $projectFullPath,
    '-c', 'Release',
    '-r', 'win-x64',
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-o', $outputFullPath
)

$oldCapture = $env:CODEX_HUD_CAPTURE_PATH
$oldExitCode = $env:CODEX_HUD_FAKE_EXIT_CODE
$oldSkipExecutable = $env:CODEX_HUD_SKIP_FAKE_EXE
try {
    if ($InternalTestMode) {
        $env:CODEX_HUD_CAPTURE_PATH = $InternalArgumentCapturePath
        if ($InternalPublisherExitCode -ne 0) {
            $env:CODEX_HUD_FAKE_EXIT_CODE =
                $InternalPublisherExitCode.ToString(
                    [System.Globalization.CultureInfo]::InvariantCulture)
        }
        else {
            Remove-Item Env:\CODEX_HUD_FAKE_EXIT_CODE -ErrorAction SilentlyContinue
        }

        $env:CODEX_HUD_SKIP_FAKE_EXE =
            if ($InternalSkipFakeExecutable) { '1' } else { '0' }
    }

    $global:LASTEXITCODE = 0
    & $DotNetExecutable @publishArguments
    $publishExitCode = $LASTEXITCODE
}
finally {
    $env:CODEX_HUD_CAPTURE_PATH = $oldCapture
    $env:CODEX_HUD_FAKE_EXIT_CODE = $oldExitCode
    $env:CODEX_HUD_SKIP_FAKE_EXE = $oldSkipExecutable
}

if ($publishExitCode -ne 0) {
    throw "dotnet publish failed with exit code $publishExitCode."
}

$expectedExecutable = Join-Path $outputFullPath 'CodexQuotaHud.App.exe'
if (-not (Test-Path -LiteralPath $expectedExecutable -PathType Leaf)) {
    throw "Published executable is missing: $expectedExecutable"
}

Write-Host "Published CodexQuotaHud to: $outputFullPath"
