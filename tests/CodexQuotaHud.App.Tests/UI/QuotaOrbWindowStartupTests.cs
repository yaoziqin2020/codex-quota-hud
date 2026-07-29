using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class QuotaOrbWindowStartupTests
{
    [Fact]
    public void CleanupForExit_ToleratesPartialConstruction()
    {
        QuotaOrbWindow.CleanupForExit(
            viewModel: null,
            propertyChangedHandler: null,
            animationController: null);
    }

    [Fact]
    public void Constructor_LoadsAllRequiredResourcesWithoutApplicationDictionary()
    {
        RunSta(() =>
        {
            var refresh = new InertRefreshController();
            using var viewModel = new QuotaOrbViewModel(
                refresh,
                new InMemorySettingsStore(),
                new AppSettings(),
                new InlineDispatcher(),
                () => { });

            var window = new QuotaOrbWindow(viewModel);

            window.CloseForExit();
        });
    }

    [Fact]
    public void PopupChrome_SeparatesShadowFromRoundedClippedCard()
    {
        RunSta(() =>
        {
            using var viewModel = new QuotaOrbViewModel(
                new InertRefreshController(),
                new InMemorySettingsStore(),
                new AppSettings(),
                new InlineDispatcher(),
                () => { });
            var window = new QuotaOrbWindow(viewModel);
            var chrome = Assert.IsType<Grid>(window.FindName("PopupChrome"));
            var shadow = Assert.IsType<Border>(
                window.FindName("PopupShadowHost"));
            var card = Assert.IsType<Border>(window.FindName("PopupCard"));

            Assert.Equal(new Thickness(14), chrome.Margin);
            Assert.IsType<DropShadowEffect>(shadow.Effect);
            Assert.Null(card.Effect);
            Assert.True(card.ClipToBounds);
            Assert.IsType<RectangleGeometry>(card.Clip);
            var clip = QuotaOrbWindow.CreateRoundedPopupClip(
                new Size(250, 400));
            Assert.Equal(12, clip.RadiusX);
            Assert.Equal(12, clip.RadiusY);
            Assert.Equal(new Rect(0, 0, 250, 400), clip.Rect);

            var decorationNames = new[]
            {
                "HudDialPopupDecoration",
                "EnergyRingPopupDecoration",
                "LiquidGlassPopupDecoration",
                "AuroraPopupDecoration",
                "LiquidTankPopupDecoration"
            };
            Assert.All(
                decorationNames,
                name => Assert.NotNull(window.FindName(name)));
            Assert.Equal(
                Visibility.Visible,
                Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("HudDialPopupDecoration")).Visibility);

            viewModel.SelectedSkin = SkinId.LiquidTank;
            Assert.Equal(
                Visibility.Collapsed,
                Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("HudDialPopupDecoration")).Visibility);
            Assert.Equal(
                Visibility.Visible,
                Assert.IsAssignableFrom<FrameworkElement>(
                    window.FindName("LiquidTankPopupDecoration")).Visibility);

            window.CloseForExit();
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

    private sealed class InertRefreshController : IQuotaRefreshController
    {
        public event Action<QuotaRefreshState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task RefreshNowAsync(
            bool onlyIfStale,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private AppSettings _settings = new();

        public AppSettings Load() => _settings;

        public void Save(AppSettings settings) => _settings = settings;
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();
    }
}
