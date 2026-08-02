using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.SkinManagement;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;
using Forms = System.Windows.Forms;

namespace CodexQuotaHud.App.Tests.UI;

[Collection(WpfUiCollection.Name)]
public sealed class TraySkinMenuTests
{
    [Fact]
    public void WpfAndTrayBuildersExposeIdenticalOrderedEntriesAndClickActions() =>
        RunSta(() =>
        {
            var entries = Entries();
            var wpfRoot = new MenuItem();
            using var trayRoot = new Forms.ToolStripMenuItem();
            var wpfCalls = new List<string>();
            var trayCalls = new List<string>();

            QuotaOrbWindow.RebuildSkinMenu(
                wpfRoot,
                entries,
                designerAvailable: true,
                key => wpfCalls.Add($"select:{key}"),
                key =>
                {
                    wpfCalls.Add($"remove:{key}");
                    return Task.CompletedTask;
                },
                () =>
                {
                    wpfCalls.Add("import");
                    return Task.CompletedTask;
                },
                () => wpfCalls.Add("designer"));
            TrayController.RebuildSkinMenu(
                trayRoot,
                entries,
                designerAvailable: true,
                key => trayCalls.Add($"select:{key}"),
                key =>
                {
                    trayCalls.Add($"remove:{key}");
                    return Task.CompletedTask;
                },
                () =>
                {
                    trayCalls.Add("import");
                    return Task.CompletedTask;
                },
                () => trayCalls.Add("designer"));

            Assert.Equal(
                ["HUD 科技仪表", "双彩能量环", "流体玻璃球", "克制极光", "液位储能舱", "Ocean", "|", "导入皮肤…", "打开皮肤设计器"],
                WpfLabels(wpfRoot));
            Assert.Equal(WpfLabels(wpfRoot), TrayLabels(trayRoot));
            Assert.DoesNotContain(
                "--preview",
                string.Join("|", WpfLabels(wpfRoot)),
                StringComparison.Ordinal);

            ((MenuItem)wpfRoot.Items[0]).RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));
            var wpfCustom = (MenuItem)wpfRoot.Items[5];
            ((MenuItem)wpfCustom.Items[0]).RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));
            ((MenuItem)wpfCustom.Items[1]).RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));
            ((MenuItem)wpfRoot.Items[7]).RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));
            ((MenuItem)wpfRoot.Items[8]).RaiseEvent(
                new RoutedEventArgs(MenuItem.ClickEvent));

            ((Forms.ToolStripMenuItem)trayRoot.DropDownItems[0]).PerformClick();
            var trayCustom = (Forms.ToolStripMenuItem)trayRoot.DropDownItems[5];
            ((Forms.ToolStripMenuItem)trayCustom.DropDownItems[0]).PerformClick();
            ((Forms.ToolStripMenuItem)trayCustom.DropDownItems[1]).PerformClick();
            ((Forms.ToolStripMenuItem)trayRoot.DropDownItems[7]).PerformClick();
            ((Forms.ToolStripMenuItem)trayRoot.DropDownItems[8]).PerformClick();

            var expectedCalls = new[]
            {
                $"select:{SkinSelectionKey.HudDial}",
                "select:custom:11111111-1111-1111-1111-111111111111",
                "remove:custom:11111111-1111-1111-1111-111111111111",
                "import",
                "designer"
            };
            Assert.Equal(expectedCalls, wpfCalls);
            Assert.Equal(expectedCalls, trayCalls);
            Assert.True(((MenuItem)wpfRoot.Items[0]).IsChecked);
            Assert.True(((Forms.ToolStripMenuItem)trayRoot.DropDownItems[0]).Checked);
        });

    [Fact]
    public void DesignerEntryIsOmittedWhenExactExecutableIsUnavailable() =>
        RunSta(() =>
        {
            var wpfRoot = new MenuItem();
            using var trayRoot = new Forms.ToolStripMenuItem();
            QuotaOrbWindow.RebuildSkinMenu(
                wpfRoot,
                Entries(),
                designerAvailable: false,
                _ => { },
                _ => Task.CompletedTask,
                () => Task.CompletedTask,
                () => { });
            TrayController.RebuildSkinMenu(
                trayRoot,
                Entries(),
                designerAvailable: false,
                _ => { },
                _ => Task.CompletedTask,
                () => Task.CompletedTask,
                () => { });

            Assert.DoesNotContain("打开皮肤设计器", WpfLabels(wpfRoot));
            Assert.Equal(WpfLabels(wpfRoot), TrayLabels(trayRoot));
        });

    [Fact]
    public void RealWindowAndTrayShareControllerSynchronizeAndUnsubscribe() =>
        RunSta(() =>
        {
            using var fixture = new CompositionFixture();
            var window = new QuotaOrbWindow(
                fixture.ViewModel,
                fixture.SkinController,
                fixture.Management);
            var tray = new TrayController(
                fixture.ViewModel,
                fixture.Catalog,
                fixture.SkinController,
                window.TryActivateSkinKey,
                fixture.Management);
            var wpfRoot = Assert.IsType<MenuItem>(window.FindName("SkinMenuRoot"));
            var trayRoot = GetTraySkinMenu(tray);
            var catalogEvents = 0;
            fixture.Management.CatalogChanged += (_, _) => catalogEvents++;

            try
            {
                Assert.Equal(WpfLabels(wpfRoot), TrayLabels(trayRoot));
                Assert.Equal(
                    ["HUD 科技仪表", "双彩能量环", "流体玻璃球", "克制极光", "液位储能舱", "|", "导入皮肤…"],
                    WpfLabels(wpfRoot));

                ((MenuItem)wpfRoot.Items[6]).RaiseEvent(
                    new RoutedEventArgs(MenuItem.ClickEvent));
                ((Forms.ToolStripMenuItem)trayRoot.DropDownItems[6]).PerformClick();

                Assert.Equal(2, fixture.Dialogs.ChooseCount);
                Assert.Equal(0, fixture.Dialogs.PreviewCount);
                Assert.Equal(0, catalogEvents);

                var importedId =
                    Guid.Parse("30303030-1111-1111-1111-111111111111");
                fixture.Dialogs.ChosenPackagePath = fixture.WritePackage(importedId);
                ((MenuItem)wpfRoot.Items[6]).RaiseEvent(
                    new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal(3, fixture.Dialogs.ChooseCount);
                Assert.Equal(1, fixture.Dialogs.PreviewCount);
                Assert.Equal(1, catalogEvents);
                Assert.Equal(WpfLabels(wpfRoot), TrayLabels(trayRoot));
                Assert.Contains("Imported skin", WpfLabels(wpfRoot));

                var customKey = $"custom:{importedId:D}";
                var wpfCustom = Assert.IsType<MenuItem>(wpfRoot.Items[5]);
                ((MenuItem)wpfCustom.Items[0]).RaiseEvent(
                    new RoutedEventArgs(MenuItem.ClickEvent));

                Assert.Equal(customKey, fixture.ViewModel.SelectedSkinKey);
                Assert.Equal(
                    customKey,
                    fixture.SkinController.CurrentDescriptor.SelectionKey);
                Assert.True(Assert.IsType<MenuItem>(wpfRoot.Items[5]).IsChecked);
                Assert.True(Assert.IsType<Forms.ToolStripMenuItem>(
                    trayRoot.DropDownItems[5]).Checked);

                var wpfBeforeClose = WpfLabels(wpfRoot);
                var trayBeforeDispose = TrayLabels(trayRoot);
                window.CloseForExit();
                tray.Dispose();
                fixture.Dialogs.ChosenPackagePath = fixture.WritePackage(
                    Guid.Parse("30303030-2222-2222-2222-222222222222"));

                var afterDispose = fixture.Management.ChooseAndImportAsync()
                    .GetAwaiter()
                    .GetResult();

                Assert.True(afterDispose!.Succeeded);
                Assert.Equal(2, catalogEvents);
                Assert.Equal(wpfBeforeClose, WpfLabels(wpfRoot));
                Assert.Equal(trayBeforeDispose, TrayLabels(trayRoot));
            }
            finally
            {
                window.CloseForExit();
                tray.Dispose();
            }
        });

    private static IReadOnlyList<SkinMenuEntry> Entries() =>
    [
        new(SkinSelectionKey.HudDial, "HUD 科技仪表", true, false),
        new(SkinSelectionKey.EnergyRing, "双彩能量环", false, false),
        new(SkinSelectionKey.LiquidGlass, "流体玻璃球", false, false),
        new(SkinSelectionKey.Aurora, "克制极光", false, false),
        new(SkinSelectionKey.LiquidTank, "液位储能舱", false, false),
        new("custom:11111111-1111-1111-1111-111111111111", "Ocean", false, true)
    ];

    private static string[] WpfLabels(MenuItem root) => root.Items
        .Cast<object>()
        .Select(item => item is Separator ? "|" : ((MenuItem)item).Header.ToString()!)
        .ToArray();

    private static string[] TrayLabels(Forms.ToolStripMenuItem root) => root.DropDownItems
        .Cast<Forms.ToolStripItem>()
        .Select(item => item is Forms.ToolStripSeparator ? "|" : item.Text ?? string.Empty)
        .ToArray();

    private static Forms.ToolStripMenuItem GetTraySkinMenu(TrayController tray) =>
        Assert.IsType<Forms.ToolStripMenuItem>(typeof(TrayController)
            .GetField("_skinMenu", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(tray));

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
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private sealed class CompositionFixture : IDisposable
    {
        private static readonly SemanticVersion HudVersion =
            SemanticVersion.Parse("1.1.1");

        public CompositionFixture()
        {
            Root = Path.Combine(
                Path.GetTempPath(),
                "CodexQuotaHud.Task9.Menu",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Paths = new SkinStoragePaths(Root);
            Catalog = new HudSkinCatalog(new InstalledSkinCatalog(
                Paths,
                HudVersion));
            var settings = new SettingsStore(
                Path.Combine(Root, "persist", "settings.json"),
                key => Catalog.TryGet(key, out _));
            ViewModel = new QuotaOrbViewModel(
                new SilentRefreshController(),
                settings,
                new AppSettings(SelectedSkinKey: SkinSelectionKey.HudDial),
                new ImmediateDispatcher(),
                static () => { },
                key => Catalog.TryGet(key, out _));
            SkinController = new SkinController(
                Catalog,
                descriptor => new MenuSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            SkinController.Render(ViewModel.SkinState);
            Dialogs = new RecordingDialogs();
            Management = new SkinManagementController(
                new SkinPackageInstaller(Paths, HudVersion),
                Catalog,
                ViewModel,
                SkinController,
                new DesignerLauncher(
                    Path.Combine(Root, "app"),
                    _ => false,
                    _ => throw new InvalidOperationException()),
                Dialogs,
                HudVersion,
                new ImmediateDispatcher());
        }

        public string Root { get; }

        public SkinStoragePaths Paths { get; }

        public HudSkinCatalog Catalog { get; }

        public QuotaOrbViewModel ViewModel { get; }

        public SkinController SkinController { get; }

        public RecordingDialogs Dialogs { get; }

        public SkinManagementController Management { get; }

        public string WritePackage(Guid skinId)
        {
            var destination = Path.Combine(Root, $"{skinId:D}.cqskin");
            var identity = new SkinImageTransform(0, 0, 1, 0, 1, 0.5, 0.5);
            var result = new SkinPackageWriter().WriteFile(
                destination,
                new SkinPackageBuildRequest(
                    new SkinManifest(
                        1,
                        skinId,
                        "Imported skin",
                        "Unverified author",
                        SemanticVersion.Parse("1.0.0"),
                        "Menu integration fixture",
                        SkinPackageLimits.FreeDecorationRingTemplateId,
                        HudVersion,
                        null,
                        []),
                    new SkinTheme(
                        1,
                        SkinPackageLimits.FreeDecorationRingTemplateId,
                        identity,
                        identity,
                        identity,
                        "#FF53DCF8",
                        "#FF9A68FF",
                        "#FF0A1622",
                        0.9,
                        96,
                        8,
                        6,
                        270,
                        "#FF24CFF2",
                        0.5,
                        28,
                        12,
                        SkinTextWeight.SemiBold,
                        SkinTextPlacement.NumberAboveLabel,
                        new SkinAnimationSettings(0.25, 0.5, 0.75, 1)),
                    new Dictionary<SkinAssetSlot, SkinAsset>()),
                overwrite: false,
                CancellationToken.None);
            Assert.True(result.IsValid);
            return destination;
        }

        public void Dispose()
        {
            ViewModel.Dispose();
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

    private sealed class RecordingDialogs : ISkinManagementDialogs
    {
        public int ChooseCount { get; private set; }

        public int PreviewCount { get; private set; }

        public string? ChosenPackagePath { get; set; }

        public string? ChoosePackagePath()
        {
            ChooseCount++;
            return ChosenPackagePath;
        }

        public SkinCollisionDecision ShowImportPreview(SkinInstallPreview preview)
        {
            PreviewCount++;
            return SkinCollisionDecision.Replace;
        }

        public bool ConfirmRemoval(SkinMenuEntry entry) => false;

        public void ShowError(string message) =>
            throw new Xunit.Sdk.XunitException(message);
    }

    private sealed class MenuSkin(string selectionKey) : IQuotaSkin
    {
        public string SelectionKey { get; } = selectionKey;

        public FrameworkElement View { get; } = new Border();

        public void Render(QuotaSkinState state)
        {
        }
    }

    private sealed class SilentRefreshController : IQuotaRefreshController
    {
        public event Action<CodexQuotaHud.Core.Refresh.QuotaRefreshState>? StateChanged
        {
            add { }
            remove { }
        }

        public Task RefreshNowAsync(
            bool onlyIfStale,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();
    }
}
