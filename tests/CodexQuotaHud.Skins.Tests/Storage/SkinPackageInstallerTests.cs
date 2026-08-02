using System.Reflection;
using System.Security.Cryptography;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;
using CodexQuotaHud.Skins.Tests.Fixtures;

namespace CodexQuotaHud.Skins.Tests.Storage;

public sealed class SkinPackageInstallerTests
{
    internal static readonly SemanticVersion InstalledHudVersion =
        SemanticVersion.Parse("1.1.1");

    [Fact]
    public void CleanInstall_DoesNotRequestACollisionDecision_AndPublishesOnlyPackageFiles()
    {
        using var fixture = new InstallerFixture();
        var packagePath = fixture.CreatePackage("1.2.3");

        var preview = fixture.Inspect(packagePath);

        Assert.Null(preview.Existing);
        Assert.False(preview.IsDowngrade);
        Assert.Empty(preview.AllowedDecisions);

        var result = fixture.Installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(
            result.Disposition == SkinInstallDisposition.Installed,
            string.Join(Environment.NewLine, result.Errors.Select(
                error => $"{error.Code}: {error.Message}")));
        var installed = AssertInstalled(result);
        Assert.Equal("custom:11111111-1111-1111-1111-111111111111", installed.SelectionKey);
        Assert.Equal(SemanticVersion.Parse("1.2.3"), installed.PackageVersion);
        fixture.AssertPublishedFilesMatch(installed);
        fixture.AssertNoOperationDirectories();
    }

    [Theory]
    [InlineData("1.2.4")]
    [InlineData("1.2.3")]
    public void ExistingSameId_AtSameOrLowerVersion_AllowsReplaceKeepCopyOrCancel(
        string importedVersion)
    {
        using var fixture = new InstallerFixture();
        fixture.InstallInitial("1.2.3");
        var packagePath = fixture.CreatePackage(importedVersion);

        var preview = fixture.Inspect(packagePath);

        Assert.NotNull(preview.Existing);
        Assert.False(preview.IsDowngrade);
        Assert.Equal(
            [
                SkinCollisionDecision.Replace,
                SkinCollisionDecision.KeepCopy,
                SkinCollisionDecision.Cancel
            ],
            preview.AllowedDecisions);
    }

