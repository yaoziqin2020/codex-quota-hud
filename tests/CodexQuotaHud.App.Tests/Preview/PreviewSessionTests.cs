using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Tests.Preview;

public sealed class PreviewSessionTests
{
    [Fact]
    public void Apply_PublishesOneValidatedSyntheticSnapshotToProductionSurfaces()
    {
        var controller = new PreviewQuotaRefreshController();
        var hud = new RecordingHud();
        using var viewModel = CreateViewModel(controller);
        var session = new PreviewSession(controller, viewModel, hud);
        var publications = 0;
        controller.StateChanged += _ => publications++;

        session.Apply(new SyntheticPreviewState(
            PreviewDisplayChoice.Dual,
            FiveHourPercent: 11,
            WeeklyPercent: 10,
            AnimationsEnabled: false,
            IsRefreshing: true,
            DetailsOpen: true,
            EdgeSide: EdgeDockSide.Bottom));

        Assert.Equal(1, publications);
        Assert.Equal(QuotaDisplayMode.Dual, viewModel.DisplayMode);
        Assert.Equal("5 小时", viewModel.PrimaryLabel);
        Assert.Equal(11, viewModel.PrimaryPercent);
        Assert.Equal(10, viewModel.SecondaryPercent);
        Assert.Equal(QuotaAlertLevel.Warning, viewModel.SkinState.PrimaryAlert);
        Assert.Equal(QuotaAlertLevel.Critical, viewModel.SkinState.SecondaryAlert);
        Assert.False(viewModel.AnimationsEnabled);
        Assert.True(viewModel.IsRefreshing);
        Assert.True(hud.DetailsOpen);
        Assert.Equal([EdgeDockSide.Bottom], hud.Edges);
    }

    [Theory]
    [InlineData(21, 10, QuotaAlertLevel.Normal, QuotaAlertLevel.Critical)]
    [InlineData(20, 11, QuotaAlertLevel.Warning, QuotaAlertLevel.Warning)]
    [InlineData(11, 20, QuotaAlertLevel.Warning, QuotaAlertLevel.Warning)]
    [InlineData(10, 21, QuotaAlertLevel.Critical, QuotaAlertLevel.Normal)]
    public void DualMixedBoundaries_ClassifyEachChannelIndependently(
        double fiveHour,
        double weekly,
        QuotaAlertLevel expectedPrimary,
        QuotaAlertLevel expectedSecondary)
    {
        var controller = new PreviewQuotaRefreshController();
        var hud = new RecordingHud();
        using var viewModel = CreateViewModel(controller);
        var session = new PreviewSession(controller, viewModel, hud);

        session.Apply(SyntheticPreviewState.Default with
        {
            FiveHourPercent = fiveHour,
            WeeklyPercent = weekly
        });

        Assert.Equal(expectedPrimary, viewModel.SkinState.PrimaryAlert);
        Assert.Equal(expectedSecondary, viewModel.SkinState.SecondaryAlert);
    }

    [Fact]
    public void Apply_RejectsInvalidSnapshotBeforePublishingAnyChange()
    {
        var controller = new PreviewQuotaRefreshController();
        var hud = new RecordingHud();
        using var viewModel = CreateViewModel(controller);
        var session = new PreviewSession(controller, viewModel, hud);
        var before = controller.CurrentState;

        Assert.Throws<ArgumentOutOfRangeException>(() => session.Apply(
            SyntheticPreviewState.Default with
            {
                FiveHourPercent = double.NaN
            }));

        Assert.Same(before, controller.CurrentState);
        Assert.True(viewModel.AnimationsEnabled);
        Assert.False(hud.DetailsOpen);
        Assert.Empty(hud.Edges);
    }

    [Fact]
    public void Controls_UpdateProductionViewModel()
    {
        var controller = new PreviewQuotaRefreshController();
        var hud = new RecordingHud();
        using var viewModel = CreateViewModel(controller);
        var session = new PreviewSession(controller, viewModel, hud);

        session.SetDisplayChoice(PreviewDisplayChoice.WeeklyOnly);
        Assert.Equal(QuotaDisplayMode.Single, viewModel.DisplayMode);
        Assert.Equal("每周", viewModel.PrimaryLabel);
        Assert.Null(viewModel.SecondaryPercent);

        session.SetFiveHourPercent(91);
        session.SetDisplayChoice(PreviewDisplayChoice.Dual);
        Assert.Equal(91, viewModel.PrimaryPercent);
        Assert.Equal(34, viewModel.SecondaryPercent);

        session.SetRefreshing(true);
        Assert.True(viewModel.SkinState.IsRefreshing);
    }

    [Fact]
    public void SkinAnimationAndHudCommands_ReachProductionSurfaces()
    {
        var controller = new PreviewQuotaRefreshController();
        var hud = new RecordingHud();
        using var viewModel = CreateViewModel(controller);
        var session = new PreviewSession(controller, viewModel, hud);

        foreach (var skin in Enum.GetValues<SkinId>())
        {
            Assert.True(session.SetBuiltInSkin(skin));
            Assert.Equal(
                SkinSelectionKey.FromBuiltIn(skin),
                hud.ActivatedKeys[^1]);
        }

        session.SetAnimationsEnabled(false);
        session.SetDetailsOpen(true);
        session.PreviewEdge(EdgeDockSide.Left);
        session.PreviewEdge(EdgeDockSide.Right);
        session.PreviewEdge(EdgeDockSide.Top);
        session.PreviewEdge(EdgeDockSide.Bottom);
        session.ForceExpanded();

        Assert.False(viewModel.AnimationsEnabled);
        Assert.True(hud.DetailsOpen);
        Assert.Equal(
            [EdgeDockSide.Left, EdgeDockSide.Right,
                EdgeDockSide.Top, EdgeDockSide.Bottom],
            hud.Edges);
        Assert.Equal(1, hud.ExpandCount);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.SetBuiltInSkin((SkinId)999));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => session.PreviewEdge(EdgeDockSide.None));
    }

    private static QuotaOrbViewModel CreateViewModel(
        PreviewQuotaRefreshController controller) =>
        new(
            controller,
            new InMemorySettingsStore(new AppSettings()),
            new AppSettings(),
            new ImmediateDispatcher(),
            () => { });

    private sealed class ImmediateDispatcher : IUiDispatcher
    {
        public bool CheckAccess() => true;
        public void Post(Action action) => action();
    }

    private sealed class RecordingHud : IPreviewHud
    {
        public bool DetailsOpen { get; private set; }
        public List<EdgeDockSide> Edges { get; } = [];
        public List<string> ActivatedKeys { get; } = [];
        public int ExpandCount { get; private set; }

        public bool TryActivateSkinKey(string selectionKey)
        {
            ActivatedKeys.Add(selectionKey);
            return true;
        }

        public void SetDetailsOpen(bool isOpen) => DetailsOpen = isOpen;
        public void PreviewEdge(EdgeDockSide side) => Edges.Add(side);
        public void ForceExpanded() => ExpandCount++;
    }
}
