using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Tests.Preview;

public sealed class PreviewSessionTests
{
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
            session.SetSkin(skin);
            Assert.Equal(skin, viewModel.SelectedSkin);
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
        public int ExpandCount { get; private set; }

        public void SetDetailsOpen(bool isOpen) => DetailsOpen = isOpen;
        public void PreviewEdge(EdgeDockSide side) => Edges.Add(side);
        public void ForceExpanded() => ExpandCount++;
    }
}
