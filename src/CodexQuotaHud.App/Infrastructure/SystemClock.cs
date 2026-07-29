using CodexQuotaHud.Core.Refresh;

namespace CodexQuotaHud.App.Infrastructure;

internal sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(
        TimeSpan delay,
        CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}
