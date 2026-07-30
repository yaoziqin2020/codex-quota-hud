using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Preview;

public partial class PreviewControlWindow : Window
{
    private readonly PreviewSession _session;
    private bool _initialized;
    private int _openInstalledRequested;

    internal PreviewControlWindow(
        PreviewSession session,
        bool installedAppAvailable)
    {
        InitializeComponent();
        _session = session ?? throw new ArgumentNullException(nameof(session));
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

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        ExitRequested?.Invoke(this, EventArgs.Empty);
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
