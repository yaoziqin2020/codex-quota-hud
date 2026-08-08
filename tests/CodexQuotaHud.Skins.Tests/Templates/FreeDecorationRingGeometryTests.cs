using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Templates.FreeDecorationRing;

namespace CodexQuotaHud.Skins.Tests.Templates;

public sealed class FreeDecorationRingGeometryTests
{
    [Theory]
    [InlineData(SkinTextPlacement.Centered, 0, 26)]
    [InlineData(SkinTextPlacement.NumberAboveLabel, -18, 25)]
    [InlineData(SkinTextPlacement.LabelAboveNumber, 18, -22)]
    public void CalculateTextLayout_ZeroValuesPreserveV123Margins(
        SkinTextPlacement placement,
        double expectedNumber,
        double expectedLabel)
    {
        var layout = FreeDecorationRingGeometry.CalculateTextLayout(
            placement,
            textOffsetY: 0,
            textLineGap: 0);

        Assert.Equal(expectedNumber, layout.NumberY);
        Assert.Equal(expectedLabel, layout.LabelY);
    }

    [Theory]
    [InlineData(SkinTextPlacement.NumberAboveLabel, -4, 6, -25, 24)]
    [InlineData(SkinTextPlacement.LabelAboveNumber, -4, 6, 17, -29)]
    [InlineData(SkinTextPlacement.Centered, 3, -8, 7, 25)]
    [InlineData(SkinTextPlacement.NumberAboveLabel, 5, -8, -9, 26)]
    public void CalculateTextLayout_AppliesSharedOffsetAndSplitsSignedGap(
        SkinTextPlacement placement,
        double textOffsetY,
        double textLineGap,
        double expectedNumber,
        double expectedLabel)
    {
        var layout = FreeDecorationRingGeometry.CalculateTextLayout(
            placement,
            textOffsetY,
            textLineGap);

        Assert.Equal(expectedNumber, layout.NumberY);
        Assert.Equal(expectedLabel, layout.LabelY);
    }

    [Fact]
    public void CalculateGuideGeometry_ClampsSecondaryAndUsesBreathingPeak()
    {
        var theme = FreeDecorationRingRendererTests.CreateDocument().Theme with
        {
            RingDiameter = 72,
            RingThickness = 16,
            RingGap = 24,
            Center = new SkinImageTransform(
                OffsetX: 7,
                OffsetY: -9,
                Scale: 1.25,
                Rotation: 0,
                Opacity: 1,
                CropFocusX: 0.5,
                CropFocusY: 0.5),
            TextPlacement = SkinTextPlacement.LabelAboveNumber,
            TextOffsetY = -4,
            TextLineGap = 6,
            Animation = new SkinAnimationSettings(
                RotationIntensity: 0,
                BreathingIntensity: 0.5,
                GlowIntensity: 0,
                FloatingIntensity: 0)
        };

        var geometry = FreeDecorationRingGeometry.CalculateGuideGeometry(theme);

        Assert.Equal(72, geometry.PrimaryDiameter);
        Assert.Equal(32, geometry.SecondaryDiameter);
        Assert.Equal(84.8, geometry.CenterPeakSize, precision: 10);
        Assert.Equal(7, geometry.CenterPeakOffsetX);
        Assert.Equal(-9, geometry.CenterPeakOffsetY);
        Assert.Equal(17, geometry.Text.NumberY);
        Assert.Equal(-29, geometry.Text.LabelY);
    }
}
