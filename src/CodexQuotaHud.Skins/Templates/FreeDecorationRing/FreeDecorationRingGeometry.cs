using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Templates.FreeDecorationRing;

public readonly record struct FreeDecorationRingTextLayout(
    double NumberY,
    double LabelY);

public readonly record struct FreeDecorationRingGuideGeometry(
    double PrimaryDiameter,
    double SecondaryDiameter,
    double CenterPeakSize,
    double CenterPeakOffsetX,
    double CenterPeakOffsetY,
    FreeDecorationRingTextLayout Text);

public static class FreeDecorationRingGeometry
{
    public static FreeDecorationRingTextLayout CalculateTextLayout(
        SkinTextPlacement placement,
        double textOffsetY,
        double textLineGap)
    {
        var halfGap = textLineGap / 2;
        return placement switch
        {
            SkinTextPlacement.LabelAboveNumber =>
                new FreeDecorationRingTextLayout(
                    18 + textOffsetY + halfGap,
                    -22 + textOffsetY - halfGap),
            SkinTextPlacement.NumberAboveLabel =>
                new FreeDecorationRingTextLayout(
                    -18 + textOffsetY - halfGap,
                    25 + textOffsetY + halfGap),
            _ => new FreeDecorationRingTextLayout(
                textOffsetY - halfGap,
                26 + textOffsetY + halfGap)
        };
    }

    public static FreeDecorationRingGuideGeometry CalculateGuideGeometry(
        SkinTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var secondaryDiameter = Math.Max(
            2 * theme.RingThickness,
            theme.RingDiameter -
            (2 * (theme.RingThickness + theme.RingGap)));
        var centerPeakSize = 64 * theme.Center.Scale *
            (1 + (0.12 * theme.Animation.BreathingIntensity));
        return new FreeDecorationRingGuideGeometry(
            theme.RingDiameter,
            secondaryDiameter,
            centerPeakSize,
            theme.Center.OffsetX,
            theme.Center.OffsetY,
            CalculateTextLayout(
                theme.TextPlacement,
                theme.TextOffsetY,
                theme.TextLineGap));
    }
}
