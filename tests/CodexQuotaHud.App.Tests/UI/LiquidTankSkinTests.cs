using System.Windows;
using System.Windows.Controls;
using CodexQuotaHud.App.UI.Controls;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class LiquidTankSkinTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(25, 24)]
    [InlineData(50, 48)]
    [InlineData(100, 96)]
    public void LiquidHeight_DirectlyTracksPrimaryRemainingPercent(
        double percent,
        double expectedHeight)
    {
        Assert.Equal(
            expectedHeight,
            LiquidTankSkin.CalculateLiquidHeight(percent),
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

            var liquid = Assert.IsAssignableFrom<FrameworkElement>(
                skin.FindName("LiquidLayer"));
            var secondary = Assert.IsType<ProgressArc>(
                skin.FindName("SecondaryArc"));
            var ticks = Assert.IsAssignableFrom<FrameworkElement>(
                skin.FindName("WeeklyTicks"));
            var label = Assert.IsType<TextBlock>(
                skin.FindName("LabelText"));
            var percent = Assert.IsType<TextBlock>(
                skin.FindName("PercentText"));

            Assert.Equal(80.64, liquid.Height, precision: 3);
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

            var liquid = Assert.IsAssignableFrom<FrameworkElement>(
                skin.FindName("LiquidLayer"));
            var secondary = Assert.IsType<ProgressArc>(
                skin.FindName("SecondaryArc"));
            var ticks = Assert.IsAssignableFrom<FrameworkElement>(
                skin.FindName("WeeklyTicks"));

            Assert.Equal(58.56, liquid.Height, precision: 3);
            Assert.Equal(84, secondary.Progress);
            Assert.Equal(Visibility.Visible, secondary.Visibility);
            Assert.Equal(Visibility.Visible, ticks.Visibility);
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
