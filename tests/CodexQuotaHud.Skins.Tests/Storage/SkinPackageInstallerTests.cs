using System.Security.Cryptography;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;
using CodexQuotaHud.Skins.Tests.Fixtures;

namespace CodexQuotaHud.Skins.Tests.Storage;

public sealed class SkinPackageInstallerTests
{
    private static readonly SemanticVersion InstalledHudVersion =
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

        Assert.Equal(SkinInstallDisposition.Installed, result.Disposition);
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
        var installer = new SkinPackageInstaller(fixture.Paths, fileSystem);

        var result = installer.Remove(installed.SkinId);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "remove.path.invalid");
        Assert.True(Directory.Exists(installed.DirectoryPath));
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
        var installer = new SkinPackageInstaller(fixture.Paths, fileSystem);

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
        var forgedPreview = cleanPreview with
        {
            Existing = forgedExisting,
            AllowedDecisions =
            [
                SkinCollisionDecision.Replace,
                SkinCollisionDecision.KeepCopy,
                SkinCollisionDecision.Cancel
            ]
        };

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
    public void Install_RechecksDowngradeWhenInstalledVersionChangesAfterInspect()
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
        Assert.Contains(result.Errors, error => error.Code == "install.downgrade");
        Assert.Equal(before, fixture.SnapshotSettings());
        Assert.Equal(
            current.PackageVersion,
            new InstalledSkinCatalog(fixture.Paths, InstalledHudVersion)
                .Find(current.SkinId)?.PackageVersion);
        fixture.AssertNoOperationDirectories();
    }

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
            Installer = new SkinPackageInstaller(Paths);
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
}
