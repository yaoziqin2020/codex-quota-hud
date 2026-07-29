using System.Windows;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI.Skins;

public partial class LiquidGlassSkin : AnimatedQuotaSkin
{
    public LiquidGlassSkin()
    {
        InitializeComponent();
        ConfigureRotation(
            nameof(GlassFluidTransform),
            idleSeconds: 30,
            refreshingSeconds: 3.2,
            clockwise: false);
    }

    public override SkinId Id => SkinId.LiquidGlass;

    protected override void RenderCore(QuotaSkinState state)
    {
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
