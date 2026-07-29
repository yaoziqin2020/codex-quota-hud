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
    private readonly Func<string, IReadOnlyList<string>> _findUserLocalInstalls;

    public CodexExecutableLocator()
        : this(
            Environment.GetEnvironmentVariable,
            CodexProcessMonitor.FindRunningCodexExecutablePaths,
            FindCodexOnPath,
            () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            File.Exists,
            FindUserLocalInstalls)
    {
    }

    internal CodexExecutableLocator(
        Func<string, string?> getEnvironmentVariable,
        Func<IReadOnlyList<string>> getRunningCodexExecutablePaths,
        Func<IReadOnlyList<string>> findOnPath,
        Func<string?> getLocalAppData,
        Func<string, bool> fileExists,
        Func<string, IReadOnlyList<string>> findUserLocalInstalls)
    {
        _getEnvironmentVariable =
            getEnvironmentVariable ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
        _getRunningCodexExecutablePaths =
            getRunningCodexExecutablePaths ??
            throw new ArgumentNullException(nameof(getRunningCodexExecutablePaths));
        _findOnPath = findOnPath ?? throw new ArgumentNullException(nameof(findOnPath));
        _getLocalAppData = getLocalAppData ?? throw new ArgumentNullException(nameof(getLocalAppData));
        _fileExists = fileExists ?? throw new ArgumentNullException(nameof(fileExists));
        _findUserLocalInstalls =
            findUserLocalInstalls ??
            throw new ArgumentNullException(nameof(findUserLocalInstalls));
    }

    public string? Find()
    {
        var environmentOverride = ExistingAbsolutePath(
            _getEnvironmentVariable(OverrideEnvironmentVariable));
        if (environmentOverride is not null)
        {
            return environmentOverride;
        }

        var localAppData = _getLocalAppData();
        if (!string.IsNullOrWhiteSpace(localAppData) &&
            Path.IsPathFullyQualified(localAppData))
        {
            foreach (var candidate in _findUserLocalInstalls(localAppData))
            {
                var executable = ExistingAbsolutePath(candidate);
                if (executable is not null)
                {
                    return executable;
                }
            }
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

    private static IReadOnlyList<string> FindUserLocalInstalls(string localAppData)
    {
        var binDirectory = Path.Combine(
            Path.GetFullPath(localAppData),
            "OpenAI",
            "Codex",
            "bin");
        try
        {
            if (!Directory.Exists(binDirectory) ||
                (File.GetAttributes(binDirectory) & FileAttributes.ReparsePoint) != 0)
            {
                return [];
            }

            var candidates = new List<(string Path, DateTime LastWriteTimeUtc)>();
            AddCandidate(Path.Combine(binDirectory, "codex.exe"), candidates);
            foreach (var directory in Directory.EnumerateDirectories(
                binDirectory,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                {
                    continue;
                }

                AddCandidate(Path.Combine(directory, "codex.exe"), candidates);
            }

            return candidates
                .OrderByDescending(static candidate => candidate.LastWriteTimeUtc)
                .ThenBy(static candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
                .Select(static candidate => candidate.Path)
                .ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void AddCandidate(
        string path,
        ICollection<(string Path, DateTime LastWriteTimeUtc)> candidates)
    {
        if (File.Exists(path))
        {
            candidates.Add((path, File.GetLastWriteTimeUtc(path)));
        }
    }
}
