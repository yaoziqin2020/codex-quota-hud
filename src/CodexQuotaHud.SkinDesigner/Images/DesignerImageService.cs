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
            try
            {
                using var project = _storage.OpenDesignerProject(
                    _draftsRoot,
                    draftId,
                    DesignerDraftProjectOpenMode.OpenExisting);
                if (project is null)
                {
                    return CommitRemove(slot);
                }

                using var assets = project.OpenAssets(create: false);
                return RemoveOwned(assets, slot, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or
                    DirectoryNotFoundException)
            {
                return Invalid(
                    "image.prepare-failed",
                    "$image",
                    "The owned image files could not be prepared safely.");
            }
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

        var destinationLeaf = Path.GetFileName(relativePath);
        var temporaryLeaf = OperationLeaf(destinationLeaf, "tmp", operation);
        var previous = SnapshotCanonicalFiles(assets, slot, cancellationToken);
        var moved = new Dictionary<string, string>(StringComparer.Ordinal);
        var promoted = false;
        var promotionStarted = false;
        try
        {
            assets.WriteAndFlushNew(temporaryLeaf, content, cancellationToken);
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
                verified = SkinImageDecoder.Decode(slot, relativePath, verifiedBytes);
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

            promotionStarted = true;
            foreach (var leaf in previous.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tomb = OperationLeaf(leaf, "tomb", operation);
                if (!assets.MoveCanonicalToOperation(leaf, tomb))
                {
                    throw new IOException(
                        "A previously read canonical asset disappeared before quarantine.");
                }

                moved.Add(leaf, tomb);
            }

            assets.MoveOperationToCanonical(temporaryLeaf, destinationLeaf);
            promoted = true;
            foreach (var tomb in moved.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                assets.DeleteOperation(tomb);
            }

            var asset = new SkinAsset(
                slot,
                relativePath,
                [.. content],
                verified.PixelWidth,
                verified.PixelHeight,
                verified.HasAlpha);
            var reference = new DraftAssetReference(
                slot,
                relativePath,
                Path.GetFileName(normalizedSource));
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
                if (!TryRestoreMovedFiles(
                        assets,
                        operation,
                        temporaryLeaf,
                        promoted,
                        moved,
                        previous))
                {
                    return RollbackFailed();
                }

                promoted = false;
                moved.Clear();
                return Invalid(
                    "image.session-rejected",
                    "$image",
                    "The draft session rejected the staged image mutation.");
            }

            assets.ReleaseOperation(temporaryLeaf);
            promoted = false;
            moved.Clear();
            return new ImageMutationResult(true, asset, reference, []);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            if (moved.Count > 0 || promoted)
            {
                if (!TryRestoreMovedFiles(
                        assets,
                        operation,
                        temporaryLeaf,
                        promoted,
                        moved,
                        previous))
                {
                    return RollbackFailed();
                }

                promoted = false;
                moved.Clear();
            }

            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            if ((moved.Count > 0 || promoted) &&
                !TryRestoreMovedFiles(
                    assets,
                    operation,
                    temporaryLeaf,
                    promoted,
                    moved,
                    previous))
            {
                return RollbackFailed();
            }

            promoted = false;
            moved.Clear();
            return Invalid(
                promotionStarted
                    ? "image.promote-failed"
                    : "image.prepare-failed",
                "$image",
                promotionStarted
                    ? "The staged image could not replace the owned canonical image safely."
                    : "The owned image files could not be prepared safely.");
        }
        finally
        {
            if (!promoted)
            {
                TryDeleteOperation(assets, temporaryLeaf);
            }

            foreach (var tomb in moved.Values)
            {
                TryDeleteOperation(assets, tomb);
            }
        }
    }

    private ImageMutationResult RemoveOwned(
        IDesignerDraftAssetsLease assets,
        SkinAssetSlot slot,
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

        var previous = SnapshotCanonicalFiles(assets, slot, cancellationToken);
        if (previous.Count == 0)
        {
            return CommitRemove(slot);
        }

        var moved = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            foreach (var leaf in previous.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tomb = OperationLeaf(leaf, "tomb", operation);
                if (!assets.MoveCanonicalToOperation(leaf, tomb))
                {
                    throw new IOException(
                        "A previously read canonical asset disappeared before quarantine.");
                }

                moved.Add(leaf, tomb);
            }

            foreach (var tomb in moved.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                assets.DeleteOperation(tomb);
            }

            var committed = CommitRemove(slot);
            if (!committed.Succeeded)
            {
                if (!TryRestoreMovedFiles(
                        assets,
                        operation,
                        promotedOperationLeaf: null,
                        promoted: false,
                        moved,
                        previous))
                {
                    return RollbackFailed();
                }

                moved.Clear();
            }

            return committed;
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested)
        {
            if (moved.Count > 0)
            {
                if (!TryRestoreMovedFiles(
                        assets,
                        operation,
                        promotedOperationLeaf: null,
                        promoted: false,
                        moved,
                        previous))
                {
                    return RollbackFailed();
                }

                moved.Clear();
            }

            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            if (moved.Count > 0 &&
                !TryRestoreMovedFiles(
                    assets,
                    operation,
                    promotedOperationLeaf: null,
                    promoted: false,
                    moved,
                    previous))
            {
                return RollbackFailed();
            }

            moved.Clear();
            return Invalid(
                "image.prepare-failed",
                "$image",
                "The owned image files could not be prepared safely.");
        }
        finally
        {
            foreach (var tomb in moved.Values)
            {
                TryDeleteOperation(assets, tomb);
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

    private static Dictionary<string, byte[]> SnapshotCanonicalFiles(
        IDesignerDraftAssetsLease assets,
        SkinAssetSlot slot,
        CancellationToken cancellationToken)
    {
        var snapshots = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var leaf in CanonicalLeaves(slot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (assets.FileExists(leaf))
            {
                snapshots.Add(leaf, assets.ReadAllBytes(leaf));
            }
        }

        return snapshots;
    }

    private static bool TryRestoreMovedFiles(
        IDesignerDraftAssetsLease assets,
        Guid operation,
        string? promotedOperationLeaf,
        bool promoted,
        IReadOnlyDictionary<string, string> moved,
        IReadOnlyDictionary<string, byte[]> previous)
    {
        try
        {
            if (promoted && promotedOperationLeaf is not null)
            {
                assets.DeleteOperation(promotedOperationLeaf);
            }

            foreach (var tomb in moved.Values)
            {
                assets.DeleteOperation(tomb);
            }

            foreach (var leaf in moved.Keys)
            {
                var rollback = OperationLeaf(leaf, "rollback", operation);
                assets.WriteAndFlushNew(
                    rollback,
                    previous[leaf],
                    CancellationToken.None);
                assets.MoveOperationToCanonical(rollback, leaf);
                assets.ReleaseOperation(rollback);
            }

            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
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
            // The committed canonical asset is never targeted by operation cleanup.
        }
    }

    private static ImageMutationResult RollbackFailed() =>
        Invalid(
            "image.rollback-failed",
            "$image",
            "The image transaction could not restore the previous owned bytes.");

    private static IReadOnlyList<string> CanonicalLeaves(SkinAssetSlot slot) =>
        slot switch
        {
            SkinAssetSlot.Background => ["background.png", "background.jpg"],
            SkinAssetSlot.Center => ["center.png", "center.jpg"],
            SkinAssetSlot.Decoration => ["decoration.png"],
            _ => throw new ArgumentOutOfRangeException(nameof(slot))
        };

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

    private static void EnsureNoExistingReparsePoint(string path)
    {
        var current = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        while (!string.IsNullOrEmpty(current))
        {
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("The owned image path contains a reparse point.");
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) ||
                string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            current = Path.TrimEndingDirectorySeparator(parent);
        }
    }

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

    private static ImageMutationResult CleanupWarning() =>
        new(
            true,
            null,
            null,
            [new SkinValidationError(
                "image.cleanup-warning",
                "$image",
                "The image was removed from the draft, but an unreferenced owned file remains for later cleanup.")]);

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
