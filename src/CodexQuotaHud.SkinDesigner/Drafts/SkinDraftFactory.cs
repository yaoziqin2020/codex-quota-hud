using System.Collections.ObjectModel;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public static class SkinDraftFactory
{
    private static readonly IReadOnlyDictionary<
        SkinAssetSlot,
        DraftAssetReference> EmptyAssets =
        new ReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>(
            new Dictionary<SkinAssetSlot, DraftAssetReference>());

    public static SkinDraftDocument CreateNew(
        Guid draftId,
        Guid skinId,
        DateTimeOffset nowUtc,
        SemanticVersion minimumHudVersion)
    {
        var identity = new SkinImageTransform(
            OffsetX: 0,
            OffsetY: 0,
            Scale: 1,
            Rotation: 0,
            Opacity: 1,
            CropFocusX: 0.5,
            CropFocusY: 0.5);
        var theme = new SkinTheme(
            SchemaVersion: SkinPackageLimits.SchemaVersion,
            TemplateId: SkinPackageLimits.FreeDecorationRingTemplateId,
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
                RotationIntensity: 0.25,
                BreathingIntensity: 0.5,
                GlowIntensity: 0.75,
                FloatingIntensity: 1));

        return new SkinDraftDocument(
            DraftSchemaVersion: 1,
            DraftId: draftId,
            SkinId: skinId,
            Revision: 0,
            ProjectName: "未命名皮肤",
            DisplayName: "未命名皮肤",
            Author: string.Empty,
            PackageVersion: SemanticVersion.Parse("1.0.0"),
            Description: string.Empty,
            MinimumHudVersion: minimumHudVersion,
            OriginSkinId: null,
            Theme: theme,
            Assets: EmptyAssets,
            CreatedAtUtc: nowUtc,
            UpdatedAtUtc: nowUtc);
    }
}
