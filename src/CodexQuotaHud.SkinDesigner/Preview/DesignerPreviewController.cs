using CodexQuotaHud.App.Preview;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Preview;

public sealed class DesignerPreviewController
{
    private readonly SyntheticPreviewComposition _composition;
    private SkinPackageDocument? _lastValidPackage;
    private bool _guidesVisible;

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
        if (!built.IsValid)
        {
            return built;
        }

        var rendered = _composition.SetCustomPackage(built.Value!);
        if (rendered.IsValid)
        {
            _lastValidPackage = built.Value;
            _composition.SetDesignerGuides(
                _lastValidPackage!.Theme,
                _guidesVisible);
        }

        return rendered;
    }

    public void SetGuidesVisible(bool value)
    {
        _guidesVisible = value;
        _composition.SetDesignerGuides(
            _lastValidPackage?.Theme,
            value);
    }
}
