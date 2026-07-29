namespace CodexQuotaHud.App.UI;

internal sealed class HoverCloseController(
    Func<Task> delayAsync,
    Action close)
{
    private int _generation;

    public void CancelPendingClose() =>
        Interlocked.Increment(ref _generation);

    public async Task<bool> ScheduleCloseAsync()
    {
        var generation = Interlocked.Increment(ref _generation);
        await delayAsync();
        if (generation != Volatile.Read(ref _generation))
        {
            return false;
        }

        close();
        return true;
    }
}
