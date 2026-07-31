using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace CodexQuotaHud.App.Tests.Packaging;

[Collection(PackagingScriptCollection.Name)]
public sealed class InstallerBuildTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task PackageRelease_CreatesSetupZipAndExactChecksums()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "release");

        var result = await RunPowerShellAsync(
            PackageScript,
            "-Version", "1.1.0",
            "-OutputPath", output,
            "-DotNetExecutable", CreateFakeDotNet(temp.Path),
            "-InnoCompilerPath", CreateFakeIscc(temp.Path),
            "-InternalTestMode");

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        var setup = Path.Combine(output, "CodexQuotaHud-Setup-v1.1.0.exe");
        var zip = Path.Combine(output, "CodexQuotaHud-v1.1.0-win-x64.zip");
        var checksums = Path.Combine(output, "SHA256SUMS.txt");
        Assert.True(File.Exists(setup));
        Assert.True(File.Exists(zip));
        Assert.True(File.Exists(checksums));

        var lines = (await File.ReadAllLinesAsync(checksums))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        Assert.Equal(
            new[]
            {
                $"{Sha256(setup)}  CodexQuotaHud-Setup-v1.1.0.exe",
                $"{Sha256(zip)}  CodexQuotaHud-v1.1.0-win-x64.zip",
            },
            lines);
        Assert.All(lines, line => Assert.Matches(
            "^[0-9a-f]{64}  CodexQuotaHud-(Setup-v1\\.1\\.0\\.exe|v1\\.1\\.0-win-x64\\.zip)$",
            line));

        using var archive = ZipFile.OpenRead(zip);
        var entries = archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .ToArray();
        Assert.Contains(
            "artifacts/CodexQuotaHud-win-x64/CodexQuotaHud.App.exe",
            entries);
        Assert.Contains("scripts/install.ps1", entries);
        Assert.Contains("scripts/uninstall.ps1", entries);
        Assert.Contains("README.md", entries);
        Assert.Contains("LICENSE", entries);
        Assert.DoesNotContain(
            entries,
            entry => entry.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                entry.Contains("Setup", StringComparison.OrdinalIgnoreCase));
        foreach (var scriptName in new[]
        {
            "scripts/install.ps1",
            "scripts/uninstall.ps1",
        })
        {
            var script = ReadZipEntry(archive, scriptName);
            Assert.DoesNotContain("InternalTest", script,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("LocalAppDataRoot", script,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("InternalShellRootPath", script,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void InstallerSources_SeparateProductionAndInternalTestHelpers()
    {
        var definition = File.ReadAllText(InnoDefinition);
        var productionHelper = Path.Combine(
            RepositoryRoot,
            "scripts",
            "installer-lifecycle-production.ps1");

        Assert.True(File.Exists(productionHelper));
        var productionSource = File.ReadAllText(productionHelper);
        Assert.DoesNotContain("InternalTest", productionSource,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LocalAppDataRoot", productionSource,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("InternalShellRootPath", productionSource,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Source: \"{#RepositoryRoot}\\scripts\\installer-lifecycle-production.ps1\"",
            definition,
            StringComparison.Ordinal);
        Assert.Contains("#ifdef InternalTestRoot", definition,
            StringComparison.Ordinal);
        Assert.Contains(
            "Source: \"{#RepositoryRoot}\\scripts\\installer-lifecycle.ps1\"",
            definition,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageRelease_CompilerFailureLeavesNoChecksumManifest()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "release");

        var result = await RunPowerShellAsync(
            PackageScript,
            "-Version", "1.1.0",
            "-OutputPath", output,
            "-DotNetExecutable", CreateFakeDotNet(temp.Path),
            "-InnoCompilerPath", CreateFakeIscc(temp.Path),
            "-InternalTestMode",
            "-InternalCompilerExitCode", "17");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ISCC.exe failed with exit code 17", result.CombinedOutput);
        Assert.False(File.Exists(Path.Combine(output, "SHA256SUMS.txt")));
    }

    [Fact]
    public async Task PackageRelease_MissingSetupLeavesNoChecksumManifest()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "release");

        var result = await RunPowerShellAsync(
            PackageScript,
            "-Version", "1.1.0",
            "-OutputPath", output,
            "-DotNetExecutable", CreateFakeDotNet(temp.Path),
            "-InnoCompilerPath", CreateFakeIscc(temp.Path),
            "-InternalTestMode",
            "-InternalSkipFakeSetup");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "Expected installer was not created",
            result.CombinedOutput,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(output, "SHA256SUMS.txt")));
    }

    [Fact]
    public async Task PackageRelease_MissingZipAfterSetupLeavesNoChecksumManifest()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "release");

        var result = await RunPowerShellAsync(
            PackageScript,
            "-Version", "1.1.0",
            "-OutputPath", output,
            "-DotNetExecutable", CreateFakeDotNet(temp.Path),
            "-InnoCompilerPath", CreateFakeIsccThatDeletesZip(temp.Path),
            "-InternalTestMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(
            Path.Combine(output, "CodexQuotaHud-Setup-v1.1.0.exe")));
        Assert.False(File.Exists(
            Path.Combine(output, "CodexQuotaHud-v1.1.0-win-x64.zip")));
        Assert.False(File.Exists(Path.Combine(output, "SHA256SUMS.txt")));
    }

    [Fact]
    public async Task PackageRelease_CleanupFailureRemovesAllManifestFiles()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "release");

        var result = await RunPowerShellAsync(
            PackageScript,
            "-Version", "1.1.0",
            "-OutputPath", output,
            "-DotNetExecutable", CreateFakeDotNet(temp.Path),
            "-InnoCompilerPath", CreateFakeIscc(temp.Path),
            "-InternalTestMode",
            "-InternalFailStageCleanup");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("stage cleanup", result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(output, "SHA256SUMS.txt")));
        Assert.Empty(Directory.Exists(output)
            ? Directory.GetFiles(output, "*SHA256SUMS*.tmp")
            : Array.Empty<string>());
    }

    [Fact]
    public async Task TestInstaller_RejectsNoncanonicalInstallerBeforeBuild()
    {
        using var temp = new TemporaryDirectory();
        var missing = Path.Combine(temp.Path, "CodexQuotaHud-Setup-v1.1.0.exe");

        var result = await RunPowerShellAsync(
            TestInstallerScript,
            "-Version", "1.1.0",
            "-InstallerPath", missing);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "InstallerPath must be exactly",
            result.CombinedOutput,
            StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(temp.Path));
    }

    [Fact]
    public async Task TestInstaller_RejectsCanonicalCandidateWithoutManifest()
    {
        using var temp = new TemporaryDirectory();
        var candidate = CreateFakeReleaseCandidate(
            temp.Path,
            manifestLine: null);

        var result = await RunPowerShellAsync(
            candidate.Script,
            "-Version", "1.1.0",
            "-InstallerPath", candidate.Installer);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Canonical checksum manifest does not exist",
            result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestInstaller_RejectsCanonicalCandidateWithStaleHash()
    {
        using var temp = new TemporaryDirectory();
        var candidate = CreateFakeReleaseCandidate(
            temp.Path,
            manifestLine:
                $"{new string('0', 64)}  CodexQuotaHud-Setup-v1.1.0.exe\n");

        var result = await RunPowerShellAsync(
            candidate.Script,
            "-Version", "1.1.0",
            "-InstallerPath", candidate.Installer);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("hash does not match SHA256SUMS.txt",
            result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestInstaller_RejectsInstallerFilenameForDifferentVersion()
    {
        using var temp = new TemporaryDirectory();
        var wrongVersion = Path.Combine(
            temp.Path,
            "CodexQuotaHud-Setup-v9.9.9.exe");
        await File.WriteAllTextAsync(wrongVersion, "MZ fake setup");

        var result = await RunPowerShellAsync(
            TestInstallerScript,
            "-Version", "1.1.0",
            "-InstallerPath", wrongVersion);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "Installer filename must be CodexQuotaHud-Setup-v1.1.0.exe",
            result.CombinedOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TestInstallerScript_IsAsciiSafeForWindowsPowerShellFive()
    {
        var script = File.ReadAllText(TestInstallerScript);

        Assert.DoesNotContain(script, character => character > 0x7f);
    }

    [Fact]
    public void TestInstallerScript_RequiresSnapshotsAndCleanupPostconditions()
    {
        var script = File.ReadAllText(TestInstallerScript);

        Assert.Contains("productionRunSnapshot", script,
            StringComparison.Ordinal);
        Assert.Contains("productionUninstallSnapshot", script,
            StringComparison.Ordinal);
        Assert.Contains("Internal uninstall key", script,
            StringComparison.Ordinal);
        Assert.Contains("Cleanup postcondition failed", script,
            StringComparison.Ordinal);
        Assert.Contains("Production registry snapshot changed", script,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildInstaller_PassesExactDefinesAndOutputToIscc()
    {
        using var temp = new TemporaryDirectory();
        var published = CreatePublishedDirectory(temp.Path);
        var output = Path.Combine(temp.Path, "release");
        var capture = Path.Combine(temp.Path, "iscc-arguments.json");
        var fakeIscc = CreateFakeIscc(temp.Path);

        var result = await RunPowerShellAsync(
            BuildScript,
            "-Version", "1.1.0",
            "-PublishedPath", published,
            "-OutputPath", output,
            "-InnoCompilerPath", fakeIscc,
            "-InternalTestMode",
            "-InternalArgumentCapturePath", capture);

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.True(File.Exists(
            Path.Combine(output, "CodexQuotaHud-Setup-v1.1.0.exe")));

        var arguments = JsonSerializer.Deserialize<string[]>(
            await File.ReadAllTextAsync(capture))!;
        Assert.Contains("/DAppVersion=1.1.0", arguments);
        Assert.Contains($"/DPublishedDir={Path.GetFullPath(published)}", arguments);
        Assert.Contains($"/DRepositoryRoot={RepositoryRoot}", arguments);
        var chineseLanguageFile = Path.Combine(
            RepositoryRoot,
            "installer",
            "Languages",
            "ChineseSimplified.isl");
        Assert.True(File.Exists(chineseLanguageFile));
        Assert.Contains(
            $"/DChineseLanguageFile={chineseLanguageFile}",
            arguments);
        Assert.Contains($"/O{Path.GetFullPath(output)}", arguments);
        Assert.Contains(Path.Combine("installer", "CodexQuotaHud.iss"), arguments);

        var internalTestId = GetDefine(arguments, "InternalTestId");
        Assert.True(Guid.TryParse(internalTestId, out _));
        var internalTestRoot = GetDefine(arguments, "InternalTestRoot");
        Assert.StartsWith(
            Path.GetFullPath(Path.GetTempPath()),
            Path.GetFullPath(internalTestRoot),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            internalTestId,
            internalTestRoot,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildInstaller_InternalTestDefinesAreUniquePerBuild()
    {
        using var temp = new TemporaryDirectory();
        var firstCapture = Path.Combine(temp.Path, "first.json");
        var secondCapture = Path.Combine(temp.Path, "second.json");
        var published = CreatePublishedDirectory(temp.Path);
        var fakeIscc = CreateFakeIscc(temp.Path);

        var first = await RunPowerShellAsync(
            BuildScript,
            "-Version", "1.1.0",
            "-PublishedPath", published,
            "-OutputPath", Path.Combine(temp.Path, "first-release"),
            "-InnoCompilerPath", fakeIscc,
            "-InternalTestMode",
            "-InternalArgumentCapturePath", firstCapture);
        var second = await RunPowerShellAsync(
            BuildScript,
            "-Version", "1.1.0",
            "-PublishedPath", published,
            "-OutputPath", Path.Combine(temp.Path, "second-release"),
            "-InnoCompilerPath", fakeIscc,
            "-InternalTestMode",
            "-InternalArgumentCapturePath", secondCapture);

        Assert.True(first.ExitCode == 0, first.CombinedOutput);
        Assert.True(second.ExitCode == 0, second.CombinedOutput);
        var firstArguments = JsonSerializer.Deserialize<string[]>(
            await File.ReadAllTextAsync(firstCapture))!;
        var secondArguments = JsonSerializer.Deserialize<string[]>(
            await File.ReadAllTextAsync(secondCapture))!;
        Assert.NotEqual(
            GetDefine(firstArguments, "InternalTestId"),
            GetDefine(secondArguments, "InternalTestId"));
        Assert.NotEqual(
            GetDefine(firstArguments, "InternalTestRoot"),
            GetDefine(secondArguments, "InternalTestRoot"));
    }

    [Fact]
    public async Task BuildInstaller_FailsWhenCompilerFails()
    {
        using var temp = new TemporaryDirectory();

        var result = await RunPowerShellAsync(
            BuildScript,
            "-Version", "1.1.0",
            "-PublishedPath", CreatePublishedDirectory(temp.Path),
            "-OutputPath", Path.Combine(temp.Path, "release"),
            "-InnoCompilerPath", CreateFakeIscc(temp.Path),
            "-InternalTestMode",
            "-InternalCompilerExitCode", "17");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("17", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildInstaller_FailsWhenSetupOutputIsMissing()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "release");

        var result = await RunPowerShellAsync(
            BuildScript,
            "-Version", "1.1.0",
            "-PublishedPath", CreatePublishedDirectory(temp.Path),
            "-OutputPath", output,
            "-InnoCompilerPath", CreateFakeIscc(temp.Path),
            "-InternalTestMode",
            "-InternalSkipFakeSetup");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "CodexQuotaHud-Setup-v1.1.0.exe",
            result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("1.1")]
    [InlineData("v1.1.0")]
    public async Task BuildInstaller_RejectsInvalidVersion(string version)
    {
        using var temp = new TemporaryDirectory();

        var result = await RunPowerShellAsync(
            BuildScript,
            "-Version", version,
            "-PublishedPath", CreatePublishedDirectory(temp.Path),
            "-OutputPath", Path.Combine(temp.Path, "release"),
            "-InnoCompilerPath", CreateFakeIscc(temp.Path),
            "-InternalTestMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Version", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildInstaller_ProductionRejectsInternalHooks()
    {
        using var temp = new TemporaryDirectory();

        var result = await RunPowerShellAsync(
            BuildScript,
            "-Version", "1.1.0",
            "-PublishedPath", CreatePublishedDirectory(temp.Path),
            "-InnoCompilerPath", CreateFakeIscc(temp.Path),
            "-InternalCompilerExitCode", "17");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "Internal installer builder hooks require -InternalTestMode",
            result.CombinedOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildInstaller_ProductionRejectsNoncanonicalOutputPath()
    {
        using var temp = new TemporaryDirectory();

        var result = await RunPowerShellAsync(
            BuildScript,
            "-Version", "1.1.0",
            "-PublishedPath", CreatePublishedDirectory(temp.Path),
            "-OutputPath", Path.Combine(temp.Path, "release"));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            "Production installer output must be exactly",
            result.CombinedOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InnoDefinition_LaunchesOnlyAfterCommitAndWiresLegacyCompensation()
    {
        var definition = File.ReadAllText(InnoDefinition);
        var normalized = definition.Replace("\r\n", "\n", StringComparison.Ordinal);
        var code = normalized[normalized.IndexOf("[Code]", StringComparison.Ordinal)..];

        Assert.DoesNotContain("\n[Run]\n", normalized, StringComparison.Ordinal);
        Assert.DoesNotContain("postinstall", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "LaunchAfterInstallCheckBox.Checked := True",
            code,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "function NextButtonClick(CurPageID: Integer): Boolean;",
            code,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            code.Split("\n", StringSplitOptions.None).Count(
                line => string.Equals(
                    line,
                    "    LaunchInstalledApp();",
                    StringComparison.Ordinal)));

        var curStepStart = code.IndexOf(
            "procedure CurStepChanged(CurStep: TSetupStep);",
            StringComparison.Ordinal);
        var curStepEnd = code.IndexOf(
            "procedure DeinitializeSetup();",
            curStepStart,
            StringComparison.Ordinal);
        Assert.True(curStepStart >= 0 && curStepEnd > curStepStart);
        var curStep = code[curStepStart..curStepEnd];
        var commit = curStep.IndexOf("'CommitInstall'", StringComparison.Ordinal);
        var failureExit = curStep.IndexOf(
            "RaiseException(ErrorText);",
            commit,
            StringComparison.Ordinal);
        var completed = curStep.IndexOf(
            "InstallCompleted := True;",
            commit,
            StringComparison.Ordinal);
        var launch = curStep.IndexOf(
            "LaunchInstalledApp();",
            commit,
            StringComparison.Ordinal);
        Assert.True(
            commit >= 0 && failureExit > commit && completed > failureExit &&
            launch > completed);

        var snapshot = code.IndexOf("'SnapshotLegacyState'", StringComparison.Ordinal);
        var removeSelections = code.IndexOf(
            "RemoveManagedSelections();", snapshot, StringComparison.Ordinal);
        Assert.True(snapshot >= 0 && removeSelections > snapshot);

        var compensate = code.IndexOf(
            "'CompensateLegacyInstall'", StringComparison.Ordinal);
        var rollback = code.IndexOf("'RollbackInstall'", compensate,
            StringComparison.Ordinal);
        Assert.True(compensate >= 0 && rollback > compensate);

        Assert.Contains("CustomMessage('GuidCreateFailure')", code,
            StringComparison.Ordinal);
        Assert.Contains("CustomMessage('PowerShellStartFailure')", code,
            StringComparison.Ordinal);
        Assert.Contains("CustomMessage('HelperCopyFailure')", code,
            StringComparison.Ordinal);
    }

    [Fact]
    public void InnoDefinition_InternalBuildRedirectsAllMachineArtifacts()
    {
        var definition = File.ReadAllText(InnoDefinition)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("#ifdef InternalTestRoot", definition,
            StringComparison.Ordinal);
        Assert.Contains(
            "#define EffectiveAppId \"CQH.Test.\" + InternalTestId",
            definition,
            StringComparison.Ordinal);
        Assert.Contains(
            "DefaultDirName={#InternalTestRoot}\\LocalAppData\\Programs\\CodexQuotaHud",
            definition,
            StringComparison.Ordinal);
        Assert.Contains(
            "Name: \"{#InternalTestRoot}\\Shell\\StartMenu\\Programs\\Codex Quota HUD\"",
            definition,
            StringComparison.Ordinal);
        Assert.Contains(
            "Name: \"{#InternalTestRoot}\\Shell\\Desktop\\Codex Quota HUD\"",
            definition,
            StringComparison.Ordinal);
        Assert.Contains(
            "ValueName: \"CodexQuotaHud.InternalTest.{#InternalTestId}\"",
            definition,
            StringComparison.Ordinal);
        Assert.Contains(
            "'Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\CQH.Test.{#InternalTestId}_is1'",
            definition,
            StringComparison.Ordinal);
        Assert.Contains(
            "' -InternalTestMode -LocalAppDataRoot ' +",
            definition,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (Action = 'SnapshotLegacyState') or",
            definition,
            StringComparison.Ordinal);
        Assert.Contains("HasCommandLineParameter('/PURGESETTINGS')", definition,
            StringComparison.Ordinal);
    }

    private static string CreatePublishedDirectory(string tempRoot)
    {
        var path = Directory.CreateDirectory(
            Path.Combine(tempRoot, "published")).FullName;
        File.WriteAllText(
            Path.Combine(path, "CodexQuotaHud.App.exe"),
            "MZ fake");
        return path;
    }

    private static string CreateFakeIscc(string directory)
    {
        var path = Path.Combine(directory, "fake-iscc.ps1");
        File.WriteAllText(
            path,
            """
            param(
                [Parameter(ValueFromRemainingArguments = $true)]
                [string[]] $RemainingArguments
            )

            if ($env:CODEX_HUD_INSTALLER_CAPTURE_PATH) {
                $RemainingArguments |
                    ConvertTo-Json -Compress |
                    Set-Content -LiteralPath `
                        $env:CODEX_HUD_INSTALLER_CAPTURE_PATH -Encoding UTF8
            }

            if ($env:CODEX_HUD_INSTALLER_FAKE_EXIT_CODE) {
                exit [int]$env:CODEX_HUD_INSTALLER_FAKE_EXIT_CODE
            }

            if ($env:CODEX_HUD_INSTALLER_SKIP_FAKE_SETUP -ne '1') {
                $outputArgument = $RemainingArguments |
                    Where-Object { $_.StartsWith('/O') } |
                    Select-Object -First 1
                $versionArgument = $RemainingArguments |
                    Where-Object { $_.StartsWith('/DAppVersion=') } |
                    Select-Object -First 1
                $output = $outputArgument.Substring(2)
                $version = $versionArgument.Substring('/DAppVersion='.Length)
                New-Item -ItemType Directory -Path $output -Force | Out-Null
                Set-Content -LiteralPath (
                    Join-Path $output "CodexQuotaHud-Setup-v$version.exe"
                ) -Value 'MZ fake setup' -Encoding Ascii
            }
            """);
        return path;
    }

    private static string CreateFakeIsccThatDeletesZip(string directory)
    {
        var path = Path.Combine(directory, "fake-iscc-delete-zip.ps1");
        File.WriteAllText(
            path,
            """
            param(
                [Parameter(ValueFromRemainingArguments = $true)]
                [string[]] $RemainingArguments
            )

            $outputArgument = $RemainingArguments |
                Where-Object { $_.StartsWith('/O') } |
                Select-Object -First 1
            $versionArgument = $RemainingArguments |
                Where-Object { $_.StartsWith('/DAppVersion=') } |
                Select-Object -First 1
            $output = $outputArgument.Substring(2)
            $version = $versionArgument.Substring('/DAppVersion='.Length)
            New-Item -ItemType Directory -Path $output -Force | Out-Null
            Set-Content -LiteralPath (
                Join-Path $output "CodexQuotaHud-Setup-v$version.exe"
            ) -Value 'MZ fake setup' -Encoding Ascii
            Remove-Item -LiteralPath (
                Join-Path $output "CodexQuotaHud-v$version-win-x64.zip"
            ) -Force -ErrorAction SilentlyContinue
            """);
        return path;
    }

    private static string CreateFakeDotNet(string directory)
    {
        var path = Path.Combine(directory, "fake-dotnet.ps1");
        File.WriteAllText(
            path,
            """
            param(
                [Parameter(ValueFromRemainingArguments = $true)]
                [string[]] $RemainingArguments
            )

            if ($env:CODEX_HUD_CAPTURE_PATH) {
                $RemainingArguments |
                    ConvertTo-Json -Compress |
                    Set-Content -LiteralPath `
                        $env:CODEX_HUD_CAPTURE_PATH -Encoding UTF8
            }

            if ($env:CODEX_HUD_FAKE_EXIT_CODE) {
                exit [int]$env:CODEX_HUD_FAKE_EXIT_CODE
            }

            if ($env:CODEX_HUD_SKIP_FAKE_EXE -ne '1') {
                $outputIndex = [Array]::IndexOf($RemainingArguments, '-o')
                $output = $RemainingArguments[$outputIndex + 1]
                New-Item -ItemType Directory -Path $output -Force | Out-Null
                Set-Content -LiteralPath (
                    Join-Path $output 'CodexQuotaHud.App.exe'
                ) -Value 'MZ fake app' -Encoding Ascii
            }
            """);
        return path;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ReadZipEntry(ZipArchive archive, string name)
    {
        var entry = Assert.Single(archive.Entries, item =>
            string.Equals(
                item.FullName.Replace('\\', '/'),
                name,
                StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static FakeReleaseCandidate CreateFakeReleaseCandidate(
        string tempRoot,
        string? manifestLine)
    {
        var repository = Directory.CreateDirectory(
            Path.Combine(tempRoot, "repository")).FullName;
        var scripts = Directory.CreateDirectory(
            Path.Combine(repository, "scripts")).FullName;
        var release = Directory.CreateDirectory(
            Path.Combine(repository, "artifacts", "release")).FullName;
        var script = Path.Combine(scripts, "test-installer.ps1");
        File.Copy(TestInstallerScript, script);
        var installer = Path.Combine(
            release,
            "CodexQuotaHud-Setup-v1.1.0.exe");
        File.WriteAllText(installer, "MZ fake release candidate");
        if (manifestLine is not null)
        {
            File.WriteAllText(
                Path.Combine(release, "SHA256SUMS.txt"),
                manifestLine);
        }
        return new FakeReleaseCandidate(script, installer);
    }

    private static string GetDefine(IEnumerable<string> arguments, string name)
    {
        var prefix = $"/D{name}=";
        return Assert.Single(arguments, argument =>
            argument.StartsWith(prefix, StringComparison.Ordinal))[prefix.Length..];
    }

    private static string BuildScript =>
        Path.Combine(RepositoryRoot, "scripts", "build-installer.ps1");

    private static string PackageScript =>
        Path.Combine(RepositoryRoot, "scripts", "package-release.ps1");

    private static string TestInstallerScript =>
        Path.Combine(RepositoryRoot, "scripts", "test-installer.ps1");

    private static string InnoDefinition =>
        Path.Combine(RepositoryRoot, "installer", "CodexQuotaHud.iss");

    private static async Task<ProcessResult> RunPowerShellAsync(
        string script,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[]
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            script,
        }.Concat(arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await standardOutput,
            await standardError);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CodexQuotaHud.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the CodexQuotaHud repository root.");
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput => StandardOutput + StandardError;
    }

    private sealed record FakeReleaseCandidate(
        string Script,
        string Installer);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "CodexQuotaHud.Tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
