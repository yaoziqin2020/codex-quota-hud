using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.SkinDesigner.UI;

namespace CodexQuotaHud.SkinDesigner.Tests.UI;

public sealed class SyntheticPreviewViewModelTests
{
    [Fact]
    public async Task ExposesExactControlsAndDelegatesOnlyToPreviewSession()
    {
        var session = new RecordingPreviewSession();
        using var sut = new SyntheticPreviewViewModel(
            session,
            animationsAllowed: () => true);

        Assert.Equal([100d, 68d, 21d, 20d, 11d, 10d, 0d],
            sut.PercentPresets);
        sut.DisplayChoice = PreviewDisplayChoice.WeeklyOnly;
        sut.FiveHourPercent = 21;
        sut.WeeklyPercent = 68;
        sut.DetailsOpen = true;
        sut.AnimationsEnabled = false;
        sut.IsRefreshing = true;
        await sut.PreviewLeftEdgeCommand.ExecuteAsync();
        await sut.PreviewRightEdgeCommand.ExecuteAsync();
        await sut.PreviewTopEdgeCommand.ExecuteAsync();
        await sut.PreviewBottomEdgeCommand.ExecuteAsync();
        await sut.ExpandCommand.ExecuteAsync();

        Assert.Equal(PreviewDisplayChoice.WeeklyOnly, session.DisplayChoice);
        Assert.Equal(21, session.FiveHourPercent);
        Assert.Equal(68, session.WeeklyPercent);
        Assert.True(session.DetailsOpen);
        Assert.False(session.AnimationsEnabled);
        Assert.True(session.IsRefreshing);
        Assert.Equal(
            [EdgeDockSide.Left, EdgeDockSide.Right, EdgeDockSide.Top,
                EdgeDockSide.Bottom],
            session.Edges);
        Assert.Equal(1, session.ExpandedCount);
        Assert.Equal(1, session.RecenterCount);
    }

    [Fact]
    public void InvalidValuesNeverDelegateAndReducedMotionCannotBeOverridden()
    {
        var session = new RecordingPreviewSession();
        using var sut = new SyntheticPreviewViewModel(
            session,
            animationsAllowed: () => false);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sut.DisplayChoice = (PreviewDisplayChoice)999);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sut.FiveHourPercent = -0.001);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            sut.WeeklyPercent = 100.001);
        sut.AnimationsEnabled = true;

        Assert.False(sut.AnimationsEnabled);
        Assert.False(session.AnimationsEnabled);
        Assert.Equal(0, session.DisplayChoiceChanges);
        Assert.Equal(0, session.FiveHourChanges);
        Assert.Equal(0, session.WeeklyChanges);
    }

    [Fact]
    public void RefreshToggle_UsesTheSharedPreviewSessionForHoldAndRestart()
    {
        var session = new RecordingPreviewSession();
        using var sut = new SyntheticPreviewViewModel(
            session,
            animationsAllowed: () => true);

        sut.IsRefreshing = true;
        sut.IsRefreshing = false;
        sut.IsRefreshing = true;

        Assert.Equal([true, false, true], session.RefreshingValues);
        Assert.True(sut.IsRefreshing);
    }

    private sealed class RecordingPreviewSession : ISyntheticPreviewSession
    {
        public PreviewDisplayChoice DisplayChoice { get; private set; } =
            PreviewDisplayChoice.Dual;

        public int DisplayChoiceChanges { get; private set; }

        public double FiveHourPercent { get; private set; } = 68;

        public int FiveHourChanges { get; private set; }

        public double WeeklyPercent { get; private set; } = 34;

        public int WeeklyChanges { get; private set; }

        public bool DetailsOpen { get; private set; }

        public bool AnimationsEnabled { get; private set; } = true;

        public bool IsRefreshing { get; private set; }

        public List<bool> RefreshingValues { get; } = [];

        public List<EdgeDockSide> Edges { get; } = [];

        public int ExpandedCount { get; private set; }

        public int RecenterCount { get; private set; }

        public void SetDisplayChoice(PreviewDisplayChoice choice)
        {
            DisplayChoice = choice;
            DisplayChoiceChanges++;
        }

        public void SetFiveHourPercent(double value)
        {
            FiveHourPercent = value;
            FiveHourChanges++;
        }

        public void SetWeeklyPercent(double value)
        {
            WeeklyPercent = value;
            WeeklyChanges++;
        }

        public void SetDetailsOpen(bool value) => DetailsOpen = value;

        public void SetAnimationsEnabled(bool value) => AnimationsEnabled = value;

        public void SetRefreshing(bool value)
        {
            IsRefreshing = value;
            RefreshingValues.Add(value);
        }

        public void PreviewEdge(EdgeDockSide side) => Edges.Add(side);

        public void ForceExpanded() => ExpandedCount++;

        public void RecenterAfterExpand() => RecenterCount++;
    }
}
