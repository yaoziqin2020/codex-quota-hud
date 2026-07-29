using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class StartupRegistrationTests
{
    [Fact]
    public void Enable_WritesQuotedExecutableAndBackgroundArgumentToCurrentUserRun()
    {
        var registry = new MemoryRegistryStore();
        var registration = new StartupRegistration(
            @"C:\Program Files\Codex Quota HUD\CodexQuotaHud.App.exe",
            registry);

        registration.Enable();

        Assert.Equal(
            "\"C:\\Program Files\\Codex Quota HUD\\CodexQuotaHud.App.exe\" --background",
            registry.Values[(StartupRegistration.RunSubKeyPath, StartupRegistration.ValueName)]);
    }

    [Fact]
    public void Disable_RemovesOnlyTheCodexQuotaHudRunValue()
    {
        var registry = new MemoryRegistryStore();
        registry.Values[(StartupRegistration.RunSubKeyPath, "AnotherApp")] = "keep-me";
        registry.Values[(StartupRegistration.RunSubKeyPath, StartupRegistration.ValueName)] =
            "remove-me";
        var registration = new StartupRegistration(@"C:\CodexQuotaHud.App.exe", registry);

        registration.Disable();

        Assert.False(
            registry.Values.ContainsKey(
                (StartupRegistration.RunSubKeyPath, StartupRegistration.ValueName)));
        Assert.Equal(
            "keep-me",
            registry.Values[(StartupRegistration.RunSubKeyPath, "AnotherApp")]);
    }

    [Fact]
    public void TryEnable_RegistryPermissionFailureIsNonFatal()
    {
        var registration = new StartupRegistration(
            @"C:\CodexQuotaHud.App.exe",
            new ThrowingRegistryStore());

        var exception = Record.Exception(
            () => Assert.False(registration.TryEnable(out var error)));

        Assert.Null(exception);
    }

    private sealed class MemoryRegistryStore : IRegistryStore
    {
        public Dictionary<(string SubKeyPath, string ValueName), string> Values { get; } = [];

        public void SetCurrentUserString(string subKeyPath, string valueName, string value)
        {
            Values[(subKeyPath, valueName)] = value;
        }

        public void DeleteCurrentUserValue(string subKeyPath, string valueName)
        {
            Values.Remove((subKeyPath, valueName));
        }
    }

    private sealed class ThrowingRegistryStore : IRegistryStore
    {
        public void SetCurrentUserString(string subKeyPath, string valueName, string value) =>
            throw new UnauthorizedAccessException("policy denied");

        public void DeleteCurrentUserValue(string subKeyPath, string valueName)
        {
        }
    }
}
