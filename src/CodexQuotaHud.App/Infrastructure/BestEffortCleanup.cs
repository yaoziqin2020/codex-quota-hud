namespace CodexQuotaHud.App.Infrastructure;

internal static class BestEffortCleanup
{
    public static async Task RunAsync(
        params Func<ValueTask>[] cleanupActions)
    {
        List<Exception>? failures = null;
        foreach (var cleanup in cleanupActions)
        {
            try
            {
                await cleanup();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException(
                "One or more application resources failed to shut down.",
                failures);
        }
    }
}
