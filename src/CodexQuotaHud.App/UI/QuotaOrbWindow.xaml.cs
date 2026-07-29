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
    private const uint MonitorDefaultToNearest = 2;
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
        CommitAnimatedLeft();
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
        var workArea = GetNearestWorkArea();
        var side = EdgeAutoHideGeometry.DetectDockSide(
            Left,
            ActualWidth > 0 ? ActualWidth : Width,
            workArea);
        _edgeAutoHideController.SetDock(side);
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
        var workArea = GetNearestWorkArea();
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var side = EdgeAutoHideGeometry.DetectDockSide(
            Left,
            width,
            workArea);
        _edgeAutoHideController.SetDock(side);
        if (side != EdgeDockSide.None)
        {
            Left = EdgeAutoHideGeometry.ExpandedLeft(side, width, workArea);
        }

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
        var target = collapsed
            ? EdgeAutoHideGeometry.CollapsedLeft(side, width, workArea)
            : EdgeAutoHideGeometry.ExpandedLeft(side, width, workArea);
        var animation = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(collapsed ? 260 : 190),
            EasingFunction = new CubicEase
            {
                EasingMode = EasingMode.EaseOut
            },
            FillBehavior = FillBehavior.HoldEnd
        };
        Timeline.SetDesiredFrameRate(animation, 30);
        animation.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, animation: null);
            Left = target;
            RefreshPopupPlacement();
        };
        BeginAnimation(
            LeftProperty,
            animation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void CommitAnimatedLeft()
    {
        var current = Left;
        BeginAnimation(LeftProperty, animation: null);
        Left = current;
    }

    private CustomPopupPlacement[] PlaceDetailsPopup(
        System.Windows.Size popupSize,
        System.Windows.Size targetSize,
        Point offset)
    {
        var workArea = GetNearestWorkArea();
        var dock = _edgeAutoHideController.DockSide;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var effectiveLeft = dock == EdgeDockSide.None
            ? Left
            : EdgeAutoHideGeometry.ExpandedLeft(dock, width, workArea);
        var placement = PopupPlacementCalculator.Calculate(
            effectiveLeft,
            Top,
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
        var theme = PopupThemeProvider.Get(_viewModel.SelectedSkin);
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
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);
        var information = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == nint.Zero || !GetMonitorInfo(monitor, ref information))
        {
            var fallback = SystemParameters.WorkArea;
            return new WorkArea(
                fallback.Left, fallback.Top, fallback.Width, fallback.Height);
        }

        var fromDevice =
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ??
            Matrix.Identity;
        var topLeft = fromDevice.Transform(
            new Point(information.Work.Left, information.Work.Top));
        var bottomRight = fromDevice.Transform(
            new Point(information.Work.Right, information.Work.Bottom));
        return new WorkArea(
            topLeft.X,
            topLeft.Y,
            bottomRight.X - topLeft.X,
            bottomRight.Y - topLeft.Y);
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo information);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
