using System.Windows.Media;
using CodexQuotaHud.Core.Models;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;

namespace CodexQuotaHud.App.UI;

public readonly record struct PopupPlacement(
    double OffsetX,
    double OffsetY,
    PopupOpenDirection Direction)
{
    public bool OpensToRight => Direction == PopupOpenDirection.Right;
}

public enum PopupOpenDirection
{
    Left,
    Right,
    Up,
    Down
}

public static class PopupPlacementCalculator
{
    public static PopupPlacement Calculate(
        double orbLeft,
        double orbTop,
        double orbWidth,
        double orbHeight,
        double popupWidth,
        double popupHeight,
        WorkArea workArea,
        EdgeDockSide dockSide,
        double gap = 10,
        double insetLeft = 0,
        double insetTop = 0,
        double insetRight = 0,
        double insetBottom = 0)
    {
        var workRight = workArea.Left + workArea.Width;
        var cardWidth = Math.Max(0, popupWidth - insetLeft - insetRight);
        var cardHeight = Math.Max(0, popupHeight - insetTop - insetBottom);
        var rightLeft = orbLeft + orbWidth + gap - insetLeft;
        var leftLeft =
            orbLeft - gap - popupWidth + insetRight;
        var canRight =
            rightLeft + insetLeft + cardWidth <= workRight;
        var canLeft = leftLeft + insetLeft >= workArea.Left;
        var opensRight = canRight || !canLeft;
        var popupLeft = opensRight ? rightLeft : leftLeft;
        var minimumOuterLeft = workArea.Left - insetLeft;
        var maximumOuterLeft =
            workRight - popupWidth + insetRight;
        popupLeft = Math.Clamp(
            popupLeft,
            minimumOuterLeft,
            Math.Max(minimumOuterLeft, maximumOuterLeft));

        var centeredCardTop =
            orbTop + ((orbHeight - cardHeight) / 2);
        var cardTop = Math.Clamp(
            centeredCardTop,
            workArea.Top,
            Math.Max(
                workArea.Top,
                workArea.Top + workArea.Height - cardHeight));
        var popupTop = cardTop - insetTop;
        return new PopupPlacement(
            popupLeft - orbLeft,
            popupTop - orbTop,
            opensRight
                ? PopupOpenDirection.Right
                : PopupOpenDirection.Left);
    }
}

public sealed record PopupTheme(
    MediaBrush Background,
    MediaBrush Border,
    MediaBrush Accent,
    MediaBrush SecondaryText,
    MediaColor ShadowColor,
    PopupDecorationKind Decoration);

public enum PopupDecorationKind
{
    HudDial,
    EnergyRing,
    LiquidGlass,
    Aurora,
    LiquidTank
}

public static class PopupThemeProvider
{
    public static PopupTheme Get(SkinId skin) =>
        skin switch
        {
            SkinId.EnergyRing => Create("#F2111024", "#8A8D62FF", "#AE7BFF", "#965FFF", PopupDecorationKind.EnergyRing),
            SkinId.LiquidGlass => Create("#E8162635", "#8AADEBFF", "#B9F1FF", "#8ADFFF", PopupDecorationKind.LiquidGlass),
            SkinId.Aurora => Create("#F212241F", "#8A57E6A3", "#62F2A0", "#44E89A", PopupDecorationKind.Aurora),
            SkinId.LiquidTank => Create("#ED102632", "#8A59CDEA", "#8DE9F5", "#3DCBE8", PopupDecorationKind.LiquidTank),
            _ => Create("#F21A202B", "#6638D9FF", "#53DCF8", "#24CFF2", PopupDecorationKind.HudDial)
        };

