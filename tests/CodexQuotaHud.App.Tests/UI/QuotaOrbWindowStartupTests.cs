using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using CodexQuotaHud.Core.Models;
using ShapeEllipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class QuotaOrbWindowStartupTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void EdgeCollapse_WaitsWhileClickOpenedDetailsRemainOpen(
        bool popupOpen,
        bool expected)
    {
        Assert.Equal(
            expected,
            QuotaOrbWindow.CanCollapseEdge(
                windowVisible: true,
                displayVisible: true,
                dragging: false,
                contextMenuOpen: false,
                pointerOverOrb: false,
                popupOpen,
                pointerOverPopup: false,
                orbMenuOpen: false));
    }

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
            var popup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(
                window.FindName("DetailsPopup"));

            Assert.Equal(278, chrome.Width);
            Assert.Equal(default, chrome.Margin);
            Assert.Equal(new Thickness(14), shadow.Margin);
            Assert.Equal(new Thickness(14), card.Margin);
            Assert.False(popup.StaysOpen);
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

    [Fact]
    public void EdgeHandle_UsesSkinThemedQuotaProgressForEverySide()
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
            var skin = Assert.IsType<ContentControl>(
                window.FindName("SkinHost"));
            var handle = Assert.IsType<Border>(
                window.FindName("EdgeHandle"));
            var track = Assert.IsType<Border>(
                window.FindName("EdgeProgressTrack"));
            var fill = Assert.IsType<Border>(
                window.FindName("EdgeProgressFill"));
            Assert.Equal(new CornerRadius(5), fill.CornerRadius);
            var texture = Assert.IsType<Border>(
                window.FindName("EdgeProgressTexture"));
            Assert.Null(window.FindName("EdgeProgressSheen"));
            Assert.False(handle.SnapsToDevicePixels);
            Assert.False(track.SnapsToDevicePixels);
            Assert.Equal(new Thickness(1), track.BorderThickness);

            foreach (var (side, horizontal, vertical, width, height) in
                new[]
                {
                    (EdgeDockSide.Left, HorizontalAlignment.Right,
                        VerticalAlignment.Center, 12d, 72d),
                    (EdgeDockSide.Right, HorizontalAlignment.Left,
                        VerticalAlignment.Center, 12d, 72d),
                    (EdgeDockSide.Top, HorizontalAlignment.Center,
                        VerticalAlignment.Bottom, 72d, 12d),
                    (EdgeDockSide.Bottom, HorizontalAlignment.Center,
                        VerticalAlignment.Top, 72d, 12d)
                })
            {
                window.ApplyEdgeVisualState(side, collapsed: true, animate: false);
                Assert.Equal(0, skin.Opacity);
                Assert.Equal(1, handle.Opacity);
                Assert.Equal(horizontal, handle.HorizontalAlignment);
                Assert.Equal(vertical, handle.VerticalAlignment);
                Assert.Equal(width, handle.Width);
                Assert.Equal(height, handle.Height);
                Assert.True(handle.IsHitTestVisible);
                Assert.Equal(
                    side switch
                    {
                        EdgeDockSide.Left => new Thickness(0, 0, 6, 0),
                        EdgeDockSide.Right => new Thickness(6, 0, 0, 0),
                        EdgeDockSide.Top => new Thickness(0, 0, 0, 6),
                        _ => new Thickness(0, 6, 0, 0)
                    },
                    handle.Margin);
            }

            viewModel.SelectedSkin = SkinId.Aurora;
            var theme = EdgeProgressThemeProvider.Get(SkinId.Aurora);
            Assert.Equal(
                theme.Track.ToString(),
                track.Background.ToString());
            Assert.Equal(theme.Border.ToString(), track.BorderBrush.ToString());
            Assert.Equal(theme.Fill.ToString(), fill.Background.ToString());
            Assert.Equal(
                theme.Texture.ToString(),
                texture.Background.ToString());
            var glow = Assert.IsType<DropShadowEffect>(handle.Effect);
            Assert.Equal(theme.GlowColor, glow.Color);
            Assert.Equal(theme.TextureOpacity, texture.Opacity);
            Assert.Equal(theme.GlowOpacity, glow.Opacity);
            Assert.True(theme.TextureOpacity <= 0.25);
            Assert.True(theme.GlowOpacity <= 0.45);
            var auroraAccent = theme.AccentColor;
            Assert.True(
                auroraAccent.G - auroraAccent.B >= 40,
                "Aurora edge progress should read as green, not cyan.");

            window.ApplyEdgeVisualState(
                EdgeDockSide.Bottom,
                collapsed: false,
                animate: false);
            Assert.Equal(1, skin.Opacity);
            Assert.Equal(0, handle.Opacity);
            Assert.False(handle.IsHitTestVisible);

            window.CloseForExit();
        });
    }

    [Fact]
    public void EnergyRing_UsesAQuietTextGlowAndEllipticalOrbit()
    {
        RunSta(() =>
        {
            using var viewModel = new QuotaOrbViewModel(
                new InertRefreshController(),
                new InMemorySettingsStore(),
                new AppSettings { SelectedSkin = SkinId.EnergyRing },
                new InlineDispatcher(),
                () => { });
            var window = new QuotaOrbWindow(viewModel);
            var skin = Assert.IsType<EnergyRingSkin>(
                Assert.IsType<ContentControl>(
                    window.FindName("SkinHost")).Content);
            var core = Assert.IsType<ShapeEllipse>(
                skin.FindName("EnergyCoreGlow"));
            var orbit = Assert.IsType<ShapeEllipse>(
                skin.FindName("EnergyOrbit"));

            Assert.IsType<RadialGradientBrush>(core.Fill);
            Assert.True(orbit.Width > orbit.Height);

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
