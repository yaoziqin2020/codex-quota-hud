using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Templates;

public interface ISkinTemplate
{
    string TemplateId { get; }

    int SchemaVersion { get; }

    CustomSkinRenderer CreateRenderer(SkinPackageDocument package);
}
