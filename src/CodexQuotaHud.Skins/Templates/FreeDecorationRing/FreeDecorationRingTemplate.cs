using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Templates.FreeDecorationRing;

public sealed class FreeDecorationRingTemplate : ISkinTemplate
{
    private static readonly SemanticVersion MinimumSupportedHudVersion =
        SemanticVersion.Parse("1.2.0");

    public string TemplateId => SkinPackageLimits.FreeDecorationRingTemplateId;

    public int SchemaVersion => SkinPackageLimits.SchemaVersion;

    public SemanticVersion MinimumHudVersion => MinimumSupportedHudVersion;

    public CustomSkinRenderer CreateRenderer(SkinPackageDocument package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new FreeDecorationRingRenderer(package);
    }
}
