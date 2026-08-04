using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.SkinDesigner.Tests.Drafts;

public sealed class SkinDraftFactoryTests
{
    private static readonly Guid DraftId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SkinId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly DateTimeOffset NowUtc =
        DateTimeOffset.Parse("2026-08-02T00:00:00Z");
    private static readonly SemanticVersion MinimumHudVersion =
        SemanticVersion.Parse("1.1.1");

    [Fact]
    public void CreateNew_UsesOnlySuppliedIdentityAndExactDraftDefaults()
    {
        var draft = SkinDraftFactory.CreateNew(
            DraftId,
            SkinId,
            NowUtc,
            MinimumHudVersion);

        Assert.Equal(1, draft.DraftSchemaVersion);
        Assert.Equal(DraftId, draft.DraftId);
        Assert.Equal(SkinId, draft.SkinId);
        Assert.Equal(0, draft.Revision);
        Assert.Equal("未命名皮肤", draft.ProjectName);
        Assert.Equal("未命名皮肤", draft.DisplayName);
        Assert.Equal(string.Empty, draft.Author);
        Assert.Equal(SemanticVersion.Parse("1.0.0"), draft.PackageVersion);
        Assert.Equal(string.Empty, draft.Description);
        Assert.Equal(MinimumHudVersion, draft.MinimumHudVersion);
        Assert.Null(draft.OriginSkinId);
        Assert.Empty(draft.Assets);
        Assert.Equal(NowUtc, draft.CreatedAtUtc);
        Assert.Equal(NowUtc, draft.UpdatedAtUtc);
    }

    [Fact]
    public void CreateNew_UsesExactFreeDecorationRingThemeDefaults()
    {
        var draft = SkinDraftFactory.CreateNew(
            DraftId,
            SkinId,
            NowUtc,
            MinimumHudVersion);
        var identity = new SkinImageTransform(
            OffsetX: 0,
            OffsetY: 0,
            Scale: 1,
            Rotation: 0,
            Opacity: 1,
            CropFocusX: 0.5,
            CropFocusY: 0.5);
        var expected = new SkinTheme(
            SchemaVersion: 1,
            TemplateId: "free-decoration-ring",
            Background: identity,
            Center: identity,
            Decoration: identity,
            PrimaryRingColor: "#FF53DCF8",
            SecondaryRingColor: "#FF9A68FF",
            BaseBackgroundColor: "#FF0A1622",
            BaseBackgroundOpacity: 0.9,
            RingDiameter: 96,
            RingThickness: 8,
            RingGap: 6,
            StartAngle: 270,
            GlowColor: "#FF24CFF2",
            GlowIntensity: 0.5,
            NumberTextSize: 28,
            LabelTextSize: 12,
            TextWeight: SkinTextWeight.SemiBold,
            TextPlacement: SkinTextPlacement.NumberAboveLabel,
            Animation: new SkinAnimationSettings(
                RotationIntensity: 0,
                BreathingIntensity: 0.55,
                GlowIntensity: 0.65,
                FloatingIntensity: 0));

        Assert.Equal(expected, draft.Theme);
    }

    [Fact]
    public void CreateNew_IsDeterministicAndItsThemePassesSharedValidation()
    {
        var first = SkinDraftFactory.CreateNew(
            DraftId,
            SkinId,
            NowUtc,
            MinimumHudVersion);
        var second = SkinDraftFactory.CreateNew(
            DraftId,
            SkinId,
            NowUtc,
            MinimumHudVersion);

        Assert.Equal(first, second);
        var validation = SkinContractValidator.ValidateTheme(first.Theme);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));
        Assert.Equal(first.Theme, validation.Value);
    }
}
