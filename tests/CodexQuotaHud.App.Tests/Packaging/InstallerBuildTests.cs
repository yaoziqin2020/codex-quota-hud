using System.Diagnostics;
using System.Text.Json;

namespace CodexQuotaHud.App.Tests.Packaging;

[Collection(PackagingScriptCollection.Name)]
public sealed class InstallerBuildTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

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

    private static string BuildScript =>
        Path.Combine(RepositoryRoot, "scripts", "build-installer.ps1");

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
