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
    EdgeProgressMaterialKind Material,
    MediaColor AccentColor,
    MediaColor GlowColor,
    double TextureOpacity,
    double GlowOpacity);

public enum EdgeProgressMaterialKind
{
    TechHighlight,
    EnergyBloom,
    GlassReflection,
    AuroraWash,
    LiquidLevel
}

public static class EdgeProgressThemeProvider
{
    public static EdgeProgressTheme Get(SkinId skin) =>
        skin switch
        {
            SkinId.EnergyRing => Create(
                "#F2111024", "#FF8D62FF",
                ["#FF9A68FF", "#FFB85FE8", "#FF765DDE"],
                45, EdgeProgressMaterialKind.EnergyBloom, 0.38, 0.64),
            SkinId.LiquidGlass => Create(
                "#D8142834", "#FFD0EEFF",
                ["#FFBDF4FF", "#FF6FD9F5", "#FF9DE8FF"],
                -35, EdgeProgressMaterialKind.GlassReflection, 0.46, 0.58),
            SkinId.Aurora => Create(
                "#F20B211B", "#FF5DDBA0",
                ["#FF55D99A", "#FF62DFA5", "#FF3DBA91"],
                28, EdgeProgressMaterialKind.AuroraWash, 0.3, 0.4),
            SkinId.LiquidTank => Create(
                "#F20C2430", "#FF59CDEA",
                ["#FF84EBF6", "#FF39C7E5", "#FF506EEB"],
                90, EdgeProgressMaterialKind.LiquidLevel, 0.42, 0.58),
            _ => Create(
                "#F20A1622", "#FF38D9FF",
                ["#FF58E6FA", "#FF24B8F2", "#FF4B7DFF"],
                0, EdgeProgressMaterialKind.TechHighlight, 0.36, 0.62)
        };

    private static EdgeProgressTheme Create(
        string track,
        string border,
        IReadOnlyList<string> fillColors,
        double angle,
        EdgeProgressMaterialKind material,
        double textureOpacity,
        double glowOpacity)
    {
        var trackBrush = Solid(track);
        var borderBrush = Solid(border);
        var fill = Gradient(fillColors, angle);
        var texture = Material(material);
        var accent = ParseColor(fillColors[0]);
        return new EdgeProgressTheme(
            trackBrush,
            borderBrush,
            fill,
            texture,
            material,
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

    private static MediaBrush Material(EdgeProgressMaterialKind material)
    {
        GradientBrush brush = material switch
        {
            EdgeProgressMaterialKind.EnergyBloom =>
                new RadialGradientBrush
                {
                    Center = new System.Windows.Point(0.38, 0.5),
                    GradientOrigin = new System.Windows.Point(0.28, 0.42),
                    RadiusX = 0.72,
                    RadiusY = 1
                },
            _ => new LinearGradientBrush
            {
                StartPoint = material == EdgeProgressMaterialKind.AuroraWash
                    ? new System.Windows.Point(0, 0.2)
                    : new System.Windows.Point(0, 0),
                EndPoint = material == EdgeProgressMaterialKind.AuroraWash
                    ? new System.Windows.Point(1, 0.8)
                    : new System.Windows.Point(0, 1)
            }
        };
        var stops = material switch
        {
            EdgeProgressMaterialKind.TechHighlight =>
                new[]
                {
                    ("#00FFFFFF", 0d),
                    ("#70FFFFFF", 0.46),
                    ("#18FFFFFF", 0.58),
                    ("#00FFFFFF", 1d)
                },
            EdgeProgressMaterialKind.EnergyBloom =>
                new[]
                {
                    ("#A8FFFFFF", 0d),
                    ("#385DEBFF", 0.5),
                    ("#00FFFFFF", 1d)
                },
            EdgeProgressMaterialKind.GlassReflection =>
                new[]
                {
                    ("#AFFFFFFF", 0d),
                    ("#42FFFFFF", 0.34),
                    ("#08FFFFFF", 0.66),
                    ("#00FFFFFF", 1d)
                },
            EdgeProgressMaterialKind.AuroraWash =>
                new[]
                {
                    ("#005CFFD2", 0d),
                    ("#786CFFD1", 0.48),
                    ("#247FFFF0", 0.72),
                    ("#005CFFD2", 1d)
                },
            _ => new[]
                {
                    ("#00FFFFFF", 0d),
                    ("#18FFFFFF", 0.34),
                    ("#86DFFFFF", 0.44),
                    ("#20FFFFFF", 0.56),
                    ("#00FFFFFF", 1d)
                }
        };
        foreach (var (color, offset) in stops)
        {
            brush.GradientStops.Add(new GradientStop(
                ParseColor(color),
                offset));
        }

        brush.Freeze();
        return brush;
    }

    private static MediaColor ParseColor(string value) =>
        (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(
            value);
}
