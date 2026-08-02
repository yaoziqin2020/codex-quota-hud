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

        var originalPosition = package.Position;
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
            var preflight = ZipEntryPolicy.Preflight(
                package,
                packageLength,
                cancellationToken);
            package.Position = 0;
            using var archive = new ZipArchive(
                package,
                ZipArchiveMode.Read,
                leaveOpen: true);
            var catalog = ZipEntryPolicy.BindArchive(
                preflight,
                archive,
                cancellationToken);

            return ValidateCatalog(
                package,
                catalog,
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
        finally
        {
            package.Position = originalPosition;
        }
    }

    private static SkinValidationResult<SkinPackageDocument> ValidateCatalog(
        Stream package,
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
            package,
            manifestEntry,
            perEntryLimit: null,
            ref extractedBytes,
            cancellationToken,
            calculateHash: false);
        var themeContent = CopyEntry(
            package,
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
                package,
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
                copied.Content,
                SkinPackageLimits.MaximumDecodedPixels - decodedPixels);
            var pixels = checked(
                (long)decoded.PixelWidth * decoded.PixelHeight);
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
        Stream package,
        SafeZipEntry entry,
        long? perEntryLimit,
        ref long extractedBytes,
        CancellationToken cancellationToken,
        bool calculateHash)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = entry.OpenDataStream(package);
        using var destination = new MemoryStream();
        using var hash = calculateHash
            ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256)
            : null;
        var crc32 = new Crc32Accumulator();
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
            crc32.Append(buffer.AsSpan(0, read));
        }

        if (entryBytes != entry.DeclaredUncompressedSize)
        {
            throw new PackageValidationException(
                "archive.size.mismatch",
                $"$archive.entries[{entry.Index}]",
                "An archive entry's actual size does not match its header.");
        }

        if (crc32.Value != entry.ExpectedCrc32)
        {
            throw new PackageValidationException(
                "archive.crc.mismatch",
                $"$archive.entries[{entry.Index}]",
                "An archive entry failed its CRC-32 integrity check.");
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

    private sealed class Crc32Accumulator
    {
        private uint _crc = uint.MaxValue;

        public uint Value => ~_crc;

        public void Append(ReadOnlySpan<byte> content)
        {
            foreach (var value in content)
            {
                _crc ^= value;
                for (var bit = 0; bit < 8; bit++)
                {
                    _crc = (_crc & 1) != 0
                        ? 0xEDB88320u ^ (_crc >> 1)
                        : _crc >> 1;
                }
            }
        }
    }
}

internal sealed class PackageValidationException : IOException
{
    public PackageValidationException(
        string code,
        string location,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Location = location;
    }

    public string Code { get; }

    public string Location { get; }
}
