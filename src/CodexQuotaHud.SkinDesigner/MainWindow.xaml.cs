using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.SkinDesigner.Documents;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Images;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.SkinDesigner.Preview;
using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;
using CodexQuotaHud.Skins.Validation;
using Microsoft.Win32;

namespace CodexQuotaHud.SkinDesigner;

public partial class MainWindow : Window, IDesignerWindow
{
    private readonly SkinDraftSession _session;
    private readonly DraftStore _store;
    private readonly SkinStoragePaths _paths;
    private readonly IUnsavedChangesDialog _dialog;
    private readonly DesignerDocumentService _documents;
    private readonly IDesignerDocumentRequestSource _documentRequests;
    private readonly DesignerOutputServices? _outputServices;
    private readonly DesignerWindowOwner? _outputWindowOwner;
    private readonly Func<DesignerDocumentResult, IDesignerWindow>
        _createReplacementWindow;
    private readonly IDesignerMonitorWorkAreaSource _monitorWorkArea;
    private readonly Action<Action> _systemEventDispatcherPost;
    private readonly DraftRecoveryService _recovery;
    private readonly DraftCloseCoordinator _closeCoordinator;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly SyntheticPreviewComposition _previewComposition;
    private readonly DesignerPreviewController _previewController;
    private int _closingRequest;
    private int _disposed;
    private bool _finalCloseAllowed;
    private bool _previewOwnerAttached;
    private bool _previewDisposed;
    private bool _suppressPreviewShowForTesting;
    private int _closeCoordinatorRequestCount;
    private bool _updatingImageTransformControls;
    private bool _restoringEditorControl;
    private bool _editorControlsReady;
    private bool _applyingLayout;
    private bool _systemEventsAttached;
    private int _layoutRefreshQueued;
    private Rect? _latestPreviewWorkArea;
    private Task _saveOperationForTesting = Task.CompletedTask;
    private Task _closeOperationForTesting = Task.CompletedTask;
    private Task _documentOperationForTesting = Task.CompletedTask;

