using CodexQuotaHud.App.UI;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Preview;

internal sealed class PreviewSession
{
    private readonly PreviewQuotaRefreshController _controller;
    private readonly QuotaOrbViewModel _viewModel;
    private readonly IPreviewHud _hud;
    private PreviewDisplayChoice _choice = PreviewDisplayChoice.Dual;
    private double _fiveHourPercent = 68;
    private double _weeklyPercent = 34;
    private bool _isRefreshing;

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
        if (!Enum.IsDefined(choice))
        {
            throw new ArgumentOutOfRangeException(nameof(choice));
        }

        _choice = choice;
        Publish();
    }

    public void SetFiveHourPercent(double value)
    {
        _fiveHourPercent = Math.Clamp(value, 0, 100);
        Publish();
    }

    public void SetWeeklyPercent(double value)
    {
        _weeklyPercent = Math.Clamp(value, 0, 100);
        Publish();
    }

    public void SetRefreshing(bool value)
    {
        _isRefreshing = value;
        Publish();
    }

    public void SetSkin(SkinId skin)
    {
        if (!Enum.IsDefined(skin))
        {
            throw new ArgumentOutOfRangeException(nameof(skin));
        }

        _ = _hud.TryActivateSkinKey(SkinSelectionKey.FromBuiltIn(skin));
    }

    public void SetAnimationsEnabled(bool value) =>
        _viewModel.AnimationsEnabled = value;

    public void SetDetailsOpen(bool isOpen) =>
        _hud.SetDetailsOpen(isOpen);

    public void PreviewEdge(EdgeDockSide side)
    {
        if (side == EdgeDockSide.None || !Enum.IsDefined(side))
        {
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        _hud.PreviewEdge(side);
    }

    public void ForceExpanded() => _hud.ForceExpanded();

    private void Publish() =>
        _controller.Publish(
            _choice,
            _fiveHourPercent,
            _weeklyPercent,
            _isRefreshing);
}
