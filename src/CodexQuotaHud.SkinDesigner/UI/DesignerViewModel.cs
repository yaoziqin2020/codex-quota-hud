using System.Collections.ObjectModel;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Images;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.SkinDesigner.UI;

public sealed record EditorMutationResult(
    bool Succeeded,
    IReadOnlyList<SkinValidationError> Errors);

public sealed class DesignerViewModel : IDisposable, IDesignerImageMutationCommitter
{
    private readonly SkinDraftSession _session;
    private readonly Dictionary<SkinAssetSlot, SkinAsset> _assets;
    private readonly ReadOnlyDictionary<SkinAssetSlot, SkinAsset> _assetsView;
    private readonly Action<
        SkinDraftDocument,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset>> _previewUpdate;
    private readonly Func<
        Func<SkinDraftDocument, SkinDraftDocument>,
        bool> _meaningfulCommit;
    private IImagePicker? _imagePicker;
    private DesignerImageService? _imageService;
    private int _disposed;

    public DesignerViewModel(
        SkinDraftSession session,
        Action<SkinDraftDocument>? previewUpdate = null)
        : this(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            previewUpdate is null
                ? null
                : (draft, _) => previewUpdate(draft))
    {
    }

    internal DesignerViewModel(
        SkinDraftSession session,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets,
        Action<
            SkinDraftDocument,
            IReadOnlyDictionary<SkinAssetSlot, SkinAsset>>? previewUpdate,
        Func<Func<SkinDraftDocument, SkinDraftDocument>, bool>?
            meaningfulCommit = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentNullException.ThrowIfNull(assets);
        _assets = assets.ToDictionary(
            pair => pair.Key,
            pair => CloneAsset(pair.Value));
        _assetsView = new ReadOnlyDictionary<SkinAssetSlot, SkinAsset>(_assets);
        _previewUpdate = previewUpdate ?? ((_, _) => { });
        _meaningfulCommit = meaningfulCommit ?? _session.ApplyMeaningful;

        BasicInformation = new BasicInformationEditorViewModel(this);
        Images = new ImageEditorViewModel(this);
        QuotaRings = new QuotaRingEditorViewModel(this);
        ColorsAndEffects = new ColorEffectsEditorViewModel(this);
        Text = new TextEditorViewModel(this);
        Animation = new AnimationEditorViewModel(this);
        Sections =
        [
            BasicInformation,
            Images,
            QuotaRings,
            ColorsAndEffects,
            Text,
            Animation
        ];

        _session.MeaningfulChange += OnMeaningfulChange;
    }

    public BasicInformationEditorViewModel BasicInformation { get; }

    public ImageEditorViewModel Images { get; }

    public QuotaRingEditorViewModel QuotaRings { get; }

    public ColorEffectsEditorViewModel ColorsAndEffects { get; }

    public TextEditorViewModel Text { get; }

    public AnimationEditorViewModel Animation { get; }

    public IReadOnlyList<EditorSectionViewModel> Sections { get; }

    public SkinDraftDocument Current => _session.Current;

    public IReadOnlyDictionary<SkinAssetSlot, SkinAsset> Assets => _assetsView;

    internal bool CanUseImageWorkflow =>
        Volatile.Read(ref _disposed) == 0 &&
        _imagePicker is not null &&
        _imageService is not null;

