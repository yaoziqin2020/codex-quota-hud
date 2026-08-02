using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.Skins.Tests.Storage;

public sealed class SkinInstallRollbackTests
{
    [Theory]
    [InlineData(InstallFailurePoint.StagingWrite)]
    [InlineData(InstallFailurePoint.StagedRevalidation)]
    [InlineData(InstallFailurePoint.ExistingToBackupMove)]
    [InlineData(InstallFailurePoint.CandidateToFinalMove)]
    public void FailureBeforeCompletedPromotion_RestoresExactOldBytesAndHidesCandidate(
        InstallFailurePoint failurePoint)
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        fixture.InstallInitial("1.2.3");
        var preview = fixture.Inspect(fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var before = fixture.SnapshotSettings();
        var fileSystem = new FaultingSkinFileSystem(failurePoint);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem);

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(fileSystem.Triggered);
        Assert.Null(result.Installed);
        Assert.NotEmpty(result.Errors);
        Assert.Equal(before, fixture.SnapshotSettings());
        var catalog = new InstalledSkinCatalog(
            fixture.Paths,
            SemanticVersion.Parse("1.1.1"));
        var visible = Assert.Single(catalog.LoadAll().Installed);
        Assert.Equal(SemanticVersion.Parse("1.2.3"), visible.PackageVersion);
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void BackupCleanupFailure_RetainsNewSkinAndRecoverableOperation()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var old = fixture.InstallInitial("1.2.3");
        var oldBytes = SnapshotDirectory(old.DirectoryPath);
        var preview = fixture.Inspect(fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var fileSystem = new FaultingSkinFileSystem(InstallFailurePoint.BackupCleanup);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem);

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(fileSystem.Triggered);
        Assert.Equal(SkinInstallDisposition.Replaced, result.Disposition);
        var installed = Assert.IsType<InstalledSkinRecord>(result.Installed);
        Assert.Equal(SemanticVersion.Parse("1.3.0"), installed.PackageVersion);
        var cleanupError = Assert.Single(
            result.Errors,
            error => error.Code == "install.cleanup-failed");

        var operationPath = Assert.Single(
            Directory.EnumerateDirectories(fixture.Paths.ImportsRoot));
        var operationId = Path.GetFileName(operationPath);
        Assert.True(Guid.TryParseExact(operationId, "D", out var parsedOperationId));
        Assert.Equal(parsedOperationId.ToString("D").ToLowerInvariant(), operationId);
        Assert.Contains(operationId, cleanupError.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(old.DirectoryPath, cleanupError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(preview.Package.Manifest.DisplayName, cleanupError.Message, StringComparison.Ordinal);

        var backupSkinPath = Path.Combine(
            operationPath,
            "backup",
            old.SkinId.ToString("D").ToLowerInvariant());
        Assert.Equal(oldBytes, SnapshotDirectory(backupSkinPath));
        var catalog = new InstalledSkinCatalog(
            fixture.Paths,
            SemanticVersion.Parse("1.1.1"));
        var visible = Assert.Single(catalog.LoadAll().Installed);
        Assert.Equal(SemanticVersion.Parse("1.3.0"), visible.PackageVersion);
    }

    [Fact]
    public void FailedInstallCleanupFailure_DoesNotClaimThatANewSkinWasInstalled()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var fileSystem = new FaultingSkinFileSystem(
            InstallFailurePoint.StagedRevalidation);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem,
            directoryDeleteProvider: ThrowingDirectoryDeleteProvider.Instance);

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Null(result.Installed);
        var error = Assert.Single(
            result.Errors,
            item => item.Code == "install.operation-cleanup-failed");
        Assert.Contains("was not installed", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("new skin was installed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Directory.EnumerateDirectories(fixture.Paths.ImportsRoot));
    }

    [Fact]
    public void CancellationDuringStaging_RemovesOperationAndPublishesNothing()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var preview = fixture.Inspect(fixture.CreatePackage("1.3.0", includeAllAssets: true));
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new CancellingSkinFileSystem(
            cancellation,
            cancelAfterExistingBackupMove: false);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem);

