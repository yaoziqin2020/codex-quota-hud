using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class HoverCloseControllerTests
{
    [Fact]
    public async Task EnteringPopupAcrossGapCancelsPendingClose()
    {
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        var controller = new HoverCloseController(
            () => delay.Task,
            () => closed = true);

        var pendingClose = controller.ScheduleCloseAsync();
        controller.CancelPendingClose();
        delay.TrySetResult();
        await pendingClose;

        Assert.False(closed);
    }

    [Fact]
    public async Task RemainingOutsideAfterDelayClosesPopup()
    {
        var controller = new HoverCloseController(
            () => Task.CompletedTask,
            () => { });

        Assert.True(await controller.ScheduleCloseAsync());
    }
}
