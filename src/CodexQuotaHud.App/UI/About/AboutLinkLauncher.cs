using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CodexQuotaHud.App.UI.About;

internal static class AboutLinkLauncher
{
    private const string FailureMessage = "无法打开项目主页。";

    internal static bool TryOpen(string url, out string? error) =>
        TryOpen(url, info => Process.Start(info) is not null, out error);

    internal static bool TryOpen(
        string url,
        Func<ProcessStartInfo, bool> start,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps,
                StringComparison.OrdinalIgnoreCase))
        {
            error = FailureMessage;
            return false;
        }

        try
        {
            var started = start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            error = started ? null : FailureMessage;
            return started;
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or IOException)
        {
            error = FailureMessage;
            return false;
        }
    }
}
