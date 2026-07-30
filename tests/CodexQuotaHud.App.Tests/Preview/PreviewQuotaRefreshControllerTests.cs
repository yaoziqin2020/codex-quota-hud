using CodexQuotaHud.App.Preview;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.Preview;

public sealed class PreviewQuotaRefreshControllerTests
{
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
