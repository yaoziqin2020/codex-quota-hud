using System.Windows;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI.Skins;

public partial class LiquidTankSkin :
    AnimatedQuotaSkin,
    IOrbAnimationTarget
{
    private const double LiquidCapacity = 96;
    private readonly LiquidTankMotionController _motionController;

    public LiquidTankSkin()
    {
        InitializeComponent();
        ConfigureSlosh(
            nameof(TankAmbientTransform),
            idleSeconds: 22,
            refreshingSeconds: 3);
        _motionController = new LiquidTankMotionController(this);
    }

    public override SkinId Id => SkinId.LiquidTank;

    internal int ConfiguredLiquidTrackCount =>
        _motionController.ConfiguredTrackCount;

    internal int ActiveLiquidClockCount =>
        _motionController.ActiveClockCount;

    internal IReadOnlyList<int?> ConfiguredLiquidFrameRates =>
        _motionController.ConfiguredFrameRates;

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

    void IOrbAnimationTarget.ApplyAnimationState(
        OrbAnimationState state,
        bool animationsEnabled)
    {
        base.ApplyAnimationState(state, animationsEnabled);
        _motionController.Apply(state, animationsEnabled);
    }
}
