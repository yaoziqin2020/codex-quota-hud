using System.Windows;

namespace CodexQuotaHud.SkinDesigner.UI;

public sealed record DesignerWindowLayout(
    Rect WindowBounds,
    double EditorWidth,
    double PreviewWidth,
    bool Compact);

public static class DesignerLayoutPolicy
{
    private const double MinimumEditorWidth = 320;
    private const double MinimumPreviewWidth = 280;

    public static DesignerWindowLayout Calculate(
        Rect workAreaDip,
        DpiScale dpi)
    {
        if (!double.IsFinite(workAreaDip.Left) ||
            !double.IsFinite(workAreaDip.Top) ||
            !double.IsFinite(workAreaDip.Width) ||
            !double.IsFinite(workAreaDip.Height) ||
            workAreaDip.Width < MinimumEditorWidth + MinimumPreviewWidth ||
            workAreaDip.Height < 480 ||
            !double.IsFinite(dpi.DpiScaleX) ||
            !double.IsFinite(dpi.DpiScaleY) ||
            dpi.DpiScaleX <= 0 ||
            dpi.DpiScaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(workAreaDip));
        }

        var compact = workAreaDip.Width < 1180 || workAreaDip.Height < 700;
        var horizontalInset = compact ? 0 : 24;
        var verticalInset = compact ? 0 : 24;
        var width = Math.Min(
            workAreaDip.Width - horizontalInset * 2,
            1480);
        var height = Math.Min(
            workAreaDip.Height - verticalInset * 2,
            900);
        var left = workAreaDip.Left + (workAreaDip.Width - width) / 2;
        var top = workAreaDip.Top + (workAreaDip.Height - height) / 2;
        var editor = compact
            ? Math.Clamp(width * 0.42, MinimumEditorWidth,
                width - MinimumPreviewWidth)
            : Math.Clamp(width * 0.36, 440, 520);
        var preview = width - editor;

        return new DesignerWindowLayout(
            new Rect(left, top, width, height),
            editor,
            preview,
            compact);
    }
}
