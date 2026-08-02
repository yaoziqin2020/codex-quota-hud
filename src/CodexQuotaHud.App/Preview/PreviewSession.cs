using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Preview;

public sealed class PreviewSession
{
    private readonly PreviewQuotaRefreshController _controller;
    private readonly QuotaOrbViewModel _viewModel;
    private readonly IPreviewHud _hud;
    private SyntheticPreviewState _state = SyntheticPreviewState.Default;

    public PreviewSession(
        PreviewQuotaRefreshController controller,
        QuotaOrbViewModel viewModel,
        IPreviewHud hud)
    {
        _controller = controller;
        _viewModel = viewModel;
        _hud = hud;
        Publish();
    }

    public void SetDisplayChoice(PreviewDisplayChoice choice)
    {
        _state = _state with { DisplayChoice = choice };
        Publish();
    }

    public void SetFiveHourPercent(double value)
    {
        _state = _state with { FiveHourPercent = value };
        Publish();
    }

    public void SetWeeklyPercent(double value)
    {
        _state = _state with { WeeklyPercent = value };
        Publish();
    }

    public void SetRefreshing(bool value)
    {
        _state = _state with { IsRefreshing = value };
        Publish();
    }

    public bool SetBuiltInSkin(SkinId skin)
    {
        if (!Enum.IsDefined(skin))
        {
            throw new ArgumentOutOfRangeException(nameof(skin));
        }

        return _hud.TryActivateSkinKey(SkinSelectionKey.FromBuiltIn(skin));
    }

    public void SetAnimationsEnabled(bool value)
    {
        _state = _state with { AnimationsEnabled = value };
        _viewModel.AnimationsEnabled = value;
    }

    public void SetDetailsOpen(bool isOpen)
    {
        _state = _state with { DetailsOpen = isOpen };
        _hud.SetDetailsOpen(isOpen);
    }

    public void PreviewEdge(EdgeDockSide side)
    {
        if (side == EdgeDockSide.None)
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        _state = _state with { EdgeSide = side };
        _hud.PreviewEdge(side);
    }

    public void ForceExpanded()
    {
        _state = _state with { EdgeSide = EdgeDockSide.None };
        _hud.ForceExpanded();
    }

    public void Apply(SyntheticPreviewState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        SyntheticPreviewState.Validate(state);

        _state = state;
        _viewModel.AnimationsEnabled = state.AnimationsEnabled;
        _hud.SetDetailsOpen(state.DetailsOpen);
        if (state.EdgeSide == EdgeDockSide.None)
        {
            _hud.ForceExpanded();
        }
        else
        {
            _hud.PreviewEdge(state.EdgeSide);
        }

        Publish();
    }

    private void Publish() =>
        _controller.Publish(
            _state.DisplayChoice,
            _state.FiveHourPercent,
            _state.WeeklyPercent,
            _state.IsRefreshing);
}
