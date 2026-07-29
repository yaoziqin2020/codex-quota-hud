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
            SkinId.EnergyRing => Create("#F20A1722", "#8A35E8FF", "#53ECFF", "#1DDCFF", PopupDecorationKind.EnergyRing),
            SkinId.LiquidGlass => Create("#E8162635", "#8AADEBFF", "#B9F1FF", "#8ADFFF", PopupDecorationKind.LiquidGlass),
            SkinId.Aurora => Create("#F2171530", "#8A9B7CFF", "#79F3E2", "#9C6DFF", PopupDecorationKind.Aurora),
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
