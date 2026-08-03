using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Documents;

public sealed record DesignerDocumentResult(
    SkinDraftDocument? Draft,
    IReadOnlyDictionary<SkinAssetSlot, SkinAsset> Assets,
    IReadOnlyList<SkinValidationError> Errors);

public sealed class DesignerDocumentService
{
    private static readonly IReadOnlyDictionary<SkinAssetSlot, SkinAsset>
        EmptyAssets = new ReadOnlyDictionary<SkinAssetSlot, SkinAsset>(
            new Dictionary<SkinAssetSlot, SkinAsset>());

    private readonly SkinStoragePaths _paths;
    private readonly DraftStore _draftStore;
    private readonly InstalledSkinCatalog _installedCatalog;
    private readonly SkinPackageReader _packageReader;
    private readonly Func<Guid> _newId;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly IDesignerDraftStorageLeaseProvider _storage;

    public DesignerDocumentService(
        SkinStoragePaths paths,
        DraftStore draftStore,
        InstalledSkinCatalog installedCatalog,
        SkinPackageReader packageReader,
        Func<Guid>? newId = null,
        Func<DateTimeOffset>? utcNow = null)
        : this(
            paths,
            draftStore,
            installedCatalog,
            packageReader,
            newId,
            utcNow,
            PhysicalDraftFileOperations.Instance)
    {
    }

