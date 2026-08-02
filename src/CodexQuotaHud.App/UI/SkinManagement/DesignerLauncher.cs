using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;

namespace CodexQuotaHud.App.UI.SkinManagement;

public sealed class DesignerLauncher
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<ProcessStartInfo, bool> _start;

    public DesignerLauncher(string appDirectory)
        : this(
            appDirectory,
            File.Exists,
            startInfo => Process.Start(startInfo) is not null)
    {
    }

    internal DesignerLauncher(
        string appDirectory,
        Func<string, bool> fileExists,
        Func<ProcessStartInfo, bool> start)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appDirectory);
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _start = start ?? throw new ArgumentNullException(nameof(start));
        ExpectedExecutablePath = Path.Combine(
            Path.GetFullPath(appDirectory),
            "designer",
            "CodexQuotaHud.SkinDesigner.exe");
    }

    public string ExpectedExecutablePath { get; }

    public bool IsAvailable
    {
        get
        {
            try
            {
                return _fileExists(ExpectedExecutablePath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or SecurityException)
            {
                return false;
            }
        }
    }

    public bool TryLaunch(out string? error)
    {
        error = null;
        if (!IsAvailable)
        {
            error = "Skin Designer is not installed at the expected application location.";
            return false;
        }

        var startInfo = new ProcessStartInfo(ExpectedExecutablePath)
        {
            UseShellExecute = true,
            Arguments = string.Empty,
            WorkingDirectory = string.Empty
        };
        try
        {
            if (_start(startInfo))
            {
                return true;
            }

            error = "Skin Designer did not start. Reinstall the optional component and try again.";
            return false;
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            IOException or
            UnauthorizedAccessException or
            InvalidOperationException or
            SecurityException)
        {
            error = $"Skin Designer could not be started: {exception.Message}";
            return false;
        }
    }
}
