using System.Diagnostics;
using System.Text.Json;

namespace CodexQuotaHud.App.Tests.Packaging;

[Collection(PackagingScriptCollection.Name)]
public sealed class InstallerLifecycleTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public async Task PrepareInstall_SignalsThenStopsOnlyExactInstalledProcess()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs", "CodexQuotaHud")).FullName;
        var executablePath = Path.Combine(installPath, "CodexQuotaHud.App.exe");
        var unrelatedPath = Path.Combine(
            temp.Path, "Other", "CodexQuotaHud.App.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelatedPath)!);

        var snapshotPath = Path.Combine(temp.Path, "processes.json");
        await File.WriteAllTextAsync(
            snapshotPath,
            JsonSerializer.Serialize(new object[]
            {
                new
                {
                    ProcessId = 101,
                    ProcessIdentity = "handle-101",
                    Name = "CodexQuotaHud.App.exe",
                    ExecutablePath = executablePath,
                },
                new
                {
                    ProcessId = 202,
                    ProcessIdentity = "handle-202",
                    Name = "CodexQuotaHud.App.exe",
                    ExecutablePath = unrelatedPath,
                },
                new
                {
                    ProcessId = 303,
                    Name = "notepad.exe",
                    ExecutablePath = executablePath,
                },
            }));
        var actionLogPath = Path.Combine(temp.Path, "actions.json");

        var result = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData,
            "-InternalProcessSnapshotPath", snapshotPath,
            "-InternalActionLogPath", actionLogPath);

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        var actions = await ReadActionsAsync(actionLogPath);
        Assert.Equal(
            new[] { "SignalShutdown", "WaitForExit", "StopProcess", "WaitForExit" },
            actions.Select(ActionName));
        var stopped = SingleAction(actions, "StopProcess");
        Assert.Equal(101, stopped.ProcessId);
        Assert.Equal("handle-101", stopped.ProcessIdentity);
    }

    [Theory]
    [InlineData("LocalAppData")]
    [InlineData("Programs")]
    [InlineData("UserProfile")]
    [InlineData("FileSystemRoot")]
    [InlineData("OtherTarget")]
    public async Task PrepareInstall_RejectsAnythingExceptExactInstallTarget(
        string targetKind)
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var installPath = targetKind switch
        {
            "LocalAppData" => localAppData,
            "Programs" => programs,
            "UserProfile" => Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "FileSystemRoot" => Path.GetPathRoot(localAppData)!,
            "OtherTarget" => Path.Combine(programs, "OtherApplication"),
            _ => throw new InvalidOperationException(targetKind),
        };
        var siblingMarker = Path.Combine(programs, "keep.txt");
        await File.WriteAllTextAsync(siblingMarker, "keep");

        var result = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exactly", result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(siblingMarker));
    }

    [Fact]
    public async Task PrepareInstall_RejectsReparsePointInstallTarget()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var outside = Directory.CreateDirectory(
            Path.Combine(temp.Path, "outside")).FullName;
        var outsideMarker = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(outsideMarker, "keep");
        var installPath = Path.Combine(programs, "CodexQuotaHud");
        var linkResult = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", installPath, outside);
        Assert.Equal(0, linkResult.ExitCode);

        try
        {
            var result = await RunLifecycleAsync(
                "PrepareInstall",
                installPath,
                localAppData);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(outsideMarker));
        }
        finally
        {
            if (Directory.Exists(installPath))
            {
                Directory.Delete(installPath);
            }
        }
    }

    [Fact]
    public async Task PrepareInstall_RejectsMatchingProcessWhosePathCannotBeInspected()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs", "CodexQuotaHud")).FullName;
        var snapshotPath = Path.Combine(temp.Path, "processes.json");
        await File.WriteAllTextAsync(
            snapshotPath,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    ProcessId = 404,
                    Name = "CodexQuotaHud.App.exe",
                    ExecutablePath = (string?)null,
                },
            }));
        var actionLogPath = Path.Combine(temp.Path, "actions.json");
        await File.WriteAllTextAsync(actionLogPath, "[]");

        var result = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData,
            "-InternalProcessSnapshotPath", snapshotPath,
            "-InternalActionLogPath", actionLogPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("inspect", result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            await ReadActionsAsync(actionLogPath),
            action => ActionName(action) == "StopProcess");
    }

    [Fact]
    public async Task PrepareInstall_RejectsExactProcessWithoutStableIdentity()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs", "CodexQuotaHud")).FullName;
        var snapshotPath = Path.Combine(temp.Path, "processes.json");
        await File.WriteAllTextAsync(
            snapshotPath,
            JsonSerializer.Serialize(new[]
            {
                new
                {
                    ProcessId = 405,
                    ProcessIdentity = (string?)null,
                    Name = "CodexQuotaHud.App.exe",
                    ExecutablePath = Path.Combine(
                        installPath, "CodexQuotaHud.App.exe"),
                },
            }));
        var actionLogPath = Path.Combine(temp.Path, "actions.json");
        await File.WriteAllTextAsync(actionLogPath, "[]");

        var result = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData,
            "-InternalProcessSnapshotPath", snapshotPath,
            "-InternalActionLogPath", actionLogPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("identity", result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            await ReadActionsAsync(actionLogPath),
            action => ActionName(action) == "StopProcess");
    }

    [Fact]
    public async Task LegacyMigration_BackupCommitAndRollbackAreIdempotent()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(programs, "CodexQuotaHud")).FullName;
        var executablePath = Path.Combine(installPath, "CodexQuotaHud.App.exe");
        var legacyPayloadPath = Path.Combine(installPath, "legacy.dat");
        await File.WriteAllTextAsync(executablePath, "legacy executable");
        await File.WriteAllTextAsync(legacyPayloadPath, "legacy payload");
        var siblingMarker = Path.Combine(programs, "keep.txt");
        await File.WriteAllTextAsync(siblingMarker, "keep");
        var backupPath = Path.Combine(
            programs,
            $"CodexQuotaHud.legacy-backup.{Guid.NewGuid():N}");

        var prepare = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");

        Assert.True(prepare.ExitCode == 0, prepare.CombinedOutput);
        Assert.Equal(
            "legacy executable",
            await File.ReadAllTextAsync(
                Path.Combine(backupPath, "CodexQuotaHud.App.exe")));
        Assert.Equal(
            "legacy payload",
            await File.ReadAllTextAsync(Path.Combine(backupPath, "legacy.dat")));
        Assert.True(File.Exists(executablePath));
        var markerPath = Path.Combine(
            backupPath, "CodexQuotaHud.LegacyBackup.json");
        using (var marker = JsonDocument.Parse(
            await File.ReadAllTextAsync(markerPath)))
        {
            Assert.Equal(
                new[] { "Source", "Destination" },
                marker.RootElement.EnumerateObject().Select(
                    property => property.Name));
            Assert.Equal(
                Path.GetFullPath(installPath),
                marker.RootElement.GetProperty("Source").GetString());
            Assert.Equal(
                Path.GetFullPath(backupPath),
                marker.RootElement.GetProperty("Destination").GetString());
        }

        Directory.Delete(installPath, recursive: true);
        Directory.CreateDirectory(installPath);
        await File.WriteAllTextAsync(executablePath, "new executable");
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "new.dat"), "new payload");

        var rollback = await RunLifecycleAsync(
            "RollbackInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");

        Assert.True(rollback.ExitCode == 0, rollback.CombinedOutput);
        Assert.Equal("legacy executable",
            await File.ReadAllTextAsync(executablePath));
        Assert.Equal("legacy payload",
            await File.ReadAllTextAsync(legacyPayloadPath));
        Assert.False(File.Exists(Path.Combine(installPath, "new.dat")));
        Assert.False(File.Exists(Path.Combine(
            installPath, "CodexQuotaHud.LegacyBackup.json")));
        Assert.False(Directory.Exists(backupPath));
        Assert.True(File.Exists(siblingMarker));

        var secondRollback = await RunLifecycleAsync(
            "RollbackInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");
        Assert.True(secondRollback.ExitCode == 0, secondRollback.CombinedOutput);
        Assert.Equal("legacy executable",
            await File.ReadAllTextAsync(executablePath));

        var secondPrepare = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");
        Assert.True(secondPrepare.ExitCode == 0, secondPrepare.CombinedOutput);
        Assert.True(Directory.Exists(backupPath));

        var commit = await RunLifecycleAsync(
            "CommitInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");
        Assert.True(commit.ExitCode == 0, commit.CombinedOutput);
        Assert.False(Directory.Exists(backupPath));
        Assert.True(Directory.Exists(installPath));
        Assert.True(File.Exists(siblingMarker));

        var secondCommit = await RunLifecycleAsync(
            "CommitInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");
        Assert.True(secondCommit.ExitCode == 0, secondCommit.CombinedOutput);
        Assert.True(File.Exists(siblingMarker));
    }

    [Fact]
    public async Task RollbackInstall_CopyFailureKeepsTargetAndBackupThenCanRetry()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(programs, "CodexQuotaHud")).FullName;
        var executablePath = Path.Combine(installPath, "CodexQuotaHud.App.exe");
        await File.WriteAllTextAsync(executablePath, "legacy executable");
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "legacy.dat"), "legacy payload");
        var backupPath = Path.Combine(
            programs,
            $"CodexQuotaHud.legacy-backup.{Guid.NewGuid():N}");
        var prepare = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");
        Assert.True(prepare.ExitCode == 0, prepare.CombinedOutput);
        Directory.Delete(installPath, recursive: true);
        Directory.CreateDirectory(installPath);
        await File.WriteAllTextAsync(executablePath, "new executable");
        var actionLogPath = Path.Combine(temp.Path, "rollback-actions.json");
        await File.WriteAllTextAsync(actionLogPath, "[]");

        var failedRollback = await RunLifecycleAsync(
            "RollbackInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal",
            "-InternalRollbackCopyFailureAfterItemCount", "1",
            "-InternalActionLogPath", actionLogPath);

        Assert.NotEqual(0, failedRollback.ExitCode);
        Assert.Contains("Injected rollback copy failure",
            failedRollback.CombinedOutput, StringComparison.Ordinal);
        var staged = SingleAction(
            await ReadActionsAsync(actionLogPath), "StageRollbackCopy");
        Assert.Equal(1, staged.ItemCount);
        Assert.Equal("new executable",
            await File.ReadAllTextAsync(executablePath));
        Assert.True(File.Exists(Path.Combine(
            backupPath, "CodexQuotaHud.LegacyBackup.json")));
        Assert.Empty(Directory.GetDirectories(
            programs,
            "CodexQuotaHud.rollback-staging.*",
            SearchOption.TopDirectoryOnly));

        var retry = await RunLifecycleAsync(
            "RollbackInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");

        Assert.True(retry.ExitCode == 0, retry.CombinedOutput);
        Assert.Equal("legacy executable",
            await File.ReadAllTextAsync(executablePath));
        Assert.Equal("legacy payload",
            await File.ReadAllTextAsync(
                Path.Combine(installPath, "legacy.dat")));
        Assert.False(Directory.Exists(backupPath));
    }

    [Fact]
    public async Task RollbackInstall_RestoresHiddenAndReadOnlyLegacyFiles()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(programs, "CodexQuotaHud")).FullName;
        var executablePath = Path.Combine(installPath, "CodexQuotaHud.App.exe");
        var hiddenPath = Path.Combine(installPath, "hidden.dat");
        var readOnlyPath = Path.Combine(installPath, "readonly.dat");
        await File.WriteAllTextAsync(executablePath, "legacy");
        await File.WriteAllTextAsync(hiddenPath, "hidden payload");
        await File.WriteAllTextAsync(readOnlyPath, "readonly payload");
        File.SetAttributes(hiddenPath, FileAttributes.Hidden);
        File.SetAttributes(readOnlyPath, FileAttributes.ReadOnly);
        var backupPath = Path.Combine(
            programs,
            $"CodexQuotaHud.legacy-backup.{Guid.NewGuid():N}");

        try
        {
            var prepare = await RunLifecycleAsync(
                "PrepareInstall",
                installPath,
                localAppData,
                "-LegacyBackupPath", backupPath,
                "-InternalSkipShutdownSignal");
            Assert.True(prepare.ExitCode == 0, prepare.CombinedOutput);
            File.SetAttributes(hiddenPath, FileAttributes.Normal);
            File.SetAttributes(readOnlyPath, FileAttributes.Normal);
            Directory.Delete(installPath, recursive: true);
            Directory.CreateDirectory(installPath);
            await File.WriteAllTextAsync(executablePath, "new");

            var rollback = await RunLifecycleAsync(
                "RollbackInstall",
                installPath,
                localAppData,
                "-LegacyBackupPath", backupPath,
                "-InternalSkipShutdownSignal");

            Assert.True(rollback.ExitCode == 0, rollback.CombinedOutput);
            Assert.Equal("hidden payload",
                await File.ReadAllTextAsync(hiddenPath));
            Assert.Equal("readonly payload",
                await File.ReadAllTextAsync(readOnlyPath));
            Assert.True((File.GetAttributes(hiddenPath) &
                FileAttributes.Hidden) != 0);
            Assert.True((File.GetAttributes(readOnlyPath) &
                FileAttributes.ReadOnly) != 0);
        }
        finally
        {
            if (File.Exists(hiddenPath))
            {
                File.SetAttributes(hiddenPath, FileAttributes.Normal);
            }

            if (File.Exists(readOnlyPath))
            {
                File.SetAttributes(readOnlyPath, FileAttributes.Normal);
            }
        }
    }

    [Theory]
    [InlineData("OutsidePrograms")]
    [InlineData("WrongPrefix")]
    public async Task LegacyMigration_RejectsBackupOutsideExactSiblingContract(
        string backupKind)
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(programs, "CodexQuotaHud")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "CodexQuotaHud.App.exe"), "legacy");
        var backupPath = backupKind switch
        {
            "OutsidePrograms" => Path.Combine(
                temp.Path,
                $"CodexQuotaHud.legacy-backup.{Guid.NewGuid():N}"),
            "WrongPrefix" => Path.Combine(
                programs,
                $"OtherApplication.legacy-backup.{Guid.NewGuid():N}"),
            _ => throw new InvalidOperationException(backupKind),
        };

        var result = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(
            backupKind == "OutsidePrograms" ? "Programs" : "prefix",
            result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(backupPath));
        Assert.True(File.Exists(
            Path.Combine(installPath, "CodexQuotaHud.App.exe")));
    }

    [Fact]
    public async Task LegacyMigration_RejectsReparsePointBackup()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(programs, "CodexQuotaHud")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "CodexQuotaHud.App.exe"), "legacy");
        var outside = Directory.CreateDirectory(
            Path.Combine(temp.Path, "outside")).FullName;
        var outsideMarker = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(outsideMarker, "keep");
        var backupPath = Path.Combine(
            programs,
            $"CodexQuotaHud.legacy-backup.{Guid.NewGuid():N}");
        var linkResult = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", backupPath, outside);
        Assert.Equal(0, linkResult.ExitCode);

        try
        {
            var result = await RunLifecycleAsync(
                "PrepareInstall",
                installPath,
                localAppData,
                "-LegacyBackupPath", backupPath,
                "-InternalSkipShutdownSignal");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(outsideMarker));
        }
        finally
        {
            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath);
            }
        }
    }

    [Fact]
    public async Task LegacyMigration_RejectsNestedReparsePointInLegacyTarget()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(programs, "CodexQuotaHud")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "CodexQuotaHud.App.exe"), "legacy");
        var outside = Directory.CreateDirectory(
            Path.Combine(temp.Path, "outside")).FullName;
        var outsideMarker = Path.Combine(outside, "private.txt");
        await File.WriteAllTextAsync(outsideMarker, "do not copy");
        var nestedLink = Path.Combine(installPath, "linked-data");
        var linkResult = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", nestedLink, outside);
        Assert.Equal(0, linkResult.ExitCode);
        var backupPath = Path.Combine(
            programs,
            $"CodexQuotaHud.legacy-backup.{Guid.NewGuid():N}");

        try
        {
            var result = await RunLifecycleAsync(
                "PrepareInstall",
                installPath,
                localAppData,
                "-LegacyBackupPath", backupPath,
                "-InternalSkipShutdownSignal");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(backupPath));
            Assert.True(File.Exists(outsideMarker));
        }
        finally
        {
            if (Directory.Exists(nestedLink))
            {
                Directory.Delete(nestedLink);
            }
        }
    }

    [Fact]
    public async Task PrepareInstall_FailureCleanupDoesNotFollowNestedBackupReparsePoint()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(programs, "CodexQuotaHud")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(installPath, "CodexQuotaHud.App.exe"), "legacy");
        var backupPath = Path.Combine(
            programs,
            $"CodexQuotaHud.legacy-backup.{Guid.NewGuid():N}");
        var outside = Directory.CreateDirectory(
            Path.Combine(temp.Path, "outside")).FullName;
        var outsideMarker = Path.Combine(outside, "keep.txt");
        await File.WriteAllTextAsync(outsideMarker, "keep");
        var actionLogPath = Path.Combine(temp.Path, "backup-actions.json");
        await File.WriteAllTextAsync(actionLogPath, "[]");

        var result = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal",
            "-InternalPrepareBackupFailureReparseTargetPath", outside,
            "-InternalActionLogPath", actionLogPath);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Injected legacy backup copy failure",
            result.CombinedOutput, StringComparison.Ordinal);
        var injected = SingleAction(
            await ReadActionsAsync(actionLogPath), "InjectBackupReparse");
        Assert.Equal(outside, injected.Source);
        Assert.True(File.Exists(outsideMarker));
        Assert.False(Directory.Exists(backupPath));
    }

    [Fact]
    public async Task RollbackInstall_RejectsNestedReparsePointBackupBeforeDeletion()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(programs, "CodexQuotaHud")).FullName;
        var executablePath = Path.Combine(installPath, "CodexQuotaHud.App.exe");
        await File.WriteAllTextAsync(executablePath, "legacy");
        var backupPath = Path.Combine(
            programs,
            $"CodexQuotaHud.legacy-backup.{Guid.NewGuid():N}");
        var prepare = await RunLifecycleAsync(
            "PrepareInstall",
            installPath,
            localAppData,
            "-LegacyBackupPath", backupPath,
            "-InternalSkipShutdownSignal");
        Assert.True(prepare.ExitCode == 0, prepare.CombinedOutput);
        await File.WriteAllTextAsync(executablePath, "new");
        var outside = Directory.CreateDirectory(
            Path.Combine(temp.Path, "outside")).FullName;
        var outsideMarker = Path.Combine(outside, "private.txt");
        await File.WriteAllTextAsync(outsideMarker, "do not restore");
        var nestedLink = Path.Combine(backupPath, "linked-data");
        var linkResult = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", nestedLink, outside);
        Assert.Equal(0, linkResult.ExitCode);

        try
        {
            var result = await RunLifecycleAsync(
                "RollbackInstall",
                installPath,
                localAppData,
                "-LegacyBackupPath", backupPath,
                "-InternalSkipShutdownSignal");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal("new", await File.ReadAllTextAsync(executablePath));
            Assert.True(File.Exists(outsideMarker));
        }
        finally
        {
            if (Directory.Exists(nestedLink))
            {
                Directory.Delete(nestedLink);
            }
        }
    }

    [Fact]
    public async Task PurgeSettings_RemovesOnlyExactSettingsDirectory()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(
                localAppData, "Programs", "CodexQuotaHud")).FullName;
        var installedMarker = Path.Combine(installPath, "installed.txt");
        await File.WriteAllTextAsync(installedMarker, "keep installed");
        var settingsPath = Directory.CreateDirectory(
            Path.Combine(localAppData, "CodexQuotaHud")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(settingsPath, "settings.json"), "{}");
        await File.WriteAllTextAsync(
            Path.Combine(settingsPath, "preview-window.json"), "{}");
        var sibling = Directory.CreateDirectory(
            Path.Combine(localAppData, "KeepMe")).FullName;
        var siblingMarker = Path.Combine(sibling, "keep.txt");
        await File.WriteAllTextAsync(siblingMarker, "keep sibling");

        var result = await RunLifecycleAsync(
            "PurgeSettings",
            installPath,
            localAppData,
            "-InternalSkipShutdownSignal");

        Assert.True(result.ExitCode == 0, result.CombinedOutput);
        Assert.False(Directory.Exists(settingsPath));
        Assert.True(File.Exists(installedMarker));
        Assert.True(File.Exists(siblingMarker));
    }

    [Theory]
    [InlineData("LocalAppData")]
    [InlineData("Programs")]
    [InlineData("UserProfile")]
    [InlineData("FileSystemRoot")]
    public async Task PurgeSettings_RejectsUnsafeInstallBoundaryBeforeDeletion(
        string targetKind)
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var programs = Directory.CreateDirectory(
            Path.Combine(localAppData, "Programs")).FullName;
        var settingsPath = Directory.CreateDirectory(
            Path.Combine(localAppData, "CodexQuotaHud")).FullName;
        var settingsMarker = Path.Combine(settingsPath, "settings.json");
        await File.WriteAllTextAsync(settingsMarker, "{}");
        var installPath = targetKind switch
        {
            "LocalAppData" => localAppData,
            "Programs" => programs,
            "UserProfile" => Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile),
            "FileSystemRoot" => Path.GetPathRoot(localAppData)!,
            _ => throw new InvalidOperationException(targetKind),
        };

        var result = await RunLifecycleAsync(
            "PurgeSettings",
            installPath,
            localAppData,
            "-InternalSkipShutdownSignal");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("exactly", result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(settingsMarker));
    }

    [Fact]
    public async Task PurgeSettings_RejectsReparsePointSettingsDirectory()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(
                localAppData, "Programs", "CodexQuotaHud")).FullName;
        var outside = Directory.CreateDirectory(
            Path.Combine(temp.Path, "outside")).FullName;
        var outsideMarker = Path.Combine(outside, "settings.json");
        await File.WriteAllTextAsync(outsideMarker, "{}");
        var settingsPath = Path.Combine(localAppData, "CodexQuotaHud");
        var linkResult = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", settingsPath, outside);
        Assert.Equal(0, linkResult.ExitCode);

        try
        {
            var result = await RunLifecycleAsync(
                "PurgeSettings",
                installPath,
                localAppData,
                "-InternalSkipShutdownSignal");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(outsideMarker));
        }
        finally
        {
            if (Directory.Exists(settingsPath))
            {
                Directory.Delete(settingsPath);
            }
        }
    }

    [Fact]
    public async Task PurgeSettings_RejectsNestedReparsePointWithoutDeletingOutside()
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(
                localAppData, "Programs", "CodexQuotaHud")).FullName;
        var settingsPath = Directory.CreateDirectory(
            Path.Combine(localAppData, "CodexQuotaHud")).FullName;
        await File.WriteAllTextAsync(
            Path.Combine(settingsPath, "settings.json"), "{}");
        var outside = Directory.CreateDirectory(
            Path.Combine(temp.Path, "outside")).FullName;
        var outsideMarker = Path.Combine(outside, "private.txt");
        await File.WriteAllTextAsync(outsideMarker, "keep");
        var nestedLink = Path.Combine(settingsPath, "linked-data");
        var linkResult = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", nestedLink, outside);
        Assert.Equal(0, linkResult.ExitCode);

        try
        {
            var result = await RunLifecycleAsync(
                "PurgeSettings",
                installPath,
                localAppData,
                "-InternalSkipShutdownSignal");

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(
                Path.Combine(settingsPath, "settings.json")));
            Assert.True(File.Exists(outsideMarker));
        }
        finally
        {
            if (Directory.Exists(nestedLink))
            {
                Directory.Delete(nestedLink);
            }
        }
    }

    [Fact]
    public async Task ProductionMode_RejectsCustomLocalAppDataRootWithoutDeletingSettings()
    {
        using var temp = new TemporaryDirectory();
        var fakeLocalAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "FakeLocalAppData")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(
                fakeLocalAppData, "Programs", "CodexQuotaHud")).FullName;
        var settingsPath = Directory.CreateDirectory(
            Path.Combine(fakeLocalAppData, "CodexQuotaHud")).FullName;
        var settingsMarker = Path.Combine(settingsPath, "settings.json");
        await File.WriteAllTextAsync(settingsMarker, "{}");

        var result = await RunLifecycleProductionAsync(
            "PurgeSettings",
            installPath,
            "-LocalAppDataRoot", fakeLocalAppData);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("LocalApplicationData", result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(settingsMarker));
    }

    [Fact]
    public async Task InternalTestMode_RejectsTempPrefixLookalikeRoot()
    {
        var systemTemp = Path.GetFullPath(Path.GetTempPath())
            .TrimEnd(Path.DirectorySeparatorChar);
        var localAppData = Path.Combine(
            systemTemp + "-Outside",
            Guid.NewGuid().ToString("N"),
            "LocalAppData");
        var installPath = Path.Combine(
            localAppData, "Programs", "CodexQuotaHud");

        var result = await RunLifecycleAsync(
            "PurgeSettings",
            installPath,
            localAppData,
            "-InternalSkipShutdownSignal");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("temporary directory", result.CombinedOutput,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("Snapshot")]
    [InlineData("ActionLog")]
    public async Task InternalTestMode_RejectsHooksOutsideUniqueTestDirectory(
        string hookKind)
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(
                localAppData, "Programs", "CodexQuotaHud")).FullName;
        var outsidePath = Path.Combine(
            Path.GetFullPath(Path.GetTempPath()),
            $"CodexQuotaHud-outside-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(
            outsidePath,
            hookKind == "Snapshot" ? "[]" : "do not overwrite");

        try
        {
            var arguments = hookKind == "Snapshot"
                ? new[]
                {
                    "-InternalProcessSnapshotPath", outsidePath,
                    "-InternalSkipShutdownSignal",
                }
                : new[]
                {
                    "-InternalActionLogPath", outsidePath,
                    "-InternalSkipShutdownSignal",
                };

            var result = await RunLifecycleAsync(
                "PrepareInstall",
                installPath,
                localAppData,
                arguments);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("test directory", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                hookKind == "Snapshot" ? "[]" : "do not overwrite",
                await File.ReadAllTextAsync(outsidePath));
        }
        finally
        {
            File.Delete(outsidePath);
        }
    }

    [Theory]
    [InlineData("Snapshot")]
    [InlineData("ActionLog")]
    public async Task InternalTestMode_RejectsReparsePointHookParent(
        string hookKind)
    {
        using var temp = new TemporaryDirectory();
        var localAppData = Directory.CreateDirectory(
            Path.Combine(temp.Path, "LocalAppData")).FullName;
        var installPath = Directory.CreateDirectory(
            Path.Combine(
                localAppData, "Programs", "CodexQuotaHud")).FullName;
        var outside = Directory.CreateDirectory(
            Path.Combine(
                Path.GetFullPath(Path.GetTempPath()),
                $"CodexQuotaHud-hook-{Guid.NewGuid():N}")).FullName;
        var hookFileName = hookKind == "Snapshot"
            ? "processes.json"
            : "actions.json";
        var outsideHookPath = Path.Combine(outside, hookFileName);
        await File.WriteAllTextAsync(
            outsideHookPath,
            hookKind == "Snapshot" ? "[]" : "do not overwrite");
        var linkedParent = Path.Combine(temp.Path, "linked-hooks");
        var linkResult = await RunProcessAsync(
            "cmd.exe", "/d", "/c", "mklink", "/J", linkedParent, outside);
        Assert.Equal(0, linkResult.ExitCode);
        var linkedHookPath = Path.Combine(linkedParent, hookFileName);

        try
        {
            var arguments = hookKind == "Snapshot"
                ? new[]
                {
                    "-InternalProcessSnapshotPath", linkedHookPath,
                    "-InternalSkipShutdownSignal",
                }
                : new[]
                {
                    "-InternalActionLogPath", linkedHookPath,
                    "-InternalSkipShutdownSignal",
                };

            var result = await RunLifecycleAsync(
                "PrepareInstall",
                installPath,
                localAppData,
                arguments);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("reparse", result.CombinedOutput,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(
                hookKind == "Snapshot" ? "[]" : "do not overwrite",
                await File.ReadAllTextAsync(outsideHookPath));
        }
        finally
        {
            if (Directory.Exists(linkedParent))
            {
                Directory.Delete(linkedParent);
            }

            if (Directory.Exists(outside))
            {
                Directory.Delete(outside, recursive: true);
            }
        }
    }

    private static async Task<ActionRecord[]> ReadActionsAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ActionRecord[]>(stream)
            ?? [];
    }

    private static string ActionName(ActionRecord action) => action.Action;

    private static ActionRecord SingleAction(
        IEnumerable<ActionRecord> actions,
        string expected)
    {
        var matches = actions.Where(
            action => string.Equals(
                action.Action,
                expected,
                StringComparison.Ordinal)).ToArray();
        Assert.True(
            matches.Length == 1,
            $"Expected one {expected} action, found {matches.Length}.");
        return matches[0];
    }

    private static Task<ProcessResult> RunLifecycleAsync(
        string action,
        string installPath,
        string localAppDataRoot,
        params string[] additionalArguments)
    {
        var arguments = new List<string>
        {
            "-Action", action,
            "-InstallPath", installPath,
            "-LocalAppDataRoot", localAppDataRoot,
            "-InternalTestMode",
        };
        arguments.AddRange(additionalArguments);
        return RunPowerShellAsync(
            Path.Combine(
                RepositoryRoot, "scripts", "installer-lifecycle.ps1"),
            arguments.ToArray());
    }

    private static Task<ProcessResult> RunLifecycleProductionAsync(
        string action,
        string installPath,
        params string[] additionalArguments)
    {
        var arguments = new List<string>
        {
            "-Action", action,
            "-InstallPath", installPath,
        };
        arguments.AddRange(additionalArguments);
        return RunPowerShellAsync(
            Path.Combine(
                RepositoryRoot, "scripts", "installer-lifecycle.ps1"),
            arguments.ToArray());
    }

    private static Task<ProcessResult> RunPowerShellAsync(
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
        return RunProcessAsync("powershell.exe", allArguments.ToArray());
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

    private sealed record ActionRecord(
        string Action,
        int? ProcessId,
        string? ExecutablePath,
        string? EventName,
        int? TimeoutSeconds,
        string? Source,
        string? Destination,
        int? ItemCount,
        string? ProcessIdentity);

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
