using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.UI;

public readonly record struct TrayIconState(
    string Text,
    double? Percent,
    Color Accent);

public static class TrayIconRenderer
{
    public static TrayIconState CreateState(
        QuotaDisplayMode mode,
        double primaryPercent,
        SkinId skin)
    {
        var hasData = mode != QuotaDisplayMode.Hidden;
        var percent = hasData
            ? Math.Round(
                Math.Clamp(primaryPercent, 0, 100),
                MidpointRounding.AwayFromZero)
            : (double?)null;
        return new TrayIconState(
            percent is null ? "—" : $"{percent:0}",
            percent,
            AccentFor(skin));
    }

    public static Icon Render(TrayIconState state, int size = 32)
    {
        if (size is < 16 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        using var bitmap = new Bitmap(
            size,
            size,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var inset = Math.Max(1f, size * 0.07f);
        var bounds = new RectangleF(
            inset,
            inset,
            size - (inset * 2),
            size - (inset * 2));
        using var background = new SolidBrush(Color.FromArgb(255, 8, 22, 32));
        graphics.FillEllipse(background, bounds);

        var ringInset = Math.Max(1.5f, size * 0.12f);
        var ringBounds = new RectangleF(
            ringInset,
            ringInset,
            size - (ringInset * 2),
            size - (ringInset * 2));
        var ringWidth = Math.Max(1.4f, size * 0.09f);
        using var track = new Pen(Color.FromArgb(105, 90, 118, 132), ringWidth);
        using var progress = new Pen(state.Accent, ringWidth)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawEllipse(track, ringBounds);
        if (state.Percent is { } percent && percent > 0)
        {
            graphics.DrawArc(
                progress,
                ringBounds,
                -90,
                (float)(Math.Clamp(percent, 0, 100) * 3.6));
        }

        var fontSize = state.Text.Length >= 3
            ? size * 0.25f
            : size * 0.32f;
        using var font = new Font(
            "Segoe UI",
            fontSize,
            FontStyle.Bold,
            GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(
            state.Text,
            font,
            textBrush,
            new RectangleF(0, 0, size, size),
            format);

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    private static Color AccentFor(SkinId skin) =>
        skin switch
        {
            SkinId.EnergyRing => Color.FromArgb(0x53, 0xEC, 0xFF),
            SkinId.LiquidGlass => Color.FromArgb(0xB9, 0xF1, 0xFF),
            SkinId.Aurora => Color.FromArgb(0x79, 0xF3, 0xE2),
            SkinId.LiquidTank => Color.FromArgb(0x8D, 0xE9, 0xF5),
            _ => Color.FromArgb(0x53, 0xDC, 0xF8)
        };

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}

internal sealed class TrayIconLifetime(Action<Icon?> assign) : IDisposable
{
    private Icon? _current;
    private bool _disposed;

    public void Replace(Icon icon)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(icon);
        var previous = _current;
        _current = icon;
        assign(icon);
        previous?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        assign(null);
        _current?.Dispose();
        _current = null;
    }
}