        Assert.Throws<OperationCanceledException>(() => installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            cancellation.Token));

        Assert.False(Directory.Exists(fixture.Paths.InstalledSkinsRoot) &&
            Directory.EnumerateDirectories(fixture.Paths.InstalledSkinsRoot).Any());
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void CancellationAfterExistingMovesToBackup_RestoresExactOldBytes()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        fixture.InstallInitial("1.2.3");
        var preview = fixture.Inspect(fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var before = fixture.SnapshotSettings();
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new CancellingSkinFileSystem(
            cancellation,
            cancelAfterExistingBackupMove: true);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem);

        Assert.Throws<OperationCanceledException>(() => installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            cancellation.Token));

        Assert.Equal(before, fixture.SnapshotSettings());
        fixture.AssertNoOperationDirectories();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationImmediatelyAfterExactPromotion_RollsBackReplaceAndHidesCleanFinal(
        bool replaceExisting)
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var original = replaceExisting ? fixture.InstallInitial("1.2.3") : null;
        var originalBytes = original is null
            ? null
            : SnapshotDirectory(original.DirectoryPath);
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var finalPath = Path.Combine(
            fixture.Paths.InstalledSkinsRoot,
            preview.Package.Manifest.SkinId.ToString("D").ToLowerInvariant());
        using var cancellation = new CancellationTokenSource();
        var moveProvider = new CancellingAfterPromotionDirectoryMoveProvider(
            cancellation);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            PhysicalSkinFileSystem.Instance,
            directoryMoveProvider: moveProvider);

        Assert.Throws<OperationCanceledException>(() => installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            cancellation.Token));

        Assert.True(moveProvider.CancelledAfterPromotion);
        AssertCancellationRollback(
            fixture,
            original,
            originalBytes,
            finalPath);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CancellationObservedAfterFinalReread_RollsBackReplaceAndHidesCleanFinal(
        bool replaceExisting)
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var original = replaceExisting ? fixture.InstallInitial("1.2.3") : null;
        var originalBytes = original is null
            ? null
            : SnapshotDirectory(original.DirectoryPath);
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var finalPath = Path.Combine(
            fixture.Paths.InstalledSkinsRoot,
            preview.Package.Manifest.SkinId.ToString("D").ToLowerInvariant());
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new CancellingAfterFinalRereadFileSystem(
            cancellation,
            finalPath);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem);

        Assert.Throws<OperationCanceledException>(() => installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            cancellation.Token));

        Assert.True(fileSystem.CancellationRequestedDuringFinalReread);
        AssertCancellationRollback(
            fixture,
            original,
            originalBytes,
            finalPath);
    }

    [Fact]
    public async Task ConcurrentInstall_AfterBackupWaitsAndFailsClosedWhenPreviewStateChanges()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var original = fixture.InstallInitial("1.2.3");
        var firstPreview = fixture.Inspect(
            fixture.CreatePackage("1.3.0", includeAllAssets: true));
        using var firstFileSystem = new PausingPromotionFileSystem();
        var firstInstaller = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            firstFileSystem);
        var secondInstaller = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion);

        var firstTask = Task.Run(() => firstInstaller.Install(
            firstPreview,
            SkinCollisionDecision.Replace,
            CancellationToken.None));
        Assert.True(firstFileSystem.WaitForBackupMove(TimeSpan.FromSeconds(5)));
        var secondInspection = secondInstaller.Inspect(
            fixture.CreatePackage("1.4.0", includeAllAssets: true),
            SkinPackageInstallerTests.InstalledHudVersion,
            CancellationToken.None);
        Assert.True(secondInspection.IsValid);
        var secondPreview = Assert.IsType<SkinInstallPreview>(secondInspection.Value);
        Assert.Null(secondPreview.Existing);

        var secondStarted = new ManualResetEventSlim();
        var secondTask = Task.Run(() =>
        {
            secondStarted.Set();
            return secondInstaller.Install(
                secondPreview,
                SkinCollisionDecision.Replace,
                CancellationToken.None);
        });
        Assert.True(secondStarted.Wait(TimeSpan.FromSeconds(5)));
        var completedBeforeRollback = await Task.WhenAny(
            secondTask,
            Task.Delay(TimeSpan.FromSeconds(1))) == secondTask;

        firstFileSystem.ReleasePromotion();
        var firstResult = await firstTask;
        var secondResult = await secondTask;

        Assert.False(completedBeforeRollback);
        Assert.Null(firstResult.Installed);
        Assert.Null(secondResult.Installed);
        Assert.Contains(
            secondResult.Errors,
            error => error.Code == "install.state-changed");
        var visible = Assert.Single(new InstalledSkinCatalog(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion).LoadAll().Installed);
        Assert.Equal(original.PackageVersion, visible.PackageVersion);
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Rollback_DoesNotDeleteFinalWhoseDirectoryIdentityChangedAfterPromotion()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var original = fixture.InstallInitial("1.2.3");
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var fileSystem = new ReplacingAfterPromotionFileSystem();
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem,
            directoryLeaseProvider: DeleteSharingDirectoryLeaseProvider.Instance,
            directoryMoveProvider:
                new ReplacingAfterPromotionDirectoryMoveProvider(fileSystem));

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(fileSystem.ReplacedFinal);
        Assert.Null(result.Installed);
        Assert.Contains(
            result.Errors,
            error => error.Code == "install.rollback-failed");
        Assert.Equal(
            ReplacingAfterPromotionFileSystem.ForeignSentinel,
            File.ReadAllText(Path.Combine(original.DirectoryPath, "foreign.txt")));
        var operationPath = Assert.Single(
            Directory.EnumerateDirectories(fixture.Paths.ImportsRoot));
        var backupPath = Path.Combine(
            operationPath,
            "backup",
            original.SkinId.ToString("D").ToLowerInvariant());
        Assert.True(Directory.Exists(backupPath));
    }

    [Fact]
    public void Install_DoesNotMoveForeignDirectoryWhenExistingChangesBeforeBackupMove()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var original = fixture.InstallInitial("1.2.3");
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var outsideDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Paths.SettingsRoot)!,
            "outside-existing-transition");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(
            Path.Combine(outsideDirectory, "sentinel.txt"),
            "foreign directory must survive");
        var outsideBefore = SnapshotDirectory(outsideDirectory);
        var fileSystem = new ExistingMoveTransitionFileSystem(outsideDirectory);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem,
            directoryMoveProvider:
                new TransitionDirectoryMoveProvider(fileSystem.TransitionBeforeMove));

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(fileSystem.Transitioned);
        Assert.Equal(
            SemanticVersion.Parse("1.3.0"),
            Assert.IsType<InstalledSkinRecord>(result.Installed).PackageVersion);
        Assert.True(Directory.Exists(outsideDirectory));
        Assert.Equal(outsideBefore, SnapshotDirectory(outsideDirectory));
        Assert.True(Directory.Exists(original.DirectoryPath));
    }

    [Fact]
    public void Install_DoesNotMoveForeignDirectoryWhenCandidateChangesBeforeFinalMove()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var outsideDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Paths.SettingsRoot)!,
            "outside-candidate-move-transition");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(
            Path.Combine(outsideDirectory, "sentinel.txt"),
            "foreign candidate must survive");
        var outsideBefore = SnapshotDirectory(outsideDirectory);
        var fileSystem = new CandidateMoveTransitionFileSystem(outsideDirectory);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem,
            directoryMoveProvider:
                new TransitionDirectoryMoveProvider(fileSystem.TransitionBeforeMove));

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(fileSystem.Transitioned);
        Assert.Equal(
            SemanticVersion.Parse("1.2.3"),
            Assert.IsType<InstalledSkinRecord>(result.Installed).PackageVersion);
        Assert.True(Directory.Exists(outsideDirectory));
        Assert.Equal(outsideBefore, SnapshotDirectory(outsideDirectory));
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void ValidationFailureAfterPromotion_RestoresOldWithExactHandleMoves()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        fixture.InstallInitial("1.2.3");
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var before = fixture.SnapshotSettings();
        var fileSystem = new FinalValidationFailureFileSystem(
            fixture.Paths.InstalledSkinsRoot);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            fileSystem,
            directoryMoveProvider:
                new FinalValidationDirectoryMoveProvider(fileSystem));

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(fileSystem.FinalValidationFailed);
        Assert.False(fileSystem.PathRollbackAttempted);
        Assert.Null(result.Installed);
        Assert.Equal(before, fixture.SnapshotSettings());
        fixture.AssertNoOperationDirectories();
    }

    private static IReadOnlyDictionary<string, byte[]> SnapshotDirectory(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);

    private static void AssertCancellationRollback(
        SkinPackageInstallerTests.InstallerFixture fixture,
        InstalledSkinRecord? original,
        IReadOnlyDictionary<string, byte[]>? originalBytes,
        string finalPath)
    {
        var catalog = new InstalledSkinCatalog(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion);
        if (original is null)
        {
            Assert.False(Directory.Exists(finalPath));
            Assert.Empty(catalog.LoadAll().Installed);
        }
        else
        {
            Assert.Equal(originalBytes, SnapshotDirectory(original.DirectoryPath));
            var visible = Assert.Single(catalog.LoadAll().Installed);
            Assert.Equal(original.SkinId, visible.SkinId);
            Assert.Equal(original.PackageVersion, visible.PackageVersion);
        }

        fixture.AssertNoOperationDirectories();
    }

    public enum InstallFailurePoint
    {
        StagingWrite,
        StagedRevalidation,
        ExistingToBackupMove,
        CandidateToFinalMove,
        BackupCleanup
    }

    private sealed class FaultingSkinFileSystem : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly InstallFailurePoint _failurePoint;

        public FaultingSkinFileSystem(InstallFailurePoint failurePoint) =>
            _failurePoint = failurePoint;

        public bool Triggered { get; private set; }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(path);

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(path, searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes)
        {
            if (_failurePoint == InstallFailurePoint.StagedRevalidation &&
                path.Contains(
                    $"{Path.DirectorySeparatorChar}candidate{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                Fail();
            }

            return _inner.ReadAllBytes(path, maximumBytes);
        }

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content)
        {
            if (_failurePoint == InstallFailurePoint.StagingWrite)
            {
                Fail();
            }

            _inner.WriteAllBytesAndFlush(path, content);
        }

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            if (_failurePoint == InstallFailurePoint.ExistingToBackupMove &&
                destinationPath.Contains(
                    $"{Path.DirectorySeparatorChar}backup{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                Fail();
            }

            if (_failurePoint == InstallFailurePoint.CandidateToFinalMove &&
                sourcePath.Contains(
                    $"{Path.DirectorySeparatorChar}candidate{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                Fail();
            }

            _inner.MoveDirectory(sourcePath, destinationPath);
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            if (_failurePoint == InstallFailurePoint.BackupCleanup)
            {
                Fail();
            }

            _inner.DeleteDirectory(path, recursive);
        }

        private void Fail()
        {
            Triggered = true;
            throw new IOException($"Injected {_failurePoint} failure.");
        }
    }

    private sealed class ThrowingDirectoryDeleteProvider : ISafeDirectoryDeleteProvider
    {
        public static ThrowingDirectoryDeleteProvider Instance { get; } = new();

        public void DeleteOwnedTree(string rootPath, int maximumEntries) =>
            throw new IOException("Injected operation cleanup failure.");
    }

    private sealed class CancellingSkinFileSystem : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly CancellationTokenSource _cancellation;
        private readonly bool _cancelAfterExistingBackupMove;
        private bool _cancelled;

        public CancellingSkinFileSystem(
            CancellationTokenSource cancellation,
            bool cancelAfterExistingBackupMove)
        {
            _cancellation = cancellation;
            _cancelAfterExistingBackupMove = cancelAfterExistingBackupMove;
        }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(path);

        public IReadOnlyList<string> EnumerateFiles(string path, SearchOption searchOption) =>
            _inner.EnumerateFiles(path, searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes) =>
            _inner.ReadAllBytes(path, maximumBytes);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content)
        {
            _inner.WriteAllBytesAndFlush(path, content);
            if (!_cancelAfterExistingBackupMove && !_cancelled)
            {
                _cancelled = true;
                _cancellation.Cancel();
            }
        }

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            _inner.MoveDirectory(sourcePath, destinationPath);
            if (_cancelAfterExistingBackupMove &&
                !_cancelled &&
                destinationPath.Contains(
                    $"{Path.DirectorySeparatorChar}backup{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                _cancelled = true;
                _cancellation.Cancel();
            }
        }

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(path, recursive);
    }

    private sealed class CancellingAfterPromotionDirectoryMoveProvider(
        CancellationTokenSource cancellation) : IDirectoryMoveProvider
    {
        public bool CancelledAfterPromotion { get; private set; }

        public void Move(
            IDirectoryLease sourceLease,
            string sourcePath,
            IDirectoryLease destinationParentLease,
            string destinationParentPath,
            string destinationChildName,
            string expectedDestinationPath)
        {
            PhysicalDirectoryLeaseProvider.Instance.Move(
                sourceLease,
                sourcePath,
                destinationParentLease,
                destinationParentPath,
                destinationChildName,
                expectedDestinationPath);
            if (!CancelledAfterPromotion &&
                sourcePath.Contains(
                    $"{Path.DirectorySeparatorChar}candidate{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                CancelledAfterPromotion = true;
                cancellation.Cancel();
            }
        }
    }

    private sealed class CancellingAfterFinalRereadFileSystem(
        CancellationTokenSource cancellation,
        string finalPath) : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly string _lastFinalAssetPath = Path.GetFullPath(Path.Combine(
            finalPath,
            "assets",
            "decoration.png"));

        public bool CancellationRequestedDuringFinalReread { get; private set; }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(path);

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(path, searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes)
        {
            var bytes = _inner.ReadAllBytes(path, maximumBytes);
            if (!CancellationRequestedDuringFinalReread &&
                string.Equals(
                    Path.GetFullPath(path),
                    _lastFinalAssetPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                CancellationRequestedDuringFinalReread = true;
                cancellation.Cancel();
            }

            return bytes;
        }

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content) =>
            _inner.WriteAllBytesAndFlush(path, content);

        public void MoveDirectory(string sourcePath, string destinationPath) =>
            _inner.MoveDirectory(sourcePath, destinationPath);

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(path, recursive);
    }

    private sealed class PausingPromotionFileSystem : ISkinFileSystem, IDisposable
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly ManualResetEventSlim _backupMoved = new();
        private readonly ManualResetEventSlim _releasePromotion = new();

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(path);

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(path, searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes) =>
            _inner.ReadAllBytes(path, maximumBytes);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content) =>
            _inner.WriteAllBytesAndFlush(path, content);

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            if (destinationPath.Contains(
                    $"{Path.DirectorySeparatorChar}backup{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                _inner.MoveDirectory(sourcePath, destinationPath);
                _backupMoved.Set();
                return;
            }

            if (sourcePath.Contains(
                    $"{Path.DirectorySeparatorChar}candidate{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!_releasePromotion.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException("The promotion test seam was not released.");
                }

                throw new IOException("Injected candidate promotion failure.");
            }

            _inner.MoveDirectory(sourcePath, destinationPath);
        }

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(path, recursive);

        public bool WaitForBackupMove(TimeSpan timeout) => _backupMoved.Wait(timeout);

        public void ReleasePromotion() => _releasePromotion.Set();

        public void Dispose()
        {
            _backupMoved.Dispose();
            _releasePromotion.Dispose();
        }
    }

    private sealed class ReplacingAfterPromotionFileSystem : ISkinFileSystem
    {
        public const string ForeignSentinel = "foreign final must survive";

        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private string? _finalPath;
        private string? _displacedCandidatePath;

        public bool ReplacedFinal { get; private set; }

        public bool DirectoryExists(string path)
        {
            if (!ReplacedFinal &&
                _finalPath is not null &&
                string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(_finalPath),
                    StringComparison.OrdinalIgnoreCase))
            {
                _inner.MoveDirectory(_finalPath, _displacedCandidatePath!);
                _inner.CreateDirectory(_finalPath);
                File.WriteAllText(
                    Path.Combine(_finalPath, "foreign.txt"),
                    ForeignSentinel);
                ReplacedFinal = true;
            }

            return _inner.DirectoryExists(path);
        }

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(path);

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(path, searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes) =>
            _inner.ReadAllBytes(path, maximumBytes);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content) =>
            _inner.WriteAllBytesAndFlush(path, content);

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            _inner.MoveDirectory(sourcePath, destinationPath);
            ArmAfterPromotion(sourcePath, destinationPath);
        }

        public void ArmAfterPromotion(string sourcePath, string destinationPath)
        {
            if (sourcePath.Contains(
                    $"{Path.DirectorySeparatorChar}candidate{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                _finalPath = destinationPath;
                var candidateRoot = Path.GetDirectoryName(sourcePath)!;
                var operationRoot = Path.GetDirectoryName(candidateRoot)!;
                _displacedCandidatePath = Path.Combine(
                    operationRoot,
                    "displaced-candidate");
            }
        }

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(path, recursive);
    }

    private sealed class DeleteSharingDirectoryLeaseProvider : IDirectoryLeaseProvider
    {
        public static DeleteSharingDirectoryLeaseProvider Instance { get; } = new();

        public IDirectoryLease Lease(string expectedPath) =>
            WindowsDirectoryLease.Open(expectedPath, allowDeleteSharing: true);
    }

    private sealed class ReplacingAfterPromotionDirectoryMoveProvider(
        ReplacingAfterPromotionFileSystem fileSystem) : IDirectoryMoveProvider
    {
        public void Move(
            IDirectoryLease sourceLease,
            string sourcePath,
            IDirectoryLease destinationParentLease,
            string destinationParentPath,
            string destinationChildName,
            string expectedDestinationPath)
        {
            PhysicalDirectoryLeaseProvider.Instance.Move(
                sourceLease,
                sourcePath,
                destinationParentLease,
                destinationParentPath,
                destinationChildName,
                expectedDestinationPath);
            fileSystem.ArmAfterPromotion(sourcePath, expectedDestinationPath);
        }
    }

    private sealed class ExistingMoveTransitionFileSystem : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly string _outsideDirectory;

        public ExistingMoveTransitionFileSystem(string outsideDirectory) =>
            _outsideDirectory = Path.GetFullPath(outsideDirectory);

        public bool Transitioned { get; private set; }

        public void TransitionBeforeMove() => Transitioned = true;

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(path);

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(path, searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes) =>
            _inner.ReadAllBytes(path, maximumBytes);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content) =>
            _inner.WriteAllBytesAndFlush(path, content);

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            if (!Transitioned &&
                destinationPath.Contains(
                    $"{Path.DirectorySeparatorChar}backup{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                TransitionBeforeMove();
                _inner.MoveDirectory(_outsideDirectory, destinationPath);
                return;
            }

            _inner.MoveDirectory(sourcePath, destinationPath);
        }

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(path, recursive);
    }

    private sealed class CandidateMoveTransitionFileSystem : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly string _outsideDirectory;

        public CandidateMoveTransitionFileSystem(string outsideDirectory) =>
            _outsideDirectory = Path.GetFullPath(outsideDirectory);

        public bool Transitioned { get; private set; }

        public void TransitionBeforeMove() => Transitioned = true;

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(path);

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(path, searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes) =>
            _inner.ReadAllBytes(path, maximumBytes);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content) =>
            _inner.WriteAllBytesAndFlush(path, content);

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            if (!Transitioned &&
                sourcePath.Contains(
                    $"{Path.DirectorySeparatorChar}candidate{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                TransitionBeforeMove();
                _inner.MoveDirectory(_outsideDirectory, destinationPath);
                return;
            }

            _inner.MoveDirectory(sourcePath, destinationPath);
        }

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(path, recursive);
    }

    private sealed class TransitionDirectoryMoveProvider(
        Action transitionBeforeMove) : IDirectoryMoveProvider
    {
        public void Move(
            IDirectoryLease sourceLease,
            string sourcePath,
            IDirectoryLease destinationParentLease,
            string destinationParentPath,
            string destinationChildName,
            string expectedDestinationPath)
        {
            transitionBeforeMove();
            PhysicalDirectoryLeaseProvider.Instance.Move(
                sourceLease,
                sourcePath,
                destinationParentLease,
                destinationParentPath,
                destinationChildName,
                expectedDestinationPath);
        }
    }

    private sealed class FinalValidationFailureFileSystem : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly string _installedRoot;
        private bool _armed;

        public FinalValidationFailureFileSystem(string installedRoot) =>
            _installedRoot = Path.GetFullPath(installedRoot);

        public bool FinalValidationFailed { get; private set; }

        public bool PathRollbackAttempted { get; private set; }

        public void ArmFinalValidation() => _armed = true;

        public bool IsInstalledRoot(string path) =>
            string.Equals(
                Path.GetFullPath(path),
                _installedRoot,
                StringComparison.OrdinalIgnoreCase);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(path);

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(path, searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes)
        {
            if (_armed && !FinalValidationFailed && IsUnderInstalledRoot(path))
            {
                FinalValidationFailed = true;
                throw new IOException("Injected final validation failure.");
            }

            return _inner.ReadAllBytes(path, maximumBytes);
        }

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content) =>
            _inner.WriteAllBytesAndFlush(path, content);

        public void MoveDirectory(string sourcePath, string destinationPath)
        {
            if (IsUnderInstalledRoot(destinationPath) &&
                sourcePath.Contains(
                    $"{Path.DirectorySeparatorChar}backup{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                PathRollbackAttempted = true;
                throw new IOException("Path-based backup rollback is forbidden.");
            }

            _inner.MoveDirectory(sourcePath, destinationPath);
        }

        public void DeleteDirectory(string path, bool recursive)
        {
            if (IsUnderInstalledRoot(path))
            {
                PathRollbackAttempted = true;
                throw new IOException("Path-based final rollback is forbidden.");
            }

            _inner.DeleteDirectory(path, recursive);
        }

        private bool IsUnderInstalledRoot(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(
                _installedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class FinalValidationDirectoryMoveProvider(
        FinalValidationFailureFileSystem fileSystem) : IDirectoryMoveProvider
    {
        public void Move(
            IDirectoryLease sourceLease,
            string sourcePath,
            IDirectoryLease destinationParentLease,
            string destinationParentPath,
            string destinationChildName,
            string expectedDestinationPath)
        {
            PhysicalDirectoryLeaseProvider.Instance.Move(
                sourceLease,
                sourcePath,
                destinationParentLease,
                destinationParentPath,
                destinationChildName,
                expectedDestinationPath);
            if (fileSystem.IsInstalledRoot(destinationParentPath))
            {
                fileSystem.ArmFinalValidation();
            }
        }
    }
}
