using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public sealed record DraftAssetReference(
    SkinAssetSlot Slot,
    string RelativePath,
    string OriginalFileName,
    string? StorageRelativePath = null);

public sealed record SkinDraftDocument(
    int DraftSchemaVersion,
    Guid DraftId,
    Guid SkinId,
    long Revision,
    string ProjectName,
    string DisplayName,
    string Author,
    SemanticVersion PackageVersion,
    string Description,
    SemanticVersion MinimumHudVersion,
    Guid? OriginSkinId,
    SkinTheme Theme,
    IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference> Assets,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
