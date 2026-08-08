using CodexQuotaHud.SkinDesigner.Preview;

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
}
