using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Threading;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Refresh;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.UI;

public interface IQuotaRefreshController
{
    event Action<QuotaRefreshState>? StateChanged;

    Task RefreshNowAsync(bool onlyIfStale, CancellationToken cancellationToken);
}

public interface IUiDispatcher
{
    bool CheckAccess();

    void Post(Action action);
}

public sealed record QuotaDetailRow(
    string Label,
    string Remaining,
    string? ResetsAt);

internal sealed class QuotaRefreshController(
    QuotaRefreshService service) : IQuotaRefreshController
{
    public event Action<QuotaRefreshState>? StateChanged
    {
        add => service.StateChanged += value;
        remove => service.StateChanged -= value;
    }

    public Task RefreshNowAsync(
        bool onlyIfStale,
        CancellationToken cancellationToken) =>
        service.RefreshNowAsync(onlyIfStale, cancellationToken);
}

internal sealed class WpfUiDispatcher(
    Dispatcher dispatcher) : IUiDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action) =>
        _ = dispatcher.BeginInvoke(action, DispatcherPriority.DataBind);
}

public sealed class QuotaOrbViewModel :
    INotifyPropertyChanged,
    IDisposable
{
    private readonly IQuotaRefreshController _refreshController;
    private readonly SettingsStore _settingsStore;
    private readonly IUiDispatcher _dispatcher;
    private readonly Action _requestExit;
    private readonly object _settingsSync = new();
    private readonly ObservableCollection<QuotaDetailRow> _details = [];
    private AppSettings _settings;
    private double _primaryPercent;
    private double? _secondaryPercent;
    private string _primaryLabel = string.Empty;
    private bool _isRefreshing;
    private bool _isStale;
    private bool _isVisible;
    private string? _lastError;
    private DateTimeOffset? _lastUpdated;
    private bool _disposed;

    public QuotaOrbViewModel(
        IQuotaRefreshController refreshController,
        SettingsStore settingsStore,
        AppSettings settings,
        IUiDispatcher dispatcher,
        Action requestExit)
    {
        _refreshController =
            refreshController ?? throw new ArgumentNullException(nameof(refreshController));
        _settingsStore =
            settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _requestExit = requestExit ?? throw new ArgumentNullException(nameof(requestExit));

        RefreshCommand = new AsyncRelayCommand(
            () => _refreshController.RefreshNowAsync(
                onlyIfStale: false,
                CancellationToken.None));
        SelectSkinCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is SkinId skin && Enum.IsDefined(skin))
                {
                    SelectedSkin = skin;
                }
            });
        ToggleAnimationsCommand = new RelayCommand(
            _ => AnimationsEnabled = !AnimationsEnabled);
        ExitCommand = new RelayCommand(_ => _requestExit());

        _refreshController.StateChanged += OnStateChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public double PrimaryPercent
    {
        get => _primaryPercent;
        private set => SetField(ref _primaryPercent, value);
    }

    public double? SecondaryPercent
    {
        get => _secondaryPercent;
        private set
        {
            if (SetField(ref _secondaryPercent, value))
            {
                OnPropertyChanged(nameof(HasSecondary));
            }
        }
    }

    public bool HasSecondary => SecondaryPercent is not null;

    public string PrimaryLabel
    {
        get => _primaryLabel;
        private set => SetField(ref _primaryLabel, value);
    }

    public IReadOnlyList<QuotaDetailRow> Details => _details;

    public bool IsRefreshing
    {
        get => _isRefreshing;
        private set => SetField(ref _isRefreshing, value);
    }

    public bool IsStale
    {
        get => _isStale;
        private set
        {
            if (SetField(ref _isStale, value))
            {
                OnPropertyChanged(nameof(StaleMessage));
            }
        }
    }

    public string? StaleMessage => IsStale ? "数据可能已过期" : null;

    public bool IsVisible
    {
        get => _isVisible;
        private set => SetField(ref _isVisible, value);
    }

    public string? LastError
    {
        get => _lastError;
        private set
        {
            if (SetField(ref _lastError, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public DateTimeOffset? LastUpdated
    {
        get => _lastUpdated;
        private set
        {
            if (SetField(ref _lastUpdated, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(LastUpdatedText));
            }
        }
    }

    public string LastUpdatedText =>
        LastUpdated is null
            ? "尚未更新"
            : LastUpdated.Value.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);

    public string StatusText =>
        LastError is not null
            ? "暂时读不到额度"
            : LastUpdated is null
                ? "尚未读取额度"
                : $"上次更新：{LastUpdated.Value.ToLocalTime():HH:mm}";

    public SkinId SelectedSkin
    {
        get => _settings.SelectedSkin;
        set
        {
            if (!Enum.IsDefined(value) || _settings.SelectedSkin == value)
            {
                return;
            }

            SaveSettings(_settings with { SelectedSkin = value });
            OnPropertyChanged();
        }
    }

    public bool AnimationsEnabled
    {
        get => _settings.AnimationsEnabled;
        set
        {
            if (_settings.AnimationsEnabled == value)
            {
                return;
            }

            SaveSettings(_settings with { AnimationsEnabled = value });
            OnPropertyChanged();
        }
    }

    public ICommand RefreshCommand { get; }

    public ICommand SelectSkinCommand { get; }

    public ICommand ToggleAnimationsCommand { get; }

    public ICommand ExitCommand { get; }

    public Task OnHoverAsync() =>
        _refreshController.RefreshNowAsync(
            onlyIfStale: true,
            CancellationToken.None);

    public void SavePosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
        {
            return;
        }

        SaveSettings(_settings with { Left = left, Top = top });
    }

    public (double? Left, double? Top) GetSavedPosition() =>
        (_settings.Left, _settings.Top);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _refreshController.StateChanged -= OnStateChanged;
        GC.SuppressFinalize(this);
    }

    private void OnStateChanged(QuotaRefreshState state)
    {
        if (_dispatcher.CheckAccess())
        {
            ApplyState(state);
            return;
        }

        _dispatcher.Post(() => ApplyState(state));
    }

    private void ApplyState(QuotaRefreshState state)
    {
        IsRefreshing = state.IsRefreshing;
        LastError = state.LastError;
        IsStale = state.Display.IsStale;
        IsVisible = state.Display.Mode != QuotaDisplayMode.Hidden;
        LastUpdated = state.Display.FetchedAt;

        var primary = state.Display.Primary;
        PrimaryPercent = RoundPercent(primary?.RemainingPercent ?? 0);
        PrimaryLabel = primary is null ? string.Empty : LabelFor(primary.Kind);
        SecondaryPercent = state.Display.Secondary is null
            ? null
            : RoundPercent(state.Display.Secondary.RemainingPercent);

        _details.Clear();
        AddDetail(primary);
        AddDetail(state.Display.Secondary);
        OnPropertyChanged(nameof(Details));

        if (!state.Display.IsStale &&
            state.Display.FetchedAt is { } fetchedAt &&
            _settings.LastSuccessfulRefresh != fetchedAt)
        {
            SaveSettings(_settings with { LastSuccessfulRefresh = fetchedAt });
        }
    }

    private void AddDetail(QuotaWindow? window)
    {
        if (window is null)
        {
            return;
        }

        _details.Add(new QuotaDetailRow(
            LabelFor(window.Kind),
            $"{RoundPercent(window.RemainingPercent):0}%",
            window.ResetsAt?.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture)));
    }

    private void SaveSettings(AppSettings settings)
    {
        lock (_settingsSync)
        {
            _settingsStore.Save(settings);
            _settings = settings;
        }
    }

    private static double RoundPercent(double value) =>
        Math.Round(
            Math.Clamp(value, 0, 100),
            MidpointRounding.AwayFromZero);

    private static string LabelFor(QuotaWindowKind kind) =>
        kind == QuotaWindowKind.FiveHour ? "5 小时" : "每周";

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(
        [CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(propertyName));

    private sealed class RelayCommand(Action<object?> execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute(parameter);
    }

    private sealed class AsyncRelayCommand(
        Func<Task> execute) : ICommand
    {
        private int _isExecuting;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) =>
            Volatile.Read(ref _isExecuting) == 0;

        public async void Execute(object? parameter)
        {
            if (Interlocked.Exchange(ref _isExecuting, 1) != 0)
            {
                return;
            }

            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try
            {
                await execute();
            }
            catch
            {
            }
            finally
            {
                Interlocked.Exchange(ref _isExecuting, 0);
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
