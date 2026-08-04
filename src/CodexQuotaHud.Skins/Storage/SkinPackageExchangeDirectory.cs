using System.IO;

namespace CodexQuotaHud.Skins.Storage;

public static class SkinPackageExchangeDirectory
{
    public const string FolderName = "Codex Quota HUD Skins";

    public static string DefaultPath
    {
        get
        {
            var documents = Environment.GetFolderPath(
                Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
            {
                documents = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
            }

            return Path.GetFullPath(Path.Combine(documents, FolderName));
        }
    }

    public static string EnsureExists()
    {
        var path = DefaultPath;
        Directory.CreateDirectory(path);
        return path;
    }

    public static string SuggestedExportPath(string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);
        var fileName = Path.GetFileName(suggestedFileName);
        if (!string.Equals(fileName, suggestedFileName, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The suggested package name must not contain a directory.",
                nameof(suggestedFileName));
        }

        return Path.Combine(EnsureExists(), fileName);
    }
}