    [Fact]
    public void OlderSameId_IsRejectedAsDowngradeWithoutAnInstallDecision()
    {
        using var fixture = new InstallerFixture();
        fixture.InstallInitial("2.0.0");
        var before = fixture.SnapshotSettings();
        var preview = fixture.Inspect(fixture.CreatePackage("1.9.9"));

        Assert.True(preview.IsDowngrade);
        Assert.Empty(preview.AllowedDecisions);

        var result = fixture.Installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Null(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "install.downgrade");
        Assert.Equal(before, fixture.SnapshotSettings());
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Replace_PreservesIdentityAndPublishesImportedVersion()
    {
        using var fixture = new InstallerFixture();
        fixture.InstallInitial("1.2.3");
        var preview = fixture.Inspect(fixture.CreatePackage("1.3.0"));

        var result = fixture.Installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Equal(SkinInstallDisposition.Replaced, result.Disposition);
        var installed = AssertInstalled(result);
        Assert.Equal(preview.Package.Manifest.SkinId, installed.SkinId);
        Assert.Equal("custom:11111111-1111-1111-1111-111111111111", installed.SelectionKey);
        Assert.Equal(SemanticVersion.Parse("1.3.0"), installed.PackageVersion);
        fixture.AssertPublishedFilesMatch(installed);
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void KeepCopy_AssignsNewIdentityAndLocalProvenanceWithoutChangingSourcePackage()
    {
        using var fixture = new InstallerFixture();
        var original = fixture.InstallInitial("1.2.3");
        var packagePath = fixture.CreatePackage("1.3.0", includeAllAssets: true);
        var sourceBytes = File.ReadAllBytes(packagePath);
        var sourceHash = SHA256.HashData(sourceBytes);
        var preview = fixture.Inspect(packagePath);

        var result = fixture.Installer.Install(
            preview,
            SkinCollisionDecision.KeepCopy,
            CancellationToken.None);

        Assert.Equal(SkinInstallDisposition.KeptCopy, result.Disposition);
        var installed = AssertInstalled(result);
        Assert.NotEqual(preview.Package.Manifest.SkinId, installed.SkinId);
        Assert.Equal(preview.Package.Manifest.SkinId, installed.Package.Manifest.OriginSkinId);
        Assert.Equal($"custom:{installed.SkinId:D}", installed.SelectionKey);
        Assert.Equal(sourceBytes, File.ReadAllBytes(packagePath));
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(packagePath)));
        Assert.True(Directory.Exists(original.DirectoryPath));
        fixture.AssertPublishedFilesMatch(installed);
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Cancel_LeavesInstalledAndImportStorageByteForByteUnchanged()
    {
        using var fixture = new InstallerFixture();
        fixture.InstallInitial("1.2.3");
        var preview = fixture.Inspect(fixture.CreatePackage("1.3.0"));
        var before = fixture.SnapshotSettings();

        var result = fixture.Installer.Install(
            preview,
            SkinCollisionDecision.Cancel,
            CancellationToken.None);

        Assert.Equal(SkinInstallDisposition.Cancelled, result.Disposition);
        Assert.Null(result.Installed);
        Assert.Empty(result.Errors);
        Assert.Equal(before, fixture.SnapshotSettings());
    }

    [Fact]
    public void Remove_DeletesExactlyOneCanonicalCustomDirectoryAndPreservesSiblings()
    {
        using var fixture = new InstallerFixture();
        var removed = fixture.InstallInitial("1.2.3");
        var sibling = Install(
            fixture,
            fixture.CreatePackage(
                "1.2.3",
                includeAllAssets: true,
                skinId: Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var siblingBytes = SnapshotDirectory(sibling.DirectoryPath);

        var result = fixture.Installer.Remove(removed.SkinId);

        Assert.True(result.IsValid);
        Assert.Equal(removed.SkinId, result.Value);
        Assert.False(Directory.Exists(removed.DirectoryPath));
        Assert.Equal(siblingBytes, SnapshotDirectory(sibling.DirectoryPath));
        Assert.Equal(
            sibling.SkinId,
            new InstalledSkinCatalog(fixture.Paths, InstalledHudVersion)
                .Find(sibling.SkinId)?.SkinId);
    }

    [Fact]
    public void Remove_RejectsUnknownIdWithoutChangingSiblings()
    {
        using var fixture = new InstallerFixture();
        fixture.InstallInitial("1.2.3");
        var before = fixture.SnapshotSettings();

        var result = fixture.Installer.Remove(
            Guid.Parse("99999999-9999-9999-9999-999999999999"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "remove.not-found");
        Assert.Equal(before, fixture.SnapshotSettings());
    }

    [Theory]
    [InlineData("10000000-0000-0000-0000-000000000001")]
    [InlineData("10000000-0000-0000-0000-000000000002")]
    [InlineData("10000000-0000-0000-0000-000000000003")]
    [InlineData("10000000-0000-0000-0000-000000000004")]
    [InlineData("10000000-0000-0000-0000-000000000005")]
    public void Remove_RejectsEveryReservedBuiltInId(string idText)
    {
        using var fixture = new InstallerFixture();
        var id = Guid.Parse(idText);
        var directory = Path.Combine(fixture.Paths.InstalledSkinsRoot, idText);
        Directory.CreateDirectory(directory);

        var result = fixture.Installer.Remove(id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "remove.reserved-id");
        Assert.True(Directory.Exists(directory));
    }

    [Fact]
    public void Remove_RejectsDirectoryWhoseActualNameIsNotLowercaseCanonicalGuid()
    {
        using var fixture = new InstallerFixture();
        var id = Guid.Parse("ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB");
        var uppercasePath = Path.Combine(
            fixture.Paths.InstalledSkinsRoot,
            id.ToString("D").ToUpperInvariant());
        Directory.CreateDirectory(uppercasePath);

        var result = fixture.Installer.Remove(id);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "remove.path.invalid");
        Assert.True(Directory.Exists(uppercasePath));
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("skins")]
    [InlineData("final")]
    public void Remove_RejectsReparseAtStorageAncestorRootOrFinalDirectory(
        string markedLocation)
    {
        using var fixture = new InstallerFixture();
        var installed = fixture.InstallInitial("1.2.3");
        var markedPath = markedLocation switch
        {
            "settings" => fixture.Paths.SettingsRoot,
            "skins" => fixture.Paths.InstalledSkinsRoot,
            "final" => installed.DirectoryPath,
            _ => throw new ArgumentOutOfRangeException(nameof(markedLocation))
        };
        var fileSystem = new InstalledSkinCatalogTests.ReparseMarkingFileSystem(
            markedPath,
            forbidMarkedEnumeration: markedLocation == "skins");
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            fileSystem);

        var result = installer.Remove(installed.SkinId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "remove.path.invalid");
        Assert.True(Directory.Exists(installed.DirectoryPath));
    }

    [Fact]
    public void Remove_DoesNotDeleteForeignDirectoryWhenPathChangesBeforeQuarantineMove()
    {
        using var fixture = new InstallerFixture();
        var installed = fixture.InstallInitial("1.2.3");
        var outsideDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Paths.SettingsRoot)!,
            "outside-remove-transition");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(
            Path.Combine(outsideDirectory, "sentinel.txt"),
            "foreign remove target must survive");
        var outsideBefore = SnapshotDirectory(outsideDirectory);
        var fileSystem = new RemoveTransitionFileSystem(
            installed.DirectoryPath,
            outsideDirectory);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            fileSystem,
            directoryMoveProvider:
                new RemoveTransitionDirectoryMoveProvider(fileSystem));

        var result = installer.Remove(installed.SkinId);

