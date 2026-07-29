using System.IO;

namespace CodexQuotaHud.App.Infrastructure;

public interface IAppServerProcess
{
    TextWriter StandardInput { get; }
    TextReader StandardOutput { get; }
    TextReader StandardError { get; }
    bool HasExited { get; }

    Task KillAsync();
}
