using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Serialization;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.Skins.Packaging;

public sealed class SkinPackageReader
{
    private const int CopyBufferSize = 64 * 1024;

    public SkinValidationResult<SkinPackageDocument> ValidateFile(
        string packagePath,
        SemanticVersion installedHudVersion,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            using var package = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return ValidateStream(
                package,
                package.Length,
                installedHudVersion,
                cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Invalid(
                "package.io",
                "$package",
                "The skin package could not be read.");
        }
        catch (IOException)
        {
            return Invalid(
                "package.io",
                "$package",
                "The skin package could not be read.");
        }
    }

    public SkinValidationResult<SkinPackageDocument> ValidateStream(
        Stream package,
        long packageLength,
        SemanticVersion installedHudVersion,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentOutOfRangeException.ThrowIfNegative(packageLength);
        cancellationToken.ThrowIfCancellationRequested();

        if (packageLength > SkinPackageLimits.MaximumPackageBytes)
        {
            return Invalid(
                "package.too-large",
                "$package",
                "The skin package exceeds the supported size.");
        }

        if (!package.CanRead || !package.CanSeek)
        {
            return Invalid(
                "package.stream",
                "$package",
                "The skin package stream must be readable and seekable.");
        }

        try
        {
            if (package.Length > SkinPackageLimits.MaximumPackageBytes)
            {
                return Invalid(
                    "package.too-large",
                    "$package",
                    "The skin package exceeds the supported size.");
            }

            if (package.Length != packageLength)
            {
                return Invalid(
                    "package.length",
                    "$package",
                    "The skin package length is inconsistent.");
            }

            package.Position = 0;
            using var archive = new ZipArchive(
                package,
                ZipArchiveMode.Read,
                leaveOpen: true);
            var catalogResult = ZipEntryPolicy.Validate(
                package,
                packageLength,
                archive,
                cancellationToken);
            if (!catalogResult.IsValid)
            {
                return new SkinValidationResult<SkinPackageDocument>(
                    null,
                    catalogResult.Errors);
            }

            return ValidateCatalog(
                catalogResult.Value!,
                installedHudVersion,
                cancellationToken);
        }
        catch (SkinImageValidationException exception)
        {
            return Invalid(
                exception.Code,
                "$image",
                exception.Message);
        }
        catch (PackageValidationException exception)
        {
            return Invalid(
                exception.Code,
                exception.Location,
                exception.Message);
        }
        catch (JsonException)
        {
            return Invalid(
                "json.invalid",
                "$",
                "The skin JSON is invalid.");
        }
        catch (InvalidDataException)
        {
            return Invalid(
                "archive.invalid",
                "$archive",
                "The skin archive is invalid.");
        }
        catch (UnauthorizedAccessException)
        {
            return Invalid(
                "package.io",
                "$package",
                "The skin package could not be read.");
        }
        catch (IOException)
        {
            return Invalid(
                "package.io",
                "$package",
                "The skin package could not be read.");
        }
    }

    private static SkinValidationResult<SkinPackageDocument> ValidateCatalog(
        SafeZipCatalog catalog,
        SemanticVersion installedHudVersion,
        CancellationToken cancellationToken)
    {
        if (!TryGetExact(
                catalog,
                SkinPackageLimits.ManifestFileName,
                out var manifestEntry) ||
            !TryGetExact(
                catalog,
                SkinPackageLimits.ThemeFileName,
                out var themeEntry))
        {
            return Invalid(
                "archive.file.missing",
                "$archive",
                "The archive is missing a required file.");
        }

        long extractedBytes = 0;
        var manifestContent = CopyEntry(
            manifestEntry,
            perEntryLimit: null,
            ref extractedBytes,
            cancellationToken,
            calculateHash: false);
        var themeContent = CopyEntry(
            themeEntry,
            perEntryLimit: null,
            ref extractedBytes,
            cancellationToken,
            calculateHash: false);

        var manifestResult = SkinJsonCodec.ParseManifest(
            manifestContent.Content);
        if (!manifestResult.IsValid)
        {
            return new SkinValidationResult<SkinPackageDocument>(
                null,
                manifestResult.Errors);
        }

        var themeResult = SkinJsonCodec.ParseTheme(themeContent.Content);
        if (!themeResult.IsValid)
        {
            return new SkinValidationResult<SkinPackageDocument>(
                null,
                themeResult.Errors);
        }

        var contractResult = SkinContractValidator.Validate(
            manifestResult.Value!,
            themeResult.Value!,
            installedHudVersion);
        if (!contractResult.IsValid)
        {
            return new SkinValidationResult<SkinPackageDocument>(
                null,
                contractResult.Errors);
        }

        var manifest = contractResult.Value!.Manifest;
        var theme = contractResult.Value.Theme;
        var allowedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            SkinPackageLimits.ManifestFileName,
            SkinPackageLimits.ThemeFileName
        };

