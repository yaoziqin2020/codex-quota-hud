using Media = System.Windows.Media;
using Drawing = System.Drawing;

namespace CodexQuotaHud.App.UI;

public enum QuotaAlertLevel
{
    Normal,
    Warning,
    Critical
}

public static class QuotaAlertPolicy
{
    public static QuotaAlertLevel Classify(double remainingPercent)
    {
        var normalized = double.IsFinite(remainingPercent)
            ? Math.Clamp(remainingPercent, 0, 100)
            : 0;
        return normalized <= 10
            ? QuotaAlertLevel.Critical
            : normalized <= 20
                ? QuotaAlertLevel.Warning
                : QuotaAlertLevel.Normal;
    }
}

public static class QuotaAlertPalette
{
    public static Media.Color WarningMediaColor { get; } =
        Media.Color.FromArgb(0xFF, 0xFF, 0xB5, 0x47);

    public static Media.Color CriticalMediaColor { get; } =
        Media.Color.FromArgb(0xFF, 0xFF, 0x5A, 0x67);

    public static Media.Brush WarningBrush { get; } = CreateFrozenBrush(WarningMediaColor);

    public static Media.Brush CriticalBrush { get; } = CreateFrozenBrush(CriticalMediaColor);

    public static Drawing.Color WarningDrawingColor { get; } =
        Drawing.Color.FromArgb(0xFF, 0xB5, 0x47);

    public static Drawing.Color CriticalDrawingColor { get; } =
        Drawing.Color.FromArgb(0xFF, 0x5A, 0x67);

    public static Media.Brush ResolveBrush(QuotaAlertLevel level, Media.Brush normal)
    {
        ArgumentNullException.ThrowIfNull(normal);
        return level switch
        {
            QuotaAlertLevel.Warning => WarningBrush,
            QuotaAlertLevel.Critical => CriticalBrush,
            _ => normal
        };
    }

    public static Media.Color ResolveMediaColor(QuotaAlertLevel level, Media.Color normal) =>
        level switch
        {
            QuotaAlertLevel.Warning => WarningMediaColor,
            QuotaAlertLevel.Critical => CriticalMediaColor,
            _ => normal
        };

    public static Drawing.Color ResolveDrawingColor(QuotaAlertLevel level, Drawing.Color normal) =>
        level switch
        {
            QuotaAlertLevel.Warning => WarningDrawingColor,
            QuotaAlertLevel.Critical => CriticalDrawingColor,
            _ => normal
        };

    private static Media.Brush CreateFrozenBrush(Media.Color color)
    {
        var brush = new Media.SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
