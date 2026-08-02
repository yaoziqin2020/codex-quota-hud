using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.App.UI.SkinManagement;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.App.Preview;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace CodexQuotaHud.App.UI;

public partial class QuotaOrbWindow : Window, IPreviewHud
{
    private const double PopupShadowMargin = 14;
    private static readonly TimeSpan PointerDismissalWindow =
        TimeSpan.FromMilliseconds(
            System.Windows.Forms.SystemInformation.DoubleClickTime);
    private readonly QuotaOrbViewModel _viewModel;
    private readonly SkinController _skinController;
    private readonly SkinManagementController? _skinManagement;
    private readonly OrbAnimationController _animationController;
    private readonly EdgeAutoHideController _edgeAutoHideController;
    private readonly DetailsPopupTogglePolicy _detailsTogglePolicy = new();
    private readonly OrbClickController _orbClickController;
    private TaskCompletionSource? _expandAnimationCompletion;
    private bool _closingDetailsProgrammatically;
    private bool _allowClose;
    private bool _isDragging;
    private bool _contextMenuOpen;

    public QuotaOrbWindow(QuotaOrbViewModel viewModel)
        : this(
            viewModel,
            new SkinController(),
            initializeSelection: true,
            skinManagement: null)
    {
    }

    internal QuotaOrbWindow(
        QuotaOrbViewModel viewModel,
        SkinController skinController)
        : this(
            viewModel,
            skinController,
            initializeSelection: false,
            skinManagement: null)
    {
    }

    internal QuotaOrbWindow(
        QuotaOrbViewModel viewModel,
        SkinController skinController,
        SkinManagementController skinManagement)
        : this(
            viewModel,
            skinController,
            initializeSelection: false,
            skinManagement)
    {
    }

    private QuotaOrbWindow(
        QuotaOrbViewModel viewModel,
        SkinController skinController,
        bool initializeSelection,
        SkinManagementController? skinManagement)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _skinController = skinController ?? throw new ArgumentNullException(
            nameof(skinController));
        _skinManagement = skinManagement;
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.SetSkinActivationHandler(TryActivateSkinKey);
        if (initializeSelection &&
            !string.Equals(
                _skinController.CurrentDescriptor.SelectionKey,
                viewModel.SelectedSkinKey,
                StringComparison.Ordinal) &&
            _skinController.TryPrepare(
                viewModel.SelectedSkinKey,
                out var initial,
                out _))
        {
            _skinController.Activate(initial!);
        }

        var selected = _skinController.CurrentSkin;
        _animationController = new OrbAnimationController(
            selected as IOrbAnimationTarget);
        SetSkinView(selected.View);
        _skinController.Render(viewModel.SkinState);
        ApplyAnimationState();
        _edgeAutoHideController = new EdgeAutoHideController(
            () => Task.Delay(TimeSpan.FromSeconds(5)),
            side => AnimateEdge(side, collapsed: true),
            side => AnimateEdge(side, collapsed: false));
        _orbClickController = new OrbClickController(
            () => Task.Delay(TimeSpan.FromMilliseconds(
                System.Windows.Forms.SystemInformation.DoubleClickTime)),
            ToggleDetailsPopup,
            () => _viewModel.RefreshCommand.Execute(parameter: null));
        DetailsPopup.CustomPopupPlacementCallback = PlaceDetailsPopup;
        ApplyPopupTheme();
        ApplyEdgeProgressState(EdgeDockSide.None);
        LocationChanged += (_, _) => RefreshPopupPlacement();
        _skinController.ActiveSkinChanged += OnSkinControllerActiveSkinChanged;
        if (_skinManagement is not null)
        {
            _skinManagement.CatalogChanged += OnManagedCatalogChanged;
        }
        RebuildOrbSkinMenu();

        var saved = viewModel.GetSavedPosition();
        if (saved.Left is { } left)
        {
            Left = left;
        }

