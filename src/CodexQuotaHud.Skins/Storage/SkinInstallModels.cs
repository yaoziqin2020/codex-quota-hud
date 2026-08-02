using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Storage;

public enum SkinCollisionDecision
{
    Replace,
    KeepCopy,
    Cancel
}

public enum SkinInstallDisposition
{
    Installed,
    Replaced,
    KeptCopy,
    Cancelled
}

public sealed class SkinInstallPreview
{
    internal SkinInstallPreview(
        SkinPackageDocument package,
        InstalledSkinRecord? existing,
        bool isDowngrade,
        IReadOnlyList<SkinCollisionDecision> allowedDecisions)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(allowedDecisions);
        Package = package;
        Existing = existing;
        IsDowngrade = isDowngrade;
        AllowedDecisions = allowedDecisions;
    }

    public SkinPackageDocument Package { get; }

    public InstalledSkinRecord? Existing { get; }

    public bool IsDowngrade { get; }

    public IReadOnlyList<SkinCollisionDecision> AllowedDecisions { get; }
}

public sealed record SkinInstallResult(
    SkinInstallDisposition Disposition,
    InstalledSkinRecord? Installed,
    IReadOnlyList<SkinValidationError> Errors);

public sealed record InstalledSkinRecord(
    string SelectionKey,
    Guid SkinId,
    string DisplayName,
    SemanticVersion PackageVersion,
    string DirectoryPath,
    SkinPackageDocument Package);

public sealed record CorruptInstalledSkin(
    string DirectoryPath,
    Guid? SkinId,
    IReadOnlyList<SkinValidationError> Errors);

public sealed record InstalledSkinCatalogResult(
    IReadOnlyList<InstalledSkinRecord> Installed,
    IReadOnlyList<CorruptInstalledSkin> Corrupt);
