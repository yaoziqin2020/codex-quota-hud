using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Preview;

public sealed record SyntheticPreviewState
{
    private PreviewDisplayChoice _displayChoice;
    private double _fiveHourPercent;
    private double _weeklyPercent;
    private EdgeDockSide _edgeSide;

    public SyntheticPreviewState(
        PreviewDisplayChoice DisplayChoice,
        double FiveHourPercent,
        double WeeklyPercent,
        bool AnimationsEnabled,
        bool IsRefreshing,
        bool DetailsOpen,
        EdgeDockSide EdgeSide)
    {
        this.DisplayChoice = DisplayChoice;
        this.FiveHourPercent = FiveHourPercent;
        this.WeeklyPercent = WeeklyPercent;
        this.AnimationsEnabled = AnimationsEnabled;
        this.IsRefreshing = IsRefreshing;
        this.DetailsOpen = DetailsOpen;
        this.EdgeSide = EdgeSide;
    }

    public static SyntheticPreviewState Default { get; } = new(
        PreviewDisplayChoice.Dual,
        FiveHourPercent: 68,
        WeeklyPercent: 34,
        AnimationsEnabled: true,
        IsRefreshing: false,
        DetailsOpen: false,
        EdgeSide: EdgeDockSide.None);

    public PreviewDisplayChoice DisplayChoice
    {
        get => _displayChoice;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(DisplayChoice));
            }

            _displayChoice = value;
        }
    }

    public double FiveHourPercent
    {
        get => _fiveHourPercent;
        init
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(FiveHourPercent));
            }

            _fiveHourPercent = value;
        }
    }

    public double WeeklyPercent
    {
        get => _weeklyPercent;
        init
        {
            if (!double.IsFinite(value))
            {
                throw new ArgumentOutOfRangeException(nameof(WeeklyPercent));
            }

            _weeklyPercent = value;
        }
    }

    public bool AnimationsEnabled { get; init; }

    public bool IsRefreshing { get; init; }

    public bool DetailsOpen { get; init; }

    public EdgeDockSide EdgeSide
    {
        get => _edgeSide;
        init
        {
            if (!Enum.IsDefined(value))
            {
                throw new ArgumentOutOfRangeException(nameof(EdgeSide));
            }

            _edgeSide = value;
        }
    }

    internal static void Validate(SyntheticPreviewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Enum.IsDefined(state.DisplayChoice))
        {
            throw new ArgumentOutOfRangeException(nameof(DisplayChoice));
        }

        if (!double.IsFinite(state.FiveHourPercent))
        {
            throw new ArgumentOutOfRangeException(nameof(FiveHourPercent));
        }

        if (!double.IsFinite(state.WeeklyPercent))
        {
            throw new ArgumentOutOfRangeException(nameof(WeeklyPercent));
        }

        if (!Enum.IsDefined(state.EdgeSide))
        {
            throw new ArgumentOutOfRangeException(nameof(EdgeSide));
        }
    }
}
