using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.App.UI.Controls;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using ShapePath = System.Windows.Shapes.Path;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class LiquidTankSkinTests
{
    [Theory]
    [InlineData(0, 96)]
    [InlineData(25, 72)]
    [InlineData(50, 48)]
    [InlineData(93, 6.72)]
    [InlineData(100, 0)]
    public void WaterlineY_MapsRemainingPercentIntoTheFullVessel(
        double percent,
        double expectedY)
    {
        Assert.Equal(
            expectedY,
            LiquidTankSkin.CalculateWaterlineY(percent),
            precision: 3);
    }

    [Fact]
    public void SingleWeekly_RendersOneLiquidValueWithoutOuterQuotaLayer() =>
        RunSta(() =>
        {
            var skin = new LiquidTankSkin();

            skin.Render(new QuotaSkinState(
                84,
                null,
                "每周",
                QuotaDisplayMode.Single,
                IsRefreshing: false,
                AnimationsEnabled: true));

            var liquid = Assert.IsType<Canvas>(
                skin.FindName("LiquidLayer"));
            var surface = Assert.IsType<Grid>(
                skin.FindName("TankSurfaceGroup"));
            var secondary = Assert.IsType<ProgressArc>(
                skin.FindName("SecondaryArc"));
            var ticks = Assert.IsAssignableFrom<FrameworkElement>(
                skin.FindName("WeeklyTicks"));
            var label = Assert.IsType<TextBlock>(
                skin.FindName("LabelText"));
            var percent = Assert.IsType<TextBlock>(
                skin.FindName("PercentText"));

            Assert.Equal(96, liquid.Height);
            Assert.Equal(15.36, skin.CurrentWaterlineY, precision: 3);
            Assert.Equal(
                skin.CurrentWaterlineY - LiquidTankSkin.WaveCenterY,
                Canvas.GetTop(surface),
                precision: 3);
            Assert.Equal(Visibility.Visible, liquid.Visibility);
            Assert.Equal(Visibility.Collapsed, secondary.Visibility);
            Assert.Equal(Visibility.Collapsed, ticks.Visibility);
            Assert.Equal("每周", label.Text);
            Assert.Equal("84%", percent.Text);
        });

    [Fact]
    public void Dual_RendersPrimaryAsLiquidAndWeeklyAsSubtleOuterLayer() =>
        RunSta(() =>
        {
            var skin = new LiquidTankSkin();

            skin.Render(new QuotaSkinState(
                61,
                84,
                "5 小时",
                QuotaDisplayMode.Dual,
                IsRefreshing: false,
                AnimationsEnabled: true));

            var liquid = Assert.IsType<Canvas>(
                skin.FindName("LiquidLayer"));
            var surface = Assert.IsType<Grid>(
                skin.FindName("TankSurfaceGroup"));
            var secondary = Assert.IsType<ProgressArc>(
                skin.FindName("SecondaryArc"));
            var ticks = Assert.IsAssignableFrom<FrameworkElement>(
                skin.FindName("WeeklyTicks"));

            Assert.Equal(96, liquid.Height);
            Assert.Equal(37.44, skin.CurrentWaterlineY, precision: 3);
            Assert.Equal(
                skin.CurrentWaterlineY - LiquidTankSkin.WaveCenterY,
                Canvas.GetTop(surface),
                precision: 3);
            Assert.Equal(84, secondary.Progress);
            Assert.Equal(Visibility.Visible, secondary.Visibility);
            Assert.Equal(Visibility.Visible, ticks.Visibility);
        });

    [Fact]
    public void LiquidSurface_UsesTwoWideClosedWavesOverAStableBodyWithoutEdgeGaps() =>
        RunSta(() =>
        {
            var skin = new LiquidTankSkin();
            var surfaceGroup = Assert.IsType<Grid>(
                skin.FindName("TankSurfaceGroup"));
            var liquidLayer = Assert.IsType<Canvas>(
                skin.FindName("LiquidLayer"));
            var vesselViewport = Assert.IsType<Grid>(
                skin.FindName("VesselViewport"));
            var liquidBody = Assert.IsType<Rectangle>(
                skin.FindName("LiquidBody"));
            var backWave = Assert.IsType<ShapePath>(
                skin.FindName("BackWaveSurface"));
            var frontWave = Assert.IsType<ShapePath>(
                skin.FindName("FrontWaveSurface"));
            var backGeometry = backWave.Data.GetFlattenedPathGeometry();
            var frontGeometry = frontWave.Data.GetFlattenedPathGeometry();

            Assert.True(surfaceGroup.RenderTransform.Value.IsIdentity);
            Assert.False(liquidLayer.ClipToBounds);
            Assert.Equal(96, liquidLayer.Height);
            Assert.NotNull(vesselViewport.Clip);
            Assert.Same(surfaceGroup, liquidBody.Parent);
            Assert.Equal(
                LiquidTankSkin.WaveCenterY,
                liquidBody.Margin.Top);
            Assert.All(
                new[] { backWave, frontWave },
                wave =>
                {
                    Assert.NotNull(wave.Fill);
                    Assert.Contains(
                        wave.Data.GetFlattenedPathGeometry().Figures,
                        figure => figure.IsClosed);
                    var contourYs = wave.Data
                        .GetFlattenedPathGeometry()
                        .Figures
                        .SelectMany(figure => figure.Segments)
                        .OfType<PolyLineSegment>()
                        .SelectMany(segment => segment.Points)
                        .Select(point => point.Y)
                        .Where(y => y <= 12)
                        .ToArray();
                    Assert.Contains(
                        contourYs,
                        y => y < LiquidTankSkin.WaveCenterY);
                    Assert.Contains(
                        contourYs,
                        y => y > LiquidTankSkin.WaveCenterY);
                });
            Assert.True(backGeometry.Bounds.Width >= 120);
            Assert.True(frontGeometry.Bounds.Width >= 120);
            Assert.True(backGeometry.Bounds.Bottom > liquidBody.Margin.Top);
            Assert.True(frontGeometry.Bounds.Bottom > liquidBody.Margin.Top);

            foreach (var track in skin.ConfiguredLiquidSurfaceTracks)
            {
                var geometry = track.TargetName == "BackWaveTransform"
                    ? backGeometry
                    : frontGeometry;
                foreach (var offset in new[] { track.From, track.To })
                {
                    Assert.True(geometry.Bounds.Left + offset <= 0);
                    Assert.True(geometry.Bounds.Right + offset >= 72);
                }
            }
        });

    [Fact]
    public void Motion_UsesTwoOpposingXOnlyWaveTracksAndTwoBubblesWithUnifiedCaps() =>
        RunSta(() =>
        {
            var skin = new LiquidTankSkin();
            var target = Assert.IsAssignableFrom<IOrbAnimationTarget>(skin);
            var surfaceTracks = skin.ConfiguredLiquidSurfaceTracks;

            Assert.Equal(4, skin.ConfiguredLiquidTrackCount);
            Assert.Equal(2, surfaceTracks.Count);
            Assert.Equal(
                ["BackWaveTransform", "FrontWaveTransform"],
                surfaceTracks.Select(track => track.TargetName));
            Assert.All(
                surfaceTracks,
                track =>
                {
                    Assert.Equal("X", track.PropertyPath);
                    Assert.InRange(track.IdleSeconds, 9, 14);
                    Assert.Equal(
                        track.Wavelength,
                        Math.Abs(track.To - track.From),
                        precision: 3);
                    Assert.True(
                        track.Wavelength / track.IdleSeconds / 4 < 1);
                });
            Assert.True(
                (surfaceTracks[0].To - surfaceTracks[0].From)
                * (surfaceTracks[1].To - surfaceTracks[1].From) < 0);

            target.ApplyAnimationState(
                OrbAnimationState.Idle,
                animationsEnabled: true);
            Assert.All(
                skin.ConfiguredLiquidFrameRates,
                frameRate => Assert.Equal(4, frameRate));
            Assert.Equal(
                skin.ConfiguredLiquidTrackCount,
                skin.ActiveLiquidClockCount);

            target.ApplyAnimationState(
                OrbAnimationState.Refreshing,
                animationsEnabled: true);
            Assert.All(
                skin.ConfiguredLiquidFrameRates,
                frameRate => Assert.Equal(24, frameRate));

            target.ApplyAnimationState(
                OrbAnimationState.Hidden,
                animationsEnabled: true);
            Assert.Equal(0, skin.ActiveLiquidClockCount);
            Assert.Equal(
                0,
                Assert.IsType<TranslateTransform>(
                    skin.FindName("BackWaveTransform")).X);
            Assert.Equal(
                0,
                Assert.IsType<TranslateTransform>(
                    skin.FindName("FrontWaveTransform")).X);
            Assert.Equal(
                0,
                Assert.IsType<Ellipse>(
                    skin.FindName("BubbleOne")).Opacity);
            Assert.Equal(
                0,
                Assert.IsType<Ellipse>(
                    skin.FindName("BubbleTwo")).Opacity);
        });

    [Fact]
    public void AnimationStates_DoNotChangeTheRenderedWaterline() =>
        RunSta(() =>
        {
            var skin = new LiquidTankSkin();
            var target = Assert.IsAssignableFrom<IOrbAnimationTarget>(skin);
            skin.Render(new QuotaSkinState(
                61,
                84,
                "5 灏忔椂",
                QuotaDisplayMode.Dual,
                IsRefreshing: false,
                AnimationsEnabled: true));
            var liquid = Assert.IsType<Canvas>(
                skin.FindName("LiquidLayer"));
            var surface = Assert.IsType<Grid>(
                skin.FindName("TankSurfaceGroup"));
            var renderedWaterline = skin.CurrentWaterlineY;
            var renderedSurfaceTop = Canvas.GetTop(surface);

            target.ApplyAnimationState(
                OrbAnimationState.Idle,
                animationsEnabled: true);
            Assert.Equal(renderedWaterline, skin.CurrentWaterlineY);
            Assert.Equal(renderedSurfaceTop, Canvas.GetTop(surface));
            Assert.Equal(96, liquid.Height);

            target.ApplyAnimationState(
                OrbAnimationState.Refreshing,
                animationsEnabled: true);
            Assert.Equal(renderedWaterline, skin.CurrentWaterlineY);
            Assert.Equal(renderedSurfaceTop, Canvas.GetTop(surface));
            Assert.Equal(96, liquid.Height);

            target.ApplyAnimationState(
                OrbAnimationState.Hidden,
                animationsEnabled: true);
            Assert.Equal(renderedWaterline, skin.CurrentWaterlineY);
            Assert.Equal(renderedSurfaceTop, Canvas.GetTop(surface));
            Assert.Equal(96, liquid.Height);
        });

    [Fact]
    public void ZeroPercent_StrictlyHidesTheLiquid() =>
        RunSta(() =>
        {
            var skin = new LiquidTankSkin();
            skin.Render(new QuotaSkinState(
                0,
                null,
                "姣忓懆",
                QuotaDisplayMode.Single,
                IsRefreshing: false,
                AnimationsEnabled: true));

            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsType<Canvas>(
                    skin.FindName("LiquidLayer")).Visibility);
        });

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