    internal void ConfigureImageWorkflow(
        IImagePicker picker,
        DesignerImageService service)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        _imagePicker = picker ?? throw new ArgumentNullException(nameof(picker));
        _imageService = service ?? throw new ArgumentNullException(nameof(service));
        Images.NotifyStateChanged();
    }

    internal async Task<ImageMutationResult?> ReplaceImageAsync(
        SkinAssetSlot slot,
        CancellationToken cancellationToken)
    {
        if (!CanUseImageWorkflow)
        {
            return null;
        }

        var source = _imagePicker!.ChooseImage(slot);
        if (source is null)
        {
            return null;
        }

        return await _imageService!.ImportAsync(
            Current.DraftId,
            slot,
            source,
            cancellationToken).ConfigureAwait(false);
    }

    internal Task<ImageMutationResult?> RemoveImageAsync(
        SkinAssetSlot slot,
        CancellationToken cancellationToken) =>
        !CanUseImageWorkflow
            ? Task.FromResult<ImageMutationResult?>(null)
            : RemoveConfiguredImageAsync(slot, cancellationToken);

    internal EditorMutationResult Apply(
        Func<SkinDraftDocument, SkinDraftDocument> edit)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(edit);

        SkinDraftDocument candidate;
        try
        {
            candidate = edit(_session.Current) ??
                throw new InvalidOperationException(
                    "An editor mutation must return a draft.");
        }
        catch (FormatException)
        {
            return Invalid(
                "editor.version.invalid",
                "$.packageVersion",
                "Package version must use canonical major.minor.patch form.");
        }

        var themeValidation = SkinContractValidator.ValidateTheme(candidate.Theme);
        if (!themeValidation.IsValid)
        {
            return new EditorMutationResult(false, themeValidation.Errors);
        }

        var draftValidation = SkinDraftValidator.Validate(candidate);
        if (!draftValidation.IsValid)
        {
            return new EditorMutationResult(false, draftValidation.Errors);
        }

        _session.Apply(_ => candidate);
        return new EditorMutationResult(true, []);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _session.MeaningfulChange -= OnMeaningfulChange;
        Images.Dispose();
    }

    bool IDesignerImageMutationCommitter.TryCommit(
        SkinAsset asset,
        DraftAssetReference reference)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(reference);
        if (asset.Slot != reference.Slot ||
            !string.Equals(
                asset.RelativePath,
                reference.RelativePath,
                StringComparison.Ordinal))
        {
            return false;
        }

        var slot = asset.Slot;
        var hadPrevious = _assets.TryGetValue(slot, out var previous);
        _assets[slot] = CloneAsset(asset);
        var accepted = false;
        try
        {
            accepted = _meaningfulCommit(draft => draft with
            {
                Assets = ReplaceReference(draft.Assets, slot, reference)
            });
        }
        catch
        {
            accepted = false;
        }

        if (!accepted)
        {
            RestoreAsset(slot, hadPrevious, previous);
        }

        return accepted;
    }

    IReadOnlyDictionary<SkinAssetSlot, SkinAsset>
        IDesignerImageMutationCommitter.SnapshotAssets() =>
        _assets.ToDictionary(
            pair => pair.Key,
            pair => CloneAsset(pair.Value));

    bool IDesignerImageMutationCommitter.TryRemove(SkinAssetSlot slot)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);
        if (!Enum.IsDefined(slot))
        {
            return false;
        }

        var hadPrevious = _assets.Remove(slot, out var previous);
        var hadReference = _session.Current.Assets.ContainsKey(slot);
        if (!hadPrevious && !hadReference)
        {
            return true;
        }

        var accepted = false;
        try
        {
            accepted = _meaningfulCommit(draft => draft with
            {
                Assets = RemoveReference(draft.Assets, slot)
            });
        }
        catch
        {
            accepted = false;
        }

        if (!accepted)
        {
            RestoreAsset(slot, hadPrevious, previous);
        }

        return accepted;
    }

    private void OnMeaningfulChange(
        object? sender,
        SkinDraftDocument draft)
    {
        Images.NotifyStateChanged();
        _previewUpdate(
            draft,
            new ReadOnlyDictionary<SkinAssetSlot, SkinAsset>(
                _assets.ToDictionary(
                    pair => pair.Key,
                    pair => CloneAsset(pair.Value))));
    }

    private async Task<ImageMutationResult?> RemoveConfiguredImageAsync(
        SkinAssetSlot slot,
        CancellationToken cancellationToken) =>
        await _imageService!.RemoveAsync(
            Current.DraftId,
            slot,
            cancellationToken).ConfigureAwait(false);

    private void RestoreAsset(
        SkinAssetSlot slot,
        bool hadPrevious,
        SkinAsset? previous)
    {
        if (hadPrevious)
        {
            _assets[slot] = previous!;
        }
        else
        {
            _assets.Remove(slot);
        }
    }

    private static IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>
        ReplaceReference(
            IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference> current,
            SkinAssetSlot slot,
            DraftAssetReference reference)
    {
        var updated = current.ToDictionary(pair => pair.Key, pair => pair.Value);
        updated[slot] = reference;
        return new ReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>(updated);
    }

    private static IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>
        RemoveReference(
            IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference> current,
            SkinAssetSlot slot)
    {
        var updated = current.ToDictionary(pair => pair.Key, pair => pair.Value);
        updated.Remove(slot);
        return new ReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>(updated);
    }

    private static SkinAsset CloneAsset(SkinAsset asset) =>
        asset with { Content = [.. asset.Content] };

    private static EditorMutationResult Invalid(
        string code,
        string location,
        string message) =>
        new(false, [new SkinValidationError(code, location, message)]);
}
