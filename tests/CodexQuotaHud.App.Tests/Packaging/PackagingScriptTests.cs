using System.Diagnostics;
using System.Text.Json;

namespace CodexQuotaHud.App.Tests.Packaging;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PackagingScriptCollection
{
    public const string Name = "Packaging scripts";
}

[Collection(PackagingScriptCollection.Name)]
public sealed class PackagingScriptTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task Publish_UsesSelfContainedSingleFileWinX64Contract()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "published");
        var capture = Path.Combine(temp.Path, "dotnet-arguments.json");
        var fakeDotNet = CreateFakeDotNet(temp.Path);

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-Version", "1.1.0",
            "-ProjectPath", Path.Combine(RepositoryRoot, "src", "CodexQuotaHud.App",
                "CodexQuotaHud.App.csproj"),
            "-OutputPath", output,
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode",
            "-InternalArgumentCapturePath", capture);

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.True(File.Exists(Path.Combine(output, "CodexQuotaHud.App.exe")));

        var arguments = JsonSerializer.Deserialize<string[]>(
            await File.ReadAllTextAsync(capture))!;
        Assert.Contains("publish", arguments);
        AssertContainsPair(arguments, "-c", "Release");
        AssertContainsPair(arguments, "-r", "win-x64");
        AssertContainsPair(arguments, "--self-contained", "true");
        Assert.Contains("-p:PublishSingleFile=true", arguments);
        Assert.Contains("-p:IncludeNativeLibrariesForSelfExtract=true", arguments);
        Assert.Contains("-p:Version=1.1.0", arguments);
        Assert.Contains("-p:FileVersion=1.1.0.0", arguments);
        Assert.Contains("-p:AssemblyVersion=1.1.0.0", arguments);
        AssertContainsPair(arguments, "-o", Path.GetFullPath(output));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("1.1")]
    [InlineData("v1.1.0")]
    [InlineData("1.1.0-beta")]
    public async Task Publish_RejectsNonReleaseVersion(string version)
    {
        using var temp = new TemporaryDirectory();
        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-Version", version,
            "-OutputPath", Path.Combine(temp.Path, "published"),
            "-InternalTestMode");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task Publish_FailsWhenDotNetPublishFails()
    {
        using var temp = new TemporaryDirectory();
        var fakeDotNet = CreateFakeDotNet(temp.Path);

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-ProjectPath", Path.Combine(RepositoryRoot, "src", "CodexQuotaHud.App",
                "CodexQuotaHud.App.csproj"),
            "-OutputPath", Path.Combine(temp.Path, "published"),
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode",
            "-InternalPublisherExitCode", "17");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("17", result.CombinedOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_FailsWhenExpectedExecutableIsMissing()
    {
        using var temp = new TemporaryDirectory();
        var fakeDotNet = CreateFakeDotNet(temp.Path);

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-ProjectPath", Path.Combine(RepositoryRoot, "src", "CodexQuotaHud.App",
                "CodexQuotaHud.App.csproj"),
            "-OutputPath", Path.Combine(temp.Path, "published"),
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode",
            "-InternalSkipFakeExecutable");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CodexQuotaHud.App.exe", result.CombinedOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Install_StagesPayloadAndPlansOnlyExactProcessStartupAndRunValue()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var published = Directory.CreateDirectory(
            Path.Combine(temp.Path, "published")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(published, "CodexQuotaHud.App.exe"), "new executable");
        await File.WriteAllTextAsync(Path.Combine(published, "payload.dat"), "new payload");

        var target = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs", "CodexQuotaHud")).FullName;
        await File.WriteAllTextAsync(Path.Combine(target, "old.dat"), "old payload");
        var unrelated = Path.Combine(temp.Path, "Other", "CodexQuotaHud.App.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
        await File.WriteAllTextAsync(unrelated, "unrelated");

        var processSnapshot = Path.Combine(temp.Path, "processes.json");
        await File.WriteAllTextAsync(
            processSnapshot,
            JsonSerializer.Serialize(new[]
            {
                new { ProcessId = 101, Name = "CodexQuotaHud.App.exe",
                    ExecutablePath = Path.Combine(target, "CodexQuotaHud.App.exe") },
                new { ProcessId = 202, Name = "CodexQuotaHud.App.exe",
                    ExecutablePath = unrelated },
                new { ProcessId = 303, Name = "notepad.exe",
                    ExecutablePath = Path.Combine(target, "CodexQuotaHud.App.exe") },
            }));
        var actionLog = Path.Combine(temp.Path, "install-actions.json");

        var result = await RunPowerShellAsync(
            Script("install.ps1"),
            "-PublishedPath", published,
            "-InternalTestMode",
            "-InternalLocalAppDataRoot", localAppData,
            "-InternalProcessSnapshotPath", processSnapshot,
            "-InternalActionLogPath", actionLog);

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.False(File.Exists(Path.Combine(target, "old.dat")));
        Assert.Equal("new payload",
            await File.ReadAllTextAsync(Path.Combine(target, "payload.dat")));
        Assert.DoesNotContain(
            Directory.GetDirectories(
                Path.GetDirectoryName(target)!,
                "*",
                SearchOption.TopDirectoryOnly),
            directory =>
                Path.GetFileName(directory).StartsWith(
                    "CodexQuotaHud.staging.",
                    StringComparison.OrdinalIgnoreCase) ||
                Path.GetFileName(directory).StartsWith(
                    "CodexQuotaHud.backup.",
                    StringComparison.OrdinalIgnoreCase));

        using var actions = JsonDocument.Parse(await File.ReadAllTextAsync(actionLog));
        var actionItems = actions.RootElement.EnumerateArray().ToArray();
        var stopped = SingleAction(actionItems, "StopProcess");
        Assert.Equal(101, stopped.GetProperty("ProcessId").GetInt32());
        var waited = SingleAction(actionItems, "WaitForProcessExit");
        Assert.Equal(101, waited.GetProperty("ProcessId").GetInt32());
        Assert.Equal(
            Path.Combine(target, "CodexQuotaHud.App.exe"),
            waited.GetProperty("ExecutablePath").GetString());
        Assert.True(
            Array.FindIndex(actionItems, item =>
                item.GetProperty("Action").GetString() == "StopProcess") <
            Array.FindIndex(actionItems, item =>
                item.GetProperty("Action").GetString() == "WaitForProcessExit"));

        var runValue = SingleAction(actionItems, "SetRunValue");
        Assert.Equal("CodexQuotaHud", runValue.GetProperty("Name").GetString());
        Assert.Equal(
            $"\"{Path.Combine(target, "CodexQuotaHud.App.exe")}\" --background",
            runValue.GetProperty("Value").GetString());

        var start = SingleAction(actionItems, "StartProcess");
        Assert.Equal(Path.Combine(target, "CodexQuotaHud.App.exe"),
            start.GetProperty("FilePath").GetString());
        Assert.Equal("--background", start.GetProperty("Arguments").GetString());
        Assert.Equal("Hidden", start.GetProperty("WindowStyle").GetString());
    }

    [Fact]
    public async Task Install_RejectsTargetOtherThanExactApplicationDirectory()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var published = Directory.CreateDirectory(
            Path.Combine(temp.Path, "published")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(published, "CodexQuotaHud.App.exe"), "new executable");
        var parent = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var marker = Path.Combine(parent, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep");

        var result = await RunPowerShellAsync(
            Script("install.ps1"),
            "-PublishedPath", published,
            "-InternalTestMode",
            "-InternalLocalAppDataRoot", localAppData,
            "-InternalTargetPath", parent);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task Uninstall_RemovesOnlyExactTargetAndOnlyItsRunValue()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var target = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs", "CodexQuotaHud")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(target, "CodexQuotaHud.App.exe"), "installed");
        var sibling = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs", "KeepMe")).FullName;
        var siblingMarker = Path.Combine(sibling, "keep.txt");
        await File.WriteAllTextAsync(siblingMarker, "keep");

        var processSnapshot = Path.Combine(temp.Path, "processes.json");
        await File.WriteAllTextAsync(
            processSnapshot,
            JsonSerializer.Serialize(new[]
            {
                new { ProcessId = 404, Name = "CodexQuotaHud.App.exe",
                    ExecutablePath = Path.Combine(target, "CodexQuotaHud.App.exe") },
                new { ProcessId = 505, Name = "CodexQuotaHud.App.exe",
                    ExecutablePath = Path.Combine(sibling, "CodexQuotaHud.App.exe") },
            }));
        var actionLog = Path.Combine(temp.Path, "uninstall-actions.json");

        var result = await RunPowerShellAsync(
            Script("uninstall.ps1"),
            "-InternalTestMode",
            "-InternalLocalAppDataRoot", localAppData,
            "-InternalProcessSnapshotPath", processSnapshot,
            "-InternalActionLogPath", actionLog);

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.False(Directory.Exists(target));
        Assert.True(File.Exists(siblingMarker));

        using var actions = JsonDocument.Parse(await File.ReadAllTextAsync(actionLog));
        var actionItems = actions.RootElement.EnumerateArray().ToArray();
        var stopped = SingleAction(actionItems, "StopProcess");
        Assert.Equal(404, stopped.GetProperty("ProcessId").GetInt32());
        var removedRunValue = SingleAction(actionItems, "RemoveRunValue");
        Assert.Equal("CodexQuotaHud",
            removedRunValue.GetProperty("Name").GetString());
    }

    [Fact]
    public async Task Uninstall_RejectsParentTargetWithoutDeletingIt()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var parent = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var marker = Path.Combine(parent, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep");

        var result = await RunPowerShellAsync(
            Script("uninstall.ps1"),
            "-InternalTestMode",
            "-InternalLocalAppDataRoot", localAppData,
            "-InternalTargetPath", parent);

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public async Task Uninstall_RejectsReparsePointTarget()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var outside = Directory.CreateDirectory(Path.Combine(temp.Path, "outside")).FullName;
        var outsideMarker = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(outsideMarker, "keep");
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var target = Path.Combine(programs, "CodexQuotaHud");
        var linkResult = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", target, outside);
        Assert.Equal(0, linkResult.ExitCode);

        try
        {
            var result = await RunPowerShellAsync(
                Script("uninstall.ps1"),
                "-InternalTestMode",
                "-InternalLocalAppDataRoot", localAppData);

            Assert.NotEqual(0, result.ExitCode);
            Assert.True(File.Exists(outsideMarker));
        }
        finally
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target);
            }
        }
    }

    private static Func<JsonElement, bool> ActionIs(string expected) =>
        item => string.Equals(
            item.GetProperty("Action").GetString(),
            expected,
            StringComparison.Ordinal);

    private static JsonElement SingleAction(
        IEnumerable<JsonElement> actions,
        string expected)
    {
        var matches = actions.Where(ActionIs(expected)).ToArray();
        Assert.True(
            matches.Length == 1,
            $"Expected one {expected} action, found {matches.Length}.");
        return matches[0];
    }

    private static void AssertContainsPair(
        IReadOnlyList<string> arguments,
        string option,
        string expectedValue)
    {
        var index = Array.IndexOf(arguments.ToArray(), option);
        Assert.True(index >= 0 && index + 1 < arguments.Count,
            $"Missing option {option} in: {string.Join(" ", arguments)}");
        Assert.Equal(expectedValue, arguments[index + 1]);
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
                    Set-Content -LiteralPath $env:CODEX_HUD_CAPTURE_PATH -Encoding UTF8
            }

            if ($env:CODEX_HUD_FAKE_EXIT_CODE) {
                exit [int]$env:CODEX_HUD_FAKE_EXIT_CODE
            }

            if ($env:CODEX_HUD_SKIP_FAKE_EXE -ne '1') {
                $outputIndex = [Array]::IndexOf($RemainingArguments, '-o')
                if ($outputIndex -ge 0) {
                    $output = $RemainingArguments[$outputIndex + 1]
                    New-Item -ItemType Directory -Path $output -Force | Out-Null
                    Set-Content -LiteralPath (
                        Join-Path $output 'CodexQuotaHud.App.exe'
                    ) -Value 'MZ fake' -Encoding Ascii
                }
            }
            """);
        return path;
    }

    private static string Script(string name) =>
        Path.Combine(RepositoryRoot, "scripts", name);

    private static async Task<ProcessResult> RunPowerShellAsync(
        string script,
        params string[] arguments)
    {
        var allArguments = new List<string>
        {
            "-NoProfile",
            "-NonInteractive",
            "-ExecutionPolicy",
            "Bypass",
            "-File",
            script,
        };
        allArguments.AddRange(arguments);
        return await RunProcessAsync("powershell.exe", allArguments.ToArray());
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
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
