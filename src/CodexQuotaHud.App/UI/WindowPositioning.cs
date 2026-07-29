namespace CodexQuotaHud.App.UI;

public readonly record struct WorkArea(
    double Left,
    double Top,
    double Width,
    double Height);

public readonly record struct WindowPosition(
    double Left,
    double Top);

public static class WindowPositioning
{
    public static WindowPosition Clamp(
        double left,
        double top,
        double width,
        double height,
        WorkArea workArea)
    {
        var maximumLeft = Math.Max(
            workArea.Left,
            workArea.Left + workArea.Width - width);
        var maximumTop = Math.Max(
            workArea.Top,
            workArea.Top + workArea.Height - height);
        var safeLeft = double.IsFinite(left)
            ? left
            : workArea.Left + Math.Max(0, workArea.Width - width) / 2;
        var safeTop = double.IsFinite(top)
            ? top
            : workArea.Top + Math.Max(0, workArea.Height - height) / 2;

        return new WindowPosition(
            Math.Clamp(safeLeft, workArea.Left, maximumLeft),
            Math.Clamp(safeTop, workArea.Top, maximumTop));
    }
}
