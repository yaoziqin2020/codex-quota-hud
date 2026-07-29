using System.Windows;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI.Skins;

public partial class LiquidTankSkin : AnimatedQuotaSkin
{
    private const double TankHeight = 80;

    public LiquidTankSkin()
    {
        InitializeComponent();
        ConfigureSlosh(
            nameof(TankSloshTransform),
            idleSeconds: 18,
            refreshingSeconds: 2.8);
        ConfigureSlosh(
            nameof(TankWaveTransform),
            idleSeconds: 23,
            refreshingSeconds: 3.2);
    }

    public override SkinId Id => SkinId.LiquidTank;

    protected override void RenderCore(QuotaSkinState state)
    {
        var height = TankHeight * state.PrimaryPercent / 100;
        LiquidFill.Height = height;
        WaveCrest.Margin = new Thickness(
            0,
            Math.Clamp(TankHeight - height - 5, -4, TankHeight - 5),
            0,
            0);
        SecondaryArc.Progress = state.SecondaryPercent ?? 0;
        SecondaryArc.Visibility = state.Mode == QuotaDisplayMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        PercentText.Text = $"{state.PrimaryPercent:0}%";
        LabelText.Text = state.PrimaryLabel;
    }
}
