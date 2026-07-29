using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using MenuItem = System.Windows.Controls.MenuItem;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace CodexQuotaHud.App.UI;

public partial class QuotaOrbWindow : Window
{
    private const double PopupShadowMargin = 14;
    private readonly QuotaOrbViewModel _viewModel;
    private readonly HoverCloseController _hoverCloseController;
    private readonly SkinController _skinController;
    private readonly OrbAnimationController _animationController;
    private readonly EdgeAutoHideController _edgeAutoHideController;
    private bool _allowClose;
    private bool _isDragging;
    private bool _contextMenuOpen;

    public QuotaOrbWindow(QuotaOrbViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _skinController = new SkinController();
        var selected = _skinController.Select(viewModel.SelectedSkin);
        _animationController = new OrbAnimationController(
            selected as IOrbAnimationTarget);
        SetSkinView(selected.View);
        _skinController.Render(viewModel.SkinState);
        ApplyAnimationState();
        _hoverCloseController = new HoverCloseController(
            () => Task.Delay(TimeSpan.FromMilliseconds(180)),
            () => DetailsPopup.IsOpen = false);
        _edgeAutoHideController = new EdgeAutoHideController(
            () => Task.Delay(TimeSpan.FromSeconds(1)),
            side => AnimateEdge(side, collapsed: true),
            side => AnimateEdge(side, collapsed: false));
        DetailsPopup.CustomPopupPlacementCallback = PlaceDetailsPopup;
        ApplyPopupTheme();
        LocationChanged += (_, _) => RefreshPopupPlacement();

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
        else if (e.PropertyName == nameof(QuotaOrbViewModel.SelectedSkin))
        {
            ApplySelectedSkin();
        }
        else if (e.PropertyName == nameof(QuotaOrbViewModel.SkinState))
        {
            _skinController.Render(_viewModel.SkinState);
            _animationController.SetAnimationsEnabled(
                _viewModel.AnimationsEnabled);
            ApplyAnimationState();
        }
    }

    private void ApplySelectedSkin()
    {
        var skin = _skinController.Select(_viewModel.SelectedSkin);
        SetSkinView(skin.View);
        _animationController.Attach(skin as IOrbAnimationTarget);
        _skinController.Render(_viewModel.SkinState);
        ApplyPopupTheme();
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
            DetailsPopup.IsOpen = false;
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
        _hoverCloseController.CancelPendingClose();
        _edgeAutoHideController.Expand();
        RefreshPopupPlacement();
        DetailsPopup.IsOpen = true;
        try
        {
            await _viewModel.OnHoverAsync();
        }
        catch
        {
        }
    }

    private async void OnOrbMouseLeave(object sender, MouseEventArgs e)
    {
        await _hoverCloseController.ScheduleCloseAsync();
        await ScheduleEdgeCollapseAsync();
    }

    private void OnPopupMouseEnter(object sender, MouseEventArgs e)
    {
        _hoverCloseController.CancelPendingClose();
        _edgeAutoHideController.CancelPendingCollapse();
    }

    private async void OnPopupMouseLeave(object sender, MouseEventArgs e)
    {
        await _hoverCloseController.ScheduleCloseAsync();
        await ScheduleEdgeCollapseAsync();
    }

    private void OnDragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        _edgeAutoHideController.Expand();
        _edgeAutoHideController.CancelPendingCollapse();
        DetailsPopup.IsOpen = false;
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

        ClampToNearestWorkArea(save: false);
        UpdateDockAfterDrag();
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        _contextMenuOpen = true;
        _edgeAutoHideController.Expand();
        SetSkinCheck(HudDialMenuItem, SkinId.HudDial);
        SetSkinCheck(EnergyRingMenuItem, SkinId.EnergyRing);
        SetSkinCheck(LiquidGlassMenuItem, SkinId.LiquidGlass);
        SetSkinCheck(AuroraMenuItem, SkinId.Aurora);
        SetSkinCheck(LiquidTankMenuItem, SkinId.LiquidTank);
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
        var side = EdgeAutoHideGeometry.NearestDockSide(
            Left, Top, width, height, workArea, workAreas);
        var expanded = EdgeAutoHideGeometry.ExpandedPosition(
            side, Left, Top, width, height, workArea);
        Left = expanded.Left;
        Top = expanded.Top;
        _edgeAutoHideController.SetDock(side);
        ApplyEdgeVisualState(side, collapsed: false, animate: false);
        _viewModel.SavePosition(Left, Top);
        await ScheduleEdgeCollapseAsync();
    }

    private void SetSkinCheck(MenuItem item, SkinId skin) =>
        item.IsChecked = _viewModel.SelectedSkin == skin;

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
        var side = EdgeAutoHideGeometry.NearestDockSide(
            Left, Top, width, height, workArea, workAreas);
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
                IsVisible &&
                _viewModel.IsVisible &&
                !_isDragging &&
                !_contextMenuOpen &&
                !DetailsPopup.IsMouseOver &&
                !OrbContextMenu.IsOpen);

    private void AnimateEdge(EdgeDockSide side, bool collapsed)
    {
        if (side == EdgeDockSide.None || !_viewModel.IsVisible)
        {
            return;
        }

        var workArea = GetNearestWorkArea();
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;
        var target = collapsed
            ? EdgeAutoHideGeometry.CollapsedPosition(
                side, Left, Top, width, height, workArea)
            : EdgeAutoHideGeometry.ExpandedPosition(
                side, Left, Top, width, height, workArea);
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
        EdgeHandle.Width = verticalPill ? 6 : 44;
        EdgeHandle.Height = verticalPill ? 44 : 6;
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

        SetOpacity(
            SkinHost,
            collapsed ? 0 : 1,
            animate);
        SetOpacity(
            EdgeHandle,
            collapsed ? 1 : 0,
            animate);
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
                dock is EdgeDockSide.Top or EdgeDockSide.Bottom
                    ? PopupPrimaryAxis.Horizontal
                    : PopupPrimaryAxis.Vertical)
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
        var theme = PopupThemeProvider.Get(_viewModel.SelectedSkin);
        PopupCard.Background = theme.Background;
        PopupShadowHost.Background = theme.Background;
        PopupCard.BorderBrush = theme.Border;
        PopupAccent.Background = theme.Accent;
        PopupCard.Resources["PopupAccentBrush"] = theme.Accent;
        PopupCard.Resources["PopupSecondaryTextBrush"] =
            theme.SecondaryText;
        PopupShadow.Color = theme.ShadowColor;
        EdgeHandle.Background = theme.Accent;
        EdgeHandleGlow.Color = theme.ShadowColor;
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
