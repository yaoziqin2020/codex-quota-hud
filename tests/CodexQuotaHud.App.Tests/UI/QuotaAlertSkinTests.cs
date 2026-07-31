using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Controls;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class QuotaAlertSkinTests
{
    [Theory]
    [InlineData(SkinId.HudDial)]
    [InlineData(SkinId.EnergyRing)]
    [InlineData(SkinId.LiquidGlass)]
    [InlineData(SkinId.Aurora)]
    public void BuiltInArcSkin_RendersIndependentAlertsAndRestoresNormalBrushes(
        SkinId skinId) =>
        RunSta(() =>
        {
            var controller = new SkinController();
            var skin = controller.Select(skinId);
            var primaryArc = Find<ProgressArc>(skin.View, "PrimaryArc");
            var secondaryArc = Find<ProgressArc>(skin.View, "SecondaryArc");
            var percentText = Find<TextBlock>(skin.View, "PercentText");

            controller.Render(CreateState(75, 80, QuotaDisplayMode.Dual));
            var normalPrimary = primaryArc.Stroke;
            var normalSecondary = secondaryArc.Stroke;
            var normalPercent = percentText.Foreground;
            var fluidBlob = skinId == SkinId.LiquidGlass
                ? Find<Ellipse>(skin.View, "FluidBlob")
                : null;
            var normalFluid = fluidBlob?.Fill;

            controller.Render(CreateState(20, 9, QuotaDisplayMode.Dual));

            Assert.Same(QuotaAlertPalette.WarningBrush, primaryArc.Stroke);
            Assert.Same(QuotaAlertPalette.WarningBrush, percentText.Foreground);
            Assert.Same(QuotaAlertPalette.CriticalBrush, secondaryArc.Stroke);
            Assert.NotSame(primaryArc.Stroke, secondaryArc.Stroke);
            if (fluidBlob is not null)
            {
                Assert.Same(QuotaAlertPalette.WarningBrush, fluidBlob.Fill);
            }

            controller.Render(CreateState(75, 80, QuotaDisplayMode.Dual));

            AssertEquivalentBrush(normalPrimary, primaryArc.Stroke);
            AssertEquivalentBrush(normalSecondary, secondaryArc.Stroke);
            AssertEquivalentBrush(normalPercent, percentText.Foreground);
            if (fluidBlob is not null)
            {
                AssertEquivalentBrush(normalFluid, fluidBlob.Fill);
            }

            controller.Render(CreateState(75, null, QuotaDisplayMode.Single));

            Assert.Equal(Visibility.Collapsed, secondaryArc.Visibility);
        });

    private static QuotaSkinState CreateState(
        double primary,
        double? secondary,
        QuotaDisplayMode mode) =>
        new(
            primary,
            secondary,
            "5 hours",
            mode,
            IsRefreshing: false,
            AnimationsEnabled: true);

    private static T Find<T>(FrameworkElement view, string name)
        where T : class =>
        Assert.IsType<T>(view.FindName(name));

    private static void AssertEquivalentBrush(Brush? expected, Brush? actual)
    {
        Assert.NotNull(expected);
        Assert.NotNull(actual);
        Assert.Equal(expected.ToString(), actual.ToString());
    }

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
