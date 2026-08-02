using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.Skins.Tests.Storage;

public sealed class DirectoryMoveCommitTests
{
    [Fact]
    public void CleanInstall_CommittedCandidatePostCheckFailure_RollsBackPublishedDirectory()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var finalPath = Path.Combine(
            fixture.Paths.InstalledSkinsRoot,
            preview.Package.Manifest.SkinId.ToString("D").ToLowerInvariant());
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            PhysicalSkinFileSystem.Instance,
            directoryMoveProvider: new PostCommitFailureMoveProvider(
                MoveFailurePoint.CandidatePromotion));

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Null(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "install.io");
        Assert.False(Directory.Exists(finalPath));
        Assert.Empty(new InstalledSkinCatalog(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion).LoadAll().Installed);
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Replace_CommittedBackupPostCheckFailure_RestoresExactOriginal()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var original = fixture.InstallInitial("1.2.3");
        var originalBytes = SnapshotDirectory(original.DirectoryPath);
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.3.0", includeAllAssets: true));
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            PhysicalSkinFileSystem.Instance,
            directoryMoveProvider: new PostCommitFailureMoveProvider(
                MoveFailurePoint.ExistingBackup));

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Null(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "install.io");
        Assert.Equal(originalBytes, SnapshotDirectory(original.DirectoryPath));
        var visible = Assert.Single(new InstalledSkinCatalog(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion).LoadAll().Installed);
        Assert.Equal(SemanticVersion.Parse("1.2.3"), visible.PackageVersion);
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void RollbackMove_CommittedPostCheckFailure_RetainsExactCandidateEvidence()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        using var cancellation = new CancellationTokenSource();
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            SkinPackageInstallerTests.InstalledHudVersion,
            PhysicalSkinFileSystem.Instance,
            directoryMoveProvider: new CancellingRollbackFailureMoveProvider(
                cancellation));
        var finalPath = Path.Combine(
            fixture.Paths.InstalledSkinsRoot,
            preview.Package.Manifest.SkinId.ToString("D").ToLowerInvariant());

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            cancellation.Token);

        Assert.Null(result.Installed);
        Assert.Contains(
            result.Errors,
            error => error.Code == "install.rollback-failed");
        Assert.False(Directory.Exists(finalPath));
        var operationPath = Assert.Single(
            Directory.EnumerateDirectories(fixture.Paths.ImportsRoot));
        var candidatePath = Path.Combine(
            operationPath,
            "candidate",
            preview.Package.Manifest.SkinId.ToString("D").ToLowerInvariant());
        Assert.True(Directory.Exists(candidatePath));
        Assert.NotEmpty(SnapshotDirectory(candidatePath));
    }

    [Fact]
    public void Move_RejectsDifferentVolumesBeforeRequiringWindowsHandles()
    {
        using var source = new FakeDirectoryLease(
            @"C:\source",
            new DirectoryIdentity(0x11111111, 1));
        using var destinationParent = new FakeDirectoryLease(
            @"D:\destination",
            new DirectoryIdentity(0x22222222, 2));

        var exception = Assert.Throws<IOException>(() =>
            PhysicalDirectoryLeaseProvider.Instance.Move(
                source,
                source.ExpectedPath,
                destinationParent,
                destinationParent.ExpectedPath,
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                Path.Combine(
                    destinationParent.ExpectedPath,
                    "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));

        Assert.Contains("different volume", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeDirectoryLease(
        string expectedPath,
        DirectoryIdentity identity) : IDirectoryLease
    {
        public DirectoryIdentity Identity { get; } = identity;

        public string ExpectedPath { get; } = Path.GetFullPath(expectedPath);

        public void Dispose()
        {
        }
    }

    private static IReadOnlyDictionary<string, byte[]> SnapshotDirectory(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);

    private enum MoveFailurePoint
    {
        ExistingBackup,
        CandidatePromotion
    }

    private sealed class PostCommitFailureMoveProvider(
        MoveFailurePoint failurePoint) : IDirectoryMoveProvider
    {
        private bool _failed;

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
            if (_failed || !MatchesFailurePoint(sourcePath, destinationParentPath))
            {
                return;
            }

            _failed = true;
            throw new DirectoryMoveException(
                "Injected committed move post-check failure.",
                moveCommitted: true,
                innerException: new IOException("Injected post-check failure."));
        }

        private bool MatchesFailurePoint(
            string sourcePath,
            string destinationParentPath) =>
            failurePoint switch
            {
                MoveFailurePoint.ExistingBackup => string.Equals(
                    Path.GetFileName(destinationParentPath),
                    "backup",
                    StringComparison.OrdinalIgnoreCase),
                MoveFailurePoint.CandidatePromotion => sourcePath.Contains(
                    $"{Path.DirectorySeparatorChar}candidate{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase),
                _ => false
            };
    }

    private sealed class CancellingRollbackFailureMoveProvider(
        CancellationTokenSource cancellation) : IDirectoryMoveProvider
    {
        private bool _promoted;

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
            if (!_promoted && sourcePath.Contains(
                    $"{Path.DirectorySeparatorChar}candidate{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
            {
                _promoted = true;
                cancellation.Cancel();
                return;
            }

            if (_promoted && string.Equals(
                    Path.GetFileName(destinationParentPath),
                    "candidate",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new DirectoryMoveException(
                    "Injected committed rollback post-check failure.",
                    moveCommitted: true,
                    innerException: new IOException("Injected rollback post-check failure."));
            }
        }
    }
}
