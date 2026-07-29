namespace CodexQuotaHud.App.UI;

public enum EdgeDockSide
{
    None,
    Left,
    Right
}

public static class EdgeAutoHideGeometry
{
    public const double DockThreshold = 16;
    public const double VisibleHandleWidth = 12;

    public static EdgeDockSide DetectDockSide(
        double left,
        double width,
        WorkArea workArea,
        double threshold = DockThreshold)
    {
        if (!double.IsFinite(left) || width <= 0 || threshold < 0)
        {
            return EdgeDockSide.None;
        }

        var right = left + width;
        if (Math.Abs(left - workArea.Left) <= threshold)
        {
            return EdgeDockSide.Left;
        }

        return Math.Abs(right - (workArea.Left + workArea.Width)) <= threshold
            ? EdgeDockSide.Right
            : EdgeDockSide.None;
    }

    public static double ExpandedLeft(
        EdgeDockSide side,
        double width,
        WorkArea workArea) =>
        side switch
        {
            EdgeDockSide.Left => workArea.Left,
            EdgeDockSide.Right => workArea.Left + workArea.Width - width,
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };

    public static double CollapsedLeft(
        EdgeDockSide side,
        double width,
        WorkArea workArea,
        double visibleHandleWidth = VisibleHandleWidth) =>
        side switch
        {
            EdgeDockSide.Left => workArea.Left - width + visibleHandleWidth,
            EdgeDockSide.Right =>
                workArea.Left + workArea.Width - visibleHandleWidth,
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
}

internal sealed class EdgeAutoHideController(
    Func<Task> delayAsync,
    Action<EdgeDockSide> collapse,
    Action<EdgeDockSide> expand) : IDisposable
{
    private int _generation;
    private bool _disposed;

    public EdgeDockSide DockSide { get; private set; }

    public void SetDock(EdgeDockSide side)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelPendingCollapse();
        DockSide = side;
    }

    public void Undock() => SetDock(EdgeDockSide.None);

    public void Expand()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CancelPendingCollapse();
        if (DockSide != EdgeDockSide.None)
        {
            expand(DockSide);
        }
    }

    public void CancelPendingCollapse() =>
        Interlocked.Increment(ref _generation);

    public async Task<bool> ScheduleCollapseAsync(Func<bool> canCollapse)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canCollapse);
        if (DockSide == EdgeDockSide.None)
        {
            return false;
        }

        var generation = Interlocked.Increment(ref _generation);
        await delayAsync();
        if (_disposed ||
            generation != Volatile.Read(ref _generation) ||
            DockSide == EdgeDockSide.None ||
            !canCollapse())
        {
            return false;
        }

        collapse(DockSide);
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        CancelPendingCollapse();
    }
}