        foreach (var assetReference in manifest.Assets)
        {
            allowedNames.Add(assetReference.Path);
            if (!TryGetExact(
                    catalog,
                    assetReference.Path,
                    out _))
            {
                return Invalid(
                    "archive.file.missing",
                    "$archive",
                    "A declared asset file is missing.");
            }
        }

        if (catalog.Entries.Any(entry =>
                !allowedNames.Contains(entry.NormalizedName)))
        {
            return Invalid(
                "archive.file.undeclared",
                "$archive",
                "The archive contains a file not declared by its manifest.");
        }

        var assets = new Dictionary<SkinAssetSlot, SkinAsset>();
        long decodedPixels = 0;
        foreach (var assetReference in manifest.Assets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = catalog.TryGet(assetReference.Path, out var assetEntry);
            var copied = CopyEntry(
                assetEntry,
                SkinPackageLimits.MaximumImageBytes,
                ref extractedBytes,
                cancellationToken,
                calculateHash: true);
            if (!string.Equals(
                    copied.Sha256,
                    assetReference.Sha256,
                    StringComparison.Ordinal))
            {
                return Invalid(
                    "asset.hash.mismatch",
                    "$asset.sha256",
                    "An asset does not match its declared SHA-256 hash.");
            }

            var decoded = SkinImageDecoder.Decode(
                assetReference.Slot,
                assetReference.Path,
                copied.Content);
            var pixels = checked(
                (long)decoded.PixelWidth * decoded.PixelHeight);
            if (pixels >
                SkinPackageLimits.MaximumDecodedPixels - decodedPixels)
            {
                return Invalid(
                    "image.pixel-budget",
                    "$image",
                    "Decoded images exceed the supported pixel budget.");
            }

            decodedPixels += pixels;
            assets.Add(
                assetReference.Slot,
                new SkinAsset(
                    assetReference.Slot,
                    assetReference.Path,
                    copied.Content,
                    decoded.PixelWidth,
                    decoded.PixelHeight,
                    decoded.HasAlpha));
        }

        return new SkinValidationResult<SkinPackageDocument>(
            new SkinPackageDocument(manifest, theme, assets),
            []);
    }

    private static CopiedEntry CopyEntry(
        SafeZipEntry entry,
        long? perEntryLimit,
        ref long extractedBytes,
        CancellationToken cancellationToken,
        bool calculateHash)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = entry.Entry.Open();
        using var destination = new MemoryStream();
        using var hash = calculateHash
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;
        var buffer = new byte[CopyBufferSize];
        long entryBytes = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            cancellationToken.ThrowIfCancellationRequested();
            if (read == 0)
            {
                break;
            }

            entryBytes = checked(entryBytes + read);
            if (perEntryLimit is { } limit && entryBytes > limit)
            {
                throw new PackageValidationException(
                    "archive.entry-size",
                    $"$archive.entries[{entry.Index}]",
                    "An image entry exceeds the supported size.");
            }

            if (read >
                SkinPackageLimits.MaximumExtractedBytes - extractedBytes)
            {
                throw new PackageValidationException(
                    "archive.extracted-size",
                    "$archive",
                    "The archive expands beyond the supported limit.");
            }

            extractedBytes += read;
            destination.Write(buffer, 0, read);
            hash?.AppendData(buffer, 0, read);
        }

        return new CopiedEntry(
            destination.ToArray(),
            hash is null
                ? null
                : Convert.ToHexString(hash.GetHashAndReset())
                    .ToLowerInvariant());
    }

    private static bool TryGetExact(
        SafeZipCatalog catalog,
        string expectedName,
        out SafeZipEntry entry)
    {
        if (catalog.TryGet(expectedName, out entry) &&
            string.Equals(
                entry.NormalizedName,
                expectedName,
                StringComparison.Ordinal))
        {
            return true;
        }

        entry = null!;
        return false;
    }

    private static SkinValidationResult<SkinPackageDocument> Invalid(
        string code,
        string location,
        string message) =>
        new(
            null,
            [new SkinValidationError(code, location, message)]);

    private sealed record CopiedEntry(byte[] Content, string? Sha256);
}

internal sealed class PackageValidationException : IOException
{
    public PackageValidationException(
        string code,
        string location,
        string message)
        : base(message)
    {
        Code = code;
        Location = location;
    }

    public string Code { get; }

    public string Location { get; }
}
