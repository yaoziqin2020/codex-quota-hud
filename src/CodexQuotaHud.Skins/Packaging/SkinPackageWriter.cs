using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Serialization;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.Skins.Packaging;

public sealed class SkinPackageWriter
{
    private static readonly DateTimeOffset DosEpoch =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly Action<string, string, bool> _finalMove;

    public SkinPackageWriter()
        : this(static (source, destination, overwrite) =>
            File.Move(source, destination, overwrite))
    {
    }

    public SkinPackageWriter(Action<string, string, bool> finalMove)
    {
        ArgumentNullException.ThrowIfNull(finalMove);
        _finalMove = finalMove;
    }

    public SkinManifest Write(
        Stream destination,
        SkinPackageBuildRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var package = Prepare(request, cancellationToken);
        WriteArchive(destination, package, cancellationToken);
        return package.Manifest;
    }

    public SkinValidationResult<SkinManifest> WriteFile(
        string destinationPath,
        SkinPackageBuildRequest request,
        bool overwrite,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(
                Path.GetExtension(destinationPath),
                ".cqskin",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Skin package destinations must use the .cqskin extension.",
                nameof(destinationPath));
        }

        var targetPath = Path.GetFullPath(destinationPath);
        var parentPath = Path.GetDirectoryName(targetPath) ??
            throw new ArgumentException(
                "The skin package destination must have a parent directory.",
                nameof(destinationPath));
        targetPath = Path.Combine(parentPath, Path.GetFileName(targetPath));

        if (!overwrite && File.Exists(targetPath))
        {
            return DestinationExists();
        }

        var package = Prepare(request, cancellationToken);
        var temporaryPath = Path.Combine(
            parentPath,
            $"{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var temporary = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                WriteArchive(temporary, package, cancellationToken);
                temporary.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var validation = new SkinPackageReader().ValidateFile(
                temporaryPath,
                package.Manifest.MinimumHudVersion,
                cancellationToken);
            if (!validation.IsValid)
            {
                return new SkinValidationResult<SkinManifest>(
                    null,
                    validation.Errors);
            }

            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _finalMove(temporaryPath, targetPath, overwrite);
            }
            catch (IOException) when (!overwrite && File.Exists(targetPath))
            {
                return DestinationExists();
            }

            return new SkinValidationResult<SkinManifest>(
                package.Manifest,
                []);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static PreparedPackage Prepare(
        SkinPackageBuildRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Manifest);
        ArgumentNullException.ThrowIfNull(request.Theme);
        ArgumentNullException.ThrowIfNull(request.Assets);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Manifest.Assets is null ||
            request.Manifest.Assets.Count != 0)
        {
            throw new ArgumentException(
                "The input manifest assets collection must be empty.",
                nameof(request));
        }

        ThrowIfInvalid(SkinContractValidator.Validate(
            request.Manifest,
            request.Theme,
            request.Manifest.MinimumHudVersion));

        var preparedAssets = new List<PreparedAsset>(request.Assets.Count);
        long decodedPixels = 0;
        foreach (var pair in request.Assets.OrderBy(pair => pair.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Enum.IsDefined(pair.Key) ||
                pair.Value is null ||
                pair.Value.Slot != pair.Key)
            {
                throw new ArgumentException(
                    "Each asset dictionary key must match a defined asset slot.",
                    nameof(request));
            }

            var asset = pair.Value;
            ArgumentNullException.ThrowIfNull(asset.Content);
            if (asset.Content.LongLength > SkinPackageLimits.MaximumImageBytes)
            {
                throw new InvalidDataException(
                    "An image asset exceeds the supported size.");
            }

            var logicalPath = GetLogicalPath(asset);
            var content = asset.Content.ToArray();
            var decoded = SkinImageDecoder.Decode(
                asset.Slot,
                logicalPath,
                content,
                SkinPackageLimits.MaximumDecodedPixels - decodedPixels);
            decodedPixels = checked(
                decodedPixels +
                (long)decoded.PixelWidth * decoded.PixelHeight);
            var hash = Convert.ToHexString(SHA256.HashData(content))
                .ToLowerInvariant();
            preparedAssets.Add(new PreparedAsset(
                new SkinAssetReference(asset.Slot, logicalPath, hash),
                content));
        }

        var manifest = request.Manifest with
        {
            Assets = preparedAssets
                .Select(asset => asset.Reference)
                .OrderBy(asset => asset.Slot)
                .ToArray()
        };
        ThrowIfInvalid(SkinContractValidator.Validate(
            manifest,
            request.Theme,
            manifest.MinimumHudVersion));

        return new PreparedPackage(
            manifest,
            SkinJsonCodec.WriteManifest(manifest),
            SkinJsonCodec.WriteTheme(request.Theme),
            preparedAssets);
    }

    private static string GetLogicalPath(SkinAsset asset)
    {
        if (string.IsNullOrWhiteSpace(asset.RelativePath))
        {
            throw new ArgumentException(
                "Each asset must declare its source image extension.",
                nameof(asset));
        }

        var extension = Path.GetExtension(asset.RelativePath);
        if (extension is not (".png" or ".jpg" or ".jpeg"))
        {
            throw new ArgumentException(
                "Asset source paths must end in .png, .jpg, or .jpeg.",
                nameof(asset));
        }

        var fileName = asset.Slot switch
        {
            SkinAssetSlot.Background => "background",
            SkinAssetSlot.Center => "center",
            SkinAssetSlot.Decoration => "decoration",
            _ => throw new ArgumentOutOfRangeException(nameof(asset))
        };
        return $"{SkinPackageLimits.AssetsDirectoryName}{fileName}{extension}";
    }

    private static void WriteArchive(
        Stream destination,
        PreparedPackage package,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!destination.CanWrite || !destination.CanSeek)
        {
            throw new ArgumentException(
                "The destination stream must be writable and seekable.",
                nameof(destination));
        }

        destination.Position = 0;
        destination.SetLength(0);
        using (var archive = new ZipArchive(
                   destination,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (var asset in package.Assets.OrderBy(
                         asset => asset.Reference.Path,
                         StringComparer.Ordinal))
            {
                WriteEntry(
                    archive,
                    asset.Reference.Path,
                    asset.Content,
                    cancellationToken);
            }

            WriteEntry(
                archive,
                SkinPackageLimits.ManifestFileName,
                package.ManifestJson,
                cancellationToken);
            WriteEntry(
                archive,
                SkinPackageLimits.ThemeFileName,
                package.ThemeJson,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void WriteEntry(
        ZipArchive archive,
        string path,
        byte[] content,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        entry.LastWriteTime = DosEpoch;
        using var destination = entry.Open();
        destination.Write(content);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void ThrowIfInvalid<T>(SkinValidationResult<T> result)
    {
        if (result.IsValid)
        {
            return;
        }

        throw new InvalidDataException(
            "The skin package build request is invalid: " +
            string.Join(", ", result.Errors.Select(error => error.Code)));
    }

    private static SkinValidationResult<SkinManifest> DestinationExists() =>
        new(
            null,
            [
                new SkinValidationError(
                    "export.destination-exists",
                    "$destination",
                    "The destination skin package already exists.")
            ]);

    private sealed record PreparedPackage(
        SkinManifest Manifest,
        byte[] ManifestJson,
        byte[] ThemeJson,
        IReadOnlyList<PreparedAsset> Assets);

    private sealed record PreparedAsset(
        SkinAssetReference Reference,
        byte[] Content);
}
