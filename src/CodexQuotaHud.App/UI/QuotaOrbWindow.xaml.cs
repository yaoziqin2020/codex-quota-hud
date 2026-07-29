using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private readonly QuotaOrbViewModel _viewModel;
    private readonly HoverCloseController _hoverCloseController;
    private readonly SkinController _skinController;
    private readonly OrbAnimationController _animationController;
    private bool _allowClose;

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
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
            ApplyAnimationState();
            return;
        }

        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _animationController.SetState(OrbAnimationState.Hidden);
        _animationController.Attach(target: null);
        base.OnClosing(e);
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
            DetailsPopup.IsOpen = false;
            Hide();
            return;
        }

        ClampToNearestWorkArea(save: false);
        if (!IsVisible)
        {
            Show();
        }
    }

    private async void OnOrbMouseEnter(object sender, MouseEventArgs e)
    {
        _hoverCloseController.CancelPendingClose();
        DetailsPopup.IsOpen = true;
        try
        {
            await _viewModel.OnHoverAsync();
        }
        catch
        {
        }
    }

    private async void OnOrbMouseLeave(object sender, MouseEventArgs e) =>
        await _hoverCloseController.ScheduleCloseAsync();

    private void OnPopupMouseEnter(object sender, MouseEventArgs e) =>
        _hoverCloseController.CancelPendingClose();

    private async void OnPopupMouseLeave(object sender, MouseEventArgs e) =>
        await _hoverCloseController.ScheduleCloseAsync();

    private void OnDragSurfaceMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        DetailsPopup.IsOpen = false;
        DragMove();
        ClampToNearestWorkArea(save: true);
    }

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        SetSkinCheck(HudDialMenuItem, SkinId.HudDial);
        SetSkinCheck(EnergyRingMenuItem, SkinId.EnergyRing);
        SetSkinCheck(LiquidGlassMenuItem, SkinId.LiquidGlass);
        SetSkinCheck(AuroraMenuItem, SkinId.Aurora);
        SetSkinCheck(LiquidTankMenuItem, SkinId.LiquidTank);
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
