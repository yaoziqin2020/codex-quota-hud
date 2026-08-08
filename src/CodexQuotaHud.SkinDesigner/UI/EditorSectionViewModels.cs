using System.ComponentModel;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.UI;

public abstract class EditorSectionViewModel
{
    protected EditorSectionViewModel(
        DesignerViewModel owner,
        string header)
    {
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Header = header;
    }

    protected DesignerViewModel Owner { get; }

    public string Header { get; }
}

public sealed class BasicInformationEditorViewModel(
    DesignerViewModel owner) : EditorSectionViewModel(owner, "基本信息")
{
    public EditorMutationResult SetProjectName(string value) =>
        Owner.Apply(draft => draft with { ProjectName = value });

    public EditorMutationResult SetDisplayName(string value) =>
        Owner.Apply(draft => draft with { DisplayName = value });

    public EditorMutationResult SetAuthor(string value) =>
        Owner.Apply(draft => draft with { Author = value });

    public EditorMutationResult SetPackageVersion(string value) =>
        Owner.Apply(draft => draft with
        {
            PackageVersion = SemanticVersion.Parse(value)
        });

    public EditorMutationResult SetDescription(string value) =>
        Owner.Apply(draft => draft with { Description = value });
}

public sealed class QuotaRingEditorViewModel(
    DesignerViewModel owner) : EditorSectionViewModel(owner, "额度环")
{
    public EditorMutationResult SetRingDiameter(double value) =>
        Update(theme => theme with { RingDiameter = value });

    public EditorMutationResult SetRingThickness(double value) =>
        Update(theme => theme with { RingThickness = value });

    public EditorMutationResult SetRingGap(double value) =>
        Update(theme => theme with { RingGap = value });

    public EditorMutationResult SetStartAngle(double value) =>
        Update(theme => theme with { StartAngle = value });

    private EditorMutationResult Update(Func<SkinTheme, SkinTheme> edit) =>
        Owner.Apply(draft => draft with { Theme = edit(draft.Theme) });
}

public sealed class ColorEffectsEditorViewModel(
    DesignerViewModel owner) : EditorSectionViewModel(owner, "颜色与效果")
{
    public EditorMutationResult SetPrimaryRingColor(string value) =>
        Update(theme => theme with { PrimaryRingColor = value });

    public EditorMutationResult SetSecondaryRingColor(string value) =>
        Update(theme => theme with { SecondaryRingColor = value });

    public EditorMutationResult SetBaseBackgroundColor(string value) =>
        Update(theme => theme with { BaseBackgroundColor = value });

    public EditorMutationResult SetBaseBackgroundOpacity(double value) =>
        Update(theme => theme with { BaseBackgroundOpacity = value });

    public EditorMutationResult SetGlowColor(string value) =>
        Update(theme => theme with { GlowColor = value });

    public EditorMutationResult SetGlowIntensity(double value) =>
        Update(theme => theme with { GlowIntensity = value });

    private EditorMutationResult Update(Func<SkinTheme, SkinTheme> edit) =>
        Owner.Apply(draft => draft with { Theme = edit(draft.Theme) });
}

public sealed class TextEditorViewModel(
    DesignerViewModel owner) : EditorSectionViewModel(owner, "文字")
{
    public EditorMutationResult SetNumberTextSize(double value) =>
        Update(theme => theme with { NumberTextSize = value });

    public EditorMutationResult SetLabelTextSize(double value) =>
        Update(theme => theme with { LabelTextSize = value });

    public EditorMutationResult SetTextWeight(SkinTextWeight value) =>
        Update(theme => theme with { TextWeight = value });

    public EditorMutationResult SetTextPlacement(SkinTextPlacement value) =>
        Update(theme => theme with { TextPlacement = value });

    public EditorMutationResult SetTextOffsetY(double value) =>
        Update(theme => theme with { TextOffsetY = value });

    public EditorMutationResult SetTextLineGap(double value) =>
        Update(theme => theme with { TextLineGap = value });

    private EditorMutationResult Update(Func<SkinTheme, SkinTheme> edit) =>
        Owner.Apply(draft => draft with { Theme = edit(draft.Theme) });
}

public sealed class AnimationEditorViewModel(
    DesignerViewModel owner) : EditorSectionViewModel(owner, "动画"),
    INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public bool CanEditDecorationAnimation =>
        Owner.Assets.ContainsKey(SkinAssetSlot.Decoration);

    public string DecorationAnimationHint => CanEditDecorationAnimation
        ? "装饰旋转和浮动会作用于当前透明装饰图。"
        : "装饰旋转和浮动需要先添加透明装饰图。";

    public string CurrentPresetName => AnimationPresets.DisplayName(
        Owner.Current.Theme.Animation,
        CanEditDecorationAnimation);

    public EditorMutationResult ApplyPreset(AnimationPresetKind preset)
    {
        var settings = AnimationPresets.Resolve(
            preset,
            CanEditDecorationAnimation);
        return Update(animation => settings with
        {
            RefreshSpeedMultiplier = animation.RefreshSpeedMultiplier,
            RefreshHoldSeconds = animation.RefreshHoldSeconds
        });
    }

    public EditorMutationResult SetRotationIntensity(double value) =>
        Update(animation => animation with { RotationIntensity = value });

    public EditorMutationResult SetBreathingIntensity(double value) =>
        Update(animation => animation with { BreathingIntensity = value });

    public EditorMutationResult SetGlowIntensity(double value) =>
        Update(animation => animation with { GlowIntensity = value });

    public EditorMutationResult SetFloatingIntensity(double value) =>
        Update(animation => animation with { FloatingIntensity = value });

    public EditorMutationResult SetRefreshSpeedMultiplier(double value) =>
        Update(animation => animation with
        {
            RefreshSpeedMultiplier = NormalizeRefreshValue(
                value,
                SkinPackageLimits.MinimumRefreshSpeedMultiplier,
                SkinPackageLimits.MaximumRefreshSpeedMultiplier)
        });

    public EditorMutationResult SetRefreshHoldSeconds(double value) =>
        Update(animation => animation with
        {
            RefreshHoldSeconds = NormalizeRefreshValue(
                value,
                SkinPackageLimits.MinimumRefreshHoldSeconds,
                SkinPackageLimits.MaximumRefreshHoldSeconds)
        });

    internal void NotifyStateChanged()
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(CanEditDecorationAnimation)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(DecorationAnimationHint)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(CurrentPresetName)));
    }

    private EditorMutationResult Update(
        Func<SkinAnimationSettings, SkinAnimationSettings> edit) =>
        Owner.Apply(draft => draft with
        {
            Theme = draft.Theme with
            {
                Animation = edit(draft.Theme.Animation)
            }
        });

    private static double NormalizeRefreshValue(
        double value,
        double minimum,
        double maximum) =>
        !double.IsFinite(value) || value < minimum || value > maximum
            ? value
            : (double)Math.Round(
                (decimal)value,
                decimals: 1,
                MidpointRounding.AwayFromZero);
}
