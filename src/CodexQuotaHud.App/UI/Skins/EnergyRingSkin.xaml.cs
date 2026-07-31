using System.Windows;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;
using Media = System.Windows.Media;

namespace CodexQuotaHud.App.UI.Skins;

public partial class EnergyRingSkin : AnimatedQuotaSkin
{
    private readonly Media.Brush _normalPrimaryStroke;
    private readonly Media.Brush _normalSecondaryStroke;
    private readonly Media.Brush _normalPercentForeground;

    public EnergyRingSkin()
    {
        InitializeComponent();
        _normalPrimaryStroke = PrimaryArc.Stroke;
        _normalSecondaryStroke = SecondaryArc.Stroke;
        _normalPercentForeground = PercentText.Foreground;
        ConfigureRotation(
            nameof(EnergyOrbitTransform),
            idleSeconds: 22,
            refreshingSeconds: 2.6);
    }

    public override SkinId Id => SkinId.EnergyRing;

    protected override void RenderCore(QuotaSkinState state)
    {
        PrimaryArc.Stroke = QuotaAlertPalette.ResolveBrush(
            state.PrimaryAlert,
            _normalPrimaryStroke);
        PercentText.Foreground = QuotaAlertPalette.ResolveBrush(
            state.PrimaryAlert,
            _normalPercentForeground);
        SecondaryArc.Stroke = QuotaAlertPalette.ResolveBrush(
            state.SecondaryAlert ?? QuotaAlertLevel.Normal,
            _normalSecondaryStroke);
        PrimaryArc.Progress = state.PrimaryPercent;
        SecondaryArc.Progress = state.SecondaryPercent ?? 0;
        SecondaryArc.Visibility = state.Mode == QuotaDisplayMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        PercentText.Text = $"{state.PrimaryPercent:0}%";
        LabelText.Text = state.PrimaryLabel;
        RefreshGlyph.Opacity = state.IsRefreshing ? 1 : .72;
    }
}
