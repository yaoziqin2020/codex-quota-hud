using System.Windows;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;
using Media = System.Windows.Media;

namespace CodexQuotaHud.App.UI.Skins;

public partial class LiquidGlassSkin : AnimatedQuotaSkin
{
    private readonly Media.Brush _normalPrimaryStroke;
    private readonly Media.Brush _normalSecondaryStroke;
    private readonly Media.Brush _normalPercentForeground;
    private readonly Media.Brush _normalFluidFill;

    public LiquidGlassSkin()
    {
        InitializeComponent();
        _normalPrimaryStroke = PrimaryArc.Stroke;
        _normalSecondaryStroke = SecondaryArc.Stroke;
        _normalPercentForeground = PercentText.Foreground;
        _normalFluidFill = FluidBlob.Fill;
        ConfigureRotation(
            nameof(GlassFluidTransform),
            idleSeconds: 30,
            refreshingSeconds: 3.2,
            clockwise: false);
    }

    public override SkinId Id => SkinId.LiquidGlass;

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
        FluidBlob.Fill = QuotaAlertPalette.ResolveBrush(
            state.PrimaryAlert,
            _normalFluidFill);
        PrimaryArc.Progress = state.PrimaryPercent;
        SecondaryArc.Progress = state.SecondaryPercent ?? 0;
        SecondaryArc.Visibility = state.Mode == QuotaDisplayMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        FluidBlob.Opacity = .30 + (.42 * state.PrimaryPercent / 100);
        PercentText.Text = $"{state.PrimaryPercent:0}%";
        LabelText.Text = state.PrimaryLabel;
    }
}
