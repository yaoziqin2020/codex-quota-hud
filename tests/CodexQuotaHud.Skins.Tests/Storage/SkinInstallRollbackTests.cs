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
        var installer = new SkinPackageInstaller(fixture.Paths, fileSystem);

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
        var installer = new SkinPackageInstaller(fixture.Paths, fileSystem);

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
    public void CancellationDuringStaging_RemovesOperationAndPublishesNothing()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var preview = fixture.Inspect(fixture.CreatePackage("1.3.0", includeAllAssets: true));
        using var cancellation = new CancellationTokenSource();
        var fileSystem = new CancellingSkinFileSystem(
            cancellation,
            cancelAfterExistingBackupMove: false);
        var installer = new SkinPackageInstaller(fixture.Paths, fileSystem);

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
        var installer = new SkinPackageInstaller(fixture.Paths, fileSystem);

        Assert.Throws<OperationCanceledException>(() => installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            cancellation.Token));

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
            if (_failurePoint == InstallFailurePoint.BackupCleanup &&
                path.Contains(
                    $"{Path.DirectorySeparatorChar}backup{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
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
}
