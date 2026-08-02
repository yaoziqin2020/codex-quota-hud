using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;
using CodexQuotaHud.Skins.Tests.Fixtures;

namespace CodexQuotaHud.Skins.Tests.Storage;

public sealed class InstalledSkinCatalogTests
{
    [Fact]
    public void LoadAllAndSelection_DiscoverACompletedCustomInstall()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var installed = fixture.InstallInitial("1.2.3");
        var catalog = new InstalledSkinCatalog(
            fixture.Paths,
            SemanticVersion.Parse("1.1.1"));

        var loaded = catalog.LoadAll();

        Assert.Empty(loaded.Corrupt);
        var record = Assert.Single(loaded.Installed);
        Assert.Equal(installed.SkinId, record.SkinId);
        Assert.Equal(record.SkinId, catalog.Find(installed.SkinId)?.SkinId);
        Assert.Equal(
            record.SkinId,
            catalog.TryLoadSelection(installed.SelectionKey)?.SkinId);
        Assert.Null(catalog.TryLoadSelection("custom:not-a-guid"));
        Assert.Null(catalog.TryLoadSelection("nebula"));
    }

    [Fact]
    public void LoadAll_SeparatesCorruptDirectoriesWithoutHidingHealthyRecords()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var healthy = Install(
            fixture,
            fixture.CreatePackage(
                "1.2.3",
                includeAllAssets: true,
                skinId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                displayName: "Healthy"));
        var corrupt = Install(
            fixture,
            fixture.CreatePackage(
                "1.2.3",
                includeAllAssets: true,
                skinId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                displayName: "Corrupt"));
        File.WriteAllBytes(
            Path.Combine(corrupt.DirectoryPath, "assets", "background.png"),
            SkinPackageFixture.OneByOneJpeg);
        Directory.CreateDirectory(Path.Combine(fixture.Paths.InstalledSkinsRoot, "not-a-guid"));
        var catalog = new InstalledSkinCatalog(
            fixture.Paths,
            SemanticVersion.Parse("1.1.1"));

        var loaded = catalog.LoadAll();

        Assert.Equal(healthy.SkinId, Assert.Single(loaded.Installed).SkinId);
        Assert.Equal(2, loaded.Corrupt.Count);
        Assert.Contains(
            loaded.Corrupt,
            record => record.SkinId == corrupt.SkinId &&
                record.Errors.Any(error => error.Code == "asset.hash.mismatch"));
        Assert.Contains(
            loaded.Corrupt,
            record => record.SkinId is null &&
                record.Errors.Any(error => error.Code == "installed.path.invalid"));
    }

    [Fact]
    public void LoadAll_SortsByDisplayNameThenGuid()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var beta = Guid.Parse("11111111-1111-1111-1111-111111111119");
        var alphaHigh = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var alphaLow = Guid.Parse("11111111-1111-1111-1111-111111111118");
        Install(fixture, fixture.CreatePackage("1.2.3", skinId: beta, displayName: "Beta"));
        Install(fixture, fixture.CreatePackage("1.2.3", skinId: alphaHigh, displayName: "alpha"));
        Install(fixture, fixture.CreatePackage("1.2.3", skinId: alphaLow, displayName: "Alpha"));

        var loaded = new InstalledSkinCatalog(
            fixture.Paths,
            SemanticVersion.Parse("1.1.1")).LoadAll();

        Assert.Equal([alphaLow, alphaHigh, beta], loaded.Installed.Select(record => record.SkinId));
    }

    [Theory]
    [InlineData("settings")]
    [InlineData("skins")]
    [InlineData("final")]
    public void LoadAll_RejectsReparseAtStorageAncestorRootOrFinalDirectory(
        string markedLocation)
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var installed = fixture.InstallInitial("1.2.3");
        var markedPath = markedLocation switch
        {
            "settings" => fixture.Paths.SettingsRoot,
            "skins" => fixture.Paths.InstalledSkinsRoot,
            "final" => installed.DirectoryPath,
            _ => throw new ArgumentOutOfRangeException(nameof(markedLocation))
        };
        var fileSystem = new ReparseMarkingFileSystem(
            markedPath,
            forbidMarkedEnumeration: markedLocation == "skins");
        var catalog = new InstalledSkinCatalog(
            fixture.Paths,
            SemanticVersion.Parse("1.1.1"),
            fileSystem);

        var loaded = catalog.LoadAll();

        Assert.Empty(loaded.Installed);
        Assert.NotEmpty(loaded.Corrupt);
    }

    [Fact]
    public void SafeOwnedDirectory_RejectsEscapeNestedAndNonCanonicalNames()
    {
        using var fixture = new SkinPackageInstallerTests.InstallerFixture();
        var root = fixture.Paths.InstalledSkinsRoot;
        Directory.CreateDirectory(root);
        var safe = new SafeOwnedDirectory(root, PhysicalSkinFileSystem.Instance);
        var id = Guid.Parse("ABCDEFAB-CDEF-ABCD-EFAB-CDEFABCDEFAB");

        Assert.False(safe.TryResolveSkinDirectory(
            Path.Combine(Path.GetDirectoryName(root)!, id.ToString("D").ToLowerInvariant()),
            out _,
            out _));
        Assert.False(safe.TryResolveSkinDirectory(
            Path.Combine(root, "nested", id.ToString("D").ToLowerInvariant()),
            out _,
            out _));
        Assert.False(safe.TryResolveSkinDirectory(
            Path.Combine(root, id.ToString("D").ToUpperInvariant()),
            out _,
            out _));
    }

    private static InstalledSkinRecord Install(
        SkinPackageInstallerTests.InstallerFixture fixture,
        string packagePath)
    {
        var result = fixture.Installer.Install(
            fixture.Inspect(packagePath),
            SkinCollisionDecision.Replace,
            CancellationToken.None);
        Assert.Empty(result.Errors);
        return Assert.IsType<InstalledSkinRecord>(result.Installed);
    }

    internal sealed class ReparseMarkingFileSystem : ISkinFileSystem
    {
        private readonly ISkinFileSystem _inner = PhysicalSkinFileSystem.Instance;
        private readonly string _markedPath;
        private readonly bool _forbidMarkedEnumeration;

        public ReparseMarkingFileSystem(
            string markedPath,
            bool forbidMarkedEnumeration = false)
        {
            _markedPath = Path.GetFullPath(markedPath);
            _forbidMarkedEnumeration = forbidMarkedEnumeration;
        }

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public bool FileExists(string path) => _inner.FileExists(path);

        public FileAttributes GetAttributes(string path)
        {
            var attributes = _inner.GetAttributes(path);
            return string.Equals(
                    Path.GetFullPath(path),
                    _markedPath,
                    StringComparison.OrdinalIgnoreCase)
                ? attributes | FileAttributes.ReparsePoint
                : attributes;
        }

        public IReadOnlyList<string> EnumerateDirectories(string path)
        {
            if (_forbidMarkedEnumeration &&
                string.Equals(
                    Path.GetFullPath(path),
                    _markedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A marked reparse directory was traversed.");
            }

            return _inner.EnumerateDirectories(path);
        }

        public IReadOnlyList<string> EnumerateFiles(string path, SearchOption searchOption) =>
            _inner.EnumerateFiles(path, searchOption);

        public byte[] ReadAllBytes(string path, long maximumBytes) =>
            _inner.ReadAllBytes(path, maximumBytes);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public void WriteAllBytesAndFlush(string path, ReadOnlySpan<byte> content) =>
            _inner.WriteAllBytesAndFlush(path, content);

        public void MoveDirectory(string sourcePath, string destinationPath) =>
            _inner.MoveDirectory(sourcePath, destinationPath);

        public void DeleteDirectory(string path, bool recursive) =>
            _inner.DeleteDirectory(path, recursive);
    }
}
