using CodexQuotaHud.SkinDesigner.Images;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.UI;

public sealed class ImageSlotViewModel : IDisposable
{
    private readonly DesignerViewModel? _owner;

    public ImageSlotViewModel(SkinAssetSlot slot)
        : this(slot, owner: null)
    {
    }

    internal ImageSlotViewModel(
        SkinAssetSlot slot,
        DesignerViewModel? owner)
    {
        if (!Enum.IsDefined(slot))
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        Slot = slot;
        _owner = owner;
        ReplaceCommand = new AsyncRelayCommand(
            ReplaceAsync,
            () => _owner?.CanUseImageWorkflow == true);
        RemoveCommand = new AsyncRelayCommand(
            RemoveAsync,
            () => _owner?.CanUseImageWorkflow == true && HasAsset);
    }

    public SkinAssetSlot Slot { get; }

    public bool HasAsset =>
        _owner?.Current.Assets.ContainsKey(Slot) == true;

    public string? OriginalFileName =>
        _owner?.Current.Assets.TryGetValue(Slot, out var reference) == true
            ? reference.OriginalFileName
            : null;

    public SkinImageTransform Transform => _owner is null
        ? new SkinImageTransform(0, 0, 1, 0, 1, 0.5, 0.5)
        : SelectTransform(_owner.Current.Theme);

    public AsyncRelayCommand ReplaceCommand { get; }

    public AsyncRelayCommand RemoveCommand { get; }

    public ImageMutationResult? LastMutation { get; private set; }

    public EditorMutationResult SetOffsetX(double value) =>
        Update(transform => transform with { OffsetX = value });

    public EditorMutationResult SetOffsetY(double value) =>
        Update(transform => transform with { OffsetY = value });

    public EditorMutationResult SetScale(double value) =>
        Update(transform => transform with { Scale = value });

    public EditorMutationResult SetRotation(double value) =>
        Update(transform => transform with { Rotation = value });

    public EditorMutationResult SetOpacity(double value) =>
        Update(transform => transform with { Opacity = value });

    public EditorMutationResult SetCropFocusX(double value) =>
        Update(transform => transform with { CropFocusX = value });

    public EditorMutationResult SetCropFocusY(double value) =>
        Update(transform => transform with { CropFocusY = value });

    public void Dispose()
    {
        ReplaceCommand.Dispose();
        RemoveCommand.Dispose();
    }

    internal void NotifyStateChanged()
    {
        ReplaceCommand.NotifyCanExecuteChanged();
        RemoveCommand.NotifyCanExecuteChanged();
    }

    private async Task ReplaceAsync(CancellationToken cancellationToken)
    {
        if (_owner is null)
        {
            return;
        }

        var result = await _owner.ReplaceImageAsync(Slot, cancellationToken)
            .ConfigureAwait(false);
        if (result is not null)
        {
            LastMutation = result;
        }
    }

    private async Task RemoveAsync(CancellationToken cancellationToken)
    {
        if (_owner is null)
        {
            return;
        }

        var result = await _owner.RemoveImageAsync(Slot, cancellationToken)
            .ConfigureAwait(false);
        if (result is not null)
        {
            LastMutation = result;
        }
    }

    private EditorMutationResult Update(
        Func<SkinImageTransform, SkinImageTransform> edit)
    {
        if (_owner is null)
        {
            return new EditorMutationResult(
                false,
                [new SkinValidationError(
                    "image.unbound",
                    "$image",
                    "The image slot is not attached to a draft session.")]);
        }

        return _owner.Apply(draft => draft with
        {
            Theme = ReplaceTransform(
                draft.Theme,
                edit(SelectTransform(draft.Theme)))
        });
    }

    private SkinImageTransform SelectTransform(SkinTheme theme) => Slot switch
    {
        SkinAssetSlot.Background => theme.Background,
        SkinAssetSlot.Center => theme.Center,
        SkinAssetSlot.Decoration => theme.Decoration,
        _ => throw new ArgumentOutOfRangeException(nameof(Slot))
    };

    private SkinTheme ReplaceTransform(
        SkinTheme theme,
        SkinImageTransform transform) => Slot switch
    {
        SkinAssetSlot.Background => theme with { Background = transform },
        SkinAssetSlot.Center => theme with { Center = transform },
        SkinAssetSlot.Decoration => theme with { Decoration = transform },
        _ => throw new ArgumentOutOfRangeException(nameof(Slot))
    };
}

public sealed class ImageEditorViewModel : EditorSectionViewModel, IDisposable
{
    internal ImageEditorViewModel(DesignerViewModel owner)
        : base(owner, "图片")
    {
        Background = new ImageSlotViewModel(SkinAssetSlot.Background, owner);
        Center = new ImageSlotViewModel(SkinAssetSlot.Center, owner);
        Decoration = new ImageSlotViewModel(SkinAssetSlot.Decoration, owner);
    }

    public ImageSlotViewModel Background { get; }

    public ImageSlotViewModel Center { get; }

    public ImageSlotViewModel Decoration { get; }

    public IReadOnlyList<ImageSlotViewModel> Slots =>
        [Background, Center, Decoration];

    public void Dispose()
    {
        foreach (var slot in Slots)
        {
            slot.Dispose();
        }
    }

    internal void NotifyStateChanged()
    {
        foreach (var slot in Slots)
        {
            slot.NotifyStateChanged();
        }
    }
}