    internal DesignerDocumentService(
        SkinStoragePaths paths,
        DraftStore draftStore,
        InstalledSkinCatalog installedCatalog,
        SkinPackageReader packageReader,
        Func<Guid>? newId,
        Func<DateTimeOffset>? utcNow,
        IDesignerDraftStorageLeaseProvider storage)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _draftStore = draftStore ?? throw new ArgumentNullException(nameof(draftStore));
        _installedCatalog = installedCatalog ??
            throw new ArgumentNullException(nameof(installedCatalog));
        _packageReader = packageReader ??
            throw new ArgumentNullException(nameof(packageReader));
        _newId = newId ?? Guid.NewGuid;
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
    }

    public DesignerDocumentResult CreateNew(
        Guid draftId,
        Guid skinId,
        DateTimeOffset nowUtc,
        SemanticVersion minimumHudVersion)
    {
        if (draftId == Guid.Empty || skinId == Guid.Empty)
        {
            return Invalid(
                "document.identity",
                "$document",
                "A new draft requires valid draft and skin identities.");
        }

        return new DesignerDocumentResult(
            SkinDraftFactory.CreateNew(
                draftId,
                skinId,
                nowUtc.ToUniversalTime(),
                minimumHudVersion),
            EmptyAssets,
            []);
    }

    public DesignerDocumentResult OpenDraft(Guid draftId)
    {
        if (draftId == Guid.Empty)
        {
            return Invalid(
                "document.identity",
                "$document",
                "A saved draft requires a valid identity.");
        }

        var opened = _draftStore.LoadForOpen(draftId);
        if (opened.Document is null)
        {
            return new DesignerDocumentResult(
                null,
                EmptyAssets,
                opened.Failures.Select(ToValidationError).ToArray());
        }

        var assets = LoadOwnedAssets(opened.Document);
        if (assets.Errors.Count > 0)
        {
            return assets;
        }

        // Non-fatal evidence about an older corrupt counterpart remains visible
        // in the catalog, but does not prevent opening the selected valid snapshot.
        return assets;
    }

    public DesignerDocumentResult EditInstalled(string selectionKey)
    {
        var installed = _installedCatalog.TryLoadSelection(selectionKey);
        if (installed is null)
        {
            return Invalid(
                "document.installed-not-editable",
                "$selection",
                "Only a healthy installed custom skin can be edited.");
        }

        return ConvertPackageToOwnedDraft(installed.Package);
    }

    public Task<DesignerDocumentResult> ImportForEditingAsync(
        string packagePath,
        SemanticVersion installedHudVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
        {
            return Task.FromResult(Invalid(
                "document.import-cancelled",
                "$package",
                "No skin package was selected."));
        }

        cancellationToken.ThrowIfCancellationRequested();
        SkinValidationResult<SkinPackageDocument> validated;
        try
        {
            validated = _packageReader.ValidateFile(
                packagePath,
                installedHudVersion,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException)
        {
            return Task.FromResult(Invalid(
                "package.io",
                "$package",
                "The skin package path is invalid."));
        }

        if (!validated.IsValid || validated.Value is null)
        {
            return Task.FromResult(new DesignerDocumentResult(
                null,
                EmptyAssets,
                validated.Errors));
        }

        return Task.FromResult(ConvertPackageToOwnedDraft(
            validated.Value,
            cancellationToken));
    }

    private DesignerDocumentResult ConvertPackageToOwnedDraft(
        SkinPackageDocument package,
        CancellationToken cancellationToken = default)
    {
        var draftId = _newId();
        if (draftId == Guid.Empty)
        {
            return Invalid(
                "document.identity",
                "$document",
                "The new draft identity is invalid.");
        }

        var now = _utcNow().ToUniversalTime();
        var manifest = package.Manifest;
        var references = new ReadOnlyDictionary<
            SkinAssetSlot,
            DraftAssetReference>(package.Assets.ToDictionary(
                pair => pair.Key,
                pair => new DraftAssetReference(
                    pair.Key,
                    pair.Value.RelativePath,
                    Path.GetFileName(pair.Value.RelativePath))));
        var draft = new SkinDraftDocument(
            DraftSchemaVersion: 1,
            DraftId: draftId,
            SkinId: manifest.SkinId,
            Revision: 0,
            ProjectName: manifest.DisplayName,
            DisplayName: manifest.DisplayName,
            Author: manifest.Author,
            PackageVersion: manifest.PackageVersion,
            Description: manifest.Description,
            MinimumHudVersion: manifest.MinimumHudVersion,
            OriginSkinId: manifest.OriginSkinId,
            Theme: package.Theme,
            Assets: references,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);

        return CopyPackageAssets(draft, package.Assets, cancellationToken);
    }

    private DesignerDocumentResult CopyPackageAssets(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> sourceAssets,
        CancellationToken cancellationToken)
    {
        IDesignerDraftProjectLease? project = null;
        IDesignerDraftAssetsLease? assets = null;
        var ownedOperations = new List<string>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            project = _storage.OpenDesignerProject(
                _paths.DraftsRoot,
                draft.DraftId,
                DesignerDraftProjectOpenMode.CreateExclusive);
            if (project is null)
            {
                return Invalid(
                    "document.draft-exists",
                    "$document",
                    "The generated draft identity is already in use.");
            }

            if (!project.WasCreated)
            {
                throw new IOException(
                    "The draft project was not exclusively claimed.");
            }

            assets = project.OpenAssets(create: true);
            var ownedAssets = new Dictionary<SkinAssetSlot, SkinAsset>();
            long decodedPixels = 0;
            foreach (var slot in Enum.GetValues<SkinAssetSlot>())
            {
                if (!sourceAssets.TryGetValue(slot, out var source))
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (source.Slot != slot ||
                    !draft.Assets.TryGetValue(slot, out var reference) ||
                    !string.Equals(
                        reference.RelativePath,
                        source.RelativePath,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "The package asset relationship is invalid.");
                }

                var destinationLeaf = ResolveOwnedAssetLeaf(
                    slot,
                    reference.RelativePath);
                var operationLeaf = AssetOperationLeaf(destinationLeaf);
                assets.WriteAndFlushNew(
                    operationLeaf,
                    source.Content,
                    cancellationToken);
                ownedOperations.Add(operationLeaf);
                var copied = assets.ReadOperationBytes(operationLeaf);
                if (!copied.AsSpan().SequenceEqual(source.Content))
                {
                    throw new IOException("The copied draft asset bytes changed.");
                }

                var decoded = SkinImageDecoder.Decode(
                    slot,
                    reference.RelativePath,
                    copied);
                if (decoded.PixelWidth != source.PixelWidth ||
                    decoded.PixelHeight != source.PixelHeight ||
                    decoded.HasAlpha != source.HasAlpha)
                {
                    throw new InvalidDataException(
                        "The copied draft asset decode changed.");
                }

                decodedPixels = checked(
                    decodedPixels + (long)decoded.PixelWidth * decoded.PixelHeight);
                if (decodedPixels > SkinPackageLimits.MaximumDecodedPixels)
                {
                    throw new InvalidDataException(
                        "The copied draft assets exceed the decoded pixel budget.");
                }

                assets.MoveOperationToCanonical(operationLeaf, destinationLeaf);
                ownedAssets.Add(slot, source with { Content = [.. copied] });
            }

            foreach (var operationLeaf in ownedOperations)
            {
                assets.ReleaseOperation(operationLeaf);
            }

            ownedOperations.Clear();

            return new DesignerDocumentResult(
                draft,
                new ReadOnlyDictionary<SkinAssetSlot, SkinAsset>(ownedAssets),
                []);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!CleanupClaimedProject(
                    ref assets,
                    ref project,
                    ownedOperations))
            {
                return Invalid(
                    "document.cleanup-failed",
                    "$document.assets",
                    "The cancelled copy could not remove only its claimed partial files.");
            }

            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or ArgumentException or NotSupportedException)
        {
            if (!CleanupClaimedProject(
                    ref assets,
                    ref project,
                    ownedOperations))
            {
                return Invalid(
                    "document.cleanup-failed",
                    "$document.assets",
                    "The failed copy could not remove only its claimed partial files.");
            }

            return DecodeOrCopyInvalid(exception);
        }
        finally
        {
            assets?.Dispose();
            project?.Dispose();
        }
    }

    private DesignerDocumentResult LoadOwnedAssets(SkinDraftDocument draft)
    {
        var assets = new Dictionary<SkinAssetSlot, SkinAsset>();
        try
        {
            using var project = _storage.OpenDesignerProject(
                _paths.DraftsRoot,
                draft.DraftId,
                DesignerDraftProjectOpenMode.OpenExisting) ??
                throw new DirectoryNotFoundException(
                    "The draft project no longer exists.");
            using var owned = project.OpenAssets(create: false);
            long decodedPixels = 0;
            foreach (var pair in draft.Assets.OrderBy(pair => pair.Key))
            {
                var leaf = ResolveOwnedAssetLeaf(
                    pair.Key,
                    pair.Value.RelativePath);
                if (!owned.FileExists(leaf))
                {
                    return Invalid(
                        "document.asset-missing",
                        $"$.assets[{(int)pair.Key}]",
                        "A draft-owned image is missing.");
                }

                var content = owned.ReadAllBytes(leaf);
                if (content.LongLength > SkinPackageLimits.MaximumImageBytes)
                {
                    return Invalid(
                        "image.too-large",
                        $"$.assets[{(int)pair.Key}]",
                        "A draft-owned image exceeds the encoded byte limit.");
                }

                var decoded = SkinImageDecoder.Decode(
                    pair.Key,
                    pair.Value.RelativePath,
                    content);
                decodedPixels = checked(
                    decodedPixels + (long)decoded.PixelWidth * decoded.PixelHeight);
                if (decodedPixels > SkinPackageLimits.MaximumDecodedPixels)
                {
                    return Invalid(
                        "image.total-pixels",
                        "$document.assets",
                        "The complete draft image set exceeds the decoded pixel limit.");
                }

                assets.Add(pair.Key, new SkinAsset(
                    pair.Key,
                    pair.Value.RelativePath,
                    content,
                    decoded.PixelWidth,
                    decoded.PixelHeight,
                    decoded.HasAlpha));
            }

            return new DesignerDocumentResult(
                draft,
                new ReadOnlyDictionary<SkinAssetSlot, SkinAsset>(assets),
                []);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return DecodeOrCopyInvalid(exception);
        }
    }

    private static string ResolveOwnedAssetLeaf(
        SkinAssetSlot slot,
        string relativePath)
    {
        var expectedLeaves = slot switch
        {
            SkinAssetSlot.Background => new[] { "background.png", "background.jpg" },
            SkinAssetSlot.Center => new[] { "center.png", "center.jpg" },
            SkinAssetSlot.Decoration => new[] { "decoration.png" },
            _ => throw new InvalidDataException("The draft asset slot is invalid.")
        };
        foreach (var leaf in expectedLeaves)
        {
            if (string.Equals(
                    relativePath,
                    $"assets/{leaf}",
                    StringComparison.Ordinal))
            {
                return leaf;
            }
        }

        throw new IOException("The draft asset path leaves owned storage.");
    }

    private static bool CleanupClaimedProject(
        ref IDesignerDraftAssetsLease? assets,
        ref IDesignerDraftProjectLease? project,
        IReadOnlyList<string> ownedOperations)
    {
        var cleaned = true;
        try
        {
            if (assets is not null)
            {
                foreach (var operationLeaf in ownedOperations.Reverse())
                {
                    try
                    {
                        assets.DeleteOperation(operationLeaf);
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        cleaned = false;
                    }
                }

                try
                {
                    assets.DeleteDirectoryIfEmpty();
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    cleaned = false;
                }

                assets.Dispose();
                assets = null;
            }

            if (project is not null)
            {
                try
                {
                    project.DeleteOwnedProjectIfEmpty();
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    cleaned = false;
                }

                project.Dispose();
                project = null;
            }
        }
        catch
        {
            cleaned = false;
        }

        return cleaned;
    }

    private static string AssetOperationLeaf(string canonicalLeaf) =>
        $".{canonicalLeaf}.tmp-{Guid.NewGuid():D}".ToLowerInvariant();

    private static DesignerDocumentResult DecodeOrCopyInvalid(Exception exception)
    {
        var property = exception.GetType().GetProperty(
            "Code",
            BindingFlags.Instance | BindingFlags.Public);
        var code = property?.PropertyType == typeof(string) &&
            property.GetValue(exception) is string decoderCode &&
            decoderCode.StartsWith("image.", StringComparison.Ordinal)
                ? decoderCode
                : "document.asset-copy-failed";
        return Invalid(
            code,
            "$document.assets",
            "The draft-owned asset conversion failed and was not opened.");
    }

    private static SkinValidationError ToValidationError(DraftLoadFailure failure) =>
        new(
            failure.ErrorCode,
            $"$draft.{failure.LeafName}",
            failure.Message);

    private static DesignerDocumentResult Invalid(
        string code,
        string location,
        string message) =>
        new(
            null,
            EmptyAssets,
            [new SkinValidationError(code, location, message)]);
}
