using CodexQuotaHud.App.UI.Animation;

namespace CodexQuotaHud.App.Tests.UI;

internal sealed class FakeAnimationDelay : IAnimationDelay
{
    public List<PendingAnimationDelay> Requests { get; } = [];

    public Task Delay(
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        var request = new PendingAnimationDelay(
            duration,
            cancellationToken);
        Requests.Add(request);
        return request.Task;
    }
}

internal sealed class PendingAnimationDelay(
    TimeSpan duration,
    CancellationToken cancellationToken)
{
    private readonly TaskCompletionSource _completion = new();

    public TimeSpan Duration { get; } = duration;

    public bool IsCancellationRequested =>
        cancellationToken.IsCancellationRequested;

    public Task Task => _completion.Task;

    public void Complete() => _completion.TrySetResult();
}
