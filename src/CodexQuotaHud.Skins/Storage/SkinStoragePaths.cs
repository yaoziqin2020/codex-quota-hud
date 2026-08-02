using System.IO;

namespace CodexQuotaHud.Skins.Storage;

public sealed class SkinStoragePaths
{
    public SkinStoragePaths(string localAppDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppDataRoot);

        var normalizedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(localAppDataRoot));
        SettingsRoot = Path.Combine(normalizedRoot, "CodexQuotaHud");
        InstalledSkinsRoot = Path.Combine(SettingsRoot, "skins");
        DraftsRoot = Path.Combine(
            SettingsRoot,
            "designer",
            "drafts");
        ImportsRoot = Path.Combine(SettingsRoot, "imports");
    }

    public string SettingsRoot { get; }

    public string InstalledSkinsRoot { get; }

    public string DraftsRoot { get; }

    public string ImportsRoot { get; }
}
