using System.IO;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public sealed class DraftProjectPaths
{
    public DraftProjectPaths(string draftsRoot, Guid draftId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftsRoot);
        if (draftId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(
                nameof(draftId),
                "A draft project requires a non-empty ID.");
        }

        var normalizedRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(draftsRoot));
        var volumeRoot = Path.TrimEndingDirectorySeparator(
            Path.GetPathRoot(normalizedRoot) ?? string.Empty);
        if (string.Equals(
                normalizedRoot,
                volumeRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The drafts root must not be a filesystem root.",
                nameof(draftsRoot));
        }

        ProjectRoot = Path.Combine(
            normalizedRoot,
            draftId.ToString("D").ToLowerInvariant());
        NamedDraftPath = Path.Combine(ProjectRoot, "draft.json");
        RecoveryPath = Path.Combine(ProjectRoot, "recovery.json");
        AssetsRoot = Path.Combine(ProjectRoot, "assets");
    }

    public string ProjectRoot { get; }

    public string NamedDraftPath { get; }

    public string RecoveryPath { get; }

    public string AssetsRoot { get; }
}
