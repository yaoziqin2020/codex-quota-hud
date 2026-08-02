using System.Windows.Media;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.App.Tests.UI;

[Collection(WpfUiCollection.Name)]
public sealed class HudSkinCatalogTests
{
    [Fact]
    public void Load_PutsStableBuiltInsBeforeHealthyCustomSkinsAndSeparatesCorruptEntries()
    {
        var alphaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var zuluId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var corruptId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var catalog = new HudSkinCatalog(new InstalledSkinCatalogResult(
            [
                Installed(zuluId, "Zulu"),
                Installed(alphaId, "Alpha")
            ],
            [new CorruptInstalledSkin(
                @"C:\Catalog\33333333-3333-3333-3333-333333333333",
                corruptId,
                [new SkinValidationError("installed.package.corrupt", "$", "bad")])]
        ));

        var snapshot = catalog.Load();

        Assert.Equal(
            [
                "builtin:HudDial",
                "builtin:EnergyRing",
                "builtin:LiquidGlass",
                "builtin:Aurora",
                "builtin:LiquidTank",
                "custom:11111111-1111-1111-1111-111111111111",
                "custom:22222222-2222-2222-2222-222222222222"
            ],
            snapshot.Healthy.Select(item => item.SelectionKey));
        Assert.Equal(
            Enum.GetValues<SkinId>().Select(id => (SkinId?)id),
            snapshot.Healthy.Take(5).Select(item => item.BuiltInId));
        Assert.Equal(
            ["HUD 科技仪表", "双彩能量环", "流体玻璃球", "克制极光", "液位储能舱"],
            snapshot.Healthy.Take(5).Select(item => item.DisplayName));
        Assert.All(snapshot.Healthy.Take(5), item => Assert.False(item.CanRemove));
        Assert.All(snapshot.Healthy.Skip(5), item => Assert.True(item.CanRemove));
        Assert.Single(snapshot.Corrupt);
        Assert.False(catalog.TryGet($"custom:{corruptId:D}", out _));
    }

    [Fact]
    public void Load_SortsEqualDisplayNamesByGuidAndReturnsOneImmutableGeneration()
    {
        var later = Installed(
            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
            "Same");
        var earlier = Installed(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "same");
        var source = new InstalledSkinCatalogResult([later, earlier], []);
        var catalog = new HudSkinCatalog(source);

        var first = catalog.Load();
        var second = catalog.Load();

        Assert.Same(first, second);
        Assert.Equal(
            [earlier.SelectionKey, later.SelectionKey],
            first.Healthy.Skip(5).Select(item => item.SelectionKey));
    }

    [Fact]
    public void CustomPresentation_DerivesEverySurfaceFromValidatedTheme()
    {
        var presentation = SkinPresentation.ForCustom(Theme());

        Assert.Equal(Color.FromArgb(0x66, 0x11, 0x22, 0x33), Solid(presentation.Popup.Background));
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), Solid(presentation.Popup.Border));
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), Solid(presentation.Popup.Accent));
        Assert.Equal(Color.FromRgb(0xAB, 0xCD, 0xEF), presentation.Popup.ShadowColor);
        Assert.Equal(PopupDecorationKind.Custom, presentation.Popup.Decoration);

        Assert.Equal(Color.FromArgb(0x66, 0x11, 0x22, 0x33), Solid(presentation.Edge.Track));
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), Solid(presentation.Edge.Border));
        var fill = Assert.IsType<LinearGradientBrush>(presentation.Edge.Fill);
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), fill.GradientStops[0].Color);
        Assert.Equal(Color.FromRgb(0x65, 0x43, 0x21), fill.GradientStops[^1].Color);
        Assert.Equal(EdgeProgressMaterialKind.TechHighlight, presentation.Edge.Material);
        Assert.Equal(Color.FromRgb(0x12, 0x34, 0x56), presentation.Edge.AccentColor);
        Assert.Equal(Color.FromRgb(0xAB, 0xCD, 0xEF), presentation.Edge.GlowColor);
        Assert.Equal(System.Drawing.Color.FromArgb(0x12, 0x34, 0x56), presentation.TrayAccent);
    }

    private static InstalledSkinRecord Installed(Guid id, string displayName)
    {
        var package = Document(id, displayName);
        return new InstalledSkinRecord(
            $"custom:{id:D}",
            id,
            displayName,
            SemanticVersion.Parse("1.0.0"),
            Path.Combine(@"C:\Catalog", id.ToString("D")),
            package);
    }

    internal static SkinPackageDocument Document(
        Guid? id = null,
        string displayName = "Ocean") =>
        new(
            new SkinManifest(
                1,
                id ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
                displayName,
                "Alice",
                SemanticVersion.Parse("1.0.0"),
                "Custom ring",
                SkinPackageLimits.FreeDecorationRingTemplateId,
                SemanticVersion.Parse("1.1.1"),
                null,
                []),
            Theme(),
            new Dictionary<SkinAssetSlot, SkinAsset>());

    internal static SkinTheme Theme() =>
        new(
            1,
            SkinPackageLimits.FreeDecorationRingTemplateId,
            IdentityTransform(),
            IdentityTransform(),
            IdentityTransform(),
            "#FF123456",
            "#FF654321",
            "#FF112233",
            0.4,
            96,
            8,
            6,
            270,
            "#FFABCDEF",
            0.5,
            28,
            12,
            SkinTextWeight.SemiBold,
            SkinTextPlacement.NumberAboveLabel,
            new SkinAnimationSettings(0.25, 0.5, 0.75, 1));

    private static SkinImageTransform IdentityTransform() =>
        new(0, 0, 1, 0, 1, 0.5, 0.5);

    private static Color Solid(Brush brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color;
}
