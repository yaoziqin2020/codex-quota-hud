using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Refresh;

namespace CodexQuotaHud.App.Preview;

internal sealed class PreviewQuotaRefreshController : IQuotaRefreshController
{
    private static readonly DateTimeOffset FetchedAt =
        DateTimeOffset.Parse("2030-01-01T00:00:00Z");

    public PreviewQuotaRefreshController()
    {
        CurrentState = CreateState(
            PreviewDisplayChoice.Dual,
            fiveHourPercent: 68,
            weeklyPercent: 34,
            isRefreshing: false);
    }

    public event Action<QuotaRefreshState>? StateChanged;

    public QuotaRefreshState CurrentState { get; private set; }

    public void Publish(
        PreviewDisplayChoice choice,
        double fiveHourPercent,
        double weeklyPercent,
        bool isRefreshing)
    {
        CurrentState = CreateState(
            choice,
            fiveHourPercent,
            weeklyPercent,
            isRefreshing);
        StateChanged?.Invoke(CurrentState);
    }

    public Task RefreshNowAsync(
        bool onlyIfStale,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StateChanged?.Invoke(CurrentState);
        return Task.CompletedTask;
    }

    private static QuotaRefreshState CreateState(
        PreviewDisplayChoice choice,
        double fiveHourPercent,
        double weeklyPercent,
        bool isRefreshing)
    {
        var fiveHour = choice is
            PreviewDisplayChoice.Dual or PreviewDisplayChoice.FiveHourOnly
            ? new QuotaWindow(
                QuotaWindowKind.FiveHour,
                Math.Clamp(fiveHourPercent, 0, 100),
                FetchedAt.AddHours(5))
            : null;
        var weekly = choice is
            PreviewDisplayChoice.Dual or PreviewDisplayChoice.WeeklyOnly
            ? new QuotaWindow(
                QuotaWindowKind.Weekly,
                Math.Clamp(weeklyPercent, 0, 100),
                FetchedAt.AddDays(7))
            : null;
        var snapshot = new QuotaSnapshot(fiveHour, weekly, FetchedAt);

        return new QuotaRefreshState(
            IsCodexRunning: true,
            IsRefreshing: isRefreshing,
            Display: QuotaDisplayState.FromSnapshot(snapshot),
            LastError: null);
    }
}
