using System.IO;
using System.Security;
using Microsoft.Win32;

namespace CodexQuotaHud.App.Infrastructure;

public sealed class StartupRegistration
{
    public const string RunSubKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "CodexQuotaHud";

    private readonly string _executablePath;
    private readonly IRegistryStore _registry;

    public StartupRegistration()
        : this(
            Environment.ProcessPath ??
            throw new InvalidOperationException("The current executable path is unavailable."))
    {
    }

    public StartupRegistration(string executablePath)
        : this(executablePath, new WindowsRegistryStore())
    {
    }

    internal StartupRegistration(string executablePath, IRegistryStore registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        if (executablePath.Contains('"'))
        {
            throw new ArgumentException(
                "The executable path cannot contain a quote.",
                nameof(executablePath));
        }

        _executablePath = executablePath;
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public void Enable()
    {
        _registry.SetCurrentUserString(
            RunSubKeyPath,
            ValueName,
            $"\"{_executablePath}\" --background");
    }

    public bool TryEnable(out string? error)
    {
        try
        {
            Enable();
            error = null;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            error = exception.Message;
            return false;
        }
    }

    public void Disable()
    {
        _registry.DeleteCurrentUserValue(RunSubKeyPath, ValueName);
    }
}

internal interface IRegistryStore
{
    void SetCurrentUserString(string subKeyPath, string valueName, string value);

    void DeleteCurrentUserValue(string subKeyPath, string valueName);
}

internal sealed class WindowsRegistryStore : IRegistryStore
{
    public void SetCurrentUserString(string subKeyPath, string valueName, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(subKeyPath, writable: true);
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteCurrentUserValue(string subKeyPath, string valueName)
    {
        using var key = Registry.CurrentUser.OpenSubKey(subKeyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}
