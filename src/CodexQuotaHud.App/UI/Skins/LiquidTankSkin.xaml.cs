using System.Windows;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI.Skins;

public partial class LiquidTankSkin : AnimatedQuotaSkin
{
    private const double LiquidCapacity = 96;

    public LiquidTankSkin()
    {
        InitializeComponent();
        ConfigureSlosh(
            nameof(TankWaveTransform),
            idleSeconds: 24,
            refreshingSeconds: 3.2);
    }

    public override SkinId Id => SkinId.LiquidTank;

    protected override void RenderCore(QuotaSkinState state)
    {
        LiquidLayer.Height = CalculateLiquidHeight(state.PrimaryPercent);
        SecondaryArc.Progress = state.SecondaryPercent ?? 0;
        var secondaryVisibility = state.Mode == QuotaDisplayMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        SecondaryArc.Visibility = secondaryVisibility;
        WeeklyTicks.Visibility = secondaryVisibility;
        PercentText.Text = $"{state.PrimaryPercent:0}%";
        LabelText.Text = state.PrimaryLabel;
    }

    internal static double CalculateLiquidHeight(double remainingPercent)
    {
        var normalized = double.IsFinite(remainingPercent)
            ? Math.Clamp(remainingPercent, 0, 100)
            : 0;
        return LiquidCapacity * normalized / 100;
    }
}
