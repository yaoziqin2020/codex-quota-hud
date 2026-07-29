namespace CodexQuotaHud.App.UI;

internal sealed class OrbClickController(
    Func<Task> doubleClickDelayAsync,
    Action toggleDetails,
    Action refresh)
{
    private int _generation;

    public async Task<bool> HandleClickAsync(int clickCount)
    {
        var generation = Interlocked.Increment(ref _generation);
        if (clickCount >= 2)
        {
            refresh();
            return false;
        }

        await doubleClickDelayAsync();
        if (generation != Volatile.Read(ref _generation))
        {
            return false;
        }

        toggleDetails();
        return true;
    }

    public void CancelPendingSingleClick() =>
        Interlocked.Increment(ref _generation);
}