    internal MainWindow(SkinDraftDocument draft)
        : this(
            draft,
            new SkinStoragePaths(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData)),
            new WindowsUnsavedChangesDialog())
    {
    }

    internal MainWindow(
        SkinDraftDocument draft,
        SkinStoragePaths paths,
        IUnsavedChangesDialog dialog)
        : this(
            draft,
            new Dictionary<SkinAssetSlot, SkinAsset>(),
            paths,
            dialog,
            CreateDocumentService(paths),
            new WindowsDesignerDocumentRequestSource(paths))
    {
    }

    internal MainWindow(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets,
        SkinStoragePaths paths,
        IUnsavedChangesDialog dialog,
        DesignerDocumentService documents,
        IDesignerDocumentRequestSource documentRequests,
        Func<DesignerDocumentResult, IDesignerWindow>? createReplacementWindow = null,
        IDesignerMonitorWorkAreaSource? monitorWorkArea = null,
        DraftStore? draftStore = null,
        DraftRecoveryService? recovery = null,
        Action<Action>? systemEventDispatcherPost = null,
        DesignerOutputServices? outputServices = null,
        DesignerWindowOwner? outputWindowOwner = null)
    {
        Draft = draft ?? throw new ArgumentNullException(nameof(draft));
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(dialog);
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _documentRequests = documentRequests ??
            throw new ArgumentNullException(nameof(documentRequests));
        _outputServices = outputServices;
        _outputWindowOwner = outputWindowOwner;
        _paths = paths;
        _dialog = dialog;
        _createReplacementWindow = createReplacementWindow ?? (result =>
            new MainWindow(
                result.Draft!,
                result.Assets,
                paths,
                dialog,
                documents,
                documentRequests,
                outputServices: outputServices,
                outputWindowOwner: outputWindowOwner));
        _monitorWorkArea = monitorWorkArea ??
            new WindowsDesignerMonitorWorkAreaSource();
        _systemEventDispatcherPost = systemEventDispatcherPost ?? (action =>
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Normal, action));

        _store = draftStore ?? new DraftStore(paths);
        _recovery = recovery ?? new DraftRecoveryService(_store);
        _session = new SkinDraftSession(draft, () => DateTimeOffset.UtcNow);
        _previewComposition = new SyntheticPreviewComposition(
            Dispatcher,
            () => Dispatcher.BeginInvoke(Close));
        _previewController = new DesignerPreviewController(_previewComposition);
        Editor = new DesignerViewModel(
            _session,
            assets,
            (current, assets) => _previewController.Update(current, assets));
        if (outputServices is not null)
        {
            Editor.ConfigureOutput(new DesignerOutputCoordinator(
                () => Editor.Current,
                () => Editor.Assets,
                outputServices.Apply,
                outputServices.Export,
                outputServices.Dialogs));
        }
        var imageService = new DesignerImageService(paths, Editor);
        Editor.ConfigureImageWorkflow(new WindowsImagePicker(), imageService);
        Synthetic = new SyntheticPreviewViewModel(
            _previewComposition.Session,
            RecenterPreviewAfterExpand);
        _closeCoordinator = new DraftCloseCoordinator(
            _session,
            _store,
            _recovery,
            dialog);

        InitializeComponent();
        DataContext = this;
        if (Editor.Output is not null)
        {
            ApplyToHudButton.Command = Editor.Output.ApplyCommand;
            ExportPackageButton.Command = Editor.Output.ExportCommand;
            ApplyToHudButton.IsEnabled = true;
            ExportPackageButton.IsEnabled = true;
        }
        SyncImageTransformControls();
        _editorControlsReady = true;
        _session.MeaningfulChange += OnMeaningfulChange;
        _ = _previewController.Update(Editor.Current, Editor.Assets);
        Loaded += OnLoaded;
        LocationChanged += OnWindowLocationChanged;
        SizeChanged += OnWindowGeometryChanged;
        StateChanged += OnWindowStateChanged;
        DpiChanged += OnWindowDpiChanged;
        Closing += OnClosing;
        Closed += OnClosed;
    }

    private static DesignerDocumentService CreateDocumentService(
        SkinStoragePaths paths)
    {
        var store = new DraftStore(paths);
        return new DesignerDocumentService(
            paths,
            store,
            new InstalledSkinCatalog(
                paths,
                DesignerHudVersion.Current()),
            new SkinPackageReader());
    }

    private void NewDraftButton_OnClick(object sender, RoutedEventArgs e) =>
        BeginDocumentOperation(CreateNewDocumentAsync);

    private void OpenDraftButton_OnClick(object sender, RoutedEventArgs e) =>
        BeginDocumentOperation(OpenSelectedDocumentAsync);

    private void EditInstalledButton_OnClick(object sender, RoutedEventArgs e) =>
        BeginDocumentOperation(EditSelectedDocumentAsync);

    private void ImportForEditingButton_OnClick(
        object sender,
        RoutedEventArgs e) =>
        BeginDocumentOperation(ImportSelectedDocumentAsync);

    private Task<DesignerDocumentResult?> CreateNewDocumentAsync() =>
        Task.FromResult<DesignerDocumentResult?>(_documents.CreateNew(
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow));

    private Task<DesignerDocumentResult?> OpenSelectedDocumentAsync()
    {
        var draftId = _documentRequests.SelectDraftId(this);
        return Task.FromResult<DesignerDocumentResult?>(
            draftId is null ? null : _documents.OpenDraft(draftId.Value));
    }

    private Task<DesignerDocumentResult?> EditSelectedDocumentAsync()
    {
        var selectionKey = _documentRequests.SelectInstalledSelectionKey(this);
        return Task.FromResult<DesignerDocumentResult?>(
            selectionKey is null
                ? null
                : _documents.EditInstalled(selectionKey));
    }

    private async Task<DesignerDocumentResult?> ImportSelectedDocumentAsync()
    {
        var packagePath = _documentRequests.SelectPackagePath(this);
        return packagePath is null
            ? null
            : await _documents.ImportForEditingAsync(
                packagePath,
                DesignerHudVersion.Current()).ConfigureAwait(false);
    }

    private void BeginDocumentOperation(
        Func<Task<DesignerDocumentResult?>> loadDocument)
    {
        ArgumentNullException.ThrowIfNull(loadDocument);
        if (!_operationGate.Wait(0))
        {
            PresentDocumentStatus(
                "Another draft operation is still running. Wait for it to finish, then try this document action again.");
            return;
        }

        _documentOperationForTesting = CompleteDocumentOperationAsync(
            loadDocument);
    }

    private async Task CompleteDocumentOperationAsync(
        Func<Task<DesignerDocumentResult?>> loadDocument)
    {
        DesignerDocumentResult? result = null;
        string? status = null;
        try
        {
            IsEnabled = false;
            var allowed = await _closeCoordinator.RequestCloseAsync()
                .ConfigureAwait(false);
            if (!allowed)
            {
                status = _closeCoordinator.Errors.Count > 0
                    ? string.Join(
                        Environment.NewLine,
                        _closeCoordinator.Errors.Select(error => error.Message))
                    : "Document action cancelled. The current draft and its recovery remain open.";
            }
            else
            {
                Task<DesignerDocumentResult?>? pending = null;
                await InvokeOnDesignerDispatcherAsync(() =>
                    pending = loadDocument()).ConfigureAwait(false);
                if (pending is null)
                {
                    status = "Document action cancelled. The current draft remains open.";
                }
                else
                {
                    result = await pending.ConfigureAwait(false);
                    if (result is null)
                    {
                        status = "Document action cancelled. The current draft remains open.";
                    }
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            status = $"The document action could not access storage: {exception.Message}";
        }
        catch
        {
            status = "The document action could not be completed. The current draft remains open; check the selected file and storage access, then try again.";
        }
        finally
        {
            _operationGate.Release();
        }

        await InvokeOnDesignerDispatcherAsync(() =>
        {
            if (result is not null)
            {
                PresentDocumentResult(result);
                return;
            }

            IsEnabled = true;
            PresentDocumentStatus(status ??
                "The document action did not complete. The current draft remains open.");
        }).ConfigureAwait(false);
    }

    private void PresentDocumentResult(DesignerDocumentResult result)
    {
        if (result.Draft is null || result.Errors.Count > 0)
        {
            IsEnabled = true;
            PresentDocumentStatus(string.Join(
                Environment.NewLine,
                result.Errors.Select(error => $"{error.Code}: {error.Message}")));
            return;
        }

        try
        {
            var replacement = _createReplacementWindow(result);
            replacement.Show();
        }
        catch
        {
            IsEnabled = true;
            PresentDocumentStatus(
                "The selected document was prepared, but its editor window could not be opened. The current draft remains open.");
            return;
        }

        _finalCloseAllowed = true;
        Close();
    }

    internal SkinDraftDocument Draft { get; }

    public DesignerViewModel Editor { get; }

    public SyntheticPreviewViewModel Synthetic { get; }

    internal QuotaOrbWindow PreviewWindowForTesting =>
        _previewComposition.HudWindow;

    internal bool PreviewDisposedForTesting => _previewDisposed;

    internal bool FinalCloseAllowedForTesting => _finalCloseAllowed;

    internal int CloseCoordinatorRequestCountForTesting =>
        _closeCoordinatorRequestCount;

    internal Task SaveOperationForTesting => _saveOperationForTesting;

    internal Task CloseOperationForTesting => _closeOperationForTesting;

    internal Task DocumentOperationForTesting => _documentOperationForTesting;

    internal double EditorColumnWidthForTesting => EditorColumn.Width.Value;

    internal double PreviewColumnWidthForTesting => PreviewColumn.Width.Value;

    internal DesignerWindowLayout ApplyLayoutForTesting(
        Rect workArea,
        DpiScale dpi) => ApplyLayout(workArea, dpi);

    internal DesignerWindowLayout ReapplyCurrentMonitorLayoutForTesting() =>
        ReapplyCurrentMonitorLayout();

    internal void NotifyDisplayEnvironmentChangedForTesting() =>
        QueueCurrentMonitorLayout();

    internal void ShowPreviewForTesting() => _previewComposition.ShowHud();

    internal void AttachPreviewOwnerForTesting()
    {
        if (!IsLoaded)
        {
            _suppressPreviewShowForTesting = true;
            ShowActivated = false;
            ShowInTaskbar = false;
            Opacity = 0;
            Show();
            return;
        }

        AttachPreviewOwner();
    }

    internal void DisposeWithoutShowingForTesting()
    {
        _finalCloseAllowed = true;
        if (IsLoaded)
        {
            Close();
        }
        else
        {
            DisposeOwnedResources();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _outputWindowOwner?.Promote(this);
        AttachSystemEvents();
        ReapplyCurrentMonitorLayout();
        AttachPreviewOwner();
        if (!_suppressPreviewShowForTesting)
        {
            _previewComposition.ShowHud();
            QueuePreviewWorkAreaUpdate();
        }
    }

    private DesignerWindowLayout ApplyLayout(Rect workArea, DpiScale dpi)
    {
        var layout = DesignerLayoutPolicy.Calculate(workArea, dpi);
        _applyingLayout = true;
        try
        {
            Left = layout.WindowBounds.Left;
            Top = layout.WindowBounds.Top;
            Width = layout.WindowBounds.Width;
            Height = layout.WindowBounds.Height;
        }
        finally
        {
            _applyingLayout = false;
        }

        var contentWidth = Math.Max(
            600,
            layout.WindowBounds.Width - 46);
        var editorWidth = Math.Clamp(
            layout.EditorWidth,
            320,
            contentWidth - 280);
        EditorColumn.Width = new GridLength(editorWidth);
        PreviewColumn.Width = new GridLength(contentWidth - editorWidth);
        return layout;
    }

    private DesignerWindowLayout ReapplyCurrentMonitorLayout()
    {
        var metrics = _monitorWorkArea.GetCurrent(this);
        return ApplyLayout(metrics.WorkAreaDip, metrics.Dpi);
    }

    private void AttachPreviewOwner()
    {
        if (_previewOwnerAttached || _previewDisposed)
        {
            return;
        }

        _previewComposition.HudWindow.Owner = this;
        _previewComposition.HudWindow.ShowInTaskbar = false;
        _previewOwnerAttached = true;
    }

    private void OnWindowLocationChanged(object? sender, EventArgs e)
    {
        QueuePreviewWorkAreaUpdate();
        if (!_applyingLayout)
        {
            QueueCurrentMonitorLayout();
        }
    }

    private void OnWindowGeometryChanged(object? sender, EventArgs e) =>
        QueuePreviewWorkAreaUpdate();

    private void OnWindowDpiChanged(object sender, DpiChangedEventArgs e)
    {
        QueueCurrentMonitorLayout();
        QueuePreviewWorkAreaUpdate();
    }

    private void QueueCurrentMonitorLayout()
    {
        if (!IsLoaded || _previewDisposed ||
            Interlocked.Exchange(ref _layoutRefreshQueued, 1) != 0)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
        {
            Interlocked.Exchange(ref _layoutRefreshQueued, 0);
            if (IsLoaded && !_previewDisposed)
            {
                ReapplyCurrentMonitorLayout();
            }
        });
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        if (_previewDisposed)
        {
            return;
        }

        if (WindowState == WindowState.Minimized)
        {
            // WPF automatically hides owned windows while their owner is minimized.
            // Calling Hide() here clears the owned window's visible state, so WPF has
            // nothing to restore when the owner returns to Normal.
            return;
        }
        else if (IsLoaded)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
            {
                if (IsLoaded && !_previewDisposed &&
                    WindowState != WindowState.Minimized)
                {
                    _previewComposition.HudWindow.WindowState = WindowState.Normal;
                    _previewComposition.ShowHud();
                    QueuePreviewWorkAreaUpdate();
                }
            });
        }
    }

    private void QueuePreviewWorkAreaUpdate()
    {
        if (!IsLoaded || _previewDisposed)
        {
            return;
        }

        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            UpdatePreviewWorkArea);
    }

    private void UpdatePreviewWorkArea()
    {
        if (!IsLoaded || _previewDisposed ||
            PreviewStage.ActualWidth <= 0 || PreviewStage.ActualHeight <= 0)
        {
            return;
        }

        var source = PresentationSource.FromVisual(PreviewStage);
        if (source?.CompositionTarget is null)
        {
            return;
        }

        var originPixels = PreviewStage.PointToScreen(new Point(0, 0));
        var originDip = source.CompositionTarget.TransformFromDevice.Transform(
            originPixels);
        var workArea = new Rect(
            originDip.X,
            originDip.Y,
            PreviewStage.ActualWidth,
            PreviewStage.ActualHeight);
        _latestPreviewWorkArea = workArea;
        _previewComposition.SetPreviewWorkArea(workArea);
    }

    private void RecenterPreviewAfterExpand()
    {
        if (_previewDisposed)
        {
            return;
        }

        if (_latestPreviewWorkArea is { } workArea)
        {
            _previewComposition.RecenterHudInPreviewWorkArea(workArea);
        }
        else
        {
            QueuePreviewWorkAreaUpdate();
        }
    }

    private void AttachSystemEvents()
    {
        if (_systemEventsAttached)
        {
            return;
        }

        SystemEvents.DisplaySettingsChanged += OnSystemDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnSystemUserPreferenceChanged;
        _systemEventsAttached = true;
    }

    private void DetachSystemEvents()
    {
        if (!_systemEventsAttached)
        {
            return;
        }

        SystemEvents.DisplaySettingsChanged -= OnSystemDisplayChanged;
        SystemEvents.UserPreferenceChanged -= OnSystemUserPreferenceChanged;
        _systemEventsAttached = false;
    }

    private void OnSystemDisplayChanged(object? sender, EventArgs e) =>
        TryQueueSystemEventLayoutRefresh();

    private void OnSystemUserPreferenceChanged(
        object sender,
        UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Desktop or
            UserPreferenceCategory.General)
        {
            TryQueueSystemEventLayoutRefresh();
        }
    }

    private void TryQueueSystemEventLayoutRefresh()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            _previewDisposed ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return;
        }

        try
        {
            _systemEventDispatcherPost(() =>
            {
                if (Volatile.Read(ref _disposed) == 0 &&
                    !_previewDisposed &&
                    !Dispatcher.HasShutdownStarted &&
                    !Dispatcher.HasShutdownFinished)
                {
                    QueueCurrentMonitorLayout();
                }
            });
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            TaskCanceledException or
            ObjectDisposedException)
        {
            // SystemEvents may race the WPF dispatcher during shutdown.
        }
    }

    private void OnMeaningfulChange(object? sender, SkinDraftDocument draft) =>
        _recovery.NotifyMeaningfulChange(draft);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_finalCloseAllowed)
        {
            return;
        }

        e.Cancel = true;
        if (Interlocked.CompareExchange(ref _closingRequest, 1, 0) != 0)
        {
            return;
        }

        _closeOperationForTesting = CompleteCloseRequestAsync();
    }

    private async Task CompleteCloseRequestAsync()
    {
        if (!_operationGate.Wait(0))
        {
            await InvokeOnDesignerDispatcherAsync(() =>
                PresentDocumentStatus(
                    "Another draft operation is still running. Wait for it to finish, then close again."));
            Interlocked.Exchange(ref _closingRequest, 0);
            return;
        }

        bool allowed = false;
        try
        {
            Interlocked.Increment(ref _closeCoordinatorRequestCount);
            await InvokeOnDesignerDispatcherAsync(() => IsEnabled = false);
            allowed = await _closeCoordinator.RequestCloseAsync()
                .ConfigureAwait(false);
        }
        catch
        {
            allowed = false;
        }
        finally
        {
            _operationGate.Release();
        }

        await InvokeOnDesignerDispatcherAsync(() =>
        {
            if (!allowed)
            {
                PresentDocumentStatus(
                _closeCoordinator.Errors.Count > 0
                    ? string.Join(
                        Environment.NewLine,
                        _closeCoordinator.Errors.Select(error => error.Message))
                    : "Close cancelled. The draft remains open and recovery was preserved.");
                IsEnabled = true;
                Interlocked.Exchange(ref _closingRequest, 0);
                return;
            }

            _finalCloseAllowed = true;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Send, Close);
        }).ConfigureAwait(false);
    }

    private void OnClosed(object? sender, EventArgs e) => DisposeOwnedResources();

    private void DisposeOwnedResources()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Loaded -= OnLoaded;
        LocationChanged -= OnWindowLocationChanged;
        SizeChanged -= OnWindowGeometryChanged;
        StateChanged -= OnWindowStateChanged;
        DpiChanged -= OnWindowDpiChanged;
        Closing -= OnClosing;
        Closed -= OnClosed;
        _session.MeaningfulChange -= OnMeaningfulChange;
        DetachSystemEvents();
        Synthetic.Dispose();
        Editor.Dispose();
        _previewComposition.Dispose();
        _previewDisposed = true;
        _recovery.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _operationGate.Dispose();
    }

    public void Dispose() => DisposeOwnedResources();

    private void DisplayChoice_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (Synthetic is null || DisplayChoiceBox is null)
        {
            return;
        }

        Synthetic.DisplayChoice = DisplayChoiceBox.SelectedIndex switch
        {
            0 => PreviewDisplayChoice.Dual,
            1 => PreviewDisplayChoice.FiveHourOnly,
            2 => PreviewDisplayChoice.WeeklyOnly,
            _ => PreviewDisplayChoice.NoQuota
        };
    }

    private void BasicText_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.Tag is not string field)
        {
            return;
        }

        var result = field switch
        {
            "ProjectName" => Editor.BasicInformation.SetProjectName(textBox.Text),
            "DisplayName" => Editor.BasicInformation.SetDisplayName(textBox.Text),
            "Author" => Editor.BasicInformation.SetAuthor(textBox.Text),
            "Version" => Editor.BasicInformation.SetPackageVersion(textBox.Text),
            "Description" => Editor.BasicInformation.SetDescription(textBox.Text),
            _ => new EditorMutationResult(false, [])
        };
        PresentMutationResult(textBox, result);
        if (!result.Succeeded)
        {
            textBox.Text = field switch
            {
                "ProjectName" => Editor.Current.ProjectName,
                "DisplayName" => Editor.Current.DisplayName,
                "Author" => Editor.Current.Author,
                "Version" => Editor.Current.PackageVersion.ToString(),
                "Description" => Editor.Current.Description,
                _ => textBox.Text
            };
        }
    }

    private void ColorText_OnLostKeyboardFocus(
        object sender,
        KeyboardFocusChangedEventArgs e)
    {
        if (!_editorControlsReady ||
            sender is not TextBox textBox || textBox.Tag is not string field)
        {
            return;
        }

        var result = field switch
        {
            "PrimaryRingColor" =>
                Editor.ColorsAndEffects.SetPrimaryRingColor(textBox.Text),
            "SecondaryRingColor" =>
                Editor.ColorsAndEffects.SetSecondaryRingColor(textBox.Text),
            "BaseBackgroundColor" =>
                Editor.ColorsAndEffects.SetBaseBackgroundColor(textBox.Text),
            "GlowColor" => Editor.ColorsAndEffects.SetGlowColor(textBox.Text),
            _ => new EditorMutationResult(false, [])
        };
        PresentMutationResult(textBox, result);
        if (!result.Succeeded)
        {
            textBox.Text = field switch
            {
                "PrimaryRingColor" => Editor.Current.Theme.PrimaryRingColor,
                "SecondaryRingColor" => Editor.Current.Theme.SecondaryRingColor,
                "BaseBackgroundColor" => Editor.Current.Theme.BaseBackgroundColor,
                "GlowColor" => Editor.Current.Theme.GlowColor,
                _ => textBox.Text
            };
        }
    }

    private void EditorSlider_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_editorControlsReady || _restoringEditorControl ||
            sender is not Slider slider || slider.Tag is not string field)
        {
            return;
        }

        var result = field switch
        {
            "RingDiameter" => Editor.QuotaRings.SetRingDiameter(e.NewValue),
            "RingThickness" => Editor.QuotaRings.SetRingThickness(e.NewValue),
            "RingGap" => Editor.QuotaRings.SetRingGap(e.NewValue),
            "StartAngle" => Editor.QuotaRings.SetStartAngle(e.NewValue),
            "BaseBackgroundOpacity" =>
                Editor.ColorsAndEffects.SetBaseBackgroundOpacity(e.NewValue),
            "GlowIntensity" =>
                Editor.ColorsAndEffects.SetGlowIntensity(e.NewValue),
            "NumberTextSize" => Editor.Text.SetNumberTextSize(e.NewValue),
            "LabelTextSize" => Editor.Text.SetLabelTextSize(e.NewValue),
            "RotationIntensity" =>
                Editor.Animation.SetRotationIntensity(e.NewValue),
            "BreathingIntensity" =>
                Editor.Animation.SetBreathingIntensity(e.NewValue),
            "AnimationGlowIntensity" =>
                Editor.Animation.SetGlowIntensity(e.NewValue),
            "FloatingIntensity" =>
                Editor.Animation.SetFloatingIntensity(e.NewValue),
            "RefreshSpeedMultiplier" =>
                Editor.Animation.SetRefreshSpeedMultiplier(e.NewValue),
            "RefreshHoldSeconds" =>
                Editor.Animation.SetRefreshHoldSeconds(e.NewValue),
            _ => new EditorMutationResult(false, [])
        };
        PresentMutationResult(slider, result);
        if (!result.Succeeded ||
            field is "RefreshSpeedMultiplier" or "RefreshHoldSeconds")
        {
            RestoreEditorSlider(slider, field);
        }
    }

    private void AnimationPreset_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tag } button ||
            !Enum.TryParse<AnimationPresetKind>(tag, out var preset))
        {
            return;
        }

        PresentMutationResult(button, Editor.Animation.ApplyPreset(preset));
    }

    private void TextWeight_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_editorControlsReady ||
            TextWeightBox is null || TextWeightBox.SelectedIndex < 0)
        {
            return;
        }

        var weight = TextWeightBox.SelectedIndex switch
        {
            0 => SkinTextWeight.Regular,
            1 => SkinTextWeight.SemiBold,
            _ => SkinTextWeight.Bold
        };
        PresentMutationResult(
            TextWeightBox,
            Editor.Text.SetTextWeight(weight));
    }

    private void TextPlacement_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (!_editorControlsReady ||
            TextPlacementBox is null || TextPlacementBox.SelectedIndex < 0)
        {
            return;
        }

        var placement = TextPlacementBox.SelectedIndex switch
        {
            0 => SkinTextPlacement.Centered,
            1 => SkinTextPlacement.NumberAboveLabel,
            _ => SkinTextPlacement.LabelAboveNumber
        };
        PresentMutationResult(
            TextPlacementBox,
            Editor.Text.SetTextPlacement(placement));
    }

    private void ImageTransformSlot_OnSelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_editorControlsReady)
        {
            SyncImageTransformControls();
        }
    }

    private void SyncImageTransformControls()
    {
        if (ImageTransformSlotBox is null ||
            ImageOffsetXSlider is null ||
            ImageOffsetYSlider is null ||
            ImageScaleSlider is null ||
            ImageRotationSlider is null ||
            ImageOpacitySlider is null ||
            ImageCropXSlider is null ||
            ImageCropYSlider is null)
        {
            return;
        }

        _updatingImageTransformControls = true;
        try
        {
            var transform = SelectedImageSlot().Transform;
            ImageOffsetXSlider.Value = transform.OffsetX;
            ImageOffsetYSlider.Value = transform.OffsetY;
            ImageScaleSlider.Value = transform.Scale;
            ImageRotationSlider.Value = transform.Rotation;
            ImageOpacitySlider.Value = transform.Opacity;
            ImageCropXSlider.Value = transform.CropFocusX;
            ImageCropYSlider.Value = transform.CropFocusY;
        }
        finally
        {
            _updatingImageTransformControls = false;
        }
    }

    private void ImageTransform_OnValueChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_editorControlsReady ||
            _updatingImageTransformControls ||
            sender is not Slider slider ||
            slider.Tag is not string field ||
            ImageTransformSlotBox is null)
        {
            return;
        }

        var slot = SelectedImageSlot();
        var result = field switch
        {
            "OffsetX" => slot.SetOffsetX(e.NewValue),
            "OffsetY" => slot.SetOffsetY(e.NewValue),
            "Scale" => slot.SetScale(e.NewValue),
            "Rotation" => slot.SetRotation(e.NewValue),
            "Opacity" => slot.SetOpacity(e.NewValue),
            "CropX" => slot.SetCropFocusX(e.NewValue),
            "CropY" => slot.SetCropFocusY(e.NewValue),
            _ => new EditorMutationResult(false, [])
        };
        PresentMutationResult(slider, result);
        if (!result.Succeeded)
        {
            SyncImageTransformControls();
        }
    }

    private void RestoreEditorSlider(Slider slider, string field)
    {
        _restoringEditorControl = true;
        try
        {
            var theme = Editor.Current.Theme;
            slider.Value = field switch
            {
                "RingDiameter" => theme.RingDiameter,
                "RingThickness" => theme.RingThickness,
                "RingGap" => theme.RingGap,
                "StartAngle" => theme.StartAngle,
                "BaseBackgroundOpacity" => theme.BaseBackgroundOpacity,
                "GlowIntensity" => theme.GlowIntensity,
                "NumberTextSize" => theme.NumberTextSize,
                "LabelTextSize" => theme.LabelTextSize,
                "RotationIntensity" => theme.Animation.RotationIntensity,
                "BreathingIntensity" => theme.Animation.BreathingIntensity,
                "AnimationGlowIntensity" => theme.Animation.GlowIntensity,
                "FloatingIntensity" => theme.Animation.FloatingIntensity,
                "RefreshSpeedMultiplier" => theme.Animation.RefreshSpeedMultiplier,
                "RefreshHoldSeconds" => theme.Animation.RefreshHoldSeconds,
                _ => slider.Value
            };
        }
        finally
        {
            _restoringEditorControl = false;
        }
    }

    private ImageSlotViewModel SelectedImageSlot() =>
        ImageTransformSlotBox.SelectedIndex switch
        {
            1 => Editor.Images.Center,
            2 => Editor.Images.Decoration,
            _ => Editor.Images.Background
        };

    private static void PresentMutationResult(
        Control control,
        EditorMutationResult result)
    {
        if (result.Succeeded)
        {
            control.ClearValue(ToolTipProperty);
            control.ClearValue(BorderBrushProperty);
            return;
        }

        control.ToolTip = string.Join(
            Environment.NewLine,
            result.Errors.Select(error => error.Message));
        control.BorderBrush = Brushes.OrangeRed;
    }

    private void SaveDraftButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_operationGate.Wait(0))
        {
            PresentDocumentStatus(
                "Another draft operation is still running. Wait for it to finish, then save again.");
            return;
        }

        _saveOperationForTesting = SaveDraftAsync();
    }

    private async Task SaveDraftAsync()
    {
        var snapshot = _session.Current;
        var valid = SkinDraftValidator.Validate(snapshot).IsValid &&
            SkinContractValidator.ValidateTheme(snapshot.Theme).IsValid;
        try
        {
            if (!valid)
            {
                PresentDocumentStatus(
                    "The draft contains invalid fields. Correct the highlighted values before saving.");
                return;
            }

            SaveDraftButton.IsEnabled = false;
            try
            {
                await _store.SaveNamedAsync(snapshot).ConfigureAwait(false);
                await InvokeOnDesignerDispatcherAsync(() =>
                {
                    if (_session.Current.Revision == snapshot.Revision)
                    {
                        _session.MarkNamedSaved();
                        PresentDocumentStatus("Draft saved.");
                    }
                    else
                    {
                        PresentDocumentStatus(
                            "The draft changed while saving. The newer changes remain unsaved; save again.");
                    }
                }).ConfigureAwait(false);
            }
            catch
            {
                await InvokeOnDesignerDispatcherAsync(() =>
                    PresentDocumentStatus(
                        "The draft could not be saved. Recovery was preserved; check storage access and try again."))
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            await InvokeOnDesignerDispatcherAsync(() =>
            {
                if (!_previewDisposed)
                {
                    SaveDraftButton.IsEnabled = true;
                }
            }).ConfigureAwait(false);

            _operationGate.Release();
        }
    }

    private Task InvokeOnDesignerDispatcherAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (_previewDisposed ||
            Dispatcher.HasShutdownStarted ||
            Dispatcher.HasShutdownFinished)
        {
            return Task.CompletedTask;
        }

        if (Dispatcher.CheckAccess())
        {
            action();
            return Task.CompletedTask;
        }

        try
        {
            return Dispatcher.InvokeAsync(
                () =>
                {
                    if (!_previewDisposed)
                    {
                        action();
                    }
                },
                DispatcherPriority.Send).Task;
        }
        catch (InvalidOperationException)
        {
            return Task.CompletedTask;
        }
    }

    private void PresentDocumentStatus(string message)
    {
        if (!_previewDisposed)
        {
            DocumentStatusText.Text = message;
        }
    }
}
