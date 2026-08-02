using System.IO;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Storage;

public sealed class InstalledSkinCatalog
{
    private readonly SkinStoragePaths _paths;
    private readonly SemanticVersion _hudVersion;
    private readonly ISkinFileSystem _fileSystem;

    public InstalledSkinCatalog(
        SkinStoragePaths paths,
        SemanticVersion hudVersion)
        : this(paths, hudVersion, PhysicalSkinFileSystem.Instance)
    {
    }

    internal InstalledSkinCatalog(
        SkinStoragePaths paths,
        SemanticVersion hudVersion,
        ISkinFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(fileSystem);
        _paths = paths;
        _hudVersion = hudVersion;
        _fileSystem = fileSystem;
    }

    public InstalledSkinCatalogResult LoadAll()
    {
        if (!_fileSystem.DirectoryExists(_paths.InstalledSkinsRoot))
        {
            return new InstalledSkinCatalogResult([], []);
        }

        var ownedRoot = new SafeOwnedDirectory(
            _paths.InstalledSkinsRoot,
            _fileSystem);
        try
        {
            if (!SafeOwnedDirectory.IsSafeStoragePath(
                    _paths,
                    _paths.InstalledSkinsRoot,
                    _fileSystem) ||
                ownedRoot.HasExistingReparsePoint(ownedRoot.RootPath))
            {
                return new InstalledSkinCatalogResult(
                    [],
                    [Corrupt(_paths.InstalledSkinsRoot, null, "installed.path.reparse")]);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new InstalledSkinCatalogResult(
                [],
                [Corrupt(_paths.InstalledSkinsRoot, null, "installed.catalog.io")]);
        }

        var installed = new List<InstalledSkinRecord>();
        var corrupt = new List<CorruptInstalledSkin>();
        var reader = new InstalledSkinReader(
            _paths.InstalledSkinsRoot,
            _hudVersion,
            _fileSystem);
        IReadOnlyList<string> directories;
        try
        {
            directories = _fileSystem.EnumerateDirectories(
                _paths.InstalledSkinsRoot);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return new InstalledSkinCatalogResult(
                [],
                [Corrupt(_paths.InstalledSkinsRoot, null, "installed.catalog.io")]);
        }

        foreach (var directory in directories)
        {
            var result = reader.Read(directory);
            if (result.IsValid)
            {
                installed.Add(result.Value!);
                continue;
            }

            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
            Guid? skinId = Guid.TryParseExact(name, "D", out var parsed)
                ? parsed
                : null;
            corrupt.Add(new CorruptInstalledSkin(directory, skinId, result.Errors));
        }

        return new InstalledSkinCatalogResult(
            installed
                .OrderBy(record => record.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.SkinId)
                .ToArray(),
            corrupt
                .OrderBy(record => record.DirectoryPath, StringComparer.OrdinalIgnoreCase)
                .ToArray());
    }

    public InstalledSkinRecord? Find(Guid skinId) =>
        LoadAll().Installed.FirstOrDefault(record => record.SkinId == skinId);

    public InstalledSkinRecord? TryLoadSelection(string selectionKey)
    {
        const string prefix = "custom:";
        if (selectionKey is null ||
            !selectionKey.StartsWith(prefix, StringComparison.Ordinal))
        {
            return null;
        }

        var idText = selectionKey[prefix.Length..];
        if (!Guid.TryParseExact(idText, "D", out var skinId) ||
            !string.Equals(
                idText,
                skinId.ToString("D").ToLowerInvariant(),
                StringComparison.Ordinal))
        {
            return null;
        }

        return Find(skinId);
    }

    private static CorruptInstalledSkin Corrupt(
        string path,
        Guid? skinId,
        string code) =>
        new(
            path,
            skinId,
            [new SkinValidationError(code, "$directory", "Installed skin storage could not be read safely.")]);
}