    private static PopupTheme Create(
        string background,
        string border,
        string accent,
        string shadow,
        PopupDecorationKind decoration)
    {
        var converter = new BrushConverter();
        return new PopupTheme(
            (MediaBrush)converter.ConvertFromString(background)!,
            (MediaBrush)converter.ConvertFromString(border)!,
            (MediaBrush)converter.ConvertFromString(accent)!,
            (MediaBrush)converter.ConvertFromString("#C5D0DD")!,
            (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(shadow),
            decoration);
    }
}

public sealed record EdgeProgressTheme(
    MediaBrush Track,
    MediaBrush Border,
    MediaBrush Fill,
    MediaBrush Texture,
    MediaColor AccentColor,
    MediaColor GlowColor,
    double TextureOpacity,
    double GlowOpacity);

public static class EdgeProgressThemeProvider
{
    public static EdgeProgressTheme Get(SkinId skin) =>
        skin switch
        {
            SkinId.EnergyRing => Create(
                "#F2111024", "#FF8D62FF",
                ["#FF9A68FF", "#FFB85FE8", "#FF765DDE"],
                "#A6E9D8FF", 45, 7, 1.2, 0.58, 0.76),
            SkinId.LiquidGlass => Create(
                "#D8142834", "#FFD0EEFF",
                ["#FFBDF4FF", "#FF6FD9F5", "#FF9DE8FF"],
                "#B8FFFFFF", -35, 11, 0.8, 0.48, 0.7),
            SkinId.Aurora => Create(
                "#F20B211B", "#FF5DDBA0",
                ["#FF55D99A", "#FF62DFA5", "#FF3DBA91"],
                "#806DDDA8", 28, 13, 1.1, 0.24, 0.44),
            SkinId.LiquidTank => Create(
                "#F20C2430", "#FF59CDEA",
                ["#FF84EBF6", "#FF39C7E5", "#FF506EEB"],
                "#A8D9F8FF", 90, 9, 1, 0.52, 0.72),
            _ => Create(
                "#F20A1622", "#FF38D9FF",
                ["#FF58E6FA", "#FF24B8F2", "#FF4B7DFF"],
                "#A6C4F7FF", 0, 6, 0.8, 0.5, 0.78)
        };

    private static EdgeProgressTheme Create(
        string track,
        string border,
        IReadOnlyList<string> fillColors,
        string textureColor,
        double angle,
        double texturePitch,
        double textureThickness,
        double textureOpacity,
        double glowOpacity)
    {
        var trackBrush = Solid(track);
        var borderBrush = Solid(border);
        var fill = Gradient(fillColors, angle);
        var texture = Texture(
            ParseColor(textureColor),
            texturePitch,
            textureThickness,
            angle);
        var accent = ParseColor(fillColors[0]);
        return new EdgeProgressTheme(
            trackBrush,
            borderBrush,
            fill,
            texture,
            accent,
            accent,
            textureOpacity,
            glowOpacity);
    }

    private static SolidColorBrush Solid(string value)
    {
        var brush = new SolidColorBrush(ParseColor(value));
        brush.Freeze();
        return brush;
    }

    private static LinearGradientBrush Gradient(
        IReadOnlyList<string> colors,
        double angle)
    {
        var radians = angle * Math.PI / 180;
        var vector = new System.Windows.Vector(
            Math.Cos(radians),
            Math.Sin(radians));
        var start = new System.Windows.Point(
            0.5 - (vector.X / 2),
            0.5 - (vector.Y / 2));
        var end = new System.Windows.Point(
            0.5 + (vector.X / 2),
            0.5 + (vector.Y / 2));
        var brush = new LinearGradientBrush
        {
            StartPoint = start,
            EndPoint = end
        };
        for (var index = 0; index < colors.Count; index++)
        {
            brush.GradientStops.Add(new GradientStop(
                ParseColor(colors[index]),
                colors.Count == 1
                    ? 0
                    : (double)index / (colors.Count - 1)));
        }

        brush.Freeze();
        return brush;
    }

    private static DrawingBrush Texture(
        MediaColor color,
        double pitch,
        double thickness,
        double angle)
    {
        var geometry = new LineGeometry(
            new System.Windows.Point(0, pitch),
            new System.Windows.Point(pitch, 0));
        var pen = new System.Windows.Media.Pen(
            new SolidColorBrush(color),
            thickness);
        var drawing = new GeometryDrawing(null, pen, geometry);
        var brush = new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new System.Windows.Rect(0, 0, pitch, pitch),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None,
            Transform = new RotateTransform(angle / 4)
        };
        brush.Freeze();
        return brush;
    }

    private static MediaColor ParseColor(string value) =>
        (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(
            value);
}
