using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CodexQuotaHud.App.Infrastructure;

internal sealed class InstalledAppLauncher
{
    private readonly Func<string, bool> _fileExists;
    private readonly Func<ProcessStartInfo, bool> _startProcess;

    public InstalledAppLauncher()
        : this(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            File.Exists,
            info => Process.Start(info) is not null)
    {
    }

    internal InstalledAppLauncher(
        string localAppData,
        Func<string, bool> fileExists,
        Func<ProcessStartInfo, bool> startProcess)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);
        _fileExists = fileExists ?? throw new ArgumentNullException(
            nameof(fileExists));
        _startProcess = startProcess ?? throw new ArgumentNullException(
            nameof(startProcess));
        ExecutablePath = Path.GetFullPath(Path.Combine(
            localAppData,
            "Programs",
            "CodexQuotaHud",
            "CodexQuotaHud.App.exe"));
    }

    public string ExecutablePath { get; }

    public bool IsAvailable => _fileExists(ExecutablePath);

    public bool TryLaunch(out string? error)
    {
        if (!IsAvailable)
        {
            error = "未找到已安装正式版";
            return false;
        }

        try
        {
            var started = _startProcess(new ProcessStartInfo(ExecutablePath)
            {
                UseShellExecute = true
            });
            error = started ? null : "正式版启动失败";
            return started;
        }
        catch (Exception exception) when (
            exception is Win32Exception or
            InvalidOperationException or
            IOException)
        {
            error = exception.Message;
            return false;
        }
    }
}
