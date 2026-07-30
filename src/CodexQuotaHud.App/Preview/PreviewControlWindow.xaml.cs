using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Preview;

public partial class PreviewControlWindow : Window
{
    private readonly PreviewSession _session;
    private bool _initialized;
    private int _openInstalledRequested;
    private readonly PreviewWindowStateStore? _windowStateStore;
    private readonly DispatcherTimer _saveTimer;
    private bool _loaded;

    internal PreviewControlWindow(
        PreviewSession session,
        bool installedAppAvailable,
        PreviewWindowStateStore? windowStateStore = null)
    {
        InitializeComponent();
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _windowStateStore = windowStateStore;
        _saveTimer = new DispatcherTimer(
            DispatcherPriority.Background,
            Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveWindowStateNow();
        };
        DisplayChoiceBox.ItemsSource = Enum.GetValues<PreviewDisplayChoice>();
        SkinBox.ItemsSource = Enum.GetValues<SkinId>();
        DisplayChoiceBox.SelectedItem = PreviewDisplayChoice.Dual;
        SkinBox.SelectedItem = SkinId.HudDial;
        FiveHourSlider.Value = 68;
        WeeklySlider.Value = 34;
        FiveHourValueText.Text = "68%";
        WeeklyValueText.Text = "34%";
        CanOpenInstalled = installedAppAvailable;
        InstalledAppMessage = installedAppAvailable
            ? null
            : "未找到已安装正式版";
        OpenInstalledButton.IsEnabled = CanOpenInstalled;
        InstalledAppMessageText.Text = InstalledAppMessage;
        ApplySavedWindowState();
        Loaded += (_, _) => _loaded = true;
        LocationChanged += (_, _) => ScheduleWindowStateSave();
        SizeChanged += (_, _) => ScheduleWindowStateSave();
        _initialized = true;
    }

    public event EventHandler? ExitRequested;
    public event EventHandler? OpenInstalledRequested;

    internal PreviewDisplayChoice SelectedDisplayChoice =>
        DisplayChoiceBox.SelectedItem is PreviewDisplayChoice choice
            ? choice
            : PreviewDisplayChoice.Dual;

    internal double FiveHourPercent => FiveHourSlider.Value;
    internal double WeeklyPercent => WeeklySlider.Value;
    internal bool CanOpenInstalled { get; }
    internal string? InstalledAppMessage { get; }

    internal void SelectDisplayChoice(PreviewDisplayChoice choice)
    {
        DisplayChoiceBox.SelectedItem = choice;
        _session.SetDisplayChoice(choice);
    }

    internal void ChangeWeeklyPercent(double value)
    {
        WeeklySlider.Value = value;
        _session.SetWeeklyPercent(value);
    }

    internal void ChangeRefreshing(bool value)
    {
        RefreshingCheckBox.IsChecked = value;
        _session.SetRefreshing(value);
    }

    internal void ChangeDetails(bool value) =>
        _session.SetDetailsOpen(value);

    internal void RequestOpenInstalled()
    {
        if (!CanOpenInstalled ||
            Interlocked.Exchange(ref _openInstalledRequested, 1) != 0)
        {
            return;
        }

        OpenInstalledRequested?.Invoke(this, EventArgs.Empty);
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    internal void SaveWindowStateNow()
    {
        if (_windowStateStore is null ||
            !double.IsFinite(Left) ||
            !double.IsFinite(Top) ||
            !double.IsFinite(Width) ||
            !double.IsFinite(Height))
        {
            return;
        }

        _windowStateStore.Save(new PreviewWindowState(
            Left, Top, Width, Height));
    }

    internal static PreviewWindowState ClampState(
        PreviewWindowState state,
        WorkArea workArea)
    {
        var width = Math.Min(state.Width, workArea.Width);
        var height = Math.Min(state.Height, workArea.Height);
        var position = WindowPositioning.Clamp(
            state.Left,
            state.Top,
            width,
            height,
            workArea);
        return new PreviewWindowState(
            position.Left,
            position.Top,
            width,
            height);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        _saveTimer.Stop();
        SaveWindowStateNow();
        base.OnClosing(e);
        ExitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySavedWindowState()
    {
        if (_windowStateStore is null)
        {
            return;
        }

        var state = _windowStateStore.Load();
        Width = state.Width;
        Height = state.Height;
        if (!double.IsFinite(state.Left) || !double.IsFinite(state.Top))
        {
            return;
        }

        var area = SystemParameters.WorkArea;
        var clamped = ClampState(
            state,
            new WorkArea(
                area.Left,
                area.Top,
                area.Width,
                area.Height));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = clamped.Left;
        Top = clamped.Top;
        Width = clamped.Width;
        Height = clamped.Height;
    }

    private void ScheduleWindowStateSave()
    {
        if (!_loaded || _windowStateStore is null)
        {
            return;
        }

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void OnDisplayChoiceChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (_initialized &&
            DisplayChoiceBox.SelectedItem is PreviewDisplayChoice choice)
        {
            _session.SetDisplayChoice(choice);
        }
    }

    private void OnFiveHourChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        FiveHourValueText.Text = $"{e.NewValue:0}%";
        if (_initialized)
        {
            _session.SetFiveHourPercent(e.NewValue);
        }
    }

    private void OnWeeklyChanged(
        object sender,
        RoutedPropertyChangedEventArgs<double> e)
    {
        WeeklyValueText.Text = $"{e.NewValue:0}%";
        if (_initialized)
        {
            _session.SetWeeklyPercent(e.NewValue);
        }
    }

    private void OnSkinChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initialized && SkinBox.SelectedItem is SkinId skin)
        {
            _session.SetSkin(skin);
        }
    }

    private void OnAnimationsChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            _session.SetAnimationsEnabled(
                AnimationsCheckBox.IsChecked == true);
        }
    }

    private void OnRefreshingChanged(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            _session.SetRefreshing(RefreshingCheckBox.IsChecked == true);
        }
    }

    private void OnOpenDetails(object sender, RoutedEventArgs e) =>
        _session.SetDetailsOpen(true);

    private void OnCloseDetails(object sender, RoutedEventArgs e) =>
        _session.SetDetailsOpen(false);

    private void OnExpand(object sender, RoutedEventArgs e) =>
        _session.ForceExpanded();

    private void OnLeftEdge(object sender, RoutedEventArgs e) =>
        _session.PreviewEdge(EdgeDockSide.Left);

    private void OnRightEdge(object sender, RoutedEventArgs e) =>
        _session.PreviewEdge(EdgeDockSide.Right);

    private void OnTopEdge(object sender, RoutedEventArgs e) =>
        _session.PreviewEdge(EdgeDockSide.Top);

    private void OnBottomEdge(object sender, RoutedEventArgs e) =>
        _session.PreviewEdge(EdgeDockSide.Bottom);

    private void OnOpenInstalled(object sender, RoutedEventArgs e) =>
        RequestOpenInstalled();
}
