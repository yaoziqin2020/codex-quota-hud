using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.Infrastructure.LocalControl;
using CodexQuotaHud.App.UI.SkinManagement;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;
using ShapeEllipse = System.Windows.Shapes.Ellipse;
using ShapePath = System.Windows.Shapes.Path;

namespace CodexQuotaHud.App.Tests.UI;

[Collection(WpfUiCollection.Name)]
public sealed class QuotaOrbWindowStartupTests
{
    private const string CustomKey =
        "custom:11111111-1111-1111-1111-111111111111";

    [Fact]
    public void NormalSettingsLoad_RejectedCustomSaveFailureCannotShortCircuitAsSuccess()
    {
        const string missing =
            "custom:99999999-9999-9999-9999-999999999999";
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"CodexQuotaHud-CustomFallback-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            settingsPath,
            $$"""
            {
              "SelectedSkinKey": "{{missing}}"
            }
            """);

        try
        {
            RunSta(() =>
            {
                var catalog = HudSkinCatalog.CreateBuiltInOnly();
                var startup = global::CodexQuotaHud.App.App
                    .LoadNormalSkinSettings(settingsPath, catalog);
                Assert.Equal(missing, startup.Settings.SelectedSkinKey);
                Assert.Equal(missing, startup.RequestedSelectionKey);
                using var lockedTarget = new FileStream(
                    settingsPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read);
                using var viewModel = new QuotaOrbViewModel(
                    new InertRefreshController(),
                    startup.Store,
                    startup.Settings,
                    new InlineDispatcher(),
                    () => { },
                    key => catalog.TryGet(key, out _));
                var controller = new SkinController(
                    catalog,
                    descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                    SkinSelectionKey.HudDial);
                var messages = new List<string>();

                Assert.False(global::CodexQuotaHud.App.App.TryApplyStartupSkinSelection(
                    startup.RequestedSelectionKey,
                    viewModel,
                    controller,
                    messages.Add));

                Assert.Equal(missing, viewModel.SelectedSkinKey);
                Assert.Contains(missing, File.ReadAllText(settingsPath));
                var message = Assert.Single(messages);
                Assert.Contains("设置未能保存", message);
                Assert.DoesNotContain("已切换", message);
            });
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StartupFallback_MissingCustomPersistsHudDialAndShowsOneBoundedMessage()
    {
        RunSta(() =>
        {
            const string missing =
                "custom:99999999-9999-9999-9999-999999999999";
            var catalog = HudSkinCatalog.CreateBuiltInOnly();
            var controller = new SkinController(
                catalog,
                descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            var store = new RecordingSettingsStore(
                [],
                initial: new AppSettings(SelectedSkinKey: missing));
            using var viewModel = new QuotaOrbViewModel(
                new InertRefreshController(),
                store,
                store.Load(),
                new InlineDispatcher(),
                () => { },
                key => catalog.TryGet(key, out _));
            var messages = new List<string>();

            Assert.False(global::CodexQuotaHud.App.App.TryApplyStartupSkinSelection(
                missing,
                viewModel,
                controller,
                messages.Add));

            Assert.Equal(SkinSelectionKey.HudDial, viewModel.SelectedSkinKey);
            Assert.Equal(SkinSelectionKey.HudDial, store.Load().SelectedSkinKey);
            Assert.Equal(SkinSelectionKey.HudDial, controller.CurrentDescriptor.SelectionKey);
            var message = Assert.Single(messages);
            Assert.Contains("99999999-9999-9999-9999-999999999999", message);
            Assert.Contains("重新导入或删除", message);
            Assert.DoesNotContain(@"C:\", message, StringComparison.OrdinalIgnoreCase);

            controller.Render(new QuotaSkinState(
                68, 34, "5 hours", QuotaDisplayMode.Dual, false, true));
            Assert.Single(messages);
        });
    }

    [Fact]
    public void StartupFallback_FactoryFailurePersistsThenActivatesSafeHudDial()
    {
        RunSta(() =>
        {
            var catalog = CatalogWithCustom();
            var hud = new RecordingQuotaSkin(SkinSelectionKey.HudDial);
            var controller = new SkinController(
                catalog,
                descriptor => descriptor.SelectionKey == CustomKey
                    ? throw new InvalidOperationException("renderer failed")
                    : hud,
                SkinSelectionKey.HudDial);
            var store = new RecordingSettingsStore(
                [],
                initial: new AppSettings(SelectedSkinKey: CustomKey));
            using var viewModel = new QuotaOrbViewModel(
                new InertRefreshController(),
                store,
                store.Load(),
                new InlineDispatcher(),
                () => { },
                key => catalog.TryGet(key, out _));
            var messages = new List<string>();

            Assert.False(global::CodexQuotaHud.App.App.TryApplyStartupSkinSelection(
                CustomKey,
                viewModel,
                controller,
                messages.Add));

            Assert.Equal(SkinSelectionKey.HudDial, store.Load().SelectedSkinKey);
            Assert.Same(hud, controller.CurrentSkin);
            Assert.Contains("Ocean", Assert.Single(messages));
        });
    }

    [Fact]
    public void StartupFallback_ColdCustomFirstRenderFailurePersistsHudDialAndWindowStaysSafe()
    {
        RunSta(() =>
        {
            var catalog = CatalogWithCustom();
            var hud = new RecordingQuotaSkin(SkinSelectionKey.HudDial);
            var controller = new SkinController(
                catalog,
                descriptor => descriptor.SelectionKey == CustomKey
                    ? new RecordingQuotaSkin(
                        CustomKey,
                        () => throw new InvalidOperationException("render failed"))
                    : hud,
                SkinSelectionKey.HudDial);
            var store = new RecordingSettingsStore(
                [],
                initial: new AppSettings(SelectedSkinKey: CustomKey));
            using var viewModel = new QuotaOrbViewModel(
                new InertRefreshController(),
                store,
                store.Load(),
                new InlineDispatcher(),
                () => { },
                key => catalog.TryGet(key, out _));
            var messages = new List<string>();

            var applied = global::CodexQuotaHud.App.App.TryApplyStartupSkinSelection(
                CustomKey,
                viewModel,
                controller,
                messages.Add);
            QuotaOrbWindow? window = null;
            var windowFailure = Record.Exception(
                () => window = new QuotaOrbWindow(viewModel, controller));

            try
            {
                Assert.False(applied);
                Assert.Equal(SkinSelectionKey.HudDial, viewModel.SelectedSkinKey);
                Assert.Equal(SkinSelectionKey.HudDial, store.Load().SelectedSkinKey);
                Assert.Same(hud, controller.CurrentSkin);
                Assert.Null(windowFailure);
                Assert.Null(Record.Exception(
                    () => controller.Render(viewModel.SkinState)));
                Assert.Contains("Ocean", Assert.Single(messages));
            }
            finally
            {
                window?.CloseForExit();
            }
        });
    }

    [Fact]
    public void StartupFallback_SaveFailureKeepsSafeRuntimeWithoutClaimingDurableSuccess()
    {
        RunSta(() =>
        {
            var catalog = CatalogWithCustom();
            var controller = new SkinController(
                catalog,
                descriptor => descriptor.SelectionKey == CustomKey
                    ? new RecordingQuotaSkin(
                        CustomKey,
                        () => throw new InvalidOperationException("render failed"))
                    : new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            var safeRuntimeSkin = controller.CurrentSkin;
            var store = new RecordingSettingsStore(
                [],
                throwOnSave: true,
                initial: new AppSettings(SelectedSkinKey: CustomKey));
            using var viewModel = new QuotaOrbViewModel(
                new InertRefreshController(),
                store,
                store.Load(),
                new InlineDispatcher(),
                () => { },
                key => catalog.TryGet(key, out _));
            var messages = new List<string>();

            Assert.False(global::CodexQuotaHud.App.App.TryApplyStartupSkinSelection(
                CustomKey,
                viewModel,
                controller,
                messages.Add));

            Assert.Same(safeRuntimeSkin, controller.CurrentSkin);
            Assert.Equal(CustomKey, viewModel.SelectedSkinKey);
            var message = Assert.Single(messages);
            Assert.Contains("临时使用", message);
            Assert.Contains("设置未能保存", message);
            Assert.DoesNotContain("已切换", message);
        });
    }

    [Fact]
    public void InteractiveSelection_OrdersPrepareSaveActivate()
    {
        RunSta(() =>
        {
            var events = new List<string>();
            var store = new RecordingSettingsStore(events);
            var custom = new RecordingQuotaSkin(CustomKey, () => events.Add("render"));
            var controller = new SkinController(
                CatalogWithCustom(),
                descriptor =>
                {
                    if (descriptor.SelectionKey == CustomKey)
                    {
                        events.Add("prepare");
                        return custom;
                    }

                    return new RecordingQuotaSkin(descriptor.SelectionKey);
                },
                SkinSelectionKey.HudDial);
            controller.ActiveSkinChanged += (_, _) => events.Add("activate");
            using var viewModel = new QuotaOrbViewModel(
                new InertRefreshController(),
                store,
                new AppSettings(),
                new InlineDispatcher(),
                () => { },
                key => CatalogWithCustom().TryGet(key, out _));
            var window = new QuotaOrbWindow(viewModel, controller);
            events.Clear();

            Assert.True(window.TryActivateSkinKey(CustomKey));

            Assert.Equal(["prepare", "render", "save", "activate"], events);
            Assert.Equal(CustomKey, viewModel.SelectedSkinKey);
            Assert.Equal(CustomKey, controller.CurrentDescriptor.SelectionKey);
            Assert.Same(custom.View, Assert.IsType<ContentControl>(
                window.FindName("SkinHost")).Content);
            window.CloseForExit();
        });
    }

    [Fact]
    public void InteractiveSelection_PrepareOrSaveFailurePreservesKeyAndVisualInstance()
    {
        RunSta(() =>
        {
            var prepareFailure = new SkinController(
                CatalogWithCustom(),
                descriptor => descriptor.SelectionKey == CustomKey
                    ? throw new InvalidOperationException("factory failed")
                    : new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            var normalStore = new RecordingSettingsStore([]);
            using var firstViewModel = ViewModel(normalStore);
            var firstWindow = new QuotaOrbWindow(firstViewModel, prepareFailure);
            var firstVisual = Assert.IsType<ContentControl>(
                firstWindow.FindName("SkinHost")).Content;

            Assert.False(firstWindow.TryActivateSkinKey(CustomKey));
            Assert.Equal(SkinSelectionKey.HudDial, firstViewModel.SelectedSkinKey);
            Assert.Same(firstVisual, Assert.IsType<ContentControl>(
                firstWindow.FindName("SkinHost")).Content);
            Assert.Equal(0, normalStore.SaveCount);
            firstWindow.CloseForExit();

            var saveFailure = new SkinController(
                CatalogWithCustom(),
                descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            var throwingStore = new RecordingSettingsStore([], throwOnSave: true);
            using var secondViewModel = ViewModel(throwingStore);
            var secondWindow = new QuotaOrbWindow(secondViewModel, saveFailure);
            var secondVisual = Assert.IsType<ContentControl>(
                secondWindow.FindName("SkinHost")).Content;

            Assert.False(secondWindow.TryActivateSkinKey(CustomKey));
            Assert.Equal(SkinSelectionKey.HudDial, secondViewModel.SelectedSkinKey);
            Assert.Equal(SkinSelectionKey.HudDial, saveFailure.CurrentDescriptor.SelectionKey);
            Assert.Same(secondVisual, Assert.IsType<ContentControl>(
                secondWindow.FindName("SkinHost")).Content);
            secondWindow.CloseForExit();
        });
    }

    [Fact]
    public void LocalControlSelection_CancelledDuringPrepareNeverSavesOrActivates()
    {
        RunSta(() =>
        {
            using var cancellation = new CancellationTokenSource();
            var store = new RecordingSettingsStore([]);
            var controller = new SkinController(
                CatalogWithCustom(),
                descriptor => descriptor.SelectionKey == CustomKey
                    ? new RecordingQuotaSkin(CustomKey, cancellation.Cancel)
                    : new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            using var viewModel = ViewModel(store);
            var window = new QuotaOrbWindow(viewModel, controller);

            var activated = window.TryActivateSkinKey(
                CustomKey,
                cancellation.Token);

            Assert.False(activated);
            Assert.Equal(0, store.SaveCount);
            Assert.Equal(SkinSelectionKey.HudDial, store.Load().SelectedSkinKey);
            Assert.Equal(SkinSelectionKey.HudDial, viewModel.SelectedSkinKey);
            Assert.Equal(
                SkinSelectionKey.HudDial,
                controller.CurrentDescriptor.SelectionKey);
            window.CloseForExit();
        });
    }

    [Fact]
    public void LocalControlSelection_CancelledAtSaveActivateBoundaryRollsBackFormalState()
    {
        RunSta(() =>
        {
            using var cancellation = new CancellationTokenSource();
            var store = new CancellingSettingsStore(cancellation);
            var controller = new SkinController(
                CatalogWithCustom(),
                descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            using var viewModel = ViewModel(store);
            var window = new QuotaOrbWindow(viewModel, controller);

            var activated = window.TryActivateSkinKey(
                CustomKey,
                cancellation.Token);

            Assert.False(activated);
            Assert.Equal(2, store.SaveCount);
            Assert.Equal(SkinSelectionKey.HudDial, store.Load().SelectedSkinKey);
            Assert.Equal(SkinSelectionKey.HudDial, viewModel.SelectedSkinKey);
            Assert.Equal(
                SkinSelectionKey.HudDial,
                controller.CurrentDescriptor.SelectionKey);
            window.CloseForExit();
        });
    }

    [Fact]
    public void LocalControlSelection_CancelledAfterSaveBeforeActivateRollsBackBothStates()
    {
        RunSta(() =>
        {
            using var cancellation = new CancellationTokenSource();
            var store = new RecordingSettingsStore([]);
            var controller = new SkinController(
                CatalogWithCustom(),
                descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            using var viewModel = ViewModel(store);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(QuotaOrbViewModel.SelectedSkinKey) &&
                    viewModel.SelectedSkinKey == CustomKey)
                {
                    cancellation.Cancel();
                }
            };
            var window = new QuotaOrbWindow(viewModel, controller);

            var activated = window.TryActivateSkinKey(
                CustomKey,
                cancellation.Token);

            Assert.False(activated);
            Assert.Equal(2, store.SaveCount);
            Assert.Equal(SkinSelectionKey.HudDial, store.Load().SelectedSkinKey);
            Assert.Equal(SkinSelectionKey.HudDial, viewModel.SelectedSkinKey);
            Assert.Equal(
                SkinSelectionKey.HudDial,
                controller.CurrentDescriptor.SelectionKey);
            window.CloseForExit();
        });
    }

    [Fact]
    public void LocalControlSelection_BlockedActiveSkinChangedPastCutoffRollsBackEverything()
    {
        RunSta(() =>
        {
            const string targetKey = "builtin:EnergyRing";
            using var cancellation = new CancellationTokenSource();
            var store = new RecordingSettingsStore([]);
            var controller = new SkinController(
                CatalogWithCustom(),
                descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            using var viewModel = ViewModel(store);
            var window = new QuotaOrbWindow(viewModel, controller);
            var previousSkin = controller.CurrentSkin;
            var previousPresentation = controller.CurrentPresentation;
            var previousView = Assert.IsType<ContentControl>(
                window.FindName("SkinHost")).Content;
            var activationEvents = new List<string>();
            controller.ActiveSkinChanged += (_, _) =>
            {
                activationEvents.Add(controller.CurrentDescriptor.SelectionKey);
                if (string.Equals(
                        controller.CurrentDescriptor.SelectionKey,
                        targetKey,
                        StringComparison.Ordinal))
                {
                    cancellation.CancelAfter(TimeSpan.FromMilliseconds(75));
                    Assert.True(cancellation.Token.WaitHandle.WaitOne(
                        TimeSpan.FromSeconds(2)));
                }
            };

            var activated = window.TryActivateSkinKey(
                targetKey,
                cancellation.Token);

            Assert.False(activated);
            Assert.True(cancellation.IsCancellationRequested);
            Assert.Equal(2, store.SaveCount);
            Assert.Equal(SkinSelectionKey.HudDial, store.Load().SelectedSkinKey);
            Assert.Equal(SkinSelectionKey.HudDial, viewModel.SelectedSkinKey);
            Assert.Equal(
                SkinSelectionKey.HudDial,
                controller.CurrentDescriptor.SelectionKey);
            Assert.Same(previousSkin, controller.CurrentSkin);
            Assert.Same(previousPresentation, controller.CurrentPresentation);
            Assert.Same(previousView, Assert.IsType<ContentControl>(
                window.FindName("SkinHost")).Content);
            Assert.Equal([targetKey, SkinSelectionKey.HudDial], activationEvents);

            var menu = Assert.IsType<MenuItem>(window.FindName("SkinMenuRoot"));
            var checkedKeys = menu.Items
                .OfType<MenuItem>()
                .Where(item => item.IsChecked)
                .Select(item => Assert.IsType<string>(item.Tag))
                .ToArray();
            Assert.Equal([SkinSelectionKey.HudDial], checkedKeys);
            window.CloseForExit();
        });
    }

    [Fact]
    public void LocalControlSelection_RollbackSaveFailureKeepsFormalAndLiveStateAligned()
    {
        RunSta(() =>
        {
            const string targetKey = "builtin:EnergyRing";
            using var cancellation = new CancellationTokenSource();
            var store = new FailingRollbackSettingsStore();
            var controller = new SkinController(
                CatalogWithCustom(),
                descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            using var viewModel = ViewModel(store);
            var window = new QuotaOrbWindow(viewModel, controller);
            controller.ActiveSkinChanged += (_, _) =>
            {
                if (string.Equals(
                        controller.CurrentDescriptor.SelectionKey,
                        targetKey,
                        StringComparison.Ordinal))
                {
                    cancellation.CancelAfter(TimeSpan.FromMilliseconds(75));
                    Assert.True(cancellation.Token.WaitHandle.WaitOne(
                        TimeSpan.FromSeconds(2)));
                }
            };

            var activated = window.TryActivateSkinKey(
                targetKey,
                cancellation.Token);

            Assert.True(activated);
            Assert.Equal(2, store.SaveCount);
            Assert.Equal(targetKey, store.Load().SelectedSkinKey);
            Assert.Equal(targetKey, viewModel.SelectedSkinKey);
            Assert.Equal(targetKey, controller.CurrentDescriptor.SelectionKey);
            Assert.Same(
                controller.CurrentView,
                Assert.IsType<ContentControl>(
                    window.FindName("SkinHost")).Content);
            Assert.Null(viewModel.LastSettingsError);
            var menu = Assert.IsType<MenuItem>(window.FindName("SkinMenuRoot"));
            var checkedItem = Assert.Single(
                menu.Items.OfType<MenuItem>(),
                item => item.IsChecked);
            Assert.Equal(targetKey, checkedItem.Tag);
            window.CloseForExit();
        });
    }

    [Fact]
    public void LocalControlSelection_PreActivateRollbackSaveFailureCommitsPreparedTarget()
    {
        RunSta(() =>
        {
            const string targetKey = "builtin:EnergyRing";
            using var cancellation = new CancellationTokenSource();
            var store = new FailingRollbackSettingsStore();
            var controller = new SkinController(
                CatalogWithCustom(),
                descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            using var viewModel = ViewModel(store);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(QuotaOrbViewModel.SelectedSkinKey) &&
                    viewModel.SelectedSkinKey == targetKey)
                {
                    cancellation.Cancel();
                }
            };
            var window = new QuotaOrbWindow(viewModel, controller);

            var activated = window.TryActivateSkinKey(
                targetKey,
                cancellation.Token);

            Assert.True(activated);
            Assert.Equal(2, store.SaveCount);
            Assert.Equal(targetKey, store.Load().SelectedSkinKey);
            Assert.Equal(targetKey, viewModel.SelectedSkinKey);
            Assert.Equal(targetKey, controller.CurrentDescriptor.SelectionKey);
            Assert.Same(
                controller.CurrentView,
                Assert.IsType<ContentControl>(
                    window.FindName("SkinHost")).Content);
            Assert.Null(viewModel.LastSettingsError);
            var menu = Assert.IsType<MenuItem>(window.FindName("SkinMenuRoot"));
            var checkedItem = Assert.Single(
                menu.Items.OfType<MenuItem>(),
                item => item.IsChecked);
            Assert.Equal(targetKey, checkedItem.Tag);
            window.CloseForExit();
        });
    }

    [Fact]
    public void LocalControlSelection_ActivateEventFailureAndRollbackSaveFailureCommitsTarget()
    {
        RunSta(() =>
        {
            const string targetKey = "builtin:EnergyRing";
            var store = new FailingRollbackSettingsStore();
            var controller = new SkinController(
                CatalogWithCustom(),
                descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            using var viewModel = ViewModel(store);
            var window = new QuotaOrbWindow(viewModel, controller);
            controller.ActiveSkinChanged += (_, _) =>
            {
                if (string.Equals(
                        controller.CurrentDescriptor.SelectionKey,
                        targetKey,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("listener failed");
                }
            };

            var activated = window.TryActivateSkinKey(targetKey);

            Assert.True(activated);
            Assert.Equal(2, store.SaveCount);
            Assert.Equal(targetKey, store.Load().SelectedSkinKey);
            Assert.Equal(targetKey, viewModel.SelectedSkinKey);
            Assert.Equal(targetKey, controller.CurrentDescriptor.SelectionKey);
            Assert.Same(
                controller.CurrentView,
                Assert.IsType<ContentControl>(
                    window.FindName("SkinHost")).Content);
            Assert.Null(viewModel.LastSettingsError);
            var menu = Assert.IsType<MenuItem>(window.FindName("SkinMenuRoot"));
            var checkedItem = Assert.Single(
                menu.Items.OfType<MenuItem>(),
                item => item.IsChecked);
            Assert.Equal(targetKey, checkedItem.Tag);
            window.CloseForExit();
        });
    }

    [Fact]
    public async Task LocalControlPipeline_RollbackSaveFailureReturnsCommittedSuccess()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "CodexQuotaHud.Task10.RollbackPipeline",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var ready = new TaskCompletionSource<RollbackPipelineFixture>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<Exception?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var uiThread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                var catalog = CatalogWithCustom();
                var store = new FailingRollbackSettingsStore();
                var controller = new SkinController(
                    catalog,
                    descriptor => new RecordingQuotaSkin(descriptor.SelectionKey),
                    SkinSelectionKey.HudDial);
                var viewModel = ViewModel(store);
                var hudVersion = SemanticVersion.Parse("1.1.1");
                var management = new SkinManagementController(
                    new SkinPackageInstaller(
                        new SkinStoragePaths(root),
                        hudVersion),
                    catalog,
                    viewModel,
                    controller,
                    new DesignerLauncher(
                        Path.Combine(root, "app"),
                        _ => false,
                        _ => throw new InvalidOperationException()),
                    new RejectingSkinManagementDialogs(),
                    hudVersion,
                    new InlineDispatcher());
                var window = new QuotaOrbWindow(
                    viewModel,
                    controller,
                    management);
                var requestCancellation = CancellationToken.None;
                controller.ActiveSkinChanged += (_, _) =>
                {
                    if (string.Equals(
                            controller.CurrentDescriptor.SelectionKey,
                            CustomKey,
                            StringComparison.Ordinal) &&
                        !requestCancellation.WaitHandle.WaitOne(
                            TimeSpan.FromSeconds(2)))
                    {
                        throw new TimeoutException(
                            "The local-control commit cutoff was not observed.");
                    }
                };

                ready.SetResult(new RollbackPipelineFixture(
                    dispatcher,
                    (selectionKey, cancellationToken) =>
                    {
                        requestCancellation = cancellationToken;
                        return window.TryActivateSkinKey(
                            selectionKey,
                            cancellationToken);
                    },
                    () =>
                    {
                        var menu = Assert.IsType<MenuItem>(
                            window.FindName("SkinMenuRoot"));
                        var checkedItem = Assert.Single(
                            menu.Items.OfType<MenuItem>(),
                            item => item.IsChecked);
                        return new RollbackPipelineSnapshot(
                            store.SaveCount,
                            store.Load().SelectedSkinKey,
                            viewModel.SelectedSkinKey,
                            controller.CurrentDescriptor.SelectionKey,
                            Assert.IsType<string>(checkedItem.Tag),
                            ReferenceEquals(
                                controller.CurrentView,
                                Assert.IsType<ContentControl>(
                                    window.FindName("SkinHost")).Content),
                            viewModel.LastSettingsError);
                    },
                    () =>
                    {
                        window.CloseForExit();
                        viewModel.Dispose();
                        dispatcher.BeginInvokeShutdown(
                            DispatcherPriority.Send);
                    }));
                Dispatcher.Run();
                stopped.TrySetResult(null);
            }
            catch (Exception exception)
            {
                ready.TrySetException(exception);
                stopped.TrySetResult(exception);
            }
        });
        uiThread.SetApartmentState(ApartmentState.STA);
        uiThread.Start();

        RollbackPipelineFixture? fixture = null;
        try
        {
            fixture = await ready.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var handler = new LocalControlActivationHandler(
                key => CatalogWithCustom().TryGet(key, out _),
                (key, cancellationToken) =>
                    LocalControlActivationHandler.InvokeOnDispatcherAsync(
                        fixture.Dispatcher,
                        token => fixture.Activate(key, token),
                        cancellationToken));
            var pipeName = $"CodexQuotaHud.Task10.{Guid.NewGuid():N}";
            await using (var server = new LocalControlServer(
                pipeName,
                handler.HandleAsync))
            {
                server.Start();
                var response = await new LocalControlClient(pipeName).SendAsync(
                    new LocalControlRequest(
                        LocalControlProtocol.ProtocolVersion,
                        LocalControlCommandKind.ActivateSkin,
                        CustomKey));

                Assert.True(response.Succeeded);
                Assert.Null(response.ErrorCode);
                Assert.Null(response.Message);
            }

            var snapshot = await fixture.Dispatcher.InvokeAsync(
                fixture.Snapshot,
                DispatcherPriority.Send).Task;
            Assert.Equal(2, snapshot.SaveCount);
            Assert.Equal(CustomKey, snapshot.PersistedSelectionKey);
            Assert.Equal(CustomKey, snapshot.ViewModelSelectionKey);
            Assert.Equal(CustomKey, snapshot.ControllerSelectionKey);
            Assert.Equal(CustomKey, snapshot.CheckedMenuSelectionKey);
            Assert.True(snapshot.ViewMatchesController);
            Assert.Null(snapshot.LastSettingsError);
        }
        finally
        {
            if (fixture is not null && !fixture.Dispatcher.HasShutdownStarted)
            {
                await fixture.Dispatcher.InvokeAsync(
                    fixture.Dispose,
                    DispatcherPriority.Send).Task;
            }

            var threadFailure = await stopped.Task.WaitAsync(
                TimeSpan.FromSeconds(3));
            Assert.Null(threadFailure);
            Assert.True(uiThread.Join(TimeSpan.FromSeconds(2)));
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void InteractiveSelection_FirstRenderFailureOccursBeforeSaveAndPreservesEverything()
    {
        RunSta(() =>
        {
            var catalog = CatalogWithCustom();
            var store = new RecordingSettingsStore([]);
            var controller = new SkinController(
                catalog,
                descriptor => descriptor.SelectionKey == CustomKey
                    ? new RecordingQuotaSkin(
                        CustomKey,
                        () => throw new InvalidOperationException("render failed"))
                    : new RecordingQuotaSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            using var viewModel = new QuotaOrbViewModel(
                new InertRefreshController(),
                store,
                new AppSettings(),
                new InlineDispatcher(),
                () => { },
                key => catalog.TryGet(key, out _));
            var window = new QuotaOrbWindow(viewModel, controller);
            var previousSkin = controller.CurrentSkin;
            var previousVisual = Assert.IsType<ContentControl>(
                window.FindName("SkinHost")).Content;

            Assert.False(window.TryActivateSkinKey(CustomKey));

            Assert.Equal(0, store.SaveCount);
            Assert.Equal(SkinSelectionKey.HudDial, viewModel.SelectedSkinKey);
            Assert.Same(previousSkin, controller.CurrentSkin);
            Assert.Same(previousVisual, Assert.IsType<ContentControl>(
                window.FindName("SkinHost")).Content);
            window.CloseForExit();
        });
    }

    [Fact]
    public void StartupMigration_WhenSaveFails_ContinuesWithMigratedSettings()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"CodexQuotaHud-StartupMigration-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            settingsPath,
            """
            {
              "Left": 12.5,
              "Top": 34.5,
              "AnimationsEnabled": false,
              "SelectedSkin": "Aurora",
              "LastSuccessfulRefresh": "2026-08-02T01:02:03+09:00"
            }
            """);

        try
        {
            var store = new SettingsStore(settingsPath);
            using var lockedTarget = new FileStream(
                settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);

            AppSettings? settings = null;
            var exception = Record.Exception(
                () => settings = global::CodexQuotaHud.App.App
                    .LoadSettingsForStartup(store));

            Assert.Null(exception);
            Assert.NotNull(settings);
            Assert.Equal(SkinSelectionKey.Aurora, settings.SelectedSkinKey);
            Assert.Equal(12.5, settings.Left);
            Assert.Equal(34.5, settings.Top);
            Assert.False(settings.AnimationsEnabled);
            Assert.Equal(
                DateTimeOffset.Parse("2026-08-02T01:02:03+09:00"),
                settings.LastSuccessfulRefresh);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

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

            Assert.True(window.TryActivateSkinKey(SkinSelectionKey.LiquidTank));
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
            var outline = Assert.IsType<Border>(
                window.FindName("EdgeProgressOutline"));
            var fill = Assert.IsType<Border>(
                window.FindName("EdgeProgressFill"));
            Assert.Equal(new CornerRadius(3), handle.CornerRadius);
            Assert.Equal(new CornerRadius(3), track.CornerRadius);
            Assert.Equal(new CornerRadius(3), fill.CornerRadius);
            Assert.Equal(new CornerRadius(3), outline.CornerRadius);
            var texture = Assert.IsType<Border>(
                window.FindName("EdgeProgressTexture"));
            Assert.Null(window.FindName("EdgeProgressSheen"));
            Assert.False(handle.SnapsToDevicePixels);
            Assert.False(track.SnapsToDevicePixels);
            Assert.Equal(default, track.BorderThickness);
            Assert.Equal(new Thickness(1), outline.BorderThickness);

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
                    side is EdgeDockSide.Left or EdgeDockSide.Right
                        ? new Thickness(0, 6, 0, 6)
                        : new Thickness(6, 0, 6, 0),
                    texture.Margin);
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

            Assert.True(window.TryActivateSkinKey(SkinSelectionKey.Aurora));
            var theme = EdgeProgressThemeProvider.Get(SkinId.Aurora);
            Assert.Equal(
                theme.Track.ToString(),
                track.Background.ToString());
            Assert.Equal(
                theme.Border.ToString(),
                outline.BorderBrush.ToString());
            Assert.Equal(
                byte.MaxValue,
                Assert.IsType<SolidColorBrush>(theme.Border).Color.A);
            Assert.Equal(theme.Fill.ToString(), fill.Background.ToString());
            Assert.Equal(
                theme.Texture.ToString(),
                texture.Background.ToString());
            var glow = Assert.IsType<DropShadowEffect>(handle.Effect);
            Assert.Equal(theme.GlowColor, glow.Color);
            Assert.Equal(theme.TextureOpacity, texture.Opacity);
            Assert.Equal(theme.GlowOpacity, glow.Opacity);
            Assert.True(theme.TextureOpacity <= 0.3);
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
    public void EdgeHandle_LowQuotaOverridesOnlyAlertAccents()
    {
        RunSta(() =>
        {
            var refresh = new MutableRefreshController();
            using var viewModel = new QuotaOrbViewModel(
                refresh,
                new InMemorySettingsStore(),
                new AppSettings(SelectedSkinKey: SkinSelectionKey.Aurora),
                new InlineDispatcher(),
                () => { });
            var window = new QuotaOrbWindow(viewModel);
            var theme = EdgeProgressThemeProvider.Get(SkinId.Aurora);
            var handle = Assert.IsType<Border>(window.FindName("EdgeHandle"));
            var track = Assert.IsType<Border>(window.FindName("EdgeProgressTrack"));
            var outline = Assert.IsType<Border>(window.FindName("EdgeProgressOutline"));
            var fill = Assert.IsType<Border>(window.FindName("EdgeProgressFill"));
            var texture = Assert.IsType<Border>(window.FindName("EdgeProgressTexture"));
            var glow = Assert.IsType<DropShadowEffect>(handle.Effect);

            refresh.Publish(DisplayState(20));
            window.ApplyEdgeVisualState(
                EdgeDockSide.Left,
                collapsed: true,
                animate: false);

            Assert.Same(QuotaAlertPalette.WarningBrush, fill.Background);
            Assert.Same(QuotaAlertPalette.WarningBrush, outline.BorderBrush);
            Assert.Equal(QuotaAlertPalette.WarningMediaColor, glow.Color);
            Assert.Equal(theme.Track.ToString(), track.Background.ToString());
            Assert.Equal(theme.Texture.ToString(), texture.Background.ToString());
            Assert.Equal(theme.TextureOpacity, texture.Opacity);
            Assert.Equal(theme.GlowOpacity, glow.Opacity);

            refresh.Publish(DisplayState(10));

            Assert.Same(QuotaAlertPalette.CriticalBrush, fill.Background);
            Assert.Same(QuotaAlertPalette.CriticalBrush, outline.BorderBrush);
            Assert.Equal(QuotaAlertPalette.CriticalMediaColor, glow.Color);
            Assert.Equal(theme.Track.ToString(), track.Background.ToString());
            Assert.Equal(theme.Texture.ToString(), texture.Background.ToString());
            Assert.Equal(theme.TextureOpacity, texture.Opacity);
            Assert.Equal(theme.GlowOpacity, glow.Opacity);

            refresh.Publish(DisplayState(75));

            Assert.Equal(theme.Track.ToString(), track.Background.ToString());
            Assert.Equal(theme.Border.ToString(), outline.BorderBrush.ToString());
            Assert.Equal(theme.Fill.ToString(), fill.Background.ToString());
            Assert.Equal(theme.Texture.ToString(), texture.Background.ToString());
            Assert.Equal(theme.GlowColor, glow.Color);
            Assert.Equal(theme.TextureOpacity, texture.Opacity);
            Assert.Equal(theme.GlowOpacity, glow.Opacity);

            window.CloseForExit();
        });
    }

    [Fact]
    public void DetailRows_UseSharedAlertBrushesAndRestoreSkinAccent()
    {
        RunSta(() =>
        {
            var refresh = new MutableRefreshController();
            using var viewModel = new QuotaOrbViewModel(
                refresh,
                new InMemorySettingsStore(),
                new AppSettings(SelectedSkinKey: SkinSelectionKey.LiquidTank),
                new InlineDispatcher(),
                () => { });
            var window = new QuotaOrbWindow(viewModel);
            window.Show();
            var details = Assert.IsType<ItemsControl>(
                window.FindName("DetailsItems"));
            var popup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(
                window.FindName("DetailsPopup"));
            popup.IsOpen = true;

            refresh.Publish(DisplayState(20, 10));
            details.ApplyTemplate();
            details.UpdateLayout();

            Assert.Same(
                QuotaAlertPalette.WarningBrush,
                RemainingText(details, 0).Foreground);
            Assert.Same(
                QuotaAlertPalette.CriticalBrush,
                RemainingText(details, 1).Foreground);

            refresh.Publish(DisplayState(75, 80));
            details.ApplyTemplate();
            details.UpdateLayout();

            var theme = PopupThemeProvider.Get(SkinId.LiquidTank);
            Assert.Equal(
                theme.Accent.ToString(),
                RemainingText(details, 0).Foreground.ToString());
            Assert.Equal(
                theme.Accent.ToString(),
                RemainingText(details, 1).Foreground.ToString());

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
                new AppSettings(SelectedSkinKey: SkinSelectionKey.EnergyRing),
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
            var shell = Assert.IsType<ShapeEllipse>(
                skin.FindName("EnergyShell"));
            var status = Assert.IsType<TextBlock>(
                skin.FindName("RefreshGlyph"));

            Assert.IsType<RadialGradientBrush>(core.Fill);
            Assert.True(orbit.Width > orbit.Height);
            Assert.InRange(
                Assert.IsType<DropShadowEffect>(shell.Effect).Opacity,
                0,
                0.25);
            Assert.True(status.FontSize >= 7);
            Assert.True(
                Assert.IsType<SolidColorBrush>(status.Foreground).Color.R >=
                0xC8);

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

    private static QuotaOrbViewModel ViewModel(ISettingsStore store) =>
        new(
            new InertRefreshController(),
            store,
            new AppSettings(),
            new InlineDispatcher(),
            () => { },
            key => CatalogWithCustom().TryGet(key, out _));

    private static HudSkinCatalog CatalogWithCustom()
    {
        var package = HudSkinCatalogTests.Document();
        var record = new InstalledSkinRecord(
            CustomKey,
            package.Manifest.SkinId,
            package.Manifest.DisplayName,
            package.Manifest.PackageVersion,
            @"C:\Catalog\11111111-1111-1111-1111-111111111111",
            package);
        return new HudSkinCatalog(new InstalledSkinCatalogResult([record], []));
    }

    private static TextBlock RemainingText(ItemsControl details, int index)
    {
        var presenter = Assert.IsType<ContentPresenter>(
            details.ItemContainerGenerator.ContainerFromIndex(index));
        return Assert.IsType<TextBlock>(
            details.ItemTemplate.FindName("RemainingText", presenter));
    }

    private static QuotaRefreshState DisplayState(
        double primary,
        double? secondary = null)
    {
        var fiveHour = new QuotaWindow(
            QuotaWindowKind.FiveHour,
            primary,
            null);
        var weekly = secondary is null
            ? null
            : new QuotaWindow(QuotaWindowKind.Weekly, secondary.Value, null);
        return new QuotaRefreshState(
            IsCodexRunning: true,
            IsRefreshing: false,
            Display: QuotaDisplayState.FromSnapshot(new QuotaSnapshot(
                fiveHour,
                weekly,
                DateTimeOffset.Parse("2026-07-31T00:00:00Z"))),
            LastError: null);
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

    private sealed class MutableRefreshController : IQuotaRefreshController
    {
        public event Action<QuotaRefreshState>? StateChanged;

        public Task RefreshNowAsync(
            bool onlyIfStale,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Publish(QuotaRefreshState state) => StateChanged?.Invoke(state);
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private AppSettings _settings = new();

        public AppSettings Load() => _settings;

        public void Save(AppSettings settings) => _settings = settings;
    }

    private sealed class RecordingSettingsStore(
        List<string> events,
        bool throwOnSave = false,
        AppSettings? initial = null) : ISettingsStore
    {
        private AppSettings _settings = initial ?? new();

        public int SaveCount { get; private set; }

        public AppSettings Load() => _settings;

        public void Save(AppSettings settings)
        {
            SaveCount++;
            events.Add("save");
            if (throwOnSave)
            {
                throw new UnauthorizedAccessException("read only");
            }

            _settings = settings;
        }
    }

    private sealed class CancellingSettingsStore(
        CancellationTokenSource cancellation) : ISettingsStore
    {
        private AppSettings _settings = new();

        public int SaveCount { get; private set; }

        public AppSettings Load() => _settings;

        public void Save(AppSettings settings)
        {
            SaveCount++;
            _settings = settings;
            if (SaveCount == 1)
            {
                cancellation.Cancel();
            }
        }
    }

    private sealed class FailingRollbackSettingsStore : ISettingsStore
    {
        private AppSettings _settings = new();

        public int SaveCount { get; private set; }

        public AppSettings Load() => _settings;

        public void Save(AppSettings settings)
        {
            SaveCount++;
            if (SaveCount == 2)
            {
                throw new UnauthorizedAccessException("rollback denied");
            }

            _settings = settings;
        }
    }

    private sealed record RollbackPipelineFixture(
        Dispatcher Dispatcher,
        Func<string, CancellationToken, bool> Activate,
        Func<RollbackPipelineSnapshot> Snapshot,
        Action Dispose);

    private sealed record RollbackPipelineSnapshot(
        int SaveCount,
        string PersistedSelectionKey,
        string ViewModelSelectionKey,
        string ControllerSelectionKey,
        string CheckedMenuSelectionKey,
        bool ViewMatchesController,
        string? LastSettingsError);

    private sealed class RejectingSkinManagementDialogs : ISkinManagementDialogs
    {
        public string? ChoosePackagePath() => null;

        public SkinCollisionDecision ShowImportPreview(SkinInstallPreview preview) =>
            SkinCollisionDecision.Cancel;

        public bool ConfirmRemoval(SkinMenuEntry entry) => false;

        public void ShowError(string message) =>
            throw new InvalidOperationException(message);
    }

    private sealed class RecordingQuotaSkin(
        string selectionKey,
        Action? onRender = null) : IQuotaSkin
    {
        public string SelectionKey { get; } = selectionKey;

        public FrameworkElement View { get; } = new Border();

        public void Render(QuotaSkinState state) => onRender?.Invoke();
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();
    }
}
