using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.SkinDesigner.UI;

internal interface ISyntheticPreviewSession
{
    void SetDisplayChoice(PreviewDisplayChoice choice);

    void SetFiveHourPercent(double value);

    void SetWeeklyPercent(double value);

    void SetDetailsOpen(bool value);

    void SetAnimationsEnabled(bool value);

    void SetRefreshing(bool value);

    void PreviewEdge(EdgeDockSide side);

    void ForceExpanded();

    void RecenterAfterExpand();
}

public sealed class SyntheticPreviewViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly IReadOnlyList<double> Presets =
        Array.AsReadOnly([100d, 68d, 21d, 20d, 11d, 10d, 0d]);

    private readonly ISyntheticPreviewSession _session;
    private readonly Func<bool> _animationsAllowed;
    private PreviewDisplayChoice _displayChoice = PreviewDisplayChoice.Dual;
    private double _fiveHourPercent = 68;
    private double _weeklyPercent = 34;
    private bool _detailsOpen;
    private bool _animationsEnabled;
    private bool _isRefreshing;

    public SyntheticPreviewViewModel(
        PreviewSession session,
        Action? recenterAfterExpand = null)
        : this(
            new PreviewSessionAdapter(session, recenterAfterExpand),
            () => SystemParameters.ClientAreaAnimation)
    {
    }

    internal SyntheticPreviewViewModel(
        ISyntheticPreviewSession session,
        Func<bool> animationsAllowed)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _animationsAllowed = animationsAllowed ??
            throw new ArgumentNullException(nameof(animationsAllowed));
        _animationsEnabled = _animationsAllowed();
        if (!_animationsEnabled)
        {
            _session.SetAnimationsEnabled(false);
        }

        PreviewLeftEdgeCommand = EdgeCommand(EdgeDockSide.Left);
        PreviewRightEdgeCommand = EdgeCommand(EdgeDockSide.Right);
        PreviewTopEdgeCommand = EdgeCommand(EdgeDockSide.Top);
        PreviewBottomEdgeCommand = EdgeCommand(EdgeDockSide.Bottom);
        ExpandCommand = new AsyncRelayCommand(_ =>
        {
            _session.ForceExpanded();
            _session.RecenterAfterExpand();
            return Task.CompletedTask;
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<double> PercentPresets => Presets;

    public PreviewDisplayChoice DisplayChoice
    {
        get => _displayChoice;
        set
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            if (SetField(ref _displayChoice, value))
            {
                _session.SetDisplayChoice(value);
            }
        }
    }

    public double FiveHourPercent
    {
        get => _fiveHourPercent;
        set
        {
            ValidatePercent(value);
            if (SetField(ref _fiveHourPercent, value))
            {
                _session.SetFiveHourPercent(value);
            }
        }
    }

    public double WeeklyPercent
    {
        get => _weeklyPercent;
        set
        {
            ValidatePercent(value);
            if (SetField(ref _weeklyPercent, value))
            {
                _session.SetWeeklyPercent(value);
            }
        }
    }

    public bool DetailsOpen
    {
        get => _detailsOpen;
        set
        {
            if (SetField(ref _detailsOpen, value))
            {
                _session.SetDetailsOpen(value);
            }
        }
    }

    public bool AnimationsEnabled
    {
        get => _animationsEnabled;
        set
        {
            var effective = value && _animationsAllowed();
            if (SetField(ref _animationsEnabled, effective))
            {
                _session.SetAnimationsEnabled(effective);
            }
            else if (value != effective)
            {
                _session.SetAnimationsEnabled(effective);
            }
        }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            if (SetField(ref _isRefreshing, value))
            {
                _session.SetRefreshing(value);
            }
        }
    }

    public AsyncRelayCommand PreviewLeftEdgeCommand { get; }

    public AsyncRelayCommand PreviewRightEdgeCommand { get; }

    public AsyncRelayCommand PreviewTopEdgeCommand { get; }

    public AsyncRelayCommand PreviewBottomEdgeCommand { get; }

    public AsyncRelayCommand ExpandCommand { get; }

    public void Dispose()
    {
        PreviewLeftEdgeCommand.Dispose();
        PreviewRightEdgeCommand.Dispose();
        PreviewTopEdgeCommand.Dispose();
        PreviewBottomEdgeCommand.Dispose();
        ExpandCommand.Dispose();
    }

    private AsyncRelayCommand EdgeCommand(EdgeDockSide side) =>
        new(_ =>
        {
            _session.PreviewEdge(side);
            return Task.CompletedTask;
        });

    private static void ValidatePercent(double value)
    {
        if (!double.IsFinite(value) || value is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    private sealed class PreviewSessionAdapter(
        PreviewSession session,
        Action? recenterAfterExpand)
        : ISyntheticPreviewSession
    {
        private readonly PreviewSession _session = session ??
            throw new ArgumentNullException(nameof(session));

        public void SetDisplayChoice(PreviewDisplayChoice choice) =>
            _session.SetDisplayChoice(choice);

        public void SetFiveHourPercent(double value) =>
            _session.SetFiveHourPercent(value);

        public void SetWeeklyPercent(double value) =>
            _session.SetWeeklyPercent(value);

        public void SetDetailsOpen(bool value) =>
            _session.SetDetailsOpen(value);

        public void SetAnimationsEnabled(bool value) =>
            _session.SetAnimationsEnabled(value);

        public void SetRefreshing(bool value) =>
            _session.SetRefreshing(value);

        public void PreviewEdge(EdgeDockSide side) =>
            _session.PreviewEdge(side);

        public void ForceExpanded() => _session.ForceExpanded();

        public void RecenterAfterExpand() => recenterAfterExpand?.Invoke();
    }
}
