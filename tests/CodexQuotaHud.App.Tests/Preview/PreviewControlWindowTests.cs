using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Tests.Preview;

[Collection(PreviewWpfCollection.Name)]
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
            var window = new PreviewControlWindow(
                session,
                installedAppAvailable: true);

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

    [Fact]
    public void InstalledHandoff_IsDisabledWhenMissingAndRaisedOnlyOnce()
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
            var missing = new PreviewControlWindow(
                session,
                installedAppAvailable: false);
            Assert.False(missing.CanOpenInstalled);
            Assert.Equal("未找到已安装正式版", missing.InstalledAppMessage);
            missing.Close();

            var window = new PreviewControlWindow(
                session,
                installedAppAvailable: true);
            var handoffs = 0;
            var exits = 0;
            window.OpenInstalledRequested += (_, _) => handoffs++;
            window.ExitRequested += (_, _) => exits++;

            window.RequestOpenInstalled();
            window.RequestOpenInstalled();

            Assert.Equal(1, handoffs);
            Assert.Equal(1, exits);
            window.Close();
        });
    }

    [Fact]
    public void WindowState_RestoresSavesAndClampsOffscreenGeometry()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud-PreviewWindow",
            Guid.NewGuid().ToString("N"));
        try
        {
            var store = new PreviewWindowStateStore(root);
            store.Save(new PreviewWindowState(120, 80, 440, 720));

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
                var window = new PreviewControlWindow(
                    session,
                    installedAppAvailable: true,
                    store);

                Assert.Equal(440, window.Width);
                Assert.Equal(720, window.Height);
                window.Left = 160;
                window.Top = 90;
                window.Width = 460;
                window.Height = 740;
                window.SaveWindowStateNow();
                Assert.Equal(
                    new PreviewWindowState(160, 90, 460, 740),
                    store.Load());
                window.Close();
            });

            var clamped = PreviewControlWindow.ClampState(
                new PreviewWindowState(4000, 3000, 440, 720),
                new WorkArea(0, 0, 1920, 1040));
            Assert.Equal(
                new PreviewWindowState(1480, 320, 440, 720),
                clamped);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
        public bool TryActivateSkinKey(string selectionKey) => true;
        public void SetDetailsOpen(bool isOpen) => DetailsOpen = isOpen;
        public void PreviewEdge(EdgeDockSide side) { }
        public void ForceExpanded() { }
    }
}
