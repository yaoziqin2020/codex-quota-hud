using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.SkinDesigner.Preview;
using CodexQuotaHud.SkinDesigner.UI;

namespace CodexQuotaHud.SkinDesigner.Tests.Preview;

public sealed class DesignerPreviewToolsViewModelTests
{
    [Fact]
    public void CompositionGuidesVisible_DefaultsOffWithoutChangingOverlay()
    {
        var changes = new List<bool>();
        var viewModel = new DesignerPreviewToolsViewModel(changes.Add);

        Assert.False(viewModel.CompositionGuidesVisible);
        Assert.Empty(changes);
    }

    [Fact]
    public void CompositionGuidesVisible_ForwardsDistinctChangesAndNotifies()
    {
        var changes = new List<bool>();
        var notifications = new List<string?>();
        var viewModel = new DesignerPreviewToolsViewModel(changes.Add);
        viewModel.PropertyChanged += (_, args) =>
            notifications.Add(args.PropertyName);

        viewModel.CompositionGuidesVisible = true;
        viewModel.CompositionGuidesVisible = true;
        viewModel.CompositionGuidesVisible = false;

        Assert.Equal([true, false], changes);
        Assert.Equal(
            [
                nameof(DesignerPreviewToolsViewModel.CompositionGuidesVisible),
                nameof(DesignerPreviewToolsViewModel.CompositionGuidesVisible)
            ],
            notifications);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshAudition_ForcesAndRestoresExactPreviousRefreshValue(
        bool previousValue)
    {
        var session = new RecordingPreviewSession();
        using var synthetic = new SyntheticPreviewViewModel(
            session,
            animationsAllowed: () => true);
        synthetic.IsRefreshing = previousValue;
        var auditions = new List<DesignerAnimationAudition>();
        using var viewModel = new DesignerPreviewToolsViewModel(
            _ => { },
            auditions.Add,
            synthetic);

        viewModel.AnimationAudition = DesignerAnimationAudition.Refresh;

        Assert.True(synthetic.IsRefreshing);
        Assert.False(viewModel.CanEditRefreshCheckbox);

        viewModel.AnimationAudition = DesignerAnimationAudition.Rotation;

        Assert.Equal(previousValue, synthetic.IsRefreshing);
        Assert.True(viewModel.CanEditRefreshCheckbox);
        Assert.Equal(
            [DesignerAnimationAudition.Refresh, DesignerAnimationAudition.Rotation],
            auditions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposeDuringRefresh_RestoresRefreshAndReenablesManualCheckbox(
        bool previousValue)
    {
        var session = new RecordingPreviewSession();
        using var synthetic = new SyntheticPreviewViewModel(
            session,
            animationsAllowed: () => true);
        synthetic.IsRefreshing = previousValue;
        var auditions = new List<DesignerAnimationAudition>();
        var notifications = new List<string?>();
        var viewModel = new DesignerPreviewToolsViewModel(
            _ => { },
            auditions.Add,
            synthetic);
        viewModel.PropertyChanged += (_, args) =>
            notifications.Add(args.PropertyName);
        viewModel.AnimationAudition = DesignerAnimationAudition.Refresh;

        viewModel.Dispose();

        Assert.Equal(previousValue, synthetic.IsRefreshing);
        Assert.Equal(DesignerAnimationAudition.All, viewModel.AnimationAudition);
        Assert.True(viewModel.CanEditRefreshCheckbox);
        Assert.Equal(
            [DesignerAnimationAudition.Refresh, DesignerAnimationAudition.All],
            auditions);
        Assert.Equal(2, notifications.Count(name =>
            name == nameof(DesignerPreviewToolsViewModel.AnimationAudition)));
        Assert.Equal(2, notifications.Count(name =>
            name == nameof(DesignerPreviewToolsViewModel.CanEditRefreshCheckbox)));
    }

    private sealed class RecordingPreviewSession : ISyntheticPreviewSession
    {
        public void SetDisplayChoice(PreviewDisplayChoice choice)
        {
        }

        public void SetFiveHourPercent(double value)
        {
        }

        public void SetWeeklyPercent(double value)
        {
        }

        public void SetDetailsOpen(bool value)
        {
        }

        public void SetAnimationsEnabled(bool value)
        {
        }

        public void SetRefreshing(bool value)
        {
        }

        public void PreviewEdge(EdgeDockSide side)
        {
        }

        public void ForceExpanded()
        {
        }

        public void RecenterAfterExpand()
        {
        }
    }
}
