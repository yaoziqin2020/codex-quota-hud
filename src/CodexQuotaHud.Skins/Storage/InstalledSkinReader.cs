using System.IO;
using System.Security.Cryptography;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Serialization;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.Skins.Storage;

internal sealed class InstalledSkinReader
{
    private readonly SafeOwnedDirectory _ownedRoot;
    private readonly ISkinFileSystem _fileSystem;
    private readonly SemanticVersion _hudVersion;
    private readonly bool _allowLocalProvenance;

    public InstalledSkinReader(
        string installedRoot,
        SemanticVersion hudVersion,
        ISkinFileSystem fileSystem,
        bool allowLocalProvenance = true)
    {
        _ownedRoot = new SafeOwnedDirectory(installedRoot, fileSystem);
        _fileSystem = fileSystem;
        _hudVersion = hudVersion;
        _allowLocalProvenance = allowLocalProvenance;
    }

    public SkinValidationResult<InstalledSkinRecord> Read(string directoryPath)
    {
        try
        {
            if (!_ownedRoot.TryResolveSkinDirectory(
                    directoryPath,
                    out var directory,
                    out var directorySkinId) ||
                !_fileSystem.DirectoryExists(directory))
            {
                return Invalid(
                    "installed.path.invalid",
                    "$directory",
                    "The installed skin directory is not an owned lowercase GUID directory.");
            }

            var manifestPath = Path.Combine(
                directory,
                SkinPackageLimits.ManifestFileName);
            var themePath = Path.Combine(
                directory,
                SkinPackageLimits.ThemeFileName);
            if (!_fileSystem.FileExists(manifestPath) ||
                !_fileSystem.FileExists(themePath))
            {
                return Invalid(
                    "installed.file.missing",
                    "$directory",
                    "The installed skin is missing a required file.");
            }

            long totalBytes = 0;
            var manifestBytes = ReadFile(
                manifestPath,
                SkinPackageLimits.MaximumExtractedBytes - totalBytes);
            totalBytes = checked(totalBytes + manifestBytes.LongLength);
            var themeBytes = ReadFile(
                themePath,
                SkinPackageLimits.MaximumExtractedBytes - totalBytes);
            totalBytes = checked(totalBytes + themeBytes.LongLength);
            var manifestResult = SkinJsonCodec.ParseManifest(manifestBytes);
            if (!manifestResult.IsValid)
            {
                return Invalid(manifestResult.Errors);
            }

            var themeResult = SkinJsonCodec.ParseTheme(themeBytes);
            if (!themeResult.IsValid)
            {
                return Invalid(themeResult.Errors);
            }

            var contract = SkinContractValidator.Validate(
                manifestResult.Value!,
                themeResult.Value!,
                _hudVersion,
                allowLocalProvenance: _allowLocalProvenance);
            if (!contract.IsValid)
            {
                return Invalid(contract.Errors);
            }

            var manifest = contract.Value!.Manifest;
            if (manifest.SkinId != directorySkinId)
            {
                return Invalid(
                    "installed.id-mismatch",
                    "$.skinId",
                    "The manifest skin ID does not match its installed directory.");
            }

            var expectedFiles = new HashSet<string>(StringComparer.Ordinal)
            {
                SkinPackageLimits.ManifestFileName,
                SkinPackageLimits.ThemeFileName
            };
            foreach (var asset in manifest.Assets)
            {
                expectedFiles.Add(asset.Path);
            }

            var actualFiles = _fileSystem.EnumerateFiles(
                    directory,
                    SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(directory, path)
                    .Replace('\\', '/'))
                .ToArray();
            if (actualFiles.Any(path => !expectedFiles.Contains(path)) ||
                expectedFiles.Any(expected => !actualFiles.Contains(expected, StringComparer.Ordinal)))
            {
                return Invalid(
                    "installed.file-set.invalid",
                    "$directory",
                    "The installed skin contains missing or undeclared files.");
            }

            var assets = new Dictionary<SkinAssetSlot, SkinAsset>();
            long decodedPixels = 0;
            foreach (var reference in manifest.Assets)
            {
                var assetPath = Path.GetFullPath(Path.Combine(
                    directory,
                    reference.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (_ownedRoot.HasExistingReparsePoint(assetPath) ||
                    !_fileSystem.FileExists(assetPath))
                {
                    return Invalid(
                        "installed.asset.invalid",
                        "$asset",
                        "An installed asset is missing or leaves owned storage.");
                }

                var remainingBytes = SkinPackageLimits.MaximumExtractedBytes - totalBytes;
                var content = ReadFile(
                    assetPath,
                    Math.Min(SkinPackageLimits.MaximumImageBytes, remainingBytes));
                totalBytes = checked(totalBytes + content.LongLength);
                if (totalBytes > SkinPackageLimits.MaximumExtractedBytes)
                {
                    return Invalid(
                        "installed.extracted-size",
                        "$directory",
                        "The installed skin exceeds its extracted size limit.");
                }

                var hash = Convert.ToHexString(SHA256.HashData(content))
                    .ToLowerInvariant();
                if (!string.Equals(hash, reference.Sha256, StringComparison.Ordinal))
                {
                    return Invalid(
                        "asset.hash.mismatch",
                        "$asset.sha256",
                        "An installed asset does not match its declared SHA-256 hash.");
                }

                var decoded = SkinImageDecoder.Decode(
                    reference.Slot,
                    reference.Path,
                    content,
                    SkinPackageLimits.MaximumDecodedPixels - decodedPixels);
                decodedPixels = checked(
                    decodedPixels + (long)decoded.PixelWidth * decoded.PixelHeight);
                assets.Add(
                    reference.Slot,
                    new SkinAsset(
                        reference.Slot,
                        reference.Path,
                        content,
                        decoded.PixelWidth,
                        decoded.PixelHeight,
                        decoded.HasAlpha));
            }

            var package = new SkinPackageDocument(manifest, contract.Value.Theme, assets);
            return new SkinValidationResult<InstalledSkinRecord>(
                new InstalledSkinRecord(
                    $"custom:{manifest.SkinId:D}",
                    manifest.SkinId,
                    manifest.DisplayName,
                    manifest.PackageVersion,
                    directory,
                    package),
                []);
        }
        catch (SkinImageValidationException exception)
        {
            return Invalid(exception.Code, "$image", exception.Message);
        }
        catch (InvalidDataException)
        {
            return Invalid(
                "installed.extracted-size",
                "$directory",
                "The installed skin exceeds its extracted size limit.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OverflowException)
        {
            return Invalid(
                "installed.io",
                "$directory",
                "The installed skin could not be read safely.");
        }
    }

    private byte[] ReadFile(string path, long maximumBytes)
    {
        if (_ownedRoot.HasExistingReparsePoint(path))
        {
            throw new IOException("Installed skin files cannot be reparse points.");
        }

        return _fileSystem.ReadAllBytes(path, maximumBytes);
    }

    private static SkinValidationResult<InstalledSkinRecord> Invalid(
        IReadOnlyList<SkinValidationError> errors) => new(null, errors);

    private static SkinValidationResult<InstalledSkinRecord> Invalid(
        string code,
        string location,
        string message) =>
        new(null, [new SkinValidationError(code, location, message)]);
}
