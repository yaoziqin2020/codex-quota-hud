namespace CodexQuotaHud.App.UI.Animation;

public interface IAnimationDelay
{
    Task Delay(
        TimeSpan duration,
        CancellationToken cancellationToken);
}

internal sealed class SystemAnimationDelay : IAnimationDelay
{
    public Task Delay(
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        Task.Delay(duration, cancellationToken);
}
