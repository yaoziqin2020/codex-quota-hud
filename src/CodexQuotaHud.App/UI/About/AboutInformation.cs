namespace CodexQuotaHud.App.UI.About;

internal sealed record AboutInformation(
    string ProductName,
    string VersionText,
    string Author,
    string RepositoryLabel,
    string RepositoryUrl,
    string LicenseName)
{
    internal static AboutInformation Current { get; } = new(
        "Codex Quota HUD",
        FormatVersion(typeof(AboutInformation).Assembly.GetName().Version),
        "老姚",
        "yaoziqin2020/codex-quota-hud",
        "https://github.com/yaoziqin2020/codex-quota-hud",
        "MIT License");

    internal static string FormatVersion(Version? version) =>
        version is null || version.Build < 0
            ? "未知"
            : $"{version.Major}.{version.Minor}.{version.Build}";
}
