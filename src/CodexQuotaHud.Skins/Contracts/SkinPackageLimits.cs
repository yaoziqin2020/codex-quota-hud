namespace CodexQuotaHud.Skins.Contracts;

public static class SkinPackageLimits
{
    public const int SchemaVersion = 1;
    public const string FreeDecorationRingTemplateId =
        "free-decoration-ring";

    public const long MaximumPackageBytes = 50L * 1024 * 1024;
    public const long MaximumExtractedBytes = 64L * 1024 * 1024;
    public const int MaximumEntries = 64;
    public const long MaximumImageBytes = 16L * 1024 * 1024;
    public const int MaximumImageDimension = 8192;
    public const long MaximumDecodedPixels = 67_108_864L;

    public const int MaximumDisplayNameScalars = 80;
    public const int MaximumAuthorScalars = 80;
    public const int MaximumDescriptionScalars = 500;

    public const string ManifestFileName = "manifest.json";
    public const string ThemeFileName = "theme.json";
    public const string AssetsDirectoryName = "assets/";

    public const double MinimumImageOffsetDip = -50;
    public const double MaximumImageOffsetDip = 50;
    public const double MinimumImageScale = 0.25;
    public const double MaximumImageScale = 3;
    public const double MinimumImageRotationDegrees = -180;
    public const double MaximumImageRotationDegrees = 180;
    public const double MinimumOpacity = 0;
    public const double MaximumOpacity = 1;
    public const double MinimumCropFocus = 0;
    public const double MaximumCropFocus = 1;
    public const double MinimumRingDiameterDip = 72;
    public const double MaximumRingDiameterDip = 116;
    public const double MinimumRingThicknessDip = 2;
    public const double MaximumRingThicknessDip = 16;
    public const double MinimumRingGapDip = 2;
    public const double MaximumRingGapDip = 24;
    public const double MinimumStartAngleDegrees = 0;
    public const double MaximumStartAngleDegrees = 359;
    public const double MinimumTextSizeDip = 12;
    public const double MaximumTextSizeDip = 34;
    public const double MinimumEffectIntensity = 0;
    public const double MaximumEffectIntensity = 1;
    public const double MinimumRefreshSpeedMultiplier = 0;
    public const double MaximumRefreshSpeedMultiplier = 4;
    public const double MinimumRefreshHoldSeconds = 0;
    public const double MaximumRefreshHoldSeconds = 3;
}
