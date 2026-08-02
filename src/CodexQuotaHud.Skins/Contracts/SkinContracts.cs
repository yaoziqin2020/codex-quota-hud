namespace CodexQuotaHud.Skins.Contracts;

public enum SkinAssetSlot
{
    Background,
    Center,
    Decoration
}

public enum SkinTextWeight
{
    Regular,
    SemiBold,
    Bold
}

public enum SkinTextPlacement
{
    Centered,
    NumberAboveLabel,
    LabelAboveNumber
}

public sealed record SkinAssetReference(
    SkinAssetSlot Slot,
    string Path,
    string Sha256);

public sealed record SkinImageTransform(
    double OffsetX,
    double OffsetY,
    double Scale,
    double Rotation,
    double Opacity,
    double CropFocusX,
    double CropFocusY);

public sealed record SkinAnimationSettings(
    double RotationIntensity,
    double BreathingIntensity,
    double GlowIntensity,
    double FloatingIntensity);

public sealed record SkinManifest(
    int SchemaVersion,
    Guid SkinId,
    string DisplayName,
    string Author,
    SemanticVersion PackageVersion,
    string Description,
    string TemplateId,
    SemanticVersion MinimumHudVersion,
    Guid? OriginSkinId,
    IReadOnlyList<SkinAssetReference> Assets);

public sealed record SkinTheme(
    int SchemaVersion,
    string TemplateId,
    SkinImageTransform Background,
    SkinImageTransform Center,
    SkinImageTransform Decoration,
    string PrimaryRingColor,
    string SecondaryRingColor,
    string BaseBackgroundColor,
    double BaseBackgroundOpacity,
    double RingDiameter,
    double RingThickness,
    double RingGap,
    double StartAngle,
    string GlowColor,
    double GlowIntensity,
    double NumberTextSize,
    double LabelTextSize,
    SkinTextWeight TextWeight,
    SkinTextPlacement TextPlacement,
    SkinAnimationSettings Animation);

public sealed record SkinAsset(
    SkinAssetSlot Slot,
    string RelativePath,
    byte[] Content,
    int PixelWidth,
    int PixelHeight,
    bool HasAlpha);

public sealed record SkinPackageDocument(
    SkinManifest Manifest,
    SkinTheme Theme,
    IReadOnlyDictionary<SkinAssetSlot, SkinAsset> Assets);

public sealed record SkinValidationError(
    string Code,
    string Location,
    string Message);

public sealed record SkinValidationResult<T>(
    T? Value,
    IReadOnlyList<SkinValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0 && Value is not null;
}
