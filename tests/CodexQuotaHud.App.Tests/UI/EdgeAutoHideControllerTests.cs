using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class EdgeAutoHideControllerTests
{
    private static readonly WorkArea SecondaryMonitor =
        new(-1920, 40, 1920, 1040);

    [Theory]
    [InlineData(-1920, EdgeDockSide.Left)]
    [InlineData(-1905, EdgeDockSide.Left)]
    [InlineData(-132, EdgeDockSide.Right)]
    [InlineData(-148, EdgeDockSide.Right)]
    [InlineData(-900, EdgeDockSide.None)]
    public void DetectDockSide_UsesOnlyLeftAndRightWorkAreaEdges(
        double left,
        EdgeDockSide expected)
    {
        Assert.Equal(
            expected,
            EdgeAutoHideGeometry.DetectDockSide(
                left,
                width: 132,
                SecondaryMonitor));
    }

    [Theory]
    [InlineData(EdgeDockSide.Left, -2028, -1920, 300, 300)]
    [InlineData(EdgeDockSide.Right, -24, -132, 300, 300)]
    [InlineData(EdgeDockSide.Top, -900, -900, -68, 40)]
    [InlineData(EdgeDockSide.Bottom, -900, -900, 1056, 948)]
    public void Positions_LeaveTwentyFourPixelGlowAreaAndExpandInsideWorkArea(
        EdgeDockSide side,
        double collapsedLeft,
        double expandedLeft,
        double collapsedTop,
        double expandedTop)
    {
        var collapsed = EdgeAutoHideGeometry.CollapsedPosition(
            side, -900, 300, 132, 132, SecondaryMonitor);
        var expanded = EdgeAutoHideGeometry.ExpandedPosition(
            side, -900, 300, 132, 132, SecondaryMonitor);

        Assert.Equal(collapsedLeft, collapsed.Left);
        Assert.Equal(collapsedTop, collapsed.Top);
        Assert.Equal(expandedLeft, expanded.Left);
        Assert.Equal(expandedTop, expanded.Top);
    }

    [Theory]
    [InlineData(420, 300, EdgeDockSide.None)]
    [InlineData(4, 400, EdgeDockSide.None)]
    [InlineData(1784, 400, EdgeDockSide.Right)]
    [InlineData(985, 4, EdgeDockSide.Top)]
    [InlineData(985, 904, EdgeDockSide.Bottom)]
    [InlineData(-1916, 400, EdgeDockSide.Left)]
    [InlineData(-1000, 944, EdgeDockSide.Bottom)]
    public void DockSideNearEdge_OnlyDocksWithinThresholdAcrossBothMonitors(
        double left,
        double top,
        EdgeDockSide expected)
    {
        var primary = new WorkArea(0, 0, 1920, 1040);
        var screens = new[] { SecondaryMonitor, primary };
        var current = left < 0 ? SecondaryMonitor : primary;

        Assert.Equal(
            expected,
            EdgeAutoHideGeometry.DockSideNearEdge(
                left,
                top,
                width: 132,
                height: 132,
                current,
                screens));
    }

    [Fact]
    public void DockSideNearEdge_ExcludesInternalMonitorSeam()
    {
        var primary = new WorkArea(0, 40, 1920, 1040);
        var screens = new[] { SecondaryMonitor, primary };

        Assert.Equal(
            EdgeDockSide.None,
            EdgeAutoHideGeometry.DockSideNearEdge(
                left: -140,
                top: 300,
                width: 132,
                height: 132,
                SecondaryMonitor,
                screens));
    }

    [Fact]
    public void NearestWorkArea_UsesWindowCenterWithNegativeCoordinates()
    {
        var primary = new WorkArea(0, 0, 1920, 1040);
        var screens = new[] { SecondaryMonitor, primary };

        Assert.Equal(
            SecondaryMonitor,
            EdgeAutoHideGeometry.NearestWorkArea(
                -1200, 300, 132, 132, screens));
        Assert.Equal(
            primary,
            EdgeAutoHideGeometry.NearestWorkArea(
                600, 300, 132, 132, screens));
    }

    [Fact]
    public async Task MouseEnterCancelsCollapseAndExpandsImmediately()
    {
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var collapsed = new List<EdgeDockSide>();
        var expanded = new List<EdgeDockSide>();
        using var controller = new EdgeAutoHideController(
            () => delay.Task,
            collapsed.Add,
            expanded.Add);
        controller.SetDock(EdgeDockSide.Right);

        var pending = controller.ScheduleCollapseAsync(() => true);
        controller.Expand();
        delay.SetResult();

        Assert.False(await pending);
        Assert.Empty(collapsed);
        Assert.Equal([EdgeDockSide.Right], expanded);
    }

    [Fact]
    public async Task CollapseRunsAfterDelayOnlyWhenStillAllowed()
    {
        var collapsed = new List<EdgeDockSide>();
        using var controller = new EdgeAutoHideController(
            () => Task.CompletedTask,
            collapsed.Add,
            _ => { });
        controller.SetDock(EdgeDockSide.Left);

        Assert.False(await controller.ScheduleCollapseAsync(() => false));
        Assert.True(await controller.ScheduleCollapseAsync(() => true));
        Assert.Equal([EdgeDockSide.Left], collapsed);
        Assert.True(controller.IsCollapsed);
    }

    [Fact]
    public async Task HoveringCollapsedProgress_RevealsOrbOnlyOnce()
    {
        var expanded = new List<EdgeDockSide>();
        using var controller = new EdgeAutoHideController(
            () => Task.CompletedTask,
            _ => { },
            expanded.Add);
        controller.SetDock(EdgeDockSide.Top);
        Assert.True(await controller.ScheduleCollapseAsync(() => true));

        Assert.True(controller.TryExpandCollapsed());
        Assert.False(controller.IsCollapsed);
        Assert.False(controller.TryExpandCollapsed());
        Assert.Equal([EdgeDockSide.Top], expanded);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(0, 0)]
    [InlineData(45, 28.8)]
    [InlineData(90, 57.6)]
    [InlineData(120, 64)]
    public void ProgressLength_MapsClampedQuotaToVisibleTrack(
        double percent,
        double expected)
    {
        Assert.Equal(
            expected,
            EdgeProgressGeometry.FillLength(
                trackLength: 64,
                percent),
            precision: 5);
    }
}
