using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Packaging;

public sealed record SkinPackageBuildRequest(
    SkinManifest Manifest,
    SkinTheme Theme,
    IReadOnlyDictionary<SkinAssetSlot, SkinAsset> Assets);
