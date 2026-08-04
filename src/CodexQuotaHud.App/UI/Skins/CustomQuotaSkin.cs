using System.Windows;
using System.Windows.Media;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Templates;
using MediaColor = System.Windows.Media.Color;

namespace CodexQuotaHud.App.UI.Skins;

public sealed class CustomQuotaSkin : IQuotaSkin, IOrbAnimationTarget
{
    private readonly CustomSkinRenderer _renderer;
    private readonly MediaColor _primaryColor;
    private readonly MediaColor _secondaryColor;
    private readonly double _refreshSpeedMultiplier;

    public CustomQuotaSkin(
        string selectionKey,
        SkinTheme theme,
        CustomSkinRenderer renderer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionKey);
        SelectionKey = selectionKey;
        ArgumentNullException.ThrowIfNull(theme);
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _primaryColor = Parse(theme.PrimaryRingColor);
        _secondaryColor = Parse(theme.SecondaryRingColor);
        _refreshSpeedMultiplier = theme.Animation.RefreshSpeedMultiplier;
        RefreshHoldDuration = TimeSpan.FromSeconds(
            theme.Animation.RefreshHoldSeconds);
    }

    public string SelectionKey { get; }

    public FrameworkElement View => _renderer;

    public TimeSpan RefreshHoldDuration { get; }

    public void Render(QuotaSkinState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _renderer.Render(new CustomSkinRenderState(
            state.PrimaryPercent,
            state.SecondaryPercent,
            state.PrimaryLabel,
            state.Mode,
            state.IsRefreshing,
            QuotaAlertPalette.ResolveMediaColor(
                state.PrimaryAlert,
                _primaryColor),
            state.SecondaryAlert is { } secondaryAlert
                ? QuotaAlertPalette.ResolveMediaColor(
                    secondaryAlert,
                    _secondaryColor)
                : null));
    }

    public void ApplyAnimationState(
        OrbAnimationState state,
        bool animationsEnabled) =>
        _renderer.ApplyAnimationState(
            state switch
            {
                OrbAnimationState.Idle => CustomSkinAnimationState.Idle,
                OrbAnimationState.Refreshing => CustomSkinAnimationState.Refreshing,
                _ => CustomSkinAnimationState.Hidden
            },
            animationsEnabled,
            _refreshSpeedMultiplier);

    private static MediaColor Parse(string value) =>
        (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value)!;
}
