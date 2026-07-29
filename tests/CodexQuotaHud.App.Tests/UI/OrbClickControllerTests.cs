using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class OrbClickControllerTests
{
    [Fact]
    public async Task SingleClick_WaitsForDoubleClickWindowBeforeTogglingDetails()
    {
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var toggles = 0;
        var refreshes = 0;
        var controller = new OrbClickController(
            () => delay.Task,
            () => toggles++,
            () => refreshes++);

        var pending = controller.HandleClickAsync(clickCount: 1);

        Assert.Equal(0, toggles);
        Assert.Equal(0, refreshes);
        Assert.False(pending.IsCompleted);

        delay.SetResult();
        Assert.True(await pending);
        Assert.Equal(1, toggles);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task DoubleClick_CancelsPendingSingleAndRefreshesWithoutToggle()
    {
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var toggles = 0;
        var refreshes = 0;
        var controller = new OrbClickController(
            () => delay.Task,
            () => toggles++,
            () => refreshes++);

        var single = controller.HandleClickAsync(clickCount: 1);
        Assert.False(await controller.HandleClickAsync(clickCount: 2));
        delay.SetResult();

        Assert.False(await single);
        Assert.Equal(0, toggles);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task Drag_CancelsPendingSingleClick()
    {
        var delay = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var toggles = 0;
        var controller = new OrbClickController(
            () => delay.Task,
            () => toggles++,
            () => { });

        var single = controller.HandleClickAsync(clickCount: 1);
        controller.CancelPendingSingleClick();
        delay.SetResult();

        Assert.False(await single);
        Assert.Equal(0, toggles);
    }
}
