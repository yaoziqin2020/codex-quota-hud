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
    [InlineData(EdgeDockSide.Left, -2040, -1920)]
    [InlineData(EdgeDockSide.Right, -12, -132)]
    public void Positions_LeaveTwelvePixelHandleAndExpandInsideWorkArea(
        EdgeDockSide side,
        double collapsed,
        double expanded)
    {
        Assert.Equal(
            collapsed,
            EdgeAutoHideGeometry.CollapsedLeft(
                side,
                width: 132,
                SecondaryMonitor));
        Assert.Equal(
            expanded,
            EdgeAutoHideGeometry.ExpandedLeft(
                side,
                width: 132,
                SecondaryMonitor));
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
    }
}
