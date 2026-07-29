namespace CodexQuotaHud.App.UI;

public enum EdgeDockSide
{
    None,
    Left,
    Right,
    Top,
    Bottom
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

    public static WorkArea NearestWorkArea(
        double left,
        double top,
        double width,
        double height,
        IReadOnlyList<WorkArea> workAreas)
    {
        ArgumentNullException.ThrowIfNull(workAreas);
        if (workAreas.Count == 0)
        {
            throw new ArgumentException(
                "At least one work area is required.",
                nameof(workAreas));
        }

        var centerX = left + (width / 2);
        var centerY = top + (height / 2);
        return workAreas
            .OrderBy(area =>
            {
                var dx = centerX < area.Left
                    ? area.Left - centerX
                    : centerX > area.Left + area.Width
                        ? centerX - (area.Left + area.Width)
                        : 0;
                var dy = centerY < area.Top
                    ? area.Top - centerY
                    : centerY > area.Top + area.Height
                        ? centerY - (area.Top + area.Height)
                        : 0;
                return (dx * dx) + (dy * dy);
            })
            .First();
    }

    public static EdgeDockSide NearestDockSide(
        double left,
        double top,
        double width,
        double height,
        WorkArea workArea,
        IReadOnlyList<WorkArea> workAreas)
    {
        if (!double.IsFinite(left) ||
            !double.IsFinite(top) ||
            width <= 0 ||
            height <= 0)
        {
            return EdgeDockSide.None;
        }

        var centerX = left + (width / 2);
        var centerY = top + (height / 2);
        var candidates = new List<(EdgeDockSide Side, double Distance)>
        {
            (EdgeDockSide.Left, Math.Abs(centerX - workArea.Left)),
            (EdgeDockSide.Right,
                Math.Abs((workArea.Left + workArea.Width) - centerX)),
            (EdgeDockSide.Top, Math.Abs(centerY - workArea.Top)),
            (EdgeDockSide.Bottom,
                Math.Abs((workArea.Top + workArea.Height) - centerY))
        };

        return candidates
            .Where(candidate =>
                IsExternalEdge(candidate.Side, workArea, workAreas))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Side)
            .Select(candidate => candidate.Side)
            .FirstOrDefault();
    }

    public static EdgeDockSide DockSideNearEdge(
        double left,
        double top,
        double width,
        double height,
        WorkArea workArea,
        IReadOnlyList<WorkArea> workAreas,
        double threshold = DockThreshold)
    {
        if (!double.IsFinite(left) ||
            !double.IsFinite(top) ||
            width <= 0 ||
            height <= 0 ||
            threshold < 0)
        {
            return EdgeDockSide.None;
        }

        var candidates = new[]
        {
            (Side: EdgeDockSide.Left,
                Distance: Math.Abs(left - workArea.Left)),
            (Side: EdgeDockSide.Right,
                Distance: Math.Abs(
                    left + width - (workArea.Left + workArea.Width))),
            (Side: EdgeDockSide.Top,
                Distance: Math.Abs(top - workArea.Top)),
            (Side: EdgeDockSide.Bottom,
                Distance: Math.Abs(
                    top + height - (workArea.Top + workArea.Height)))
        };
        return candidates
            .Where(candidate => candidate.Distance <= threshold)
            .Where(candidate =>
                IsExternalEdge(candidate.Side, workArea, workAreas))
            .OrderBy(candidate => candidate.Distance)
            .ThenBy(candidate => candidate.Side)
            .Select(candidate => candidate.Side)
            .FirstOrDefault();
    }

    public static WindowPosition ExpandedPosition(
        EdgeDockSide side,
        double left,
        double top,
        double width,
        double height,
        WorkArea workArea)
    {
        var clamped = WindowPositioning.Clamp(
            left, top, width, height, workArea);
        return side switch
        {
            EdgeDockSide.Left =>
                new WindowPosition(workArea.Left, clamped.Top),
            EdgeDockSide.Right =>
                new WindowPosition(
                    workArea.Left + workArea.Width - width,
                    clamped.Top),
            EdgeDockSide.Top =>
                new WindowPosition(clamped.Left, workArea.Top),
            EdgeDockSide.Bottom =>
                new WindowPosition(
                    clamped.Left,
                    workArea.Top + workArea.Height - height),
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
    }

    public static WindowPosition CollapsedPosition(
        EdgeDockSide side,
        double left,
        double top,
        double width,
        double height,
        WorkArea workArea,
        double visibleHandleWidth = VisibleHandleWidth)
    {
        var expanded = ExpandedPosition(
            side, left, top, width, height, workArea);
        return side switch
        {
            EdgeDockSide.Left =>
                expanded with
                {
                    Left = workArea.Left - width + visibleHandleWidth
                },
            EdgeDockSide.Right =>
                expanded with
                {
                    Left = workArea.Left + workArea.Width - visibleHandleWidth
                },
            EdgeDockSide.Top =>
                expanded with
                {
                    Top = workArea.Top - height + visibleHandleWidth
                },
            EdgeDockSide.Bottom =>
                expanded with
                {
                    Top = workArea.Top + workArea.Height - visibleHandleWidth
                },
            _ => throw new ArgumentOutOfRangeException(nameof(side))
        };
    }

    private static bool IsExternalEdge(
        EdgeDockSide side,
        WorkArea current,
        IReadOnlyList<WorkArea> workAreas)
    {
        const double epsilon = 0.5;
        foreach (var other in workAreas)
        {
            if (other.Equals(current))
            {
                continue;
            }

            var hasNeighbour = side switch
            {
                EdgeDockSide.Left =>
                    Math.Abs(
                        other.Left + other.Width - current.Left) <= epsilon &&
                    IntervalsOverlap(
                        current.Top,
                        current.Top + current.Height,
                        other.Top,
                        other.Top + other.Height),
                EdgeDockSide.Right =>
                    Math.Abs(
                        other.Left - (current.Left + current.Width)) <= epsilon &&
                    IntervalsOverlap(
                        current.Top,
                        current.Top + current.Height,
                        other.Top,
                        other.Top + other.Height),
                EdgeDockSide.Top =>
                    Math.Abs(
                        other.Top + other.Height - current.Top) <= epsilon &&
                    IntervalsOverlap(
                        current.Left,
                        current.Left + current.Width,
                        other.Left,
                        other.Left + other.Width),
                EdgeDockSide.Bottom =>
                    Math.Abs(
                        other.Top - (current.Top + current.Height)) <= epsilon &&
                    IntervalsOverlap(
                        current.Left,
                        current.Left + current.Width,
                        other.Left,
                        other.Left + other.Width),
                _ => false
            };

            if (hasNeighbour)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IntervalsOverlap(
        double firstStart,
        double firstEnd,
        double secondStart,
        double secondEnd) =>
        Math.Min(firstEnd, secondEnd) >
        Math.Max(firstStart, secondStart);
}

public static class EdgeProgressGeometry
{
    public static double FillLength(double trackLength, double percent)
    {
        if (!double.IsFinite(trackLength) || trackLength <= 0)
        {
            return 0;
        }

        var normalizedPercent = double.IsFinite(percent)
            ? Math.Clamp(percent, 0, 100)
            : 0;
        return trackLength * normalizedPercent / 100;
    }
}

internal sealed class EdgeAutoHideController(
    Func<Task> delayAsync,
    Action<EdgeDockSide> collapse,
    Action<EdgeDockSide> expand) : IDisposable
{
    private int _generation;
    private bool _disposed;

    public EdgeDockSide DockSide { get; private set; }
    public bool IsCollapsed { get; private set; }

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
        IsCollapsed = false;
        if (DockSide != EdgeDockSide.None)
        {
            expand(DockSide);
        }
    }

    public bool TryExpandCollapsed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsCollapsed)
        {
            return false;
        }

        Expand();
        return true;
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

        IsCollapsed = true;
        collapse(DockSide);
        return true;
    }

    public void Dispose()
    {
        _disposed = true;
        CancelPendingCollapse();
    }
}