        Assert.True(fileSystem.Transitioned);
        Assert.True(result.IsValid);
        Assert.True(Directory.Exists(outsideDirectory));
        Assert.Equal(outsideBefore, SnapshotDirectory(outsideDirectory));
        Assert.Null(new InstalledSkinCatalog(
            fixture.Paths,
            InstalledHudVersion).Find(installed.SkinId));
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Remove_DoesNotCreateQuarantineOutsideWhenImportsChangesBeforeOperationCreation()
    {
        using var fixture = new InstallerFixture();
        var installed = fixture.InstallInitial("1.2.3");
        var outsideDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Paths.SettingsRoot)!,
            "outside-remove-operation-create-transition");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(
            Path.Combine(outsideDirectory, "sentinel.txt"),
            "remove operation create target must survive byte-for-byte");
        var outsideBefore = SnapshotDirectory(outsideDirectory);
        var fileSystem = new ImportsCreateTransitionFileSystem(
            fixture.Paths.ImportsRoot,
            outsideDirectory);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            fileSystem,
            ownedStorageWriter:
                new RemoveOperationTransitionOwnedStorageWriter(fileSystem));

        var result = installer.Remove(installed.SkinId);

        Assert.True(fileSystem.TransitionedBeforeCandidateCreation);
        Assert.False(result.IsValid);
        Assert.Equal(outsideBefore, SnapshotDirectory(outsideDirectory));
        Assert.Equal(
            ["sentinel.txt"],
            Directory.EnumerateFileSystemEntries(
                    outsideDirectory,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(outsideDirectory, path)
                    .Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray());
        Assert.True(Directory.Exists(installed.DirectoryPath));
    }

    [Fact]
    public void Remove_CleanupFailureRetainsRecoveryOperationAfterSkinIsQuarantined()
    {
        using var fixture = new InstallerFixture();
        var installed = fixture.InstallInitial("1.2.3");
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            PhysicalSkinFileSystem.Instance,
            directoryDeleteProvider: ThrowingDirectoryDeleteProvider.Instance);

        var result = installer.Remove(installed.SkinId);

        Assert.False(result.IsValid);
        Assert.Equal(installed.SkinId, result.Value);
        var error = Assert.Single(
            result.Errors,
            item => item.Code == "remove.cleanup-failed");
        Assert.Contains("Recovery operation:", error.Message, StringComparison.Ordinal);
        Assert.Null(new InstalledSkinCatalog(
            fixture.Paths,
            InstalledHudVersion).Find(installed.SkinId));
        Assert.Single(Directory.EnumerateDirectories(fixture.Paths.ImportsRoot));
    }

    [Fact]
    public void Remove_UncommittedQuarantineFailure_CleansTemporaryOperation()
    {
        using var fixture = new InstallerFixture();
        var installed = fixture.InstallInitial("1.2.3");
        var installedBytes = SnapshotDirectory(installed.DirectoryPath);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            PhysicalSkinFileSystem.Instance,
            directoryMoveProvider: ThrowingBeforeMoveDirectoryMoveProvider.Instance);

        var result = installer.Remove(installed.SkinId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "remove.io");
        Assert.Equal(installedBytes, SnapshotDirectory(installed.DirectoryPath));
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Remove_UncommittedFailureAndCleanupFailure_ReturnsRecoveryOperation()
    {
        using var fixture = new InstallerFixture();
        var installed = fixture.InstallInitial("1.2.3");
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            PhysicalSkinFileSystem.Instance,
            directoryMoveProvider: ThrowingBeforeMoveDirectoryMoveProvider.Instance,
            directoryDeleteProvider: ThrowingDirectoryDeleteProvider.Instance);

        var result = installer.Remove(installed.SkinId);

        Assert.False(result.IsValid);
        var error = Assert.Single(
            result.Errors,
            item => item.Code == "remove.operation-cleanup-failed");
        var operationPath = Assert.Single(
            Directory.EnumerateDirectories(fixture.Paths.ImportsRoot));
        Assert.Contains(
            Path.GetFileName(operationPath),
            error.Message,
            StringComparison.Ordinal);
        Assert.True(Directory.Exists(installed.DirectoryPath));
    }

    [Fact]
    public void Remove_CommittedQuarantinePostCheckFailure_ReportsRemovedAndRetainsEvidence()
    {
        using var fixture = new InstallerFixture();
        var installed = fixture.InstallInitial("1.2.3");
        var installedBytes = SnapshotDirectory(installed.DirectoryPath);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            PhysicalSkinFileSystem.Instance,
            directoryMoveProvider: CommittedRemovePostCheckFailureMoveProvider.Instance);

        var result = installer.Remove(installed.SkinId);

        Assert.False(result.IsValid);
        Assert.Equal(installed.SkinId, result.Value);
        var error = Assert.Single(
            result.Errors,
            item => item.Code == "remove.quarantine-verification-failed");
        var operationPath = Assert.Single(
            Directory.EnumerateDirectories(fixture.Paths.ImportsRoot));
        Assert.Contains(
            Path.GetFileName(operationPath),
            error.Message,
            StringComparison.Ordinal);
        var quarantinePath = Path.Combine(
            operationPath,
            "remove",
            installed.SkinId.ToString("D").ToLowerInvariant());
        Assert.Equal(installedBytes, SnapshotDirectory(quarantinePath));
        Assert.Null(new InstalledSkinCatalog(
            fixture.Paths,
            InstalledHudVersion).Find(installed.SkinId));
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("imports")]
    [InlineData("skins")]
    public void Install_RejectsReparseStorageAncestorsBeforeStaging(string markedRoot)
    {
        using var fixture = new InstallerFixture();
        var preview = fixture.Inspect(fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var markedPath = markedRoot switch
        {
            "settings" => fixture.Paths.SettingsRoot,
            "imports" => fixture.Paths.ImportsRoot,
            "skins" => fixture.Paths.InstalledSkinsRoot,
            _ => throw new ArgumentOutOfRangeException(nameof(markedRoot))
        };
        Directory.CreateDirectory(markedPath);
        var fileSystem = new InstalledSkinCatalogTests.ReparseMarkingFileSystem(markedPath);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            fileSystem);

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Null(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "install.path.invalid");
        Assert.Empty(new InstalledSkinCatalog(
            fixture.Paths,
            InstalledHudVersion,
            fileSystem).LoadAll().Installed);
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Install_DoesNotWriteOutsideStorageWhenCandidateChangesBeforeManifestWrite()
    {
        using var fixture = new InstallerFixture();
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var outsideDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Paths.SettingsRoot)!,
            "outside-candidate-transition");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(
            Path.Combine(outsideDirectory, "sentinel.txt"),
            "must survive byte-for-byte");
        var outsideBefore = SnapshotDirectory(outsideDirectory);
        var fileSystem = new CandidateTransitionFileSystem(outsideDirectory);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            fileSystem,
            directoryLeaseProvider:
                new CandidateTransitionDirectoryLeaseProvider(fileSystem));

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(fileSystem.Transitioned);
        Assert.Null(result.Installed);
        Assert.Equal(outsideBefore, SnapshotDirectory(outsideDirectory));
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Install_DoesNotCreateCandidateOutsideWhenImportsChangesBeforeCandidateCreation()
    {
        using var fixture = new InstallerFixture();
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var outsideDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Paths.SettingsRoot)!,
            "outside-imports-create-transition");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(
            Path.Combine(outsideDirectory, "sentinel.txt"),
            "imports create target must survive byte-for-byte");
        var outsideBefore = SnapshotDirectory(outsideDirectory);
        var fileSystem = new ImportsCreateTransitionFileSystem(
            fixture.Paths.ImportsRoot,
            outsideDirectory);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            fileSystem,
            directoryDeleteProvider: ThrowingDirectoryDeleteProvider.Instance,
            ownedStorageWriter:
                new ImportsCreateTransitionOwnedStorageWriter(fileSystem));

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(fileSystem.TransitionedBeforeCandidateCreation);
        Assert.Null(result.Installed);
        Assert.Equal(outsideBefore, SnapshotDirectory(outsideDirectory));
        Assert.Equal(
            ["sentinel.txt"],
            Directory.EnumerateFileSystemEntries(
                    outsideDirectory,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(outsideDirectory, path)
                    .Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray());
    }

    [Fact]
    public void Install_DoesNotWriteAssetOutsideWhenAssetsChangesAfterParentCreation()
    {
        using var fixture = new InstallerFixture();
        var preview = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var outsideDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Paths.SettingsRoot)!,
            "outside-assets-write-transition");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(
            Path.Combine(outsideDirectory, "sentinel.txt"),
            "assets write target must survive byte-for-byte");
        var outsideBefore = SnapshotDirectory(outsideDirectory);
        var fileSystem = new AssetsWriteTransitionFileSystem(outsideDirectory);
        var installer = new SkinPackageInstaller(
            fixture.Paths,
            InstalledHudVersion,
            fileSystem,
            ownedStorageWriter:
                new AssetsWriteTransitionOwnedStorageWriter(fileSystem));

        var result = installer.Install(
            preview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.True(fileSystem.TransitionedAfterAssetsCreation);
        Assert.Null(result.Installed);
        Assert.Equal(outsideBefore, SnapshotDirectory(outsideDirectory));
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Install_RejectsForgedExistingDirectoryOutsideOwnedSkinStorage()
    {
        using var fixture = new InstallerFixture();
        var cleanPreview = fixture.Inspect(fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var outsideDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Paths.SettingsRoot)!,
            "outside-skin");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(outsideDirectory, "sentinel.txt"), "must survive");
        var outsideBytes = SnapshotDirectory(outsideDirectory);
        var forgedExisting = new InstalledSkinRecord(
            $"custom:{cleanPreview.Package.Manifest.SkinId:D}",
            cleanPreview.Package.Manifest.SkinId,
            "Forged",
            SemanticVersion.Parse("1.0.0"),
            outsideDirectory,
            cleanPreview.Package);
        var forgedPreview = NewPreview(
            cleanPreview.Package,
            forgedExisting,
            cleanPreview.IsDowngrade,
            [
                SkinCollisionDecision.Replace,
                SkinCollisionDecision.KeepCopy,
                SkinCollisionDecision.Cancel
            ]);

        var result = fixture.Installer.Install(
            forgedPreview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Null(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "install.preview.invalid");
        Assert.Equal(outsideBytes, SnapshotDirectory(outsideDirectory));
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Install_FailsClosedWhenInstalledVersionChangesAfterInspect()
    {
        using var fixture = new InstallerFixture();
        fixture.InstallInitial("1.0.0");
        var stalePreview = fixture.Inspect(fixture.CreatePackage("1.5.0", includeAllAssets: true));
        var current = Install(
            fixture,
            fixture.CreatePackage("2.0.0", includeAllAssets: true));
        var before = fixture.SnapshotSettings();

        var result = fixture.Installer.Install(
            stalePreview,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Null(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "install.state-changed");
        Assert.Equal(before, fixture.SnapshotSettings());
        Assert.Equal(
            current.PackageVersion,
            new InstalledSkinCatalog(fixture.Paths, InstalledHudVersion)
                .Find(current.SkinId)?.PackageVersion);
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Install_RejectsForgedPreviewWhoseMinimumHudExceedsCurrentVersion()
    {
        using var fixture = new InstallerFixture();
        var inspected = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var forgedPackage = inspected.Package with
        {
            Manifest = inspected.Package.Manifest with
            {
                MinimumHudVersion = SemanticVersion.Parse("9.0.0")
            }
        };
        var forged = NewPreview(
            forgedPackage,
            inspected.Existing,
            inspected.IsDowngrade,
            inspected.AllowedDecisions);

        var result = fixture.Installer.Install(
            forged,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Null(result.Installed);
        Assert.Contains(
            result.Errors,
            error => error.Code is "version.incompatible" or "install.preview.invalid");
        Assert.False(Directory.Exists(fixture.Paths.InstalledSkinsRoot) &&
            Directory.EnumerateDirectories(fixture.Paths.InstalledSkinsRoot).Any());
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Install_RejectsExternalProvenanceOutsideKeepCopy()
    {
        using var fixture = new InstallerFixture();
        var inspected = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var forgedPackage = inspected.Package with
        {
            Manifest = inspected.Package.Manifest with
            {
                OriginSkinId = Guid.Parse("22222222-2222-2222-2222-222222222222")
            }
        };
        var forged = NewPreview(
            forgedPackage,
            inspected.Existing,
            inspected.IsDowngrade,
            inspected.AllowedDecisions);

        var result = fixture.Installer.Install(
            forged,
            SkinCollisionDecision.Replace,
            CancellationToken.None);

        Assert.Null(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "provenance.not-allowed");
        fixture.AssertNoOperationDirectories();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Install_RejectsMissingOrInconsistentAssetDictionaryWithoutLeakingOperation(
        bool omitAsset)
    {
        using var fixture = new InstallerFixture();
        var inspected = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var assets = inspected.Package.Assets.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (omitAsset)
        {
            assets.Remove(SkinAssetSlot.Background);
        }
        else
        {
            var background = assets[SkinAssetSlot.Background];
            assets[SkinAssetSlot.Background] = background with
            {
                Slot = SkinAssetSlot.Center,
                RelativePath = "assets/center.jpg"
            };
        }

        var forged = NewPreview(
            inspected.Package with { Assets = assets },
            inspected.Existing,
            inspected.IsDowngrade,
            inspected.AllowedDecisions);
        SkinInstallResult? result = null;

        var exception = Record.Exception(() => result = fixture.Installer.Install(
            forged,
            SkinCollisionDecision.Replace,
            CancellationToken.None));

        Assert.Null(exception);
        Assert.NotNull(result);
        Assert.Null(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "install.preview.invalid");
        fixture.AssertNoOperationDirectories();
    }

    [Theory]
    [InlineData(false, SkinCollisionDecision.Cancel)]
    [InlineData(true, SkinCollisionDecision.Replace)]
    public void Install_ValidatesCrossRootExistingBeforeCancelOrDowngrade(
        bool forgedDowngrade,
        SkinCollisionDecision decision)
    {
        using var fixture = new InstallerFixture();
        var inspected = fixture.Inspect(
            fixture.CreatePackage("1.2.3", includeAllAssets: true));
        var outsideDirectory = Path.Combine(
            Path.GetDirectoryName(fixture.Paths.SettingsRoot)!,
            "outside-preview");
        Directory.CreateDirectory(outsideDirectory);
        File.WriteAllText(Path.Combine(outsideDirectory, "sentinel.txt"), "must survive");
        var forgedExisting = new InstalledSkinRecord(
            $"custom:{inspected.Package.Manifest.SkinId:D}",
            inspected.Package.Manifest.SkinId,
            "Forged",
            SemanticVersion.Parse("9.0.0"),
            outsideDirectory,
            inspected.Package);
        var forged = NewPreview(
            inspected.Package,
            forgedExisting,
            forgedDowngrade,
            [
                SkinCollisionDecision.Replace,
                SkinCollisionDecision.KeepCopy,
                SkinCollisionDecision.Cancel
            ]);

        var result = fixture.Installer.Install(
            forged,
            decision,
            CancellationToken.None);

        Assert.Null(result.Installed);
        Assert.Contains(result.Errors, error => error.Code == "install.preview.invalid");
        Assert.Equal("must survive", File.ReadAllText(Path.Combine(outsideDirectory, "sentinel.txt")));
        fixture.AssertNoOperationDirectories();
    }

    [Fact]
    public void Preview_HasNoPublicConstructorOrPublicSetters()
    {
        Assert.Empty(typeof(SkinInstallPreview).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.All(
            typeof(SkinInstallPreview).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            property => Assert.False(property.SetMethod?.IsPublic ?? false));
    }

    private static SkinInstallPreview NewPreview(
        SkinPackageDocument package,
        InstalledSkinRecord? existing,
        bool isDowngrade,
        IReadOnlyList<SkinCollisionDecision> allowedDecisions) =>
        new(package, existing, isDowngrade, allowedDecisions);

    private static InstalledSkinRecord AssertInstalled(SkinInstallResult result)
    {
        Assert.Empty(result.Errors);
        return Assert.IsType<InstalledSkinRecord>(result.Installed);
    }

    private static InstalledSkinRecord Install(
        InstallerFixture fixture,
        string packagePath)
    {
        var result = fixture.Installer.Install(
            fixture.Inspect(packagePath),
            SkinCollisionDecision.Replace,
            CancellationToken.None);
        return AssertInstalled(result);
    }

    private static IReadOnlyDictionary<string, byte[]> SnapshotDirectory(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(
                path => Path.GetRelativePath(root, path).Replace('\\', '/'),
                File.ReadAllBytes,
                StringComparer.Ordinal);

    internal sealed class InstallerFixture : IDisposable
    {
        private readonly SkinPackageFixture _packages = new();

        public InstallerFixture()
        {
            Paths = new SkinStoragePaths(Path.Combine(_packages.RootDirectory, "local"));
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.SettingsRoot)!);
            Installer = new SkinPackageInstaller(Paths, InstalledHudVersion);
        }

        public SkinStoragePaths Paths { get; }

        public SkinPackageInstaller Installer { get; }

        public string CreatePackage(
            string version,
            bool includeAllAssets = false,
            Guid? skinId = null,
            string displayName = "Ocean")
        {
            var assets = includeAllAssets
                ? new[]
                {
                    new SkinPackageFixture.FixtureAsset(
                        SkinAssetSlot.Background,
                        "assets/background.png",
                        SkinPackageFixture.OneByOnePng),
                    new SkinPackageFixture.FixtureAsset(
                        SkinAssetSlot.Center,
                        "assets/center.jpg",
                        SkinPackageFixture.OneByOneJpeg),
                    new SkinPackageFixture.FixtureAsset(
                        SkinAssetSlot.Decoration,
                        "assets/decoration.png",
                        SkinPackageFixture.OneByOnePng)
                }
                : [];

            return _packages.CreatePackage(
                assets,
                transformManifest: manifest => manifest with
                {
                    SkinId = skinId ?? manifest.SkinId,
                    DisplayName = displayName,
                    PackageVersion = SemanticVersion.Parse(version)
                });
        }

        public SkinInstallPreview Inspect(string packagePath) =>
            AssertValid(Installer.Inspect(
                packagePath,
                InstalledHudVersion,
                CancellationToken.None));

        public InstalledSkinRecord InstallInitial(string version)
        {
            var preview = Inspect(CreatePackage(version, includeAllAssets: true));
            var result = Installer.Install(
                preview,
                SkinCollisionDecision.Replace,
                CancellationToken.None);
            return AssertInstalled(result);
        }

        public IReadOnlyDictionary<string, byte[]> SnapshotSettings()
        {
            if (!Directory.Exists(Paths.SettingsRoot))
            {
                return new Dictionary<string, byte[]>();
            }

            return Directory.EnumerateFiles(
                    Paths.SettingsRoot,
                    "*",
                    SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToDictionary(
                    path => Path.GetRelativePath(Paths.SettingsRoot, path)
                        .Replace('\\', '/'),
                    File.ReadAllBytes,
                    StringComparer.Ordinal);
        }

        public void AssertPublishedFilesMatch(InstalledSkinRecord installed)
        {
            var expected = new[]
                {
                    SkinPackageLimits.ManifestFileName,
                    SkinPackageLimits.ThemeFileName
                }
                .Concat(installed.Package.Manifest.Assets.Select(asset => asset.Path))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var actual = Directory.EnumerateFiles(
                    installed.DirectoryPath,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(installed.DirectoryPath, path)
                    .Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected, actual);
            foreach (var asset in installed.Package.Assets.Values)
            {
                Assert.Equal(
                    asset.Content,
                    File.ReadAllBytes(Path.Combine(
                        installed.DirectoryPath,
                        asset.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
            }
        }

        public void AssertNoOperationDirectories()
        {
            Assert.True(
                !Directory.Exists(Paths.ImportsRoot) ||
                !Directory.EnumerateDirectories(Paths.ImportsRoot).Any());
        }

        public void Dispose() => _packages.Dispose();

        private static T AssertValid<T>(SkinValidationResult<T> result)
        {
            Assert.True(
                result.IsValid,
                string.Join(Environment.NewLine, result.Errors.Select(
                    error => $"{error.Code} {error.Location}: {error.Message}")));
            return Assert.IsType<T>(result.Value);
        }
    }

    private sealed class CandidateTransitionFileSystem : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly string _outsideDirectory;
        private string? _candidateDirectory;

        public CandidateTransitionFileSystem(string outsideDirectory) =>
            _outsideDirectory = Path.GetFullPath(outsideDirectory);

        public bool Transitioned { get; private set; }

        public void TransitionBeforeLease() => Transitioned = true;

        public bool DirectoryExists(string path) => _inner.DirectoryExists(Map(path));

        public bool FileExists(string path) => _inner.FileExists(Map(path));

        public FileAttributes GetAttributes(string path)
        {
            if (Transitioned && IsCandidateRoot(path))
            {
                return _inner.GetAttributes(_outsideDirectory) |
                    FileAttributes.ReparsePoint;
            }

            return _inner.GetAttributes(Map(path));
        }

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(Map(path));

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(Map(path), searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes) =>
            _inner.ReadAllBytes(Map(path), maximumBytes);

        public void CreateDirectory(string path)
        {
            _inner.CreateDirectory(Map(path));
            var parent = Path.GetDirectoryName(path);
            if (_candidateDirectory is null &&
                string.Equals(
                    Path.GetFileName(parent),
                    "candidate",
                    StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(Path.GetFileName(path), "D", out _))
            {
                _candidateDirectory = Path.GetFullPath(path);
            }
        }

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content)
            => _inner.WriteAllBytesAndFlush(Map(path), content);

        public void MoveDirectory(string sourcePath, string destinationPath) =>
            _inner.MoveDirectory(Map(sourcePath), Map(destinationPath));

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(Map(path), recursive);

        private bool IsCandidateRoot(string path) =>
            _candidateDirectory is not null &&
            string.Equals(
                Path.GetFullPath(path),
                _candidateDirectory,
                StringComparison.OrdinalIgnoreCase);

        private bool IsCandidateDescendant(string path)
        {
            if (_candidateDirectory is null)
            {
                return false;
            }

            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(
                _candidateDirectory + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private string Map(string path)
        {
            if (!Transitioned || !IsCandidateDescendant(path))
            {
                return path;
            }

            return Path.Combine(
                _outsideDirectory,
                Path.GetRelativePath(_candidateDirectory!, Path.GetFullPath(path)));
        }
    }

    private sealed class CandidateTransitionDirectoryLeaseProvider(
        CandidateTransitionFileSystem fileSystem) : IDirectoryLeaseProvider
    {
        public IDirectoryLease Lease(string expectedPath)
        {
            fileSystem.TransitionBeforeLease();
            if ((fileSystem.GetAttributes(expectedPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The candidate changed before its directory lease.");
            }

            return new EmptyLease(expectedPath);
        }

        private sealed class EmptyLease(string expectedPath) : IDirectoryLease
        {
            public DirectoryIdentity Identity => default;

            public string ExpectedPath { get; } = Path.GetFullPath(expectedPath);

            public void Dispose()
            {
            }
        }
    }

    private sealed class RemoveTransitionFileSystem : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly string _installedDirectory;
        private readonly string _outsideDirectory;

        public RemoveTransitionFileSystem(
            string installedDirectory,
            string outsideDirectory)
        {
            _installedDirectory = Path.GetFullPath(installedDirectory);
            _outsideDirectory = Path.GetFullPath(outsideDirectory);
        }

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

        public void MoveDirectory(string sourcePath, string destinationPath) =>
            _inner.MoveDirectory(sourcePath, destinationPath);

        public void DeleteDirectory(string path, bool recursive)
        {
            if (!Transitioned &&
                string.Equals(
                    Path.GetFullPath(path),
                    _installedDirectory,
                    StringComparison.OrdinalIgnoreCase))
            {
                TransitionBeforeMove();
                _inner.DeleteDirectory(_outsideDirectory, recursive);
                return;
            }

            _inner.DeleteDirectory(path, recursive);
        }
    }

    private sealed class ImportsCreateTransitionFileSystem(
        string importsRoot,
        string outsideDirectory) : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly string _importsRoot = Path.GetFullPath(importsRoot);
        private readonly string _outsideDirectory = Path.GetFullPath(outsideDirectory);

        public bool TransitionedBeforeCandidateCreation { get; private set; }

        public void TransitionBeforeCandidateCreation() =>
            TransitionedBeforeCandidateCreation = true;

        public bool DirectoryExists(string path) => _inner.DirectoryExists(Map(path));

        public bool FileExists(string path) => _inner.FileExists(Map(path));

        public FileAttributes GetAttributes(string path) =>
            _inner.GetAttributes(Map(path));

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(Map(path));

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(Map(path), searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes) =>
            _inner.ReadAllBytes(Map(path), maximumBytes);

        public void CreateDirectory(string path)
        {
            if (!TransitionedBeforeCandidateCreation && IsUnderImports(path))
            {
                TransitionedBeforeCandidateCreation = true;
            }

            _inner.CreateDirectory(Map(path));
        }

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content) =>
            _inner.WriteAllBytesAndFlush(Map(path), content);

        public void MoveDirectory(string sourcePath, string destinationPath) =>
            _inner.MoveDirectory(Map(sourcePath), Map(destinationPath));

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(Map(path), recursive);

        private bool IsUnderImports(string path)
        {
            var fullPath = Path.GetFullPath(path);
            return fullPath.StartsWith(
                _importsRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        private string Map(string path)
        {
            if (!TransitionedBeforeCandidateCreation || !IsUnderImports(path))
            {
                return path;
            }

            return Path.Combine(
                _outsideDirectory,
                Path.GetRelativePath(_importsRoot, Path.GetFullPath(path)));
        }
    }

    private sealed class ImportsCreateTransitionOwnedStorageWriter(
        ImportsCreateTransitionFileSystem fileSystem) : IOwnedStorageWriter
    {
        private readonly FileSystemOwnedStorageWriter _inner = new(
            fileSystem,
            PhysicalDirectoryLeaseProvider.Instance);

        public IDirectoryLease OpenOrCreateChildDirectory(
            IDirectoryLease parentLease,
            string fixedSingleSegmentName,
            string expectedPath)
        {
            if (string.Equals(
                    parentLease.ExpectedPath,
                    Path.GetDirectoryName(expectedPath),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    parentLease.ExpectedPath,
                    Path.GetFullPath(
                        Path.Combine(
                            Path.GetDirectoryName(parentLease.ExpectedPath)!,
                            "imports")),
                    StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(fixedSingleSegmentName, "D", out _))
            {
                fileSystem.TransitionBeforeCandidateCreation();
                throw new IOException(
                    "The imports directory changed before relative creation.");
            }

            return _inner.OpenOrCreateChildDirectory(
                parentLease,
                fixedSingleSegmentName,
                expectedPath);
        }

        public void CreateNewChildFileAndFlush(
            IDirectoryLease parentLease,
            string fixedSingleSegmentName,
            ReadOnlySpan<byte> content) =>
            _inner.CreateNewChildFileAndFlush(
                parentLease,
                fixedSingleSegmentName,
                content);
    }

    private sealed class RemoveOperationTransitionOwnedStorageWriter(
        ImportsCreateTransitionFileSystem fileSystem) : IOwnedStorageWriter
    {
        private readonly FileSystemOwnedStorageWriter _inner = new(
            fileSystem,
            PhysicalDirectoryLeaseProvider.Instance);

        public IDirectoryLease OpenOrCreateChildDirectory(
            IDirectoryLease parentLease,
            string fixedSingleSegmentName,
            string expectedPath)
        {
            if (string.Equals(
                    Path.GetFileName(parentLease.ExpectedPath),
                    "imports",
                    StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(fixedSingleSegmentName, "D", out _))
            {
                fileSystem.TransitionBeforeCandidateCreation();
                throw new IOException(
                    "The imports directory changed before removal operation creation.");
            }

            return _inner.OpenOrCreateChildDirectory(
                parentLease,
                fixedSingleSegmentName,
                expectedPath);
        }

        public void CreateNewChildFileAndFlush(
            IDirectoryLease parentLease,
            string fixedSingleSegmentName,
            ReadOnlySpan<byte> content) =>
            _inner.CreateNewChildFileAndFlush(
                parentLease,
                fixedSingleSegmentName,
                content);
    }

    private sealed class AssetsWriteTransitionFileSystem(
        string outsideDirectory) : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly string _outsideDirectory = Path.GetFullPath(outsideDirectory);
        private string? _assetsRoot;

        public bool TransitionedAfterAssetsCreation { get; private set; }

        public void TransitionAfterAssetsCreation()
        {
            Assert.NotNull(_assetsRoot);
            TransitionedAfterAssetsCreation = true;
        }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(Map(path));

        public bool FileExists(string path) => _inner.FileExists(Map(path));

        public FileAttributes GetAttributes(string path) =>
            _inner.GetAttributes(Map(path));

        public IReadOnlyList<string> EnumerateDirectories(string path) =>
            _inner.EnumerateDirectories(Map(path));

        public IReadOnlyList<string> EnumerateFiles(
            string path,
            SearchOption searchOption) =>
            _inner.EnumerateFiles(Map(path), searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes) =>
            _inner.ReadAllBytes(Map(path), maximumBytes);

        public void CreateDirectory(string path)
        {
            _inner.CreateDirectory(Map(path));
            if (_assetsRoot is null &&
                string.Equals(
                    Path.GetFileName(path),
                    "assets",
                    StringComparison.OrdinalIgnoreCase) &&
                Guid.TryParseExact(
                    Path.GetFileName(Path.GetDirectoryName(path)),
                    "D",
                    out _))
            {
                _assetsRoot = Path.GetFullPath(path);
            }
        }

        public void WriteAllBytesAndFlush(
            string path,
            ReadOnlySpan<byte> content) =>
            _inner.WriteAllBytesAndFlush(Map(path), content);

        public void MoveDirectory(string sourcePath, string destinationPath) =>
            _inner.MoveDirectory(Map(sourcePath), Map(destinationPath));

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(Map(path), recursive);

        private string Map(string path)
        {
            if (!TransitionedAfterAssetsCreation || _assetsRoot is null)
            {
                return path;
            }

            var fullPath = Path.GetFullPath(path);
            if (!fullPath.StartsWith(
                    _assetsRoot + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return Path.Combine(
                _outsideDirectory,
                Path.GetRelativePath(_assetsRoot, fullPath));
        }
    }

    private sealed class AssetsWriteTransitionOwnedStorageWriter(
        AssetsWriteTransitionFileSystem fileSystem) : IOwnedStorageWriter
    {
        private readonly FileSystemOwnedStorageWriter _inner = new(
            fileSystem,
            PhysicalDirectoryLeaseProvider.Instance);

        public IDirectoryLease OpenOrCreateChildDirectory(
            IDirectoryLease parentLease,
            string fixedSingleSegmentName,
            string expectedPath)
        {
            var lease = _inner.OpenOrCreateChildDirectory(
                parentLease,
                fixedSingleSegmentName,
                expectedPath);
            if (string.Equals(
                    fixedSingleSegmentName,
                    "assets",
                    StringComparison.Ordinal))
            {
                fileSystem.TransitionAfterAssetsCreation();
            }

            return lease;
        }

        public void CreateNewChildFileAndFlush(
            IDirectoryLease parentLease,
            string fixedSingleSegmentName,
            ReadOnlySpan<byte> content)
        {
            if (fileSystem.TransitionedAfterAssetsCreation &&
                string.Equals(
                    Path.GetFileName(parentLease.ExpectedPath),
                    "assets",
                    StringComparison.Ordinal))
            {
                throw new IOException(
                    "The assets directory changed before relative file creation.");
            }

            _inner.CreateNewChildFileAndFlush(
                parentLease,
                fixedSingleSegmentName,
                content);
        }
    }

    private sealed class RemoveTransitionDirectoryMoveProvider(
        RemoveTransitionFileSystem fileSystem) : IDirectoryMoveProvider
    {
        public void Move(
            IDirectoryLease sourceLease,
            string sourcePath,
            IDirectoryLease destinationParentLease,
            string destinationParentPath,
            string destinationChildName,
            string expectedDestinationPath)
        {
            fileSystem.TransitionBeforeMove();
            PhysicalDirectoryLeaseProvider.Instance.Move(
                sourceLease,
                sourcePath,
                destinationParentLease,
                destinationParentPath,
                destinationChildName,
                expectedDestinationPath);
        }
    }

    private sealed class ThrowingDirectoryDeleteProvider : ISafeDirectoryDeleteProvider
    {
        public static ThrowingDirectoryDeleteProvider Instance { get; } = new();

        public void DeleteOwnedTree(string rootPath, int maximumEntries) =>
            throw new IOException("Injected cleanup failure.");
    }

    private sealed class ThrowingBeforeMoveDirectoryMoveProvider : IDirectoryMoveProvider
    {
        public static ThrowingBeforeMoveDirectoryMoveProvider Instance { get; } = new();

        public void Move(
            IDirectoryLease sourceLease,
            string sourcePath,
            IDirectoryLease destinationParentLease,
            string destinationParentPath,
            string destinationChildName,
            string expectedDestinationPath) =>
            throw new IOException("Injected failure before quarantine move.");
    }

    private sealed class CommittedRemovePostCheckFailureMoveProvider : IDirectoryMoveProvider
    {
        public static CommittedRemovePostCheckFailureMoveProvider Instance { get; } = new();

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
            throw new DirectoryMoveException(
                "Injected committed quarantine post-check failure.",
                moveCommitted: true,
                innerException: new IOException("Injected post-check failure."));
        }
    }
}