        if (saved.Top is { } top)
        {
            Top = top;
        }
    }

    internal SkinController SkinController => _skinController;

    bool IPreviewHud.TryActivateSkinKey(string selectionKey) =>
        TryActivateSkinKey(selectionKey);

    public bool TryActivateSkinKey(string selectionKey)
        => TryActivateSkinKey(selectionKey, CancellationToken.None);

    internal bool TryActivateSkinKey(
        string selectionKey,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (!_skinController.TryPrepare(
                selectionKey,
                out var candidate,
                out _))
        {
            return false;
        }

        var previousCandidate = _skinController.CaptureActiveCandidate();
        var previousSelectionKey = _viewModel.SelectedSkinKey;
        if (cancellationToken.IsCancellationRequested ||
            !_viewModel.TrySelectSkinKey(selectionKey, cancellationToken))
        {
            return false;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ResolveRollbackOutcome(
                selectionKey,
                candidate!,
                previousSelectionKey,
                previousCandidate);
        }

        try
        {
            _skinController.Activate(candidate!);
        }
        catch (Exception)
        {
            return ResolveRollbackOutcome(
                selectionKey,
                candidate!,
                previousSelectionKey,
                previousCandidate);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ResolveRollbackOutcome(
                selectionKey,
                candidate!,
                previousSelectionKey,
                previousCandidate);
        }

        return true;
    }

    private bool ResolveRollbackOutcome(
        string targetSelectionKey,
        SkinActivationCandidate targetCandidate,
        string previousSelectionKey,
        SkinActivationCandidate previousCandidate)
    {
        if (TryRollbackActivation(previousSelectionKey, previousCandidate))
        {
            return false;
        }

        return TryFinalizePreparedTarget(targetSelectionKey, targetCandidate);
    }

    private bool TryRollbackActivation(
        string previousSelectionKey,
        SkinActivationCandidate previousCandidate)
    {
        if (!_viewModel.TrySelectSkinKey(previousSelectionKey))
        {
            return false;
        }

        try
        {
            _skinController.RestoreActiveCandidate(previousCandidate);
        }
        catch (Exception)
        {
            // The controller changes its active state before notifying listeners.
            // A listener failure is contained as an activation failure.
        }

        var rolledBack = IsActiveCandidate(
            previousSelectionKey,
            previousCandidate);
        if (rolledBack)
        {
            ApplyActiveSkin();
            RebuildOrbSkinMenu();
        }

        return rolledBack;
    }

    private bool TryFinalizePreparedTarget(
        string targetSelectionKey,
        SkinActivationCandidate targetCandidate)
    {
        if (!string.Equals(
                _viewModel.SelectedSkinKey,
                targetSelectionKey,
                StringComparison.Ordinal))
        {
            return false;
        }

        if (!IsActiveCandidate(targetSelectionKey, targetCandidate))
        {
            try
            {
                _skinController.RestoreActiveCandidate(targetCandidate);
            }
            catch (Exception)
            {
                // RestoreActiveCandidate changes controller state before
                // notifying listeners, so audit the resulting state below.
            }
        }

        if (!IsActiveCandidate(targetSelectionKey, targetCandidate))
        {
            return false;
        }

        ApplyActiveSkin();
        RebuildOrbSkinMenu();
        _viewModel.ClearSkinSelectionError();
        return true;
    }

    private bool IsActiveCandidate(
        string selectionKey,
        SkinActivationCandidate candidate) =>
        string.Equals(
            _viewModel.SelectedSkinKey,
            selectionKey,
            StringComparison.Ordinal) &&
        string.Equals(
            _skinController.CurrentDescriptor.SelectionKey,
            selectionKey,
            StringComparison.Ordinal) &&
        ReferenceEquals(_skinController.CurrentDescriptor, candidate.Descriptor) &&
        ReferenceEquals(_skinController.CurrentSkin, candidate.Skin) &&
        ReferenceEquals(
            _skinController.CurrentPresentation,
            candidate.Presentation);

    public void SetSkinView(FrameworkElement view)
    {
        ArgumentNullException.ThrowIfNull(view);
        SkinHost.Content = view;
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    void IPreviewHud.SetDetailsOpen(bool isOpen)
    {
        if (isOpen)
        {
            ShowDetailsPopup();
        }
        else
        {
            CloseDetailsPopup();
        }
    }

    void IPreviewHud.PreviewEdge(EdgeDockSide side)
    {
        if (side == EdgeDockSide.None || !Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        var workArea = GetNearestWorkArea();
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var collapsed = EdgeAutoHideGeometry.CollapsedPosition(
            side, Left, Top, width, height, workArea);
        _edgeAutoHideController.SetDock(side);
        Left = collapsed.Left;
        Top = collapsed.Top;
        ApplyEdgeVisualState(side, collapsed: true, animate: false);
    }

    void IPreviewHud.ForceExpanded()
    {
        if (_edgeAutoHideController.DockSide == EdgeDockSide.None)
        {
            return;
        }

        _edgeAutoHideController.Expand();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel is null || _animationController is null)
        {
            _edgeAutoHideController?.Dispose();
            CleanupForExit(
                _viewModel,
                OnViewModelPropertyChanged,
                _animationController);
            base.OnClosing(e);
            return;
        }

        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            ApplyAnimationState();
            return;
        }

        _edgeAutoHideController.Dispose();
        _skinController.ActiveSkinChanged -= OnSkinControllerActiveSkinChanged;
        if (_skinManagement is not null)
        {
            _skinManagement.CatalogChanged -= OnManagedCatalogChanged;
        }
        CleanupForExit(
            _viewModel,
            OnViewModelPropertyChanged,
            _animationController);
        base.OnClosing(e);
    }

    internal static void CleanupForExit(
        QuotaOrbViewModel? viewModel,
        PropertyChangedEventHandler? propertyChangedHandler,
        OrbAnimationController? animationController)
    {
        if (viewModel is not null && propertyChangedHandler is not null)
        {
            viewModel.PropertyChanged -= propertyChangedHandler;
        }

        animationController?.SetState(OrbAnimationState.Hidden);
        animationController?.Attach(target: null);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuotaOrbViewModel.IsVisible))
        {
            ApplyVisibility();
            ApplyAnimationState();
        }
        else if (e.PropertyName == nameof(QuotaOrbViewModel.SkinState))
        {
            _skinController.Render(_viewModel.SkinState);
            ApplyEdgeProgressState(_edgeAutoHideController.DockSide);
            _animationController.SetAnimationsEnabled(
                _viewModel.AnimationsEnabled);
            ApplyAnimationState();
        }
    }

    private void ApplyActiveSkin()
    {
        var skin = _skinController.CurrentSkin;
        SetSkinView(skin.View);
        _animationController.Attach(skin as IOrbAnimationTarget);
        ApplyPopupTheme();
        ApplyEdgeProgressState(_edgeAutoHideController.DockSide);
        ApplyAnimationState();
    }

    private void ApplyAnimationState()
    {
        _animationController.SetAnimationsEnabled(
            _viewModel.AnimationsEnabled);
        _animationController.SetState(
            SelectAnimationState(
                IsVisible,
                _viewModel.IsVisible &&
                _viewModel.DisplayMode != QuotaDisplayMode.Hidden,
                _viewModel.IsRefreshing));
    }

    internal static OrbAnimationState SelectAnimationState(
        bool windowVisible,
        bool displayVisible,
        bool refreshing) =>
        !windowVisible || !displayVisible
            ? OrbAnimationState.Hidden
            : refreshing
                ? OrbAnimationState.Refreshing
                : OrbAnimationState.Idle;

    private void ApplyVisibility()
    {
        if (!_viewModel.IsVisible)
        {
            _edgeAutoHideController.CancelPendingCollapse();
            CloseDetailsPopup();
            Hide();
            return;
        }

        ClampToNearestWorkArea(save: false);
        if (!IsVisible)
        {
            Show();
        }

        _ = ScheduleEdgeCollapseAsync();
    }

    private async void OnOrbMouseEnter(object sender, MouseEventArgs e)
    {
        _edgeAutoHideController.CancelPendingCollapse();
        try
        {
            await RevealOrbAsync();
        }
        catch
        {
        }
    }

    private async void OnOrbMouseLeave(object sender, MouseEventArgs e)
    {
        await ScheduleEdgeCollapseAsync();
    }

    private async void OnEdgeHandleMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        e.Handled = true;
        _edgeAutoHideController.CancelPendingCollapse();
        CloseDetailsPopup();
        try
        {
            await RevealOrbAsync();
            await ScheduleEdgeCollapseAsync();
        }
        catch
        {
        }
    }

    private async void OnDragSurfaceMouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        if (_detailsTogglePolicy.ShouldDismissPointerDown(
                DetailsPopup.IsOpen,
                PointerDismissalWindow))
        {
            e.Handled = true;
            _orbClickController.CancelPendingSingleClick();
            CloseDetailsPopup();
            return;
        }

        var clickCount = e.ClickCount;
        if (clickCount >= 2)
        {
            e.Handled = true;
            await _orbClickController.HandleClickAsync(clickCount);
            return;
        }

        var startLeft = Left;
        var startTop = Top;
        _edgeAutoHideController.Expand();
        _edgeAutoHideController.CancelPendingCollapse();
        CommitAnimatedPosition();
        _isDragging = true;
        try
        {
            DragMove();
        }
        finally
        {
            _isDragging = false;
        }

        var moved = !PointerGesture.IsClick(
            startLeft,
            startTop,
            Left,
            Top);
        if (!moved)
        {
            if (await _orbClickController.HandleClickAsync(clickCount))
            {
                _ = RefreshAfterClickAsync();
            }

            return;
        }

        _orbClickController.CancelPendingSingleClick();
        CloseDetailsPopup();
        ClampToNearestWorkArea(save: false);
        UpdateDockAfterDrag();
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        _orbClickController.CancelPendingSingleClick();
        _contextMenuOpen = true;
        CloseDetailsPopup();
        _edgeAutoHideController.Expand();
        RebuildOrbSkinMenu();
    }

    private async void OnContextMenuClosed(object sender, RoutedEventArgs e)
    {
        _contextMenuOpen = false;
        await ScheduleEdgeCollapseAsync();
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var workAreas = GetWorkAreas();
        var workArea = EdgeAutoHideGeometry.NearestWorkArea(
            Left, Top, width, height, workAreas);
        var side = EdgeAutoHideGeometry.DockSideNearEdge(
            Left, Top, width, height, workArea, workAreas);
        if (side == EdgeDockSide.None)
        {
            _edgeAutoHideController.Undock();
            ApplyEdgeVisualState(side, collapsed: false, animate: false);
            _viewModel.SavePosition(Left, Top);
            return;
        }

        var expanded = EdgeAutoHideGeometry.ExpandedPosition(
            side, Left, Top, width, height, workArea);
        Left = expanded.Left;
        Top = expanded.Top;
        _edgeAutoHideController.SetDock(side);
        ApplyEdgeVisualState(side, collapsed: false, animate: false);
        _viewModel.SavePosition(Left, Top);
        await ScheduleEdgeCollapseAsync();
    }

    private void OnSkinControllerActiveSkinChanged(object? sender, EventArgs e)
    {
        ApplyActiveSkin();
        RebuildOrbSkinMenu();
    }

    private void OnManagedCatalogChanged(object? sender, EventArgs e) =>
        RebuildOrbSkinMenu();

    private void RebuildOrbSkinMenu()
    {
        if (_skinManagement is null)
        {
            SkinMenuRoot.Items.Clear();
            foreach (var entry in BuiltInMenuEntries())
            {
                var item = new MenuItem
                {
                    Header = entry.DisplayName,
                    IsCheckable = true,
                    IsChecked = entry.IsSelected,
                    Tag = entry.SelectionKey
                };
                item.Click += (_, _) => _ = TryActivateSkinKey(entry.SelectionKey);
                SkinMenuRoot.Items.Add(item);
            }

            return;
        }

        RebuildSkinMenu(
            SkinMenuRoot,
            _skinManagement.Entries,
            _skinManagement.DesignerAvailable,
            key => _ = TryActivateSkinKey(key),
            key => _skinManagement.RemoveAsync(key),
            async () =>
            {
                _ = await _skinManagement.ChooseAndImportAsync();
            },
            () => _ = _skinManagement.OpenDesigner());
    }

    private IReadOnlyList<SkinMenuEntry> BuiltInMenuEntries() =>
    [
        BuiltInEntry(SkinSelectionKey.HudDial, "HUD 科技仪表"),
        BuiltInEntry(SkinSelectionKey.EnergyRing, "双彩能量环"),
        BuiltInEntry(SkinSelectionKey.LiquidGlass, "流体玻璃球"),
        BuiltInEntry(SkinSelectionKey.Aurora, "克制极光"),
        BuiltInEntry(SkinSelectionKey.LiquidTank, "液位储能舱")
    ];

    private SkinMenuEntry BuiltInEntry(string selectionKey, string displayName) =>
        new(
            selectionKey,
            displayName,
            string.Equals(
                selectionKey,
                _skinController.CurrentDescriptor.SelectionKey,
                StringComparison.Ordinal),
            CanRemove: false);

    internal static void RebuildSkinMenu(
        MenuItem root,
        IReadOnlyList<SkinMenuEntry> entries,
        bool designerAvailable,
        Action<string> select,
        Func<string, Task> remove,
        Func<Task> import,
        Action openDesigner)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(openDesigner);
        root.Items.Clear();

        foreach (var entry in entries)
        {
            var item = new MenuItem
            {
                Header = entry.DisplayName,
                IsCheckable = true,
                IsChecked = entry.IsSelected,
                Tag = entry.SelectionKey
            };
            if (entry.CanRemove)
            {
                var selectItem = new MenuItem { Header = "选择" };
                selectItem.Click += (_, _) => select(entry.SelectionKey);
                var removeItem = new MenuItem { Header = "删除" };
                removeItem.Click += async (_, _) => await remove(entry.SelectionKey);
                item.Items.Add(selectItem);
                item.Items.Add(removeItem);
            }
            else
            {
                item.Click += (_, _) => select(entry.SelectionKey);
            }

            root.Items.Add(item);
        }

        root.Items.Add(new System.Windows.Controls.Separator());
        var importItem = new MenuItem { Header = "导入皮肤…" };
        importItem.Click += async (_, _) => await import();
        root.Items.Add(importItem);
        if (designerAvailable)
        {
            var designerItem = new MenuItem { Header = "打开皮肤设计器" };
            designerItem.Click += (_, _) => openDesigner();
            root.Items.Add(designerItem);
        }
    }

    private void ClampToNearestWorkArea(bool save)
    {
        var workArea = GetNearestWorkArea();
        var position = WindowPositioning.Clamp(
            Left,
            Top,
            ActualWidth > 0 ? ActualWidth : Width,
            ActualHeight > 0 ? ActualHeight : Height,
            workArea);
        Left = position.Left;
        Top = position.Top;
        if (save)
        {
            _viewModel.SavePosition(Left, Top);
        }
    }

    private void UpdateDockAfterDrag()
    {
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var workAreas = GetWorkAreas();
        var workArea = EdgeAutoHideGeometry.NearestWorkArea(
            Left, Top, width, height, workAreas);
        var side = EdgeAutoHideGeometry.DockSideNearEdge(
            Left, Top, width, height, workArea, workAreas);
        if (side == EdgeDockSide.None)
        {
            _edgeAutoHideController.Undock();
            ApplyEdgeVisualState(side, collapsed: false, animate: false);
            _viewModel.SavePosition(Left, Top);
            return;
        }

        var expanded = EdgeAutoHideGeometry.ExpandedPosition(
            side, Left, Top, width, height, workArea);
        _edgeAutoHideController.SetDock(side);
        Left = expanded.Left;
        Top = expanded.Top;
        ApplyEdgeVisualState(side, collapsed: false, animate: false);

        _viewModel.SavePosition(Left, Top);
        _ = ScheduleEdgeCollapseAsync();
    }

    private Task<bool> ScheduleEdgeCollapseAsync() =>
        _edgeAutoHideController.ScheduleCollapseAsync(
            () =>
                CanCollapseEdge(
                    IsVisible,
                    _viewModel.IsVisible,
                    _isDragging,
                    _contextMenuOpen,
                    IsMouseOver,
                    DetailsPopup.IsOpen,
                    DetailsPopup.IsMouseOver,
                    OrbContextMenu.IsOpen));

    internal static bool CanCollapseEdge(
        bool windowVisible,
        bool displayVisible,
        bool dragging,
        bool contextMenuOpen,
        bool pointerOverOrb,
        bool popupOpen,
        bool pointerOverPopup,
        bool orbMenuOpen) =>
        windowVisible &&
        displayVisible &&
        !dragging &&
        !contextMenuOpen &&
        !pointerOverOrb &&
        !popupOpen &&
        !pointerOverPopup &&
        !orbMenuOpen;

    private async void OnDetailsPopupClosed(object? sender, EventArgs e)
    {
        _detailsTogglePolicy.ObserveClosed(
            IsPointerOverOrb(),
            _closingDetailsProgrammatically,
            PointerDismissalWindow);
        if (_allowClose)
        {
            return;
        }

        try
        {
            await ScheduleEdgeCollapseAsync();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private bool IsPointerOverOrb()
    {
        var pointer = Mouse.GetPosition(OrbRoot);
        return pointer.X >= 0 &&
               pointer.Y >= 0 &&
               pointer.X <= OrbRoot.ActualWidth &&
               pointer.Y <= OrbRoot.ActualHeight;
    }

    private void AnimateEdge(EdgeDockSide side, bool collapsed)
    {
        if (side == EdgeDockSide.None || !_viewModel.IsVisible)
        {
            return;
        }

        if (collapsed)
        {
            CloseDetailsPopup();
        }

        var workArea = GetNearestWorkArea();
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var target = collapsed
            ? EdgeAutoHideGeometry.CollapsedPosition(
                side, Left, Top, width, height, workArea)
            : EdgeAutoHideGeometry.ExpandedPosition(
                side, Left, Top, width, height, workArea);
        TaskCompletionSource? expandCompletion = null;
        if (!collapsed)
        {
            _expandAnimationCompletion?.TrySetResult();
            expandCompletion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _expandAnimationCompletion = expandCompletion;
        }

        ApplyEdgeVisualState(side, collapsed, animate: true);
        var animation = new DoubleAnimation
        {
            To = side is EdgeDockSide.Left or EdgeDockSide.Right
                ? target.Left
                : target.Top,
            Duration = TimeSpan.FromMilliseconds(collapsed ? 260 : 190),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            },
            FillBehavior = FillBehavior.HoldEnd
        };
        Timeline.SetDesiredFrameRate(animation, 30);
        var property = side is EdgeDockSide.Left or EdgeDockSide.Right
            ? LeftProperty
            : TopProperty;
        if (property == LeftProperty)
        {
            Top = target.Top;
        }
        else
        {
            Left = target.Left;
        }

        animation.Completed += (_, _) =>
        {
            BeginAnimation(property, animation: null);
            Left = target.Left;
            Top = target.Top;
            RefreshPopupPlacement();
            expandCompletion?.TrySetResult();
        };
        BeginAnimation(
            property,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void CommitAnimatedPosition()
    {
        var currentLeft = Left;
        var currentTop = Top;
        BeginAnimation(LeftProperty, animation: null);
        BeginAnimation(TopProperty, animation: null);
        Left = currentLeft;
        Top = currentTop;
    }

    internal void ApplyEdgeVisualState(
        EdgeDockSide side,
        bool collapsed,
        bool animate)
    {
        var verticalPill = side is EdgeDockSide.Left or EdgeDockSide.Right;
        EdgeHandle.Width = verticalPill ? 12 : 72;
        EdgeHandle.Height = verticalPill ? 72 : 12;
        EdgeHandle.HorizontalAlignment = side switch
        {
            EdgeDockSide.Left => System.Windows.HorizontalAlignment.Right,
            EdgeDockSide.Right => System.Windows.HorizontalAlignment.Left,
            _ => System.Windows.HorizontalAlignment.Center
        };
        EdgeHandle.VerticalAlignment = side switch
        {
            EdgeDockSide.Top => System.Windows.VerticalAlignment.Bottom,
            EdgeDockSide.Bottom => System.Windows.VerticalAlignment.Top,
            _ => System.Windows.VerticalAlignment.Center
        };
        EdgeHandle.Margin = side switch
        {
            EdgeDockSide.Left => new Thickness(0, 0, 6, 0),
            EdgeDockSide.Right => new Thickness(6, 0, 0, 0),
            EdgeDockSide.Top => new Thickness(0, 0, 0, 6),
            EdgeDockSide.Bottom => new Thickness(0, 6, 0, 0),
            _ => default
        };
        EdgeHandle.IsHitTestVisible = collapsed;
        ApplyEdgeProgressState(side);

        SetOpacity(
            SkinHost,
            collapsed ? 0 : 1,
            animate);
        SetOpacity(
            EdgeHandle,
            collapsed ? 1 : 0,
            animate);
    }

    private void ApplyEdgeProgressState(EdgeDockSide side)
    {
        var theme = _skinController.CurrentPresentation.Edge;
        var level = _viewModel.DisplayMode == QuotaDisplayMode.Hidden
            ? QuotaAlertLevel.Normal
            : QuotaAlertPolicy.Classify(_viewModel.PrimaryPercent);
        EdgeProgressTrack.Background = theme.Track;
        EdgeProgressOutline.BorderBrush =
            QuotaAlertPalette.ResolveBrush(level, theme.Border);
        EdgeProgressFill.Background =
            QuotaAlertPalette.ResolveBrush(level, theme.Fill);
        EdgeProgressTexture.Background = theme.Texture;
        EdgeProgressTexture.Opacity = theme.TextureOpacity;
        EdgeHandleGlow.Color =
            QuotaAlertPalette.ResolveMediaColor(level, theme.GlowColor);
        EdgeHandleGlow.Opacity = theme.GlowOpacity;

        var vertical = side is EdgeDockSide.Left or EdgeDockSide.Right;
        var fillLength = EdgeProgressGeometry.FillLength(
            trackLength: 72,
            _viewModel.DisplayMode == QuotaDisplayMode.Hidden
                ? 0
                : _viewModel.PrimaryPercent);
        EdgeProgressFill.HorizontalAlignment = vertical
            ? System.Windows.HorizontalAlignment.Stretch
            : System.Windows.HorizontalAlignment.Left;
        EdgeProgressFill.VerticalAlignment = vertical
            ? System.Windows.VerticalAlignment.Bottom
            : System.Windows.VerticalAlignment.Stretch;
        EdgeProgressFill.Width = vertical
            ? double.NaN
            : fillLength;
        EdgeProgressFill.Height = vertical
            ? fillLength
            : double.NaN;
        EdgeProgressTexture.Margin = vertical
            ? new Thickness(0, 6, 0, 6)
            : new Thickness(6, 0, 6, 0);
    }

    private async Task RevealOrbAsync()
    {
        if (!_edgeAutoHideController.TryExpandCollapsed())
        {
            return;
        }

        var completion = _expandAnimationCompletion;
        if (completion is not null)
        {
            await completion.Task;
        }
    }

    private void ShowDetailsPopup()
    {
        RefreshPopupPlacement();
        DetailsPopup.IsOpen = true;
    }

    private void ToggleDetailsPopup()
    {
        if (_detailsTogglePolicy.IsOpenSuppressed)
        {
            return;
        }

        if (DetailsPopup.IsOpen)
        {
            CloseDetailsPopup();
            return;
        }

        ShowDetailsPopup();
    }

    private void OnPopupPointerDown(object sender, MouseButtonEventArgs e)
    {
        CloseDetailsPopup();
    }

    private void CloseDetailsPopup()
    {
        if (!DetailsPopup.IsOpen)
        {
            return;
        }

        _closingDetailsProgrammatically = true;
        try
        {
            DetailsPopup.IsOpen = false;
        }
        finally
        {
            _closingDetailsProgrammatically = false;
        }
    }

    private async Task RefreshAfterClickAsync()
    {
        try
        {
            await _viewModel.OnHoverAsync();
        }
        catch
        {
        }
    }

    private static void SetOpacity(
        UIElement element,
        double target,
        bool animate)
    {
        element.BeginAnimation(UIElement.OpacityProperty, animation: null);
        if (!animate)
        {
            element.Opacity = target;
            return;
        }

        var animation = new DoubleAnimation
        {
            From = element.Opacity,
            To = target,
            Duration = TimeSpan.FromMilliseconds(180),
            FillBehavior = FillBehavior.Stop
        };
        Timeline.SetDesiredFrameRate(animation, 30);
        element.Opacity = target;
        element.BeginAnimation(
            UIElement.OpacityProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private CustomPopupPlacement[] PlaceDetailsPopup(
        System.Windows.Size popupSize,
        System.Windows.Size targetSize,
        Point offset)
    {
        var workArea = GetNearestWorkArea();
        var dock = _edgeAutoHideController.DockSide;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var expanded = dock == EdgeDockSide.None
            ? new WindowPosition(Left, Top)
            : EdgeAutoHideGeometry.ExpandedPosition(
                dock, Left, Top, width, height, workArea);
        var placement = PopupPlacementCalculator.Calculate(
            expanded.Left,
            expanded.Top,
            targetSize.Width,
            targetSize.Height,
            popupSize.Width,
            popupSize.Height,
            workArea,
            dock,
            insetLeft: PopupShadowMargin,
            insetTop: PopupShadowMargin,
            insetRight: PopupShadowMargin,
            insetBottom: PopupShadowMargin);
        return
        [
            new CustomPopupPlacement(
                new Point(placement.OffsetX, placement.OffsetY),
                PopupPrimaryAxis.Vertical)
        ];
    }

    private void RefreshPopupPlacement()
    {
        if (!DetailsPopup.IsOpen)
        {
            return;
        }

        DetailsPopup.CustomPopupPlacementCallback = null;
        DetailsPopup.CustomPopupPlacementCallback = PlaceDetailsPopup;
    }

    private void ApplyPopupTheme()
    {
        var theme = _skinController.CurrentPresentation.Popup;
        PopupCard.Background = theme.Background;
        PopupShadowHost.Background = theme.Background;
        PopupCard.BorderBrush = theme.Border;
        PopupAccent.Background = theme.Accent;
        PopupCard.Resources["PopupAccentBrush"] = theme.Accent;
        PopupCard.Resources["PopupSecondaryTextBrush"] =
            theme.SecondaryText;
        PopupShadow.Color = theme.ShadowColor;
        HudDialPopupDecoration.Visibility =
            theme.Decoration == PopupDecorationKind.HudDial
                ? Visibility.Visible : Visibility.Collapsed;
        EnergyRingPopupDecoration.Visibility =
            theme.Decoration == PopupDecorationKind.EnergyRing
                ? Visibility.Visible : Visibility.Collapsed;
        LiquidGlassPopupDecoration.Visibility =
            theme.Decoration == PopupDecorationKind.LiquidGlass
                ? Visibility.Visible : Visibility.Collapsed;
        AuroraPopupDecoration.Visibility =
            theme.Decoration == PopupDecorationKind.Aurora
                ? Visibility.Visible : Visibility.Collapsed;
        LiquidTankPopupDecoration.Visibility =
            theme.Decoration == PopupDecorationKind.LiquidTank
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPopupCardSizeChanged(
        object sender,
        SizeChangedEventArgs e)
    {
        PopupCard.Clip = CreateRoundedPopupClip(e.NewSize);
    }

    internal static RectangleGeometry CreateRoundedPopupClip(
        System.Windows.Size size) =>
        new(
            new Rect(new Point(), size),
            radiusX: 12,
            radiusY: 12);

    private WorkArea GetNearestWorkArea()
    {
        var workAreas = GetWorkAreas();
        return EdgeAutoHideGeometry.NearestWorkArea(
            Left,
            Top,
            ActualWidth > 0 ? ActualWidth : Width,
            ActualHeight > 0 ? ActualHeight : Height,
            workAreas);
    }

    private IReadOnlyList<WorkArea> GetWorkAreas()
    {
        _ = new WindowInteropHelper(this).EnsureHandle();
        var fromDevice =
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ??
            Matrix.Identity;
        var areas = System.Windows.Forms.Screen.AllScreens
            .Select(screen =>
            {
                var work = screen.WorkingArea;
                var topLeft = fromDevice.Transform(
                    new Point(work.Left, work.Top));
                var bottomRight = fromDevice.Transform(
                    new Point(work.Right, work.Bottom));
                return new WorkArea(
                    topLeft.X,
                    topLeft.Y,
                    bottomRight.X - topLeft.X,
                    bottomRight.Y - topLeft.Y);
            })
            .ToArray();
        if (areas.Length > 0)
        {
            return areas;
        }

        var fallback = SystemParameters.WorkArea;
        return
        [
            new WorkArea(
                fallback.Left,
                fallback.Top,
                fallback.Width,
                fallback.Height)
        ];
    }
}
