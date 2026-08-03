using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexQuotaHud.App.Tests.Packaging;

[Collection(PackagingScriptCollection.Name)]
public sealed class InstallerComponentLifecycleTests
{
    private const string DesignerLinkName = "Codex Quota HUD 皮肤设计器.lnk";
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task PrepareDesignerRemoval_MovesOnlyExactManagedArtifacts()
    {
        using var fixture = new LifecycleFixture();
        var expectedPayloadHash = Sha256(fixture.DesignerPayload);
        var expectedPayloadTime = File.GetLastWriteTimeUtc(fixture.DesignerPayload);
        var expectedShortcutHash = Sha256(fixture.DesignerShortcut);

        var result = await fixture.RunAsync("PrepareDesignerComponentRemoval");

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.False(Directory.Exists(fixture.DesignerPath));
        Assert.False(File.Exists(fixture.DesignerShortcut));
        Assert.Equal(expectedPayloadHash, Sha256(Path.Combine(
            fixture.BackupPath, "designer", "payload.bin")));
        Assert.Equal(expectedPayloadTime, File.GetLastWriteTimeUtc(Path.Combine(
            fixture.BackupPath, "designer", "payload.bin")));
        Assert.Equal(expectedShortcutHash, Sha256(Path.Combine(
            fixture.BackupPath, "DesignerStartMenu.lnk")));

        using var marker = JsonDocument.Parse(await File.ReadAllTextAsync(
            Path.Combine(fixture.BackupPath,
                "CodexQuotaHud.DesignerRemoval.json")));
        Assert.Equal(fixture.DesignerPath,
            marker.RootElement.GetProperty("Source").GetString());
        Assert.Equal(fixture.BackupPath,
            marker.RootElement.GetProperty("Destination").GetString());
        Assert.Equal(fixture.DesignerShortcut,
            marker.RootElement.GetProperty("ShortcutSource").GetString());
        Assert.True(marker.RootElement.GetProperty("ShortcutExisted").GetBoolean());
        Assert.True(marker.RootElement.GetProperty("DesignerExisted").GetBoolean());
        Assert.Equal("Prepared",
            marker.RootElement.GetProperty("State").GetString());
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task RollbackDesignerRemoval_RestoresBytesTimestampsAndIsRetrySafe()
    {
        using var fixture = new LifecycleFixture();
        var expectedPayloadHash = Sha256(fixture.DesignerPayload);
        var expectedPayloadTime = File.GetLastWriteTimeUtc(fixture.DesignerPayload);
        var expectedShortcutHash = Sha256(fixture.DesignerShortcut);
        var expectedShortcutTime = File.GetLastWriteTimeUtc(fixture.DesignerShortcut);
        AssertSuccess(await fixture.RunAsync("PrepareDesignerComponentRemoval"));

        var rollback = await fixture.RunAsync("RollbackDesignerComponentRemoval");
        var retry = await fixture.RunAsync("RollbackDesignerComponentRemoval");

        AssertSuccess(rollback);
        AssertSuccess(retry);
        Assert.Equal(expectedPayloadHash, Sha256(fixture.DesignerPayload));
        Assert.Equal(expectedPayloadTime,
            File.GetLastWriteTimeUtc(fixture.DesignerPayload));
        Assert.Equal(expectedShortcutHash, Sha256(fixture.DesignerShortcut));
        Assert.Equal(expectedShortcutTime,
            File.GetLastWriteTimeUtc(fixture.DesignerShortcut));
        Assert.False(Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task CommitDesignerRemoval_RemovesCheckedBackupAndIsRetrySafe()
    {
        using var fixture = new LifecycleFixture();
        AssertSuccess(await fixture.RunAsync("PrepareDesignerComponentRemoval"));

        var commit = await fixture.RunAsync("CommitDesignerComponentRemoval");
        var retry = await fixture.RunAsync("CommitDesignerComponentRemoval");

        AssertSuccess(commit);
        AssertSuccess(retry);
        Assert.False(Directory.Exists(fixture.BackupPath));
        Assert.False(Directory.Exists(fixture.DesignerPath));
        Assert.False(File.Exists(fixture.DesignerShortcut));
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task PrepareDesignerRemoval_HandlesAbsentPayloadOrShortcut(
        bool includeDesigner,
        bool includeShortcut)
    {
        using var fixture = new LifecycleFixture(includeDesigner, includeShortcut);

        var prepare = await fixture.RunAsync("PrepareDesignerComponentRemoval");

        AssertSuccess(prepare);
        Assert.False(Directory.Exists(fixture.DesignerPath));
        Assert.False(File.Exists(fixture.DesignerShortcut));
        Assert.Equal(includeDesigner || includeShortcut,
            Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task DesignerRemoval_RestoresHiddenReadOnlyPayload()
    {
        using var fixture = new LifecycleFixture();
        var expectedAttributes = FileAttributes.Hidden | FileAttributes.ReadOnly;
        File.SetAttributes(fixture.DesignerPayload, expectedAttributes);
        try
        {
            AssertSuccess(await fixture.RunAsync(
                "PrepareDesignerComponentRemoval"));
            AssertSuccess(await fixture.RunAsync(
                "RollbackDesignerComponentRemoval"));

            Assert.Equal(expectedAttributes,
                File.GetAttributes(fixture.DesignerPayload) & expectedAttributes);
            fixture.AssertPreserved();
        }
        finally
        {
            if (File.Exists(fixture.DesignerPayload))
            {
                File.SetAttributes(fixture.DesignerPayload, FileAttributes.Normal);
            }
        }
    }

    [Theory]
    [InlineData("wrong-prefix")]
    [InlineData("outside-programs")]
    [InlineData("nested-under-install")]
    public async Task DesignerRemoval_RejectsBackupOutsideExactGuidSiblingContract(
        string backupKind)
    {
        using var fixture = new LifecycleFixture();
        var backup = backupKind switch
        {
            "wrong-prefix" => Path.Combine(
                fixture.ProgramsPath, $"DesignerBackup.{Guid.NewGuid():N}"),
            "outside-programs" => Path.Combine(
                fixture.RootPath,
                $"CodexQuotaHud.designer-removal-backup.{Guid.NewGuid():N}"),
            "nested-under-install" => Path.Combine(
                fixture.InstallPath,
                $"CodexQuotaHud.designer-removal-backup.{Guid.NewGuid():N}"),
            _ => throw new InvalidOperationException(backupKind),
        };

        var result = await fixture.RunAsync(
            "PrepareDesignerComponentRemoval", backup);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            backupKind == "wrong-prefix" ? "prefix" : "directly under Programs",
            result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(fixture.DesignerPath));
        Assert.True(File.Exists(fixture.DesignerShortcut));
        Assert.False(Directory.Exists(backup));
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("shortcut")]
    [InlineData("extra-property")]
    public async Task DesignerRemoval_RejectsForgedMarkerWithoutMutation(
        string forgery)
    {
        using var fixture = new LifecycleFixture();
        AssertSuccess(await fixture.RunAsync("PrepareDesignerComponentRemoval"));
        var markerPath = Path.Combine(
            fixture.BackupPath, "CodexQuotaHud.DesignerRemoval.json");
        var marker = new Dictionary<string, object?>
        {
            ["Source"] = forgery == "source"
                ? Path.Combine(fixture.RootPath, "forged")
                : fixture.DesignerPath,
            ["Destination"] = forgery == "destination"
                ? Path.Combine(fixture.RootPath, "forged")
                : fixture.BackupPath,
            ["ShortcutSource"] = forgery == "shortcut"
                ? Path.Combine(fixture.RootPath, "forged.lnk")
                : fixture.DesignerShortcut,
            ["ShortcutExisted"] = true,
            ["DesignerExisted"] = true,
            ["State"] = "Prepared",
        };
        if (forgery == "extra-property")
        {
            marker["Unexpected"] = "forged";
        }
        await File.WriteAllTextAsync(markerPath, JsonSerializer.Serialize(marker));

        var rollback = await fixture.RunAsync("RollbackDesignerComponentRemoval");
        var commit = await fixture.RunAsync("CommitDesignerComponentRemoval");

        Assert.NotEqual(0, rollback.ExitCode);
        Assert.NotEqual(0, commit.ExitCode);
        Assert.False(Directory.Exists(fixture.DesignerPath));
        Assert.False(File.Exists(fixture.DesignerShortcut));
        Assert.True(Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData("designer-target")]
    [InlineData("designer-nested")]
    [InlineData("backup-target")]
    public async Task DesignerRemoval_RejectsReparsePointsWithoutFollowingThem(
        string reparseKind)
    {
        using var fixture = new LifecycleFixture();
        var outside = Directory.CreateDirectory(
            Path.Combine(fixture.RootPath, "outside", reparseKind)).FullName;
        var outsideMarker = Path.Combine(outside, "outside.txt");
        await File.WriteAllTextAsync(outsideMarker, "outside keep");
        string junction;
        if (reparseKind == "designer-target")
        {
            Directory.Delete(fixture.DesignerPath, recursive: true);
            junction = fixture.DesignerPath;
        }
        else if (reparseKind == "designer-nested")
        {
            junction = Path.Combine(fixture.DesignerPath, "nested");
        }
        else
        {
            junction = fixture.BackupPath;
        }
        var link = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", junction, outside);
        Assert.Equal(0, link.ExitCode);

        try
        {
            var result = await fixture.RunAsync(
                "PrepareDesignerComponentRemoval");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(outsideMarker));
            Assert.True(File.Exists(fixture.DesignerShortcut));
            fixture.AssertPreserved();
        }
        finally
        {
            if (Directory.Exists(junction))
            {
                Directory.Delete(junction);
            }
        }
    }

    [Theory]
    [InlineData("PrepareAfterDesignerMove")]
    [InlineData("PrepareAfterShortcutMove")]
    public async Task PrepareDesignerRemoval_MoveFailureCanBeRolledBack(
        string failureStage)
    {
        using var fixture = new LifecycleFixture();

        var failed = await fixture.RunAsync(
            "PrepareDesignerComponentRemoval",
            fixture.BackupPath,
            "-InternalDesignerFailureStage", failureStage);

        Assert.NotEqual(0, failed.ExitCode);
        Assert.True(Directory.Exists(fixture.BackupPath));
        AssertSuccess(await fixture.RunAsync("RollbackDesignerComponentRemoval"));
        Assert.True(Directory.Exists(fixture.DesignerPath));
        Assert.True(File.Exists(fixture.DesignerShortcut));
        Assert.False(Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task RollbackDesignerRemoval_CopyFailureRetainsBackupAndCanRetry()
    {
        using var fixture = new LifecycleFixture();
        AssertSuccess(await fixture.RunAsync("PrepareDesignerComponentRemoval"));

        var failed = await fixture.RunAsync(
            "RollbackDesignerComponentRemoval",
            fixture.BackupPath,
            "-InternalDesignerFailureStage", "RollbackCopy");

        Assert.NotEqual(0, failed.ExitCode);
        Assert.True(Directory.Exists(fixture.BackupPath));
        Assert.False(Directory.Exists(fixture.DesignerPath));
        AssertSuccess(await fixture.RunAsync("RollbackDesignerComponentRemoval"));
        Assert.True(Directory.Exists(fixture.DesignerPath));
        Assert.False(Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task CommitDesignerRemoval_DeleteFailureRetainsBackupAndCanRetry()
    {
        using var fixture = new LifecycleFixture();
        AssertSuccess(await fixture.RunAsync("PrepareDesignerComponentRemoval"));

        var failed = await fixture.RunAsync(
            "CommitDesignerComponentRemoval",
            fixture.BackupPath,
            "-InternalDesignerFailureStage", "CommitDelete");

        Assert.NotEqual(0, failed.ExitCode);
        Assert.True(Directory.Exists(fixture.BackupPath));
        AssertSuccess(await fixture.RunAsync("CommitDesignerComponentRemoval"));
        Assert.False(Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task CommitDesignerRemoval_RealMidDeleteFailureKeepsMarkerAndRetries()
    {
        using var fixture = new LifecycleFixture();
        AssertSuccess(await fixture.RunAsync("PrepareDesignerComponentRemoval"));
        var backupDesigner = Path.Combine(fixture.BackupPath, "designer");
        var backupShortcut = Path.Combine(
            fixture.BackupPath, "DesignerStartMenu.lnk");
        var marker = Path.Combine(
            fixture.BackupPath, "CodexQuotaHud.DesignerRemoval.json");

        ProcessResult failed;
        using (File.Open(
            backupShortcut,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            failed = await fixture.RunAsync("CommitDesignerComponentRemoval");
        }

        Assert.NotEqual(0, failed.ExitCode);
        Assert.False(Directory.Exists(backupDesigner));
        Assert.True(File.Exists(backupShortcut));
        Assert.True(File.Exists(marker));
        Assert.False(Directory.Exists(fixture.DesignerPath));
        Assert.False(File.Exists(fixture.DesignerShortcut));

        AssertSuccess(await fixture.RunAsync("CommitDesignerComponentRemoval"));
        Assert.False(Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task RollbackDesignerRemoval_RealMidDeleteFailureKeepsMarkerAndRetries()
    {
        using var fixture = new LifecycleFixture();
        var expectedLockedPayloadHash = Sha256(fixture.LockedDesignerPayload);
        var expectedShortcutHash = Sha256(fixture.DesignerShortcut);
        AssertSuccess(await fixture.RunAsync("PrepareDesignerComponentRemoval"));
        var backupDesigner = Path.Combine(fixture.BackupPath, "designer");
        var deletedBackupPayload = Path.Combine(
            backupDesigner, "a-deleted.bin");
        var lockedBackupPayload = Path.Combine(
            backupDesigner, "z-locked.bin");
        var markerPath = Path.Combine(
            fixture.BackupPath, "CodexQuotaHud.DesignerRemoval.json");

        ProcessResult failed;
        using (File.Open(
            lockedBackupPayload,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite))
        {
            failed = await fixture.RunAsync("RollbackDesignerComponentRemoval");
        }

        Assert.NotEqual(0, failed.ExitCode);
        Assert.True(Directory.Exists(fixture.DesignerPath));
        Assert.True(File.Exists(fixture.DesignerShortcut));
        Assert.Equal(expectedLockedPayloadHash,
            Sha256(fixture.LockedDesignerPayload));
        Assert.Equal(expectedShortcutHash, Sha256(fixture.DesignerShortcut));
        var failureDiagnostic = $"Exit: {failed.ExitCode}{Environment.NewLine}" +
            $"Output: {failed.CombinedOutput}{Environment.NewLine}" +
            $"Marker: {(File.Exists(markerPath)
                ? await File.ReadAllTextAsync(markerPath)
                : "<missing>")}";
        Assert.False(File.Exists(deletedBackupPayload), failureDiagnostic);
        Assert.True(File.Exists(lockedBackupPayload));
        Assert.True(File.Exists(markerPath));
        using (var marker = JsonDocument.Parse(
            await File.ReadAllTextAsync(markerPath)))
        {
            Assert.Equal("RestoreVerified",
                marker.RootElement.GetProperty("State").GetString());
        }

        AssertSuccess(await fixture.RunAsync("RollbackDesignerComponentRemoval"));
        Assert.False(Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task ProductionCommitDesignerRemoval_UsesExactSourceAndRetriesRealFault()
    {
        using var fixture = new LifecycleFixture();
        AssertProductionSource(await fixture.RunProductionAsync("Prepare"));
        var backupDesigner = Path.Combine(fixture.BackupPath, "designer");
        var backupShortcut = Path.Combine(
            fixture.BackupPath, "DesignerStartMenu.lnk");
        var marker = Path.Combine(
            fixture.BackupPath, "CodexQuotaHud.DesignerRemoval.json");

        ProductionResult failed;
        using (File.Open(
            backupShortcut,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read))
        {
            failed = await fixture.RunProductionAsync("Commit");
        }

        AssertProductionSource(failed);
        Assert.NotEqual(0, failed.Process.ExitCode);
        Assert.False(Directory.Exists(backupDesigner));
        Assert.True(File.Exists(backupShortcut));
        Assert.True(File.Exists(marker));
        AssertProductionSuccess(await fixture.RunProductionAsync("Commit"));
        Assert.False(Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task ProductionRollbackDesignerRemoval_UsesExactSourceAndRetriesRealFault()
    {
        using var fixture = new LifecycleFixture();
        var expectedLockedPayloadHash = Sha256(fixture.LockedDesignerPayload);
        var expectedShortcutHash = Sha256(fixture.DesignerShortcut);
        AssertProductionSuccess(await fixture.RunProductionAsync("Prepare"));
        var backupDesigner = Path.Combine(fixture.BackupPath, "designer");
        var deletedBackupPayload = Path.Combine(
            backupDesigner, "a-deleted.bin");
        var lockedBackupPayload = Path.Combine(
            backupDesigner, "z-locked.bin");
        var markerPath = Path.Combine(
            fixture.BackupPath, "CodexQuotaHud.DesignerRemoval.json");

        ProductionResult failed;
        using (File.Open(
            lockedBackupPayload,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite))
        {
            failed = await fixture.RunProductionAsync("Rollback");
        }

        AssertProductionSource(failed);
        Assert.NotEqual(0, failed.Process.ExitCode);
        Assert.True(Directory.Exists(fixture.DesignerPath));
        Assert.True(File.Exists(fixture.DesignerShortcut));
        Assert.Equal(expectedLockedPayloadHash,
            Sha256(fixture.LockedDesignerPayload));
        Assert.Equal(expectedShortcutHash, Sha256(fixture.DesignerShortcut));
        var failureDiagnostic =
            $"Exit: {failed.Process.ExitCode}{Environment.NewLine}" +
            $"Output: {failed.Process.CombinedOutput}{Environment.NewLine}" +
            $"Marker: {(File.Exists(markerPath)
                ? await File.ReadAllTextAsync(markerPath)
                : "<missing>")}";
        Assert.False(File.Exists(deletedBackupPayload), failureDiagnostic);
        Assert.True(File.Exists(lockedBackupPayload));
        Assert.True(File.Exists(markerPath));
        AssertProductionSuccess(await fixture.RunProductionAsync("Rollback"));
        Assert.False(Directory.Exists(fixture.BackupPath));
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task PrepareDesignerRemoval_ClosesOnlyExactDesignerWithoutForceKill()
    {
        using var fixture = new LifecycleFixture();
        var unrelatedPath = Path.Combine(
            fixture.RootPath, "Other", "CodexQuotaHud.SkinDesigner.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelatedPath)!);
        var processes = new[]
        {
            new
            {
                ProcessId = 701,
                ProcessIdentity = "designer-701",
                Name = "CodexQuotaHud.SkinDesigner.exe",
                ExecutablePath = fixture.DesignerExecutable,
                ExitAfterClose = true,
            },
            new
            {
                ProcessId = 702,
                ProcessIdentity = "designer-702",
                Name = "CodexQuotaHud.SkinDesigner.exe",
                ExecutablePath = unrelatedPath,
                ExitAfterClose = false,
            },
        };
        await File.WriteAllTextAsync(
            fixture.ProcessSnapshotPath, JsonSerializer.Serialize(processes));

        var result = await fixture.RunAsync(
            "PrepareDesignerComponentRemoval",
            fixture.BackupPath,
            "-InternalProcessSnapshotPath", fixture.ProcessSnapshotPath,
            "-InternalActionLogPath", fixture.ActionLogPath);

        AssertSuccess(result);
        var actions = await ReadActionsAsync(fixture.ActionLogPath);
        var close = Assert.Single(actions,
            action => action.Action == "CloseDesignerWindow");
        Assert.Equal(701, close.ProcessId);
        Assert.Equal("designer-701", close.ProcessIdentity);
        Assert.DoesNotContain(actions,
            action => action.Action is "StopProcess" or "KillProcess");
    }

    [Theory]
    [InlineData(null, "designer-801")]
    [InlineData("exact", null)]
    public async Task PrepareDesignerRemoval_UnprovedDesignerFailsBeforeMovingFiles(
        string? executableKind,
        string? identity)
    {
        using var fixture = new LifecycleFixture();
        var executable = executableKind == "exact"
            ? fixture.DesignerExecutable
            : null;
        await File.WriteAllTextAsync(
            fixture.ProcessSnapshotPath,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    ProcessId = 801,
                    ProcessIdentity = identity,
                    Name = "CodexQuotaHud.SkinDesigner.exe",
                    ExecutablePath = executable,
                    ExitAfterClose = true,
                },
            }));

        var result = await fixture.RunAsync(
            "PrepareDesignerComponentRemoval",
            fixture.BackupPath,
            "-InternalProcessSnapshotPath", fixture.ProcessSnapshotPath,
            "-InternalActionLogPath", fixture.ActionLogPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            executableKind == "exact" ? "identity" : "inspect",
            result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(fixture.DesignerPath));
        Assert.True(File.Exists(fixture.DesignerShortcut));
        Assert.False(Directory.Exists(fixture.BackupPath));
        Assert.DoesNotContain(
            await ReadActionsAsync(fixture.ActionLogPath),
            action => action.Action is "StopProcess" or "KillProcess");
        fixture.AssertPreserved();
    }

    [Fact]
    public async Task PrepareDesignerRemoval_RunningDesignerTimeoutFailsWithoutForceKillOrMove()
    {
        using var fixture = new LifecycleFixture();
        await File.WriteAllTextAsync(
            fixture.ProcessSnapshotPath,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    ProcessId = 901,
                    ProcessIdentity = "designer-901",
                    Name = "CodexQuotaHud.SkinDesigner.exe",
                    ExecutablePath = fixture.DesignerExecutable,
                    ExitAfterClose = false,
                },
            }));

        var result = await fixture.RunAsync(
            "PrepareDesignerComponentRemoval",
            fixture.BackupPath,
            "-InternalProcessSnapshotPath", fixture.ProcessSnapshotPath,
            "-InternalActionLogPath", fixture.ActionLogPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("running", result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(Directory.Exists(fixture.DesignerPath));
        Assert.True(File.Exists(fixture.DesignerShortcut));
        Assert.False(Directory.Exists(fixture.BackupPath));
        var actions = await ReadActionsAsync(fixture.ActionLogPath);
        Assert.Contains(actions,
            action => action.Action == "CloseDesignerWindow");
        Assert.DoesNotContain(actions,
            action => action.Action is "StopProcess" or "KillProcess");
        fixture.AssertPreserved();
    }

    [Theory]
    [InlineData("PrepareInstall")]
    [InlineData("PrepareUninstall")]
    public async Task InstallAndUninstallPreparation_CloseExactDesignerNormally(
        string action)
    {
        using var fixture = new LifecycleFixture();
        await File.WriteAllTextAsync(
            fixture.ProcessSnapshotPath,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    ProcessId = 1001,
                    ProcessIdentity = "designer-1001",
                    Name = "CodexQuotaHud.SkinDesigner.exe",
                    ExecutablePath = fixture.DesignerExecutable,
                    ExitAfterClose = true,
                },
            }));

        var result = await fixture.RunAsync(
            action,
            fixture.BackupPath,
            "-InternalProcessSnapshotPath", fixture.ProcessSnapshotPath,
            "-InternalActionLogPath", fixture.ActionLogPath);

        AssertSuccess(result);
        var actions = await ReadActionsAsync(fixture.ActionLogPath);
        Assert.Contains(actions,
            item => item.Action == "CloseDesignerWindow" &&
                item.ProcessId == 1001);
        Assert.DoesNotContain(actions,
            item => item.Action is "StopProcess" or "KillProcess");
    }

    private static void AssertSuccess(ProcessResult result) =>
        Assert.True(result.ExitCode == 0, result.CombinedOutput);

    private static void AssertProductionSource(ProductionResult result)
    {
        Assert.False(string.IsNullOrWhiteSpace(result.ProductionSourceHash));
        Assert.Equal(
            result.ProductionSourceHash,
            result.HarnessSourceExtentHash);
    }

    private static void AssertProductionSuccess(ProductionResult result)
    {
        AssertProductionSource(result);
        AssertSuccess(result.Process);
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task<ActionRecord[]> ReadActionsAsync(string path)
    {
        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ActionRecord[]>(stream)
            ?? [];
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
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(process.ExitCode, await output, await error);
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

    private sealed record ActionRecord(
        string Action,
        int? ProcessId,
        string? ProcessIdentity,
        string? ExecutablePath,
        int? TimeoutSeconds);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError)
    {
        public string CombinedOutput => StandardOutput + StandardError;
    }

    private sealed record ProductionResult(
        ProcessResult Process,
        string ProductionSourceHash,
        string HarnessSourceExtentHash);

    private sealed class LifecycleFixture : IDisposable
    {
        private readonly Dictionary<string, string> preservedHashes;

        public LifecycleFixture(
            bool includeDesigner = true,
            bool includeShortcut = true)
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                "CodexQuotaHud.Tests",
                Guid.NewGuid().ToString("N"));
            LocalAppDataPath = Directory.CreateDirectory(
                Path.Combine(RootPath, "LocalAppData")).FullName;
            ProgramsPath = Directory.CreateDirectory(
                Path.Combine(LocalAppDataPath, "Programs")).FullName;
            InstallPath = Directory.CreateDirectory(
                Path.Combine(ProgramsPath, "CodexQuotaHud")).FullName;
            DesignerPath = Path.Combine(InstallPath, "designer");
            DesignerPayload = Path.Combine(DesignerPath, "payload.bin");
            DeletedDesignerPayload = Path.Combine(
                DesignerPath, "a-deleted.bin");
            LockedDesignerPayload = Path.Combine(
                DesignerPath, "z-locked.bin");
            DesignerExecutable = Path.Combine(
                DesignerPath, "CodexQuotaHud.SkinDesigner.exe");
            if (includeDesigner)
            {
                Directory.CreateDirectory(DesignerPath);
                File.WriteAllText(DesignerPayload, "designer payload");
                File.WriteAllText(
                    DeletedDesignerPayload, "delete before locked payload");
                File.WriteAllText(
                    LockedDesignerPayload, "locked designer payload");
                File.WriteAllText(DesignerExecutable, "designer executable");
                File.SetLastWriteTimeUtc(
                    DesignerPayload,
                    new DateTime(2024, 2, 3, 4, 5, 6, DateTimeKind.Utc));
            }

            ShellRootPath = Directory.CreateDirectory(
                Path.Combine(RootPath, "Shell")).FullName;
            var startMenu = Directory.CreateDirectory(Path.Combine(
                ShellRootPath, "StartMenu", "Programs")).FullName;
            var desktop = Directory.CreateDirectory(
                Path.Combine(ShellRootPath, "Desktop")).FullName;
            DesignerShortcut = Path.Combine(startMenu, DesignerLinkName);
            if (includeShortcut)
            {
                File.WriteAllText(DesignerShortcut, "designer shortcut");
                File.SetLastWriteTimeUtc(
                    DesignerShortcut,
                    new DateTime(2024, 3, 4, 5, 6, 8, DateTimeKind.Utc));
            }

            var settings = Directory.CreateDirectory(
                Path.Combine(LocalAppDataPath, "CodexQuotaHud")).FullName;
            var preserved = new[]
            {
                Write("normal executable", Path.Combine(
                    InstallPath, "CodexQuotaHud.App.exe")),
                Write("normal uninstaller", Path.Combine(
                    InstallPath, "unins000.exe")),
                Write("unrelated program", Path.Combine(
                    InstallPath, "keep.bin")),
                Write("normal start", Path.Combine(
                    startMenu, "Codex Quota HUD.lnk")),
                Write("normal desktop", Path.Combine(
                    desktop, "Codex Quota HUD.lnk")),
                Write("unrelated shell", Path.Combine(
                    startMenu, "Keep Me.lnk")),
                Write("settings", Path.Combine(settings, "settings.json")),
                Write("installed skin", Path.Combine(
                    settings, "skins", "11111111-1111-1111-1111-111111111111",
                    "skin.json")),
                Write("draft", Path.Combine(
                    settings, "designer", "drafts",
                    "22222222-2222-2222-2222-222222222222", "draft.json")),
                Write("recovery", Path.Combine(
                    settings, "designer", "recovery", "recovery.json")),
                Write("import", Path.Combine(
                    settings, "imports", "import.cqskin")),
            };
            preservedHashes = preserved.ToDictionary(
                path => path,
                Sha256,
                StringComparer.OrdinalIgnoreCase);
            BackupPath = Path.Combine(
                ProgramsPath,
                $"CodexQuotaHud.designer-removal-backup.{Guid.NewGuid():N}");
            ProcessSnapshotPath = Path.Combine(RootPath, "processes.json");
            ActionLogPath = Path.Combine(RootPath, "actions.json");
        }

        public string RootPath { get; }
        public string LocalAppDataPath { get; }
        public string ProgramsPath { get; }
        public string InstallPath { get; }
        public string DesignerPath { get; }
        public string DesignerPayload { get; }
        public string DeletedDesignerPayload { get; }
        public string LockedDesignerPayload { get; }
        public string DesignerExecutable { get; }
        public string ShellRootPath { get; }
        public string DesignerShortcut { get; }
        public string BackupPath { get; }
        public string ProcessSnapshotPath { get; }
        public string ActionLogPath { get; }

        public Task<ProcessResult> RunAsync(
            string action,
            string? backupPath = null,
            params string[] additionalArguments)
        {
            var arguments = new List<string>
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                Path.Combine(RepositoryRoot,
                    "scripts", "installer-lifecycle.ps1"),
                "-Action", action,
                "-InstallPath", InstallPath,
                "-LocalAppDataRoot", LocalAppDataPath,
                "-DesignerBackupPath", backupPath ?? BackupPath,
                "-InternalShellRootPath", ShellRootPath,
                "-InternalTestMode",
            };
            arguments.AddRange(additionalArguments);
            return RunProcessAsync("powershell.exe", arguments.ToArray());
        }

        public async Task<ProductionResult> RunProductionAsync(string action)
        {
            var productionPath = Path.Combine(
                RepositoryRoot, "scripts", "installer-lifecycle-production.ps1");
            var source = await File.ReadAllTextAsync(productionPath);
            const string sourceStart = "Set-StrictMode -Version Latest";
            const string sourceEnd = "$localRoot = Get-NormalizedPath";
            var start = source.IndexOf(sourceStart, StringComparison.Ordinal);
            var end = source.IndexOf(sourceEnd, start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start,
                "Production helper function source extent was not found.");
            var exactSourceExtent = source[start..end];
            Assert.Contains("function Prepare-DesignerRemoval", exactSourceExtent,
                StringComparison.Ordinal);
            Assert.Contains("function Restore-DesignerRemoval", exactSourceExtent,
                StringComparison.Ordinal);
            Assert.Contains("function Commit-DesignerRemoval", exactSourceExtent,
                StringComparison.Ordinal);

            var harnessPath = Path.Combine(
                RootPath, $"production-designer-{Guid.NewGuid():N}.ps1");
            var invocation = $$"""

$install = [Text.Encoding]::UTF8.GetString(
    [Convert]::FromBase64String('{{Encode(InstallPath)}}'))
$backup = [Text.Encoding]::UTF8.GetString(
    [Convert]::FromBase64String('{{Encode(BackupPath)}}'))
$shortcut = [Text.Encoding]::UTF8.GetString(
    [Convert]::FromBase64String('{{Encode(DesignerShortcut)}}'))
$programs = [Text.Encoding]::UTF8.GetString(
    [Convert]::FromBase64String('{{Encode(ProgramsPath)}}'))
switch ('{{action}}') {
    'Prepare' {
        Prepare-DesignerRemoval -Install $install -Backup $backup `
            -Shortcut $shortcut -Programs $programs
    }
    'Commit' {
        Commit-DesignerRemoval -Install $install -Backup $backup `
            -Shortcut $shortcut -Programs $programs
    }
    'Rollback' {
        Restore-DesignerRemoval -Install $install -Backup $backup `
            -Shortcut $shortcut -Programs $programs
    }
    default { throw 'Unknown production harness action.' }
}
""";
            await File.WriteAllTextAsync(
                harnessPath,
                exactSourceExtent + invocation,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            var harnessSource = await File.ReadAllTextAsync(harnessPath);
            var harnessExtent = harnessSource[..exactSourceExtent.Length];
            var result = await RunProcessAsync(
                "powershell.exe",
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy",
                "Bypass",
                "-File",
                harnessPath);
            return new ProductionResult(
                result,
                Sha256Text(exactSourceExtent),
                Sha256Text(harnessExtent));
        }

        public void AssertPreserved()
        {
            foreach (var pair in preservedHashes)
            {
                Assert.True(File.Exists(pair.Key), $"Missing sentinel: {pair.Key}");
                Assert.Equal(pair.Value, Sha256(pair.Key));
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(RootPath))
            {
                foreach (var file in Directory.EnumerateFiles(
                    RootPath, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(RootPath, recursive: true);
            }
        }

        private static string Write(string content, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        private static string Encode(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

        private static string Sha256Text(string value) => Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }
}
