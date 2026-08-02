using System.ComponentModel;
using CodexQuotaHud.App.UI.SkinManagement;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using Forms = System.Windows.Forms;

namespace CodexQuotaHud.App.UI;

public sealed class TrayController : IDisposable
{
    private readonly QuotaOrbViewModel _viewModel;
    private readonly HudSkinCatalog _catalog;
    private readonly SkinController _skinController;
    private readonly SkinManagementController? _skinManagement;
    private readonly Func<string, bool> _tryActivateSkinKey;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Forms.ToolStripMenuItem _statusItem;
    private readonly Forms.ToolStripMenuItem _animationsItem;
    private readonly Forms.ToolStripMenuItem _skinMenu;
    private readonly TrayIconLifetime _iconLifetime;
    private bool _disposed;

    public TrayController(
        QuotaOrbViewModel viewModel,
        HudSkinCatalog catalog,
        SkinController skinController,
        Func<string, bool> tryActivateSkinKey)
        : this(
            viewModel,
            catalog,
            skinController,
            tryActivateSkinKey,
            skinManagement: null)
    {
    }

    internal TrayController(
        QuotaOrbViewModel viewModel,
        HudSkinCatalog catalog,
        SkinController skinController,
        Func<string, bool> tryActivateSkinKey,
        SkinManagementController? skinManagement)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _skinController = skinController ?? throw new ArgumentNullException(
            nameof(skinController));
        _skinManagement = skinManagement;
        _tryActivateSkinKey = tryActivateSkinKey ?? throw new ArgumentNullException(
            nameof(tryActivateSkinKey));

        var menu = new Forms.ContextMenuStrip();
        var refreshItem = menu.Items.Add("立即刷新");
        refreshItem.Click += (_, _) =>
            _viewModel.RefreshCommand.Execute(parameter: null);

        _skinMenu = new Forms.ToolStripMenuItem("皮肤");
        _skinMenu.DropDownOpening += (_, _) => RebuildManagedSkinMenu();
        menu.Items.Add(_skinMenu);

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
        if (_skinManagement is not null)
        {
            _skinManagement.CatalogChanged += OnManagedCatalogChanged;
        }
        RebuildManagedSkinMenu();
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
        if (_skinManagement is not null)
        {
            _skinManagement.CatalogChanged -= OnManagedCatalogChanged;
        }
        _notifyIcon.Visible = false;
        _iconLifetime.Dispose();
        _notifyIcon.Dispose();
        GC.SuppressFinalize(this);
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

    internal static void RebuildSkinMenu(
        Forms.ToolStripMenuItem root,
        IReadOnlyList<SkinMenuEntry> entries,
        bool designerAvailable,
        Action<string> select,
        Func<string, Task> remove,
        Func<Task> import,
        Action openDesigner)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(select);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(openDesigner);
        root.DropDownItems.Clear();

        foreach (var entry in entries)
        {
            var item = new Forms.ToolStripMenuItem(entry.DisplayName)
            {
                Checked = entry.IsSelected,
                CheckOnClick = false,
                Tag = entry.SelectionKey
            };
            if (entry.CanRemove)
            {
                var selectItem = new Forms.ToolStripMenuItem("选择");
                selectItem.Click += (_, _) => select(entry.SelectionKey);
                var removeItem = new Forms.ToolStripMenuItem("删除");
                removeItem.Click += async (_, _) => await remove(entry.SelectionKey);
                item.DropDownItems.Add(selectItem);
                item.DropDownItems.Add(removeItem);
            }
            else
            {
                item.Click += (_, _) => select(entry.SelectionKey);
            }

            root.DropDownItems.Add(item);
        }

        root.DropDownItems.Add(new Forms.ToolStripSeparator());
        var importItem = new Forms.ToolStripMenuItem("导入皮肤…");
        importItem.Click += async (_, _) => await import();
        root.DropDownItems.Add(importItem);
        if (designerAvailable)
        {
            var designerItem = new Forms.ToolStripMenuItem("打开皮肤设计器");
            designerItem.Click += (_, _) => openDesigner();
            root.DropDownItems.Add(designerItem);
        }
    }

    private void OnActiveSkinChanged(object? sender, EventArgs e) =>
        Synchronize();

    private void OnManagedCatalogChanged(object? sender, EventArgs e)
    {
        RebuildManagedSkinMenu();
        Synchronize();
    }

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
        foreach (var item in _skinMenu.DropDownItems
                     .OfType<Forms.ToolStripMenuItem>()
                     .Where(item => item.Tag is string))
        {
            var selectionKey = (string)item.Tag!;
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

    private void RebuildManagedSkinMenu()
    {
        if (_skinManagement is null)
        {
            _skinMenu.DropDownItems.Clear();
            foreach (var descriptor in _catalog.Load().Healthy)
            {
                _skinMenu.DropDownItems.Add(CreateSkinMenuItem(
                    descriptor,
                    _tryActivateSkinKey));
            }

            return;
        }

        RebuildSkinMenu(
            _skinMenu,
            _skinManagement.Entries,
            _skinManagement.DesignerAvailable,
            key => _ = _tryActivateSkinKey(key),
            key => _skinManagement.RemoveAsync(key),
            async () =>
            {
                _ = await _skinManagement.ChooseAndImportAsync();
            },
            () => _ = _skinManagement.OpenDesigner());
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
