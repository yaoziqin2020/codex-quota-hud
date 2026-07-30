using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Tests.Preview;

public sealed class PreviewControlWindowTests
{
    [Fact]
    public void Controls_StartWithSafeDefaultsAndDriveSession()
    {
        RunSta(() =>
        {
            var controller = new PreviewQuotaRefreshController();
            var hud = new RecordingHud();
            using var viewModel = new QuotaOrbViewModel(
                controller,
                new InMemorySettingsStore(new AppSettings()),
                new AppSettings(),
                new InlineDispatcher(),
                () => { });
            var session = new PreviewSession(controller, viewModel, hud);
            var window = new PreviewControlWindow(session);

            Assert.Equal("Codex Quota HUD — 开发预览", window.Title);
            Assert.False(window.Topmost);
            Assert.Equal(PreviewDisplayChoice.Dual, window.SelectedDisplayChoice);
            Assert.Equal(68, window.FiveHourPercent);
            Assert.Equal(34, window.WeeklyPercent);

            window.SelectDisplayChoice(PreviewDisplayChoice.WeeklyOnly);
            window.ChangeWeeklyPercent(72);
            window.ChangeRefreshing(true);
            window.ChangeDetails(true);

            Assert.Equal("每周", viewModel.PrimaryLabel);
            Assert.Equal(72, viewModel.PrimaryPercent);
            Assert.True(viewModel.IsRefreshing);
            Assert.True(hud.DetailsOpen);
            window.Close();
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
    }

    private sealed class RecordingHud : IPreviewHud
    {
        public bool DetailsOpen { get; private set; }
        public void SetDetailsOpen(bool isOpen) => DetailsOpen = isOpen;
        public void PreviewEdge(EdgeDockSide side) { }
        public void ForceExpanded() { }
    }
}
