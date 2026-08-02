using CodexQuotaHud.App.Preview;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Preview;

public sealed class DesignerPreviewController
{
    private readonly SyntheticPreviewComposition _composition;

    public DesignerPreviewController(SyntheticPreviewComposition composition)
    {
        _composition = composition ?? throw new ArgumentNullException(
            nameof(composition));
    }

    public SkinValidationResult<SkinPackageDocument> Update(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets)
    {
        var built = DraftPreviewDocumentBuilder.Build(draft, assets);
        return built.IsValid
            ? _composition.SetCustomPackage(built.Value!)
            : built;
    }
}
