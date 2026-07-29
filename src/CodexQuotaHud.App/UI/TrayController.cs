using System.ComponentModel;
using CodexQuotaHud.Core.Models;
using Forms = System.Windows.Forms;

namespace CodexQuotaHud.App.UI;

public sealed class TrayController : IDisposable
{
    private readonly QuotaOrbViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _animationsItem;
    private readonly Dictionary<SkinId, Forms.ToolStripMenuItem> _skinItems = [];
    private readonly TrayIconLifetime _iconLifetime;
    private bool _disposed;

    public TrayController(QuotaOrbViewModel viewModel)
    {
        _viewModel =
            viewModel ?? throw new ArgumentNullException(nameof(viewModel));

        var menu = new Forms.ContextMenuStrip();
        var refreshItem = menu.Items.Add("立即刷新");
        refreshItem.Click += (_, _) =>
            _viewModel.RefreshCommand.Execute(parameter: null);

        var skinItem = new Forms.ToolStripMenuItem("皮肤");
        AddSkinItem(skinItem, SkinId.HudDial, "HUD 科技仪表");
        AddSkinItem(skinItem, SkinId.EnergyRing, "双彩能量环");
        AddSkinItem(skinItem, SkinId.LiquidGlass, "流体玻璃球");
        AddSkinItem(skinItem, SkinId.Aurora, "克制极光");
        AddSkinItem(skinItem, SkinId.LiquidTank, "液位储能舱");
        menu.Items.Add(skinItem);

        _animationsItem = new Forms.ToolStripMenuItem("动画")
        {
            CheckOnClick = true
        };
        _animationsItem.Click += (_, _) =>
            _viewModel.ToggleAnimationsCommand.Execute(parameter: null);
        menu.Items.Add(_animationsItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        _statusItem = new Forms.ToolStripMenuItem
        {
            Enabled = false
        };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new Forms.ToolStripSeparator());

        var exitItem = menu.Items.Add("退出");
        exitItem.Click += (_, _) =>
            _viewModel.ExitCommand.Execute(parameter: null);

        _notifyIcon = new Forms.NotifyIcon
        {
            ContextMenuStrip = menu,
            Visible = false
        };
        _iconLifetime = new TrayIconLifetime(
            icon => _notifyIcon.Icon = icon);
        _notifyIcon.DoubleClick += (_, _) =>
            _viewModel.RefreshCommand.Execute(parameter: null);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Synchronize();
        _notifyIcon.Visible = true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _notifyIcon.Visible = false;
        _iconLifetime.Dispose();
        _notifyIcon.Dispose();
        GC.SuppressFinalize(this);
    }

    private void AddSkinItem(
        Forms.ToolStripMenuItem parent,
        SkinId skin,
        string text)
    {
        var item = new Forms.ToolStripMenuItem(text)
        {
            CheckOnClick = false,
            Tag = skin
        };
        item.Click += (_, _) =>
            _viewModel.SelectSkinCommand.Execute(skin);
        parent.DropDownItems.Add(item);
        _skinItems.Add(skin, item);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(QuotaOrbViewModel.SelectedSkin) or
            nameof(QuotaOrbViewModel.AnimationsEnabled) or
            nameof(QuotaOrbViewModel.DisplayMode) or
            nameof(QuotaOrbViewModel.PrimaryPercent) or
            nameof(QuotaOrbViewModel.PrimaryLabel) or
            nameof(QuotaOrbViewModel.StatusText) or
            nameof(QuotaOrbViewModel.LastUpdated) or
            nameof(QuotaOrbViewModel.LastError))
        {
            Synchronize();
        }
    }

    private void Synchronize()
    {
        foreach (var (skin, item) in _skinItems)
        {
            item.Checked = skin == _viewModel.SelectedSkin;
        }

        _animationsItem.Checked = _viewModel.AnimationsEnabled;
        _statusItem.Text = _viewModel.StatusText;
        _iconLifetime.Replace(
            TrayIconRenderer.Render(
                TrayIconRenderer.CreateState(
                    _viewModel.DisplayMode,
                    _viewModel.PrimaryPercent,
                    _viewModel.SelectedSkin)));
        _notifyIcon.Text = TruncateTooltip(CreateTooltip());
    }

    private string CreateTooltip() =>
        _viewModel.DisplayMode == QuotaDisplayMode.Hidden
            ? "Codex · 暂无额度数据"
            : $"Codex · {_viewModel.PrimaryLabel} " +
              $"{_viewModel.PrimaryPercent:0}% · " +
              $"{_viewModel.LastUpdatedText}";

    private static string TruncateTooltip(string value) =>
        value.Length <= 63 ? value : value[..63];
}
