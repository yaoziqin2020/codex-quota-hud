using System.Windows.Media;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Skins.Contracts;
using DrawingColor = System.Drawing.Color;
using MediaColor = System.Windows.Media.Color;

namespace CodexQuotaHud.App.UI.Skins;

public sealed record SkinPresentation(
    PopupTheme Popup,
    EdgeProgressTheme Edge,
    DrawingColor TrayAccent)
{
    public static SkinPresentation ForBuiltIn(SkinId id) =>
        new(
            PopupThemeProvider.Get(id),
            EdgeProgressThemeProvider.Get(id),
            BuiltInTrayAccent(id));

    public static SkinPresentation ForCustom(SkinTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        var primary = Parse(theme.PrimaryRingColor);
        var secondary = Parse(theme.SecondaryRingColor);
        var glow = Parse(theme.GlowColor);
        var background = Parse(theme.BaseBackgroundColor);
        background.A = (byte)Math.Round(
            background.A * Math.Clamp(theme.BaseBackgroundOpacity, 0, 1),
            MidpointRounding.AwayFromZero);

        var backgroundBrush = Solid(background);
        var primaryBrush = Solid(primary);
        var secondaryText = PopupThemeProvider.Get(SkinId.HudDial).SecondaryText;
        var fill = new LinearGradientBrush(primary, secondary, 0);
        fill.Freeze();
        var builtInTech = EdgeProgressThemeProvider.Get(SkinId.HudDial);

        return new SkinPresentation(
            new PopupTheme(
                backgroundBrush,
                primaryBrush,
                primaryBrush,
                secondaryText,
                glow,
                PopupDecorationKind.Custom),
            new EdgeProgressTheme(
                backgroundBrush,
                primaryBrush,
                fill,
                builtInTech.Texture,
                EdgeProgressMaterialKind.TechHighlight,
                primary,
                glow,
                builtInTech.TextureOpacity,
                theme.GlowIntensity),
            DrawingColor.FromArgb(primary.A, primary.R, primary.G, primary.B));
    }

    private static DrawingColor BuiltInTrayAccent(SkinId id) => id switch
    {
        SkinId.EnergyRing => DrawingColor.FromArgb(0x53, 0xEC, 0xFF),
        SkinId.LiquidGlass => DrawingColor.FromArgb(0xB9, 0xF1, 0xFF),
        SkinId.Aurora => DrawingColor.FromArgb(0x79, 0xF3, 0xE2),
        SkinId.LiquidTank => DrawingColor.FromArgb(0x8D, 0xE9, 0xF5),
        _ => DrawingColor.FromArgb(0x53, 0xDC, 0xF8)
    };

    private static MediaColor Parse(string value) =>
        (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(value)!;

    private static SolidColorBrush Solid(MediaColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
