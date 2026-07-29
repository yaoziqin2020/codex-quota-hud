using CodexQuotaHud.App.Infrastructure;

namespace CodexQuotaHud.App.Tests.Infrastructure;

public sealed class CodexExecutableLocatorTests
{
    [Fact]
    public void LocatorPrefersExplicitEnvironmentOverride()
    {
        const string overridePath = @"C:\Portable Codex\codex.exe";
        var locator = CreateLocator(
            environmentOverride: overridePath,
            runningModulePaths: [@"C:\Desktop\Codex.exe"],
            pathMatches: [@"C:\Path\codex.exe"],
            existingPaths: [overridePath, @"C:\Desktop\resources\codex.exe", @"C:\Path\codex.exe"]);

        Assert.Equal(overridePath, locator.Find());
    }

    [Fact]
    public void LocatorPrefersUserLocalCliOverInaccessiblePackagedDesktopCli()
    {
        const string localAppData = @"C:\Users\test\AppData\Local";
        const string localCli =
            localAppData + @"\OpenAI\Codex\bin\current\codex.exe";
        const string desktopModule =
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.721.4979.0_x64__test\app\ChatGPT.exe";
        const string packagedCli =
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.721.4979.0_x64__test\app\resources\codex.exe";

        var locator = CreateLocator(
            environmentOverride: null,
            runningModulePaths: [desktopModule],
            pathMatches: [packagedCli],
            existingPaths: [localCli, packagedCli],
            localAppData: localAppData,
            userLocalMatches: [localCli]);

        Assert.Equal(localCli, locator.Find());
    }

    [Fact]
    public void LocatorFallsBackToRunningCodexModuleThenPath()
    {
        const string desktopModule =
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.721.4979.0_x64__test\app\Codex.exe";
        const string packagedCli =
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.721.4979.0_x64__test\app\resources\codex.exe";
        const string pathCli = @"C:\Tools\codex.exe";

        var moduleLocator = CreateLocator(
            environmentOverride: null,
            runningModulePaths: [desktopModule],
            pathMatches: [pathCli],
            existingPaths: [packagedCli, pathCli]);
        var pathLocator = CreateLocator(
            environmentOverride: null,
            runningModulePaths: [],
            pathMatches: [pathCli],
            existingPaths: [pathCli]);

        Assert.Equal(packagedCli, moduleLocator.Find());
        Assert.Equal(pathCli, pathLocator.Find());
    }

    [Fact]
    public void LocatorUsesWindowsAppsAliasAsLastFallback()
    {
        const string localAppData = @"C:\Users\test\AppData\Local";
        const string alias = localAppData + @"\Microsoft\WindowsApps\codex.exe";
        var locator = CreateLocator(
            environmentOverride: null,
            runningModulePaths: [],
            pathMatches: [],
            existingPaths: [alias],
            localAppData: localAppData);

        Assert.Equal(alias, locator.Find());
    }

    [Fact]
    public void LocatorReturnsNullWhenNoAbsoluteCandidateExists()
    {
        var locator = CreateLocator(
            environmentOverride: @"relative\codex.exe",
            runningModulePaths: [@"relative\Codex.exe"],
            pathMatches: [@"relative\codex.exe"],
            existingPaths: [@"relative\codex.exe"],
            localAppData: null);

        Assert.Null(locator.Find());
    }

    private static CodexExecutableLocator CreateLocator(
        string? environmentOverride,
        IReadOnlyList<string> runningModulePaths,
        IReadOnlyList<string> pathMatches,
        IReadOnlyList<string> existingPaths,
        string? localAppData = @"C:\Users\test\AppData\Local",
        IReadOnlyList<string>? userLocalMatches = null)
    {
        var existing = existingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new CodexExecutableLocator(
            getEnvironmentVariable: name =>
                name == CodexExecutableLocator.OverrideEnvironmentVariable
                    ? environmentOverride
                    : null,
            getRunningCodexExecutablePaths: () => runningModulePaths,
            findOnPath: () => pathMatches,
            getLocalAppData: () => localAppData,
            fileExists: existing.Contains,
            findUserLocalInstalls: _ => userLocalMatches ?? []);
    }
}
