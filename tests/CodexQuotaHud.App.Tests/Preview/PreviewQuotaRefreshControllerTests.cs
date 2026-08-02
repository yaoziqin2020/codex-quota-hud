using CodexQuotaHud.App.Preview;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.Preview;

public sealed class PreviewQuotaRefreshControllerTests
{
    public static TheoryData<PreviewDisplayChoice, QuotaDisplayMode,
        QuotaWindowKind?, QuotaWindowKind?> DisplayShapes => new()
    {
        { PreviewDisplayChoice.Dual, QuotaDisplayMode.Dual,
            QuotaWindowKind.FiveHour, QuotaWindowKind.Weekly },
        { PreviewDisplayChoice.FiveHourOnly, QuotaDisplayMode.Single,
            QuotaWindowKind.FiveHour, null },
        { PreviewDisplayChoice.WeeklyOnly, QuotaDisplayMode.Single,
            QuotaWindowKind.Weekly, null },
        { PreviewDisplayChoice.NoQuota, QuotaDisplayMode.Hidden, null, null }
    };

    [Theory]
    [MemberData(nameof(DisplayShapes))]
    public void Publish_CoversEveryShapeAndExactBoundaryPreset(
        PreviewDisplayChoice choice,
        QuotaDisplayMode expectedMode,
        QuotaWindowKind? expectedPrimary,
        QuotaWindowKind? expectedSecondary)
    {
        var controller = new PreviewQuotaRefreshController();

        foreach (var preset in new[] { 100d, 68d, 21d, 20d, 11d, 10d, 0d })
        {
            controller.Publish(choice, preset, preset, isRefreshing: false);

            var display = controller.CurrentState.Display;
            Assert.Equal(expectedMode, display.Mode);
            Assert.Equal(expectedPrimary, display.Primary?.Kind);
            Assert.Equal(expectedSecondary, display.Secondary?.Kind);
            Assert.Equal(expectedPrimary is null ? null : preset,
                display.Primary?.RemainingPercent);
            Assert.Equal(expectedSecondary is null ? null : preset,
                display.Secondary?.RemainingPercent);
            if (display.Primary is not null)
            {
                Assert.Equal(
                    CodexQuotaHud.App.UI.QuotaAlertPolicy.Classify(preset),
                    CodexQuotaHud.App.UI.QuotaAlertPolicy.Classify(
                        display.Primary.RemainingPercent));
            }

            if (display.Secondary is not null)
            {
                Assert.Equal(
                    CodexQuotaHud.App.UI.QuotaAlertPolicy.Classify(preset),
                    CodexQuotaHud.App.UI.QuotaAlertPolicy.Classify(
                        display.Secondary.RemainingPercent));
            }
        }
    }

    [Theory]
    [InlineData((int)PreviewDisplayChoice.Dual, QuotaDisplayMode.Dual,
        QuotaWindowKind.FiveHour, true)]
    [InlineData((int)PreviewDisplayChoice.FiveHourOnly, QuotaDisplayMode.Single,
        QuotaWindowKind.FiveHour, false)]
    [InlineData((int)PreviewDisplayChoice.WeeklyOnly, QuotaDisplayMode.Single,
        QuotaWindowKind.Weekly, false)]
    [InlineData((int)PreviewDisplayChoice.NoQuota, QuotaDisplayMode.Hidden,
        null, false)]
    public void Publish_ProducesRequestedProductionDisplayShape(
        int choice,
        QuotaDisplayMode expectedMode,
        QuotaWindowKind? expectedPrimary,
        bool expectedSecondary)
    {
        var controller = new PreviewQuotaRefreshController();

        controller.Publish(
            (PreviewDisplayChoice)choice,
            68,
            34,
            isRefreshing: false);

        Assert.Equal(expectedMode, controller.CurrentState.Display.Mode);
        Assert.Equal(expectedPrimary, controller.CurrentState.Display.Primary?.Kind);
        Assert.Equal(expectedSecondary,
            controller.CurrentState.Display.Secondary is not null);
    }

    [Fact]
    public void Publish_ClampsPercentagesAndCarriesRefreshingState()
    {
        var controller = new PreviewQuotaRefreshController();

        controller.Publish(
            PreviewDisplayChoice.Dual,
            fiveHourPercent: 125,
            weeklyPercent: -8,
            isRefreshing: true);

        Assert.Equal(100,
            controller.CurrentState.Display.Primary?.RemainingPercent);
        Assert.Equal(0,
            controller.CurrentState.Display.Secondary?.RemainingPercent);
        Assert.True(controller.CurrentState.IsRefreshing);
    }

    [Fact]
    public void Publish_RejectsInvalidValuesBeforePublishing()
    {
        var controller = new PreviewQuotaRefreshController();
        var original = controller.CurrentState;
        var publications = 0;
        controller.StateChanged += _ => publications++;

        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Publish(
            (PreviewDisplayChoice)999,
            68,
            34,
            isRefreshing: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Publish(
            PreviewDisplayChoice.Dual,
            double.NaN,
            34,
            isRefreshing: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => controller.Publish(
            PreviewDisplayChoice.Dual,
            68,
            double.PositiveInfinity,
            isRefreshing: false));

        Assert.Same(original, controller.CurrentState);
        Assert.Equal(0, publications);
    }

    [Fact]
    public async Task RefreshNow_RepublishesCurrentState()
    {
        var controller = new PreviewQuotaRefreshController();
        var published = new List<object>();
        controller.StateChanged += published.Add;

        await controller.RefreshNowAsync(
            onlyIfStale: false,
            CancellationToken.None);

        Assert.Single(published);
        Assert.Same(controller.CurrentState, published[0]);
    }
}
