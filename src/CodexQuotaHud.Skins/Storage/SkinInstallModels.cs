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

public sealed record SkinInstallPreview(
    SkinPackageDocument Package,
    InstalledSkinRecord? Existing,
    bool IsDowngrade,
    IReadOnlyList<SkinCollisionDecision> AllowedDecisions);

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
