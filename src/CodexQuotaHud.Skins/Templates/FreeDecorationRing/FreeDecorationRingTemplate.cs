using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Templates.FreeDecorationRing;

public sealed class FreeDecorationRingTemplate : ISkinTemplate
{
    public string TemplateId => SkinPackageLimits.FreeDecorationRingTemplateId;

    public int SchemaVersion => SkinPackageLimits.SchemaVersion;

    public CustomSkinRenderer CreateRenderer(SkinPackageDocument package)
    {
        ArgumentNullException.ThrowIfNull(package);
        return new FreeDecorationRingRenderer(package);
    }
}
