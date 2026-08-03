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
    public async Task Publish_CreatesExactDualApplicationTreeWithMatchingContract()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "published");
        var capture = Path.Combine(temp.Path, "dotnet-publishes.jsonl");
        var fakeDotNet = CreateFakeDotNet(temp.Path);

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-Version", "1.1.0",
            "-OutputPath", output,
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode",
            "-InternalArgumentCapturePath", capture);

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        var records = await ReadPublishRecordsAsync(capture);
        Assert.Equal(2, records.Length);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(RepositoryRoot, "src",
                "CodexQuotaHud.App", "CodexQuotaHud.App.csproj")),
            records[0].Project);
        var stagePrefix = Path.GetFullPath(output) + ".stage.";
        Assert.StartsWith(
            stagePrefix,
            records[0].Output,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Guid.TryParseExact(
            records[0].Output[stagePrefix.Length..], "N", out _));
        Assert.Equal(
            Path.GetFullPath(Path.Combine(RepositoryRoot, "src",
                "CodexQuotaHud.SkinDesigner",
                "CodexQuotaHud.SkinDesigner.csproj")),
            records[1].Project);
        Assert.Equal(
            Path.Combine(records[0].Output, "designer"),
            records[1].Output);

        Assert.All(records, record =>
        {
            Assert.Equal("Release", record.Configuration);
            Assert.Equal("win-x64", record.Runtime);
            Assert.Equal("true", record.SelfContained);
            Assert.Equal("true", record.PublishSingleFile);
            Assert.Equal("true", record.IncludeNativeLibrariesForSelfExtract);
            Assert.Equal("None", record.DebugType);
            Assert.Equal("false", record.DebugSymbols);
            Assert.Equal("1.1.0", record.Version);
            Assert.Equal("1.1.0.0", record.FileVersion);
            Assert.Equal("1.1.0.0", record.AssemblyVersion);
        });
        Assert.Null(records[0].GenerateRuntimeConfigurationFiles);
        Assert.Equal("false", records[1].GenerateRuntimeConfigurationFiles);
        Assert.Equal(
            new[]
            {
                "CodexQuotaHud.App.exe",
                "designer/CodexQuotaHud.SkinDesigner.exe",
            },
            RelativeFiles(output));
        AssertNoPublishOperationResidue(output);
    }

    [Fact]
    public async Task Publish_UsesIndependentProjectOverridesAndRejectsOldOverride()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "published");
        var appProject = Path.Combine(RepositoryRoot, "src", "CodexQuotaHud.App",
            "CodexQuotaHud.App.csproj");
        var designerProject = Path.Combine(RepositoryRoot, "src",
            "CodexQuotaHud.SkinDesigner", "CodexQuotaHud.SkinDesigner.csproj");
        var fakeDotNet = CreateFakeDotNet(temp.Path);

        var accepted = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-AppProjectPath", appProject,
            "-DesignerProjectPath", designerProject,
            "-OutputPath", output,
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode");

        Assert.True(accepted.ExitCode == 0, accepted.CombinedOutput);

        var rejected = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-ProjectPath", appProject,
            "-OutputPath", Path.Combine(temp.Path, "legacy-output"),
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode");

        Assert.NotEqual(0, rejected.ExitCode);
        Assert.Contains("ProjectPath", rejected.CombinedOutput,
            StringComparison.Ordinal);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_RejectsEitherMissingProject(bool missingDesigner)
    {
        using var temp = new TemporaryDirectory();
        var missing = Path.Combine(temp.Path,
            missingDesigner ? "missing-designer.csproj" : "missing-app.csproj");
        var appProject = missingDesigner
            ? Path.Combine(RepositoryRoot, "src", "CodexQuotaHud.App",
                "CodexQuotaHud.App.csproj")
            : missing;
        var designerProject = missingDesigner
            ? missing
            : Path.Combine(RepositoryRoot, "src", "CodexQuotaHud.SkinDesigner",
                "CodexQuotaHud.SkinDesigner.csproj");

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-AppProjectPath", appProject,
            "-DesignerProjectPath", designerProject,
            "-OutputPath", Path.Combine(temp.Path, "published"),
            "-InternalTestMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            missingDesigner
                ? "Designer project file does not exist"
                : "App project file does not exist",
            result.CombinedOutput,
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(temp.Path, "published")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Publish_FailureOfEitherProjectLeavesNoPartialTree(
        bool failDesigner)
    {
        using var temp = new TemporaryDirectory();
        var project = failDesigner
            ? "CodexQuotaHud.SkinDesigner.csproj"
            : "CodexQuotaHud.App.csproj";
        var output = Path.Combine(temp.Path, "published");
        var fakeDotNet = CreateFakeDotNet(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, $"fail-{project}"), "17");

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-OutputPath", output,
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(project, result.CombinedOutput, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
        AssertNoPublishOperationResidue(output);
    }

    [Theory]
    [InlineData("CodexQuotaHud.App.csproj", "CodexQuotaHud.App.exe")]
    [InlineData("CodexQuotaHud.SkinDesigner.csproj",
        "CodexQuotaHud.SkinDesigner.exe")]
    public async Task Publish_MissingEitherExecutableLeavesNoPartialTree(
        string project,
        string executable)
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "published");
        var fakeDotNet = CreateFakeDotNet(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, $"skip-{project}"), "1");

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-OutputPath", output,
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(executable, result.CombinedOutput, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
        AssertNoPublishOperationResidue(output);
    }

    [Theory]
    [InlineData("pdb", ".pdb")]
    [InlineData("source", ".cs")]
    [InlineData("executable", "Unexpected.exe")]
    public async Task Publish_RejectsContaminatedTreeAndRemovesIt(
        string contamination,
        string expectedText)
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "published");
        var fakeDotNet = CreateFakeDotNet(temp.Path);
        File.WriteAllText(Path.Combine(temp.Path, $"contaminate-{contamination}"),
            "1");

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-OutputPath", output,
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedText, result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(output));
        AssertNoPublishOperationResidue(output);
    }

    [Fact]
    public async Task Publish_ReparseContaminatedStageIsRemovedWithoutFollowingTarget()
    {
        using var temp = new TemporaryDirectory();
        var output = Path.Combine(temp.Path, "published");
        var outside = Directory.CreateDirectory(
            Path.Combine(temp.Path, "reparse-target")).FullName;
        var outsideMarker = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(outsideMarker, "keep");
        File.WriteAllText(
            Path.Combine(temp.Path, "contaminate-reparse"),
            "1");

        try
        {
            var result = await RunPowerShellAsync(
                Script("publish.ps1"),
                "-OutputPath", output,
                "-DotNetExecutable", CreateFakeDotNet(temp.Path),
                "-InternalTestMode");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse-point", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal("keep", await File.ReadAllTextAsync(outsideMarker));
            Assert.False(Directory.Exists(output));
            AssertNoPublishOperationResidue(output);
        }
        finally
        {
            foreach (var stage in Directory.GetDirectories(
                temp.Path,
                "published.stage.*",
                SearchOption.TopDirectoryOnly))
            {
                var link = Path.Combine(stage, "linked-outside");
                if (Directory.Exists(link))
                {
                    Directory.Delete(link);
                }
                if (Directory.Exists(stage))
                {
                    Directory.Delete(stage, recursive: true);
                }
            }
        }

        Assert.Equal("keep", await File.ReadAllTextAsync(outsideMarker));
    }

    [Fact]
    public async Task Publish_FailurePreservesPriorOutputByteForByte()
    {
        using var temp = new TemporaryDirectory();
        var output = Directory.CreateDirectory(
            Path.Combine(temp.Path, "published")).FullName;
        var designer = Directory.CreateDirectory(
            Path.Combine(output, "designer")).FullName;
        await File.WriteAllBytesAsync(
            Path.Combine(output, "CodexQuotaHud.App.exe"),
            new byte[] { 1, 2, 3, 4 });
        await File.WriteAllBytesAsync(
            Path.Combine(designer, "CodexQuotaHud.SkinDesigner.exe"),
            new byte[] { 5, 6, 7, 8 });
        var before = SnapshotTree(output);
        var fakeDotNet = CreateFakeDotNet(temp.Path);
        File.WriteAllText(
            Path.Combine(temp.Path, "fail-CodexQuotaHud.SkinDesigner.csproj"),
            "29");

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-OutputPath", output,
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(before, SnapshotTree(output));
        AssertNoPublishOperationResidue(output);
    }

    [Fact]
    public async Task Publish_SuccessAtomicallyReplacesPriorOutput()
    {
        using var temp = new TemporaryDirectory();
        var output = Directory.CreateDirectory(
            Path.Combine(temp.Path, "published")).FullName;
        await File.WriteAllTextAsync(Path.Combine(output, "stale.txt"), "old");

        var result = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-OutputPath", output,
            "-DotNetExecutable", CreateFakeDotNet(temp.Path),
            "-InternalTestMode");

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.Equal(
            new[]
            {
                "CodexQuotaHud.App.exe",
                "designer/CodexQuotaHud.SkinDesigner.exe",
            },
            RelativeFiles(output));
        AssertNoPublishOperationResidue(output);
    }

    [Fact]
    public async Task Publish_PartialBackupCleanupFailureKeepsPromotedTreeAndDiagnosticBackup()
    {
        using var temp = new TemporaryDirectory();
        var output = Directory.CreateDirectory(
            Path.Combine(temp.Path, "published")).FullName;
        await File.WriteAllTextAsync(Path.Combine(output, "old-a.txt"), "old a");
        await File.WriteAllTextAsync(Path.Combine(output, "old-b.txt"), "old b");
        var fakeDotNet = CreateFakeDotNet(temp.Path);

        var failed = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-OutputPath", output,
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode",
            "-InternalFailBackupCleanupAfterFirstFile");

        Assert.NotEqual(0, failed.ExitCode);
        Assert.Contains("partial publish backup cleanup failure",
            failed.CombinedOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            new[]
            {
                "CodexQuotaHud.App.exe",
                "designer/CodexQuotaHud.SkinDesigner.exe",
            },
            RelativeFiles(output));
        var diagnosticBackup = Assert.Single(Directory.GetDirectories(
            temp.Path,
            "published.backup.*",
            SearchOption.TopDirectoryOnly));
        Assert.False(File.Exists(Path.Combine(diagnosticBackup, "old-a.txt")));
        Assert.Equal("old b", await File.ReadAllTextAsync(
            Path.Combine(diagnosticBackup, "old-b.txt")));
        var diagnosticSnapshot = SnapshotTree(diagnosticBackup);
        Assert.Empty(Directory.GetDirectories(
            temp.Path,
            "published.stage.*",
            SearchOption.TopDirectoryOnly));

        var retried = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-OutputPath", output,
            "-DotNetExecutable", fakeDotNet,
            "-InternalTestMode");

        Assert.True(retried.ExitCode == 0, retried.CombinedOutput);
        Assert.Equal(
            new[]
            {
                "CodexQuotaHud.App.exe",
                "designer/CodexQuotaHud.SkinDesigner.exe",
            },
            RelativeFiles(output));
        Assert.Equal(diagnosticSnapshot, SnapshotTree(diagnosticBackup));
        Assert.Equal(
            new[] { diagnosticBackup },
            Directory.GetDirectories(
                temp.Path,
                "published.backup.*",
                SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetDirectories(
            temp.Path,
            "published.stage.*",
            SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task Publish_RejectsReparseOutputWithoutTouchingTarget()
    {
        using var temp = new TemporaryDirectory();
        var outside = Directory.CreateDirectory(
            Path.Combine(temp.Path, "outside")).FullName;
        var marker = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(marker, "keep");
        var output = Path.Combine(temp.Path, "published");
        var link = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", output, outside);
        Assert.Equal(0, link.ExitCode);

        try
        {
            var result = await RunPowerShellAsync(
                Script("publish.ps1"),
                "-OutputPath", output,
                "-DotNetExecutable", CreateFakeDotNet(temp.Path),
                "-InternalTestMode");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse-point", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal("keep", await File.ReadAllTextAsync(marker));
        }
        finally
        {
            if (Directory.Exists(output))
            {
                Directory.Delete(output);
            }
        }
    }

    [Fact]
    public async Task Publish_RejectsNonTemporaryInternalAndNonExactProductionOutputs()
    {
        using var temp = new TemporaryDirectory();
        var forbiddenInternal = Path.Combine(
            RepositoryRoot, "artifacts", $"task16-{Guid.NewGuid():N}");
        var internalResult = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-OutputPath", forbiddenInternal,
            "-InternalTestMode");
        var productionResult = await RunPowerShellAsync(
            Script("publish.ps1"),
            "-OutputPath", Path.Combine(temp.Path, "production"));

        Assert.NotEqual(0, internalResult.ExitCode);
        Assert.Contains("system temporary", internalResult.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(0, productionResult.ExitCode);
        Assert.Contains("exactly", productionResult.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(forbiddenInternal));
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

    private static async Task<PublishRecord[]> ReadPublishRecordsAsync(
        string capture)
    {
        var lines = (await File.ReadAllLinesAsync(capture))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        return lines
            .Select(line => JsonSerializer.Deserialize<PublishRecord>(line)!)
            .ToArray();
    }

    private static string[] RelativeFiles(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string[] SnapshotTree(string root) =>
        Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path =>
                $"{Path.GetRelativePath(root, path).Replace('\\', '/')}=" +
                Convert.ToBase64String(File.ReadAllBytes(path)))
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static void AssertNoPublishOperationResidue(string output)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(output))!;
        if (!Directory.Exists(parent))
        {
            return;
        }

        var outputName = Path.GetFileName(output);
        Assert.Empty(Directory.GetFileSystemEntries(
            parent, $"{outputName}.stage.*", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetFileSystemEntries(
            parent, $"{outputName}.backup.*", SearchOption.TopDirectoryOnly));
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

            function Get-PairValue([string] $Name) {
                $index = [Array]::IndexOf($RemainingArguments, $Name)
                if ($index -lt 0 -or $index + 1 -ge $RemainingArguments.Count) {
                    return $null
                }
                return $RemainingArguments[$index + 1]
            }

            function Get-PropertyValue([string] $Name) {
                $prefix = "-p:$Name="
                $argument = $RemainingArguments |
                    Where-Object { $_.StartsWith($prefix) } |
                    Select-Object -First 1
                if ($null -eq $argument) { return $null }
                return $argument.Substring($prefix.Length)
            }

            $project = $RemainingArguments[1]
            $projectFile = [System.IO.Path]::GetFileName($project)
            $output = Get-PairValue '-o'
            if ($env:CODEX_HUD_CAPTURE_PATH) {
                $record = [ordered]@{
                    Project = [System.IO.Path]::GetFullPath($project)
                    Configuration = Get-PairValue '-c'
                    Runtime = Get-PairValue '-r'
                    SelfContained = Get-PairValue '--self-contained'
                    PublishSingleFile = Get-PropertyValue 'PublishSingleFile'
                    IncludeNativeLibrariesForSelfExtract =
                        Get-PropertyValue 'IncludeNativeLibrariesForSelfExtract'
                    DebugType = Get-PropertyValue 'DebugType'
                    DebugSymbols = Get-PropertyValue 'DebugSymbols'
                    GenerateRuntimeConfigurationFiles =
                        Get-PropertyValue 'GenerateRuntimeConfigurationFiles'
                    Version = Get-PropertyValue 'Version'
                    FileVersion = Get-PropertyValue 'FileVersion'
                    AssemblyVersion = Get-PropertyValue 'AssemblyVersion'
                    Output = [System.IO.Path]::GetFullPath($output)
                }
                $json = $record | ConvertTo-Json -Compress
                Add-Content -LiteralPath $env:CODEX_HUD_CAPTURE_PATH `
                    -Value $json -Encoding UTF8
            }

            if ($env:CODEX_HUD_FAKE_EXIT_CODE) {
                exit [int]$env:CODEX_HUD_FAKE_EXIT_CODE
            }

            $failMarker = Join-Path $PSScriptRoot "fail-$projectFile"
            if (Test-Path -LiteralPath $failMarker) {
                exit [int](Get-Content -LiteralPath $failMarker -Raw)
            }

            New-Item -ItemType Directory -Path $output -Force | Out-Null
            $skipMarker = Join-Path $PSScriptRoot "skip-$projectFile"
            if ($env:CODEX_HUD_SKIP_FAKE_EXE -ne '1' -and
                -not (Test-Path -LiteralPath $skipMarker)) {
                $executable = if ($projectFile -eq
                    'CodexQuotaHud.SkinDesigner.csproj') {
                    'CodexQuotaHud.SkinDesigner.exe'
                } else {
                    'CodexQuotaHud.App.exe'
                }
                Set-Content -LiteralPath (Join-Path $output $executable) `
                    -Value "MZ fake $projectFile" -Encoding Ascii
                if ($projectFile -eq 'CodexQuotaHud.SkinDesigner.csproj') {
                    $generateRuntimeConfigurationFiles =
                        Get-PropertyValue 'GenerateRuntimeConfigurationFiles'
                    if ($generateRuntimeConfigurationFiles -ne 'false') {
                        Set-Content -LiteralPath (Join-Path $output `
                            'CodexQuotaHud.App.runtimeconfig.json') `
                            -Value '{}' -Encoding Ascii
                    }
                }
            }

            if (Test-Path -LiteralPath (
                Join-Path $PSScriptRoot 'contaminate-pdb')) {
                Set-Content -LiteralPath (Join-Path $output 'leaked.pdb') `
                    -Value pdb -Encoding Ascii
            }
            if (Test-Path -LiteralPath (
                Join-Path $PSScriptRoot 'contaminate-source')) {
                Set-Content -LiteralPath (Join-Path $output 'LeakedSource.cs') `
                    -Value source -Encoding Ascii
            }
            if (Test-Path -LiteralPath (
                Join-Path $PSScriptRoot 'contaminate-executable')) {
                Set-Content -LiteralPath (Join-Path $output 'Unexpected.exe') `
                    -Value executable -Encoding Ascii
            }
            if ($projectFile -eq 'CodexQuotaHud.App.csproj' -and
                (Test-Path -LiteralPath (
                    Join-Path $PSScriptRoot 'contaminate-reparse'))) {
                New-Item -ItemType Junction `
                    -Path (Join-Path $output 'linked-outside') `
                    -Target (Join-Path $PSScriptRoot 'reparse-target') | Out-Null
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

    private sealed record PublishRecord
    {
        public string Project { get; init; } = string.Empty;
        public string Configuration { get; init; } = string.Empty;
        public string Runtime { get; init; } = string.Empty;
        public string SelfContained { get; init; } = string.Empty;
        public string PublishSingleFile { get; init; } = string.Empty;
        public string IncludeNativeLibrariesForSelfExtract { get; init; } =
            string.Empty;
        public string DebugType { get; init; } = string.Empty;
        public string DebugSymbols { get; init; } = string.Empty;
        public string? GenerateRuntimeConfigurationFiles { get; init; }
        public string Version { get; init; } = string.Empty;
        public string FileVersion { get; init; } = string.Empty;
        public string AssemblyVersion { get; init; } = string.Empty;
        public string Output { get; init; } = string.Empty;
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
