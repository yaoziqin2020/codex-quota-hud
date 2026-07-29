namespace CodexQuotaHud.App.Infrastructure;

public interface ICodexProcessMonitor
{
    bool IsRunning { get; }

    event Action<bool>? RunningChanged;
}
