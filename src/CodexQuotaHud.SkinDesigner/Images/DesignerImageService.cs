using System.IO;
using System.Reflection;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Images;

public sealed record ImageMutationResult(
    bool Succeeded,
    SkinAsset? Asset,
    DraftAssetReference? Reference,
    IReadOnlyList<SkinValidationError> Errors);

public interface IDesignerImageMutationCommitter
{
    IReadOnlyDictionary<SkinAssetSlot, SkinAsset> SnapshotAssets();

    bool TryCommit(SkinAsset asset, DraftAssetReference reference);

    bool TryRemove(SkinAssetSlot slot);
}

public sealed class DesignerImageService
{
    private readonly string _draftsRoot;
    private readonly IDesignerImageMutationCommitter _committer;
    private readonly Func<Guid> _operationId;
    private readonly IDesignerDraftStorageLeaseProvider _storage;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public DesignerImageService(
        SkinStoragePaths paths,
        Func<Guid>? operationId = null)
        : this(paths, AcceptingCommitter.Instance, operationId)
    {
    }

    internal DesignerImageService(
        SkinStoragePaths paths,
        IDesignerImageMutationCommitter committer,
        Func<Guid>? operationId = null,
        IDesignerDraftStorageLeaseProvider? storage = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _committer = committer ?? throw new ArgumentNullException(nameof(committer));
        _draftsRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(paths.DraftsRoot));
        _operationId = operationId ?? Guid.NewGuid;
        _storage = storage ?? PhysicalDraftFileOperations.Instance;
    }

    public async Task<ImageMutationResult> ImportAsync(
        Guid draftId,
        SkinAssetSlot slot,
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (draftId == Guid.Empty || !Enum.IsDefined(slot))
        {
            return Invalid(
                "image.identity",
                "$image",
                "The draft and image slot must be valid.");
        }

        if (string.IsNullOrWhiteSpace(sourcePath) ||
            !Path.IsPathFullyQualified(sourcePath))
        {
            return Invalid(
                "image.source-path",
                "$image.source",
                "The selected image must use an absolute local path.");
        }

        string normalizedSource;
        try
        {
            normalizedSource = Path.GetFullPath(sourcePath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or NotSupportedException)
        {
            return Invalid(
                "image.source-path",
                "$image.source",
                "The selected image path is invalid.");
        }

        var extension = Path.GetExtension(normalizedSource).ToLowerInvariant();
        var isPng = extension == ".png";
        var isJpeg = extension is ".jpg" or ".jpeg";
        if ((!isPng && !isJpeg) ||
            slot == SkinAssetSlot.Decoration && !isPng)
        {
            return Invalid(
                "image.extension",
                "$image.source",
                slot == SkinAssetSlot.Decoration
                    ? "Decoration images must use PNG."
                    : "Images must use PNG or JPEG.");
        }

        var canonicalExtension = isPng ? ".png" : ".jpg";
        var relativePath = RelativePath(slot, canonicalExtension);
        byte[] content;
        try
        {
            using var source = _storage.OpenDesignerSource(normalizedSource);
            if (source.Length > SkinPackageLimits.MaximumImageBytes)
            {
                return Invalid(
                    "image.too-large",
                    "$image.source",
                    "The selected image exceeds the encoded byte limit.");
            }

            content = source.ReadAllBytes(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Invalid(
                "image.source-unavailable",
                "$image.source",
                "The selected image could not be read.");
        }

        SkinDecodedImage decoded;
        try
        {
            decoded = SkinImageDecoder.Decode(slot, relativePath, content);
        }
        catch (IOException exception)
        {
            return DecodeInvalid(exception);
        }

        if (slot == SkinAssetSlot.Decoration && !decoded.HasAlpha)
        {
            return Invalid(
                "image.decoration-alpha",
                "$image",
                "Decoration images must use an alpha-capable PNG pixel format.");
        }

        var aggregateError = ValidateAggregatePixels(slot, decoded);
        if (aggregateError is not null)
        {
            return aggregateError;
        }

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                using var project = _storage.OpenDesignerProject(
                    _draftsRoot,
                    draftId,
                    DesignerDraftProjectOpenMode.OpenOrCreate) ??
                    throw new IOException("The draft project could not be leased.");
                using var assets = project.OpenAssets(create: true);
                return ImportOwned(
                    assets,
                    slot,
                    normalizedSource,
                    relativePath,
                    content,
                    decoded,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                return Invalid(
                    "image.owned-path",
                    "$image",
                    "The draft-owned image path is unsafe.");
            }
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<ImageMutationResult> RemoveAsync(
        Guid draftId,
        SkinAssetSlot slot,
        CancellationToken cancellationToken = default)
    {
        if (draftId == Guid.Empty || !Enum.IsDefined(slot))
        {
            return Invalid(
                "image.identity",
                "$image",
                "The draft and image slot must be valid.");
        }

        await _mutationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return CommitRemove(slot);
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private ImageMutationResult ImportOwned(
        IDesignerDraftAssetsLease assets,
        SkinAssetSlot slot,
        string normalizedSource,
        string relativePath,
        byte[] content,
        SkinDecodedImage decoded,
        CancellationToken cancellationToken)
    {
        var operation = _operationId();
        if (operation == Guid.Empty)
        {
            return Invalid(
                "image.operation-id",
                "$image",
                "The image operation identity is invalid.");
        }

        var storageRelativePath = DraftAssetStorage.CreateContentRelativePath(
            relativePath,
            content);
        var destinationLeaf = Path.GetFileName(storageRelativePath);
        var temporaryLeaf = OperationLeaf(
            Path.GetFileName(relativePath),
            "tmp",
            operation);
        var reference = new DraftAssetReference(
            slot,
            relativePath,
            Path.GetFileName(normalizedSource),
            storageRelativePath);
        var operationCreated = false;
        try
        {
            if (assets.FileExists(destinationLeaf))
            {
                var existing = assets.ReadAllBytes(destinationLeaf);
                if (!existing.AsSpan().SequenceEqual(content) ||
                    !DraftAssetStorage.MatchesContent(reference, existing))
                {
                    return StorageHashMismatch();
                }
            }
            else
            {
                assets.WriteAndFlushNew(temporaryLeaf, content, cancellationToken);
                operationCreated = true;
                var verifiedBytes = assets.ReadOperationBytes(temporaryLeaf);
                if (!verifiedBytes.AsSpan().SequenceEqual(content))
                {
                    return Invalid(
                        "image.stage-verify",
                        "$image",
                        "The staged image bytes changed before promotion.");
                }

                SkinDecodedImage verified;
                try
                {
                    verified = SkinImageDecoder.Decode(
                        slot,
                        relativePath,
                        verifiedBytes);
                }
                catch (IOException exception)
                {
                    return DecodeInvalid(exception);
                }

                if (slot == SkinAssetSlot.Decoration && !verified.HasAlpha)
                {
                    return Invalid(
                        "image.decoration-alpha",
                        "$image",
                        "Decoration images must use an alpha-capable PNG pixel format.");
                }

                var aggregateError = ValidateAggregatePixels(slot, verified);
                if (aggregateError is not null)
                {
                    return aggregateError;
                }

                try
                {
                    assets.MoveOperationToImmutable(
                        temporaryLeaf,
                        destinationLeaf);
                    operationCreated = false;
                    assets.ReleaseOperation(temporaryLeaf);
                }
                catch (IOException)
                {
                    if (!assets.FileExists(destinationLeaf))
                    {
                        throw;
                    }

                    var winner = assets.ReadAllBytes(destinationLeaf);
                    if (!winner.AsSpan().SequenceEqual(content) ||
                        !DraftAssetStorage.MatchesContent(reference, winner))
                    {
                        return StorageHashMismatch();
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var asset = new SkinAsset(
                slot,
                relativePath,
                [.. content],
                decoded.PixelWidth,
                decoded.PixelHeight,
                decoded.HasAlpha);
            bool accepted;
            try
            {
                accepted = _committer.TryCommit(asset, reference);
            }
            catch
            {
                accepted = false;
            }

            if (!accepted)
            {
                return Invalid(
                    "image.session-rejected",
                    "$image",
                    "The draft session rejected the staged image mutation.");
            }

            return new ImageMutationResult(true, asset, reference, []);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return Invalid(
                operationCreated
                    ? "image.promote-failed"
                    : "image.prepare-failed",
                "$image",
                operationCreated
                    ? "The staged image could not become an immutable draft asset safely."
                    : "The owned image files could not be prepared safely.");
        }
        finally
        {
            if (operationCreated)
            {
                TryDeleteOperation(assets, temporaryLeaf);
            }
        }
    }

    private ImageMutationResult CommitRemove(SkinAssetSlot slot)
    {
        var accepted = false;
        try
        {
            accepted = _committer.TryRemove(slot);
        }
        catch
        {
            accepted = false;
        }

        return accepted
            ? new ImageMutationResult(true, null, null, [])
            : Invalid(
                "image.session-rejected",
                "$image",
                "The draft session rejected the image removal.");
    }

    private ImageMutationResult? ValidateAggregatePixels(
        SkinAssetSlot replacingSlot,
        SkinDecodedImage candidate)
    {
        long decodedPixels = checked((long)candidate.PixelWidth * candidate.PixelHeight);
        foreach (var pair in _committer.SnapshotAssets())
        {
            if (pair.Key == replacingSlot)
            {
                continue;
            }

            decodedPixels = checked(
                decodedPixels + checked((long)pair.Value.PixelWidth * pair.Value.PixelHeight));
            if (decodedPixels > SkinPackageLimits.MaximumDecodedPixels)
            {
                return Invalid(
                    "image.total-pixels",
                    "$image",
                    "The complete draft image set exceeds the decoded pixel limit.");
            }
        }

        return decodedPixels > SkinPackageLimits.MaximumDecodedPixels
            ? Invalid(
                "image.total-pixels",
                "$image",
                "The complete draft image set exceeds the decoded pixel limit.")
            : null;
    }

    private static string OperationLeaf(
        string canonicalLeaf,
        string kind,
        Guid operation) =>
        $".{canonicalLeaf}.{kind}-{operation:D}".ToLowerInvariant();

    private static void TryDeleteOperation(
        IDesignerDraftAssetsLease assets,
        string operationLeaf)
    {
        try
        {
            assets.DeleteOperation(operationLeaf);
        }
        catch
        {
            // A promoted immutable asset is never targeted by operation cleanup.
        }
    }

    private static ImageMutationResult StorageHashMismatch() =>
        Invalid(
            "image.storage-hash-mismatch",
            "$image",
            "An existing immutable draft asset does not match its content address.");

    private static string RelativePath(
        SkinAssetSlot slot,
        string extension) => slot switch
    {
        SkinAssetSlot.Background => $"assets/background{extension}",
        SkinAssetSlot.Center => $"assets/center{extension}",
        SkinAssetSlot.Decoration when extension == ".png" =>
            "assets/decoration.png",
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    private static ImageMutationResult DecodeInvalid(IOException exception)
    {
        var code = "image.decode";
        var property = exception.GetType().GetProperty(
            "Code",
            BindingFlags.Instance | BindingFlags.Public);
        if (property?.PropertyType == typeof(string) &&
            property.GetValue(exception) is string decoderCode &&
            decoderCode.StartsWith("image.", StringComparison.Ordinal))
        {
            code = decoderCode;
        }

        var detail = exception.InnerException is null
            ? exception.Message
            : $"{exception.Message} ({exception.InnerException.GetType().Name}: {exception.InnerException.Message})";
        return Invalid(code, "$image", detail);
    }

    private static ImageMutationResult Invalid(
        string code,
        string location,
        string message) =>
        new(false, null, null, [new SkinValidationError(code, location, message)]);

    private sealed class AcceptingCommitter : IDesignerImageMutationCommitter
    {
        internal static AcceptingCommitter Instance { get; } = new();

        public IReadOnlyDictionary<SkinAssetSlot, SkinAsset> SnapshotAssets() =>
            new Dictionary<SkinAssetSlot, SkinAsset>();

        public bool TryCommit(
            SkinAsset asset,
            DraftAssetReference reference) => true;

        public bool TryRemove(SkinAssetSlot slot) => true;
    }

}
