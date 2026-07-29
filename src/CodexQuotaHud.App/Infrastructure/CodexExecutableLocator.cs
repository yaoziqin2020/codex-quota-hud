using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace CodexQuotaHud.App.Infrastructure;

public sealed class CodexExecutableLocator
{
    public const string OverrideEnvironmentVariable = "CODEX_QUOTA_HUD_CODEX_PATH";

    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<IReadOnlyList<string>> _getRunningCodexExecutablePaths;
    private readonly Func<IReadOnlyList<string>> _findOnPath;
    private readonly Func<string?> _getLocalAppData;
    private readonly Func<string, bool> _fileExists;

    public CodexExecutableLocator()
        : this(
            Environment.GetEnvironmentVariable,
            CodexProcessMonitor.FindRunningCodexExecutablePaths,
            FindCodexOnPath,
            () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            File.Exists)
    {
    }

    internal CodexExecutableLocator(
        Func<string, string?> getEnvironmentVariable,
        Func<IReadOnlyList<string>> getRunningCodexExecutablePaths,
        Func<IReadOnlyList<string>> findOnPath,
        Func<string?> getLocalAppData,
        Func<string, bool> fileExists)
    {
        _getEnvironmentVariable =
            getEnvironmentVariable ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
        _getRunningCodexExecutablePaths =
            getRunningCodexExecutablePaths ??
            throw new ArgumentNullException(nameof(getRunningCodexExecutablePaths));
        _findOnPath = findOnPath ?? throw new ArgumentNullException(nameof(findOnPath));
        _getLocalAppData = getLocalAppData ?? throw new ArgumentNullException(nameof(getLocalAppData));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
    }

    public string? Find()
    {
        var environmentOverride = ExistingAbsolutePath(
            _getEnvironmentVariable(OverrideEnvironmentVariable));
        if (environmentOverride is not null)
        {
            return environmentOverride;
        }

        foreach (var modulePath in _getRunningCodexExecutablePaths())
        {
            var packagedCli = FindPackagedCli(modulePath);
            if (packagedCli is not null)
            {
                return packagedCli;
            }
        }

        foreach (var pathMatch in _findOnPath())
        {
            var executable = ExistingAbsolutePath(pathMatch);
            if (executable is not null)
            {
                return executable;
            }
        }

        var localAppData = _getLocalAppData();
        if (string.IsNullOrWhiteSpace(localAppData) || !Path.IsPathFullyQualified(localAppData))
        {
            return null;
        }

        return ExistingAbsolutePath(
            Path.Combine(localAppData, "Microsoft", "WindowsApps", "codex.exe"));
    }

    private string? FindPackagedCli(string? modulePath)
    {
        if (string.IsNullOrWhiteSpace(modulePath) || !Path.IsPathFullyQualified(modulePath))
        {
            return null;
        }

        var moduleDirectory = Path.GetDirectoryName(modulePath);
        if (moduleDirectory is null)
        {
            return null;
        }

        var resourcesCli = ExistingAbsolutePath(
            Path.Combine(moduleDirectory, "resources", "codex.exe"));
        if (resourcesCli is not null)
        {
            return resourcesCli;
        }

        return string.Equals(
            Path.GetFileName(modulePath),
            "codex.exe",
            StringComparison.OrdinalIgnoreCase)
            ? ExistingAbsolutePath(modulePath)
            : null;
    }

    private string? ExistingAbsolutePath(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(candidate);
        return _fileExists(fullPath) ? fullPath : null;
    }

    private static IReadOnlyList<string> FindCodexOnPath()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "where.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("codex");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return [];
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                return [];
            }

            return output
                .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException)
        {
            return [];
        }
    }
}
