using System.Windows;
using System.Windows.Controls;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;
using Media = System.Windows.Media;

namespace CodexQuotaHud.App.UI.Skins;

public partial class LiquidTankSkin :
    AnimatedQuotaSkin,
    IOrbAnimationTarget
{
    private const double LiquidCapacity = 96;
    internal const double WaveCenterY = 4;
    private readonly LiquidTankMotionController _motionController;
    private readonly Media.Brush _normalLiquidBodyFill;
    private readonly Media.Brush _normalBackWaveFill;
    private readonly Media.Brush _normalFrontWaveFill;
    private readonly Media.Brush _normalPercentForeground;
    private readonly Media.Brush _normalSecondaryStroke;

    public LiquidTankSkin()
    {
        InitializeComponent();
        _normalLiquidBodyFill = LiquidBody.Fill;
        _normalBackWaveFill = BackWaveSurface.Fill;
        _normalFrontWaveFill = FrontWaveSurface.Fill;
        _normalPercentForeground = PercentText.Foreground;
        _normalSecondaryStroke = SecondaryArc.Stroke;
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

    internal IReadOnlyList<LiquidSurfaceTrackDescriptor>
        ConfiguredLiquidSurfaceTracks =>
            _motionController.ConfiguredSurfaceTracks;

    internal double CurrentWaterlineY { get; private set; } = 48;

    protected override void RenderCore(QuotaSkinState state)
    {
        LiquidBody.Fill = QuotaAlertPalette.ResolveBrush(
            state.PrimaryAlert,
            _normalLiquidBodyFill);
        BackWaveSurface.Fill = QuotaAlertPalette.ResolveBrush(
            state.PrimaryAlert,
            _normalBackWaveFill);
        FrontWaveSurface.Fill = QuotaAlertPalette.ResolveBrush(
            state.PrimaryAlert,
            _normalFrontWaveFill);
        PercentText.Foreground = QuotaAlertPalette.ResolveBrush(
            state.PrimaryAlert,
            _normalPercentForeground);
        SecondaryArc.Stroke = QuotaAlertPalette.ResolveBrush(
            state.SecondaryAlert ?? QuotaAlertLevel.Normal,
            _normalSecondaryStroke);
        CurrentWaterlineY = CalculateWaterlineY(state.PrimaryPercent);
        Canvas.SetTop(
            TankSurfaceGroup,
            CurrentWaterlineY - WaveCenterY);
        LiquidLayer.Visibility = CurrentWaterlineY < LiquidCapacity
            ? Visibility.Visible
            : Visibility.Collapsed;
        SecondaryArc.Progress = state.SecondaryPercent ?? 0;
        var secondaryVisibility = state.Mode == QuotaDisplayMode.Dual
            ? Visibility.Visible
            : Visibility.Collapsed;
        SecondaryArc.Visibility = secondaryVisibility;
        WeeklyTicks.Visibility = secondaryVisibility;
        PercentText.Text = $"{state.PrimaryPercent:0}%";
        LabelText.Text = state.PrimaryLabel;
    }

    internal static double CalculateWaterlineY(double remainingPercent)
    {
        var normalized = double.IsFinite(remainingPercent)
            ? Math.Clamp(remainingPercent, 0, 100)
            : 0;
        return LiquidCapacity * (1 - normalized / 100);
    }

    void IOrbAnimationTarget.ApplyAnimationState(
        OrbAnimationState state,
        bool animationsEnabled)
    {
        base.ApplyAnimationState(state, animationsEnabled);
        _motionController.Apply(state, animationsEnabled);
    }
}
