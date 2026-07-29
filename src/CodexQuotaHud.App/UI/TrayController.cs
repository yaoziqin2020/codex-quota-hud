using System.ComponentModel;
using CodexQuotaHud.Core.Models;
using DrawingIcon = System.Drawing.Icon;
using DrawingSystemIcons = System.Drawing.SystemIcons;
using Forms = System.Windows.Forms;

namespace CodexQuotaHud.App.UI;

public sealed class TrayController : IDisposable
{
    private readonly QuotaOrbViewModel _viewModel;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _animationsItem;
    private readonly Dictionary<SkinId, Forms.ToolStripMenuItem> _skinItems = [];
    private bool _disposed;

    public TrayController(
        QuotaOrbViewModel viewModel,
        DrawingIcon? icon = null)
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
            Icon = icon ?? DrawingSystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) =>
            _viewModel.RefreshCommand.Execute(parameter: null);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Synchronize();
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
        _notifyIcon.Text = TruncateTooltip(
            $"Codex 剩余额度 - {_viewModel.StatusText}");
    }

    private static string TruncateTooltip(string value) =>
        value.Length <= 63 ? value : value[..63];
}
