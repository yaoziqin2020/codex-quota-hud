using System.ComponentModel;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using Forms = System.Windows.Forms;

namespace CodexQuotaHud.App.UI;

public sealed class TrayController : IDisposable
{
    private readonly QuotaOrbViewModel _viewModel;
    private readonly SkinController _skinController;
    private readonly Func<string, bool> _tryActivateSkinKey;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _animationsItem;
    private readonly Dictionary<string, Forms.ToolStripMenuItem> _skinItems =
        new(StringComparer.Ordinal);
    private readonly TrayIconLifetime _iconLifetime;
    private bool _disposed;

    public TrayController(
        QuotaOrbViewModel viewModel,
        HudSkinCatalog catalog,
        SkinController skinController,
        Func<string, bool> tryActivateSkinKey)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ArgumentNullException.ThrowIfNull(catalog);
        _skinController = skinController ?? throw new ArgumentNullException(
            nameof(skinController));
        _tryActivateSkinKey = tryActivateSkinKey ?? throw new ArgumentNullException(
            nameof(tryActivateSkinKey));

        var menu = new Forms.ContextMenuStrip();
        var refreshItem = menu.Items.Add("立即刷新");
        refreshItem.Click += (_, _) =>
            _viewModel.RefreshCommand.Execute(parameter: null);

        var skinItem = new Forms.ToolStripMenuItem("皮肤");
        foreach (var descriptor in catalog.Load().Healthy)
        {
            AddSkinItem(skinItem, descriptor);
        }

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
        _skinController.ActiveSkinChanged += OnActiveSkinChanged;
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
        _skinController.ActiveSkinChanged -= OnActiveSkinChanged;
        _notifyIcon.Visible = false;
        _iconLifetime.Dispose();
        _notifyIcon.Dispose();
        GC.SuppressFinalize(this);
    }

    private void AddSkinItem(
        Forms.ToolStripMenuItem parent,
        SkinDescriptor descriptor)
    {
        var item = CreateSkinMenuItem(descriptor, _tryActivateSkinKey);
        parent.DropDownItems.Add(item);
        _skinItems.Add(descriptor.SelectionKey, item);
    }

    internal static Forms.ToolStripMenuItem CreateSkinMenuItem(
        SkinDescriptor descriptor,
        Func<string, bool> tryActivateSkinKey)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(tryActivateSkinKey);
        var item = new Forms.ToolStripMenuItem(descriptor.DisplayName)
        {
            CheckOnClick = false,
            Tag = descriptor.SelectionKey
        };
        item.Click += (_, _) => _ = tryActivateSkinKey(descriptor.SelectionKey);
        return item;
    }

    private void OnActiveSkinChanged(object? sender, EventArgs e) =>
        Synchronize();

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName is
            nameof(QuotaOrbViewModel.SelectedSkinKey) or
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
        foreach (var (selectionKey, item) in _skinItems)
        {
            item.Checked = string.Equals(
                selectionKey,
                _skinController.CurrentDescriptor.SelectionKey,
                StringComparison.Ordinal);
        }

        _animationsItem.Checked = _viewModel.AnimationsEnabled;
        _statusItem.Text = _viewModel.StatusText;
        _iconLifetime.Replace(
            TrayIconRenderer.Render(
                TrayIconRenderer.CreateState(
                    _viewModel.DisplayMode,
                    _viewModel.PrimaryPercent,
                    _skinController.CurrentPresentation.TrayAccent)));
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
