using CodexQuotaHud.Skins.Templates;
using CodexQuotaHud.Skins.Templates.FreeDecorationRing;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Tests.Templates;

public sealed class SkinTemplateRegistryTests
{
    [Fact]
    public void CreateDefault_ResolvesOnlyTheSupportedTemplateVersion()
    {
        var registry = SkinTemplateRegistry.CreateDefault();

        Assert.True(registry.TryResolve("free-decoration-ring", 1, out var template));
        Assert.IsType<FreeDecorationRingTemplate>(template);
        Assert.Equal(
            SemanticVersion.Parse("1.3.0"),
            template.MinimumHudVersion);
        Assert.False(registry.TryResolve("unknown", 1, out _));
        Assert.False(registry.TryResolve("free-decoration-ring", 2, out _));
        Assert.Equal(
            new[] { (TemplateId: "free-decoration-ring", SchemaVersion: 1) },
            registry.RegisteredKeys);
    }
}
