using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.App.UI.SkinManagement;

public sealed class SkinManagementController
{
    private readonly SkinPackageInstaller _installer;
    private readonly HudSkinCatalog _catalog;
    private readonly QuotaOrbViewModel _viewModel;
    private readonly SkinController _skinController;
    private readonly DesignerLauncher _designerLauncher;
    private readonly ISkinManagementDialogs _dialogs;
    private readonly SemanticVersion _hudVersion;
    private readonly IUiDispatcher _dispatcher;
    private IReadOnlyList<SkinMenuEntry> _entries;

    public SkinManagementController(
        SkinPackageInstaller installer,
        HudSkinCatalog catalog,
        QuotaOrbViewModel viewModel,
        SkinController skinController,
        DesignerLauncher designerLauncher,
        ISkinManagementDialogs dialogs,
        SemanticVersion hudVersion,
        IUiDispatcher dispatcher)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        _skinController = skinController ?? throw new ArgumentNullException(
            nameof(skinController));
        _designerLauncher = designerLauncher ?? throw new ArgumentNullException(
            nameof(designerLauncher));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _hudVersion = hudVersion;
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _entries = BuildEntries(_catalog.Load());
    }

    public event EventHandler? CatalogChanged;

    public IReadOnlyList<SkinMenuEntry> Entries => Array.AsReadOnly(_entries
        .Select(entry => entry with
        {
            IsSelected = string.Equals(
                entry.SelectionKey,
                _viewModel.SelectedSkinKey,
                StringComparison.Ordinal)
        })
        .ToArray());

    public bool DesignerAvailable => _designerLauncher.IsAvailable;

    internal async Task<SkinImportResult?> ChooseAndImportAsync(
        CancellationToken cancellationToken = default)
    {
        var packagePath = _dialogs.ChoosePackagePath();
        if (packagePath is null)
        {
            return null;
        }

        return await ImportAsync(packagePath, cancellationToken);
    }

    public Task<SkinImportResult> ImportAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        cancellationToken.ThrowIfCancellationRequested();

        var inspected = _installer.Inspect(
            packagePath,
            _hudVersion,
            cancellationToken);
        if (!inspected.IsValid)
        {
            ShowErrors(inspected.Errors);
            return Task.FromResult(new SkinImportResult(
                Succeeded: false,
                Cancelled: false,
                Installed: null,
                inspected.Errors));
        }

        var preview = inspected.Value!;
        var decision = _dialogs.ShowImportPreview(preview);
        if (decision == SkinCollisionDecision.Cancel)
        {
            return Task.FromResult(new SkinImportResult(
                Succeeded: false,
                Cancelled: true,
                Installed: null,
                Errors: []));
        }

        if (preview.IsDowngrade)
        {
            var errors = new[]
            {
                new SkinValidationError(
                    "install.downgrade",
                    "$.packageVersion",
                    "The installed skin is newer than this package.")
            };
            ShowErrors(errors);
            return Task.FromResult(new SkinImportResult(
                Succeeded: false,
                Cancelled: false,
                Installed: null,
                Errors: errors));
        }

        var installed = _installer.Install(preview, decision, cancellationToken);
        if (installed.Installed is null)
        {
            if (installed.Errors.Count > 0)
            {
                ShowErrors(installed.Errors);
            }

            return Task.FromResult(new SkinImportResult(
                Succeeded: false,
                Cancelled: installed.Disposition == SkinInstallDisposition.Cancelled &&
                    installed.Errors.Count == 0,
                Installed: null,
                installed.Errors));
        }

        if (installed.Errors.Count > 0)
        {
            ShowErrors(installed.Errors);
        }

        _ = SynchronizeCatalog(_catalog.Refresh());
        return Task.FromResult(new SkinImportResult(
            Succeeded: true,
            Cancelled: false,
            installed.Installed,
            installed.Errors));
    }

    public Task<bool> RemoveAsync(
        string selectionKey,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var entry = _entries.FirstOrDefault(candidate => string.Equals(
            candidate.SelectionKey,
            selectionKey,
            StringComparison.Ordinal));
        if (entry is null || !entry.CanRemove)
        {
            if (SkinSelectionKey.TryGetBuiltIn(selectionKey, out _))
            {
                return Task.FromResult(false);
            }

            ShowErrors(
            [
                new SkinValidationError(
                    "remove.unknown",
                    "$selectionKey",
                    "The selection is not a healthy installed custom skin.")
            ]);
            return Task.FromResult(false);
        }

        if (!SkinSelectionKey.TryGetCustomId(selectionKey, out var skinId))
        {
            ShowErrors(
            [
                new SkinValidationError(
                    "remove.unknown",
                    "$selectionKey",
                    "The selection is not a healthy installed custom skin.")
            ]);
            return Task.FromResult(false);
        }

        if (!_dialogs.ConfirmRemoval(entry))
        {
            return Task.FromResult(false);
        }

        var isCurrent = string.Equals(
                selectionKey,
                _viewModel.SelectedSkinKey,
                StringComparison.Ordinal) ||
            string.Equals(
                selectionKey,
                _skinController.CurrentDescriptor.SelectionKey,
                StringComparison.Ordinal);
        if (isCurrent && !TryFallbackToHudDial())
        {
            return Task.FromResult(false);
        }

        if (isCurrent)
        {
            _entries = BuildEntries(_catalog.Load());
        }

        var removed = _installer.Remove(skinId);
        if (removed.Errors.Count > 0)
        {
            ShowErrors(removed.Errors);
        }

        var removalCommitted = removed.Value == skinId;
        if (!removalCommitted)
        {
            return Task.FromResult(false);
        }

        _ = SynchronizeCatalog(_catalog.Refresh());
        return Task.FromResult(true);
    }

    internal bool SynchronizeCatalog(HudSkinCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var runtimeSynchronized = ReplaceCatalogWithFallback(snapshot);
        _entries = BuildEntries(snapshot);
        RaiseCatalogChanged();
        return runtimeSynchronized;
    }

    public bool OpenDesigner()
    {
        if (_designerLauncher.TryLaunch(out var error))
        {
            return true;
        }

        ShowErrors(
        [
            new SkinValidationError(
                "designer.launch",
                "$designer.executable",
                error ?? "Skin Designer could not be started.")
        ]);
        return false;
    }

    private IReadOnlyList<SkinMenuEntry> BuildEntries(
        HudSkinCatalogSnapshot snapshot) =>
        Array.AsReadOnly(snapshot.Healthy
            .Select(descriptor => new SkinMenuEntry(
                descriptor.SelectionKey,
                descriptor.DisplayName,
                string.Equals(
                    descriptor.SelectionKey,
                    _viewModel.SelectedSkinKey,
                    StringComparison.Ordinal),
                descriptor.CanRemove))
            .ToArray());

    private void ShowErrors(IReadOnlyList<SkinValidationError> errors) =>
        _dialogs.ShowError(string.Join(
            Environment.NewLine,
            errors.Select(error =>
                $"{error.Code} ({error.Location}): {error.Message}")));

    private bool ReplaceCatalogWithFallback(HudSkinCatalogSnapshot snapshot)
    {
        if (_skinController.ReplaceCatalog(snapshot, out _))
        {
            return true;
        }

        return TryFallbackToHudDial();
    }

    private bool TryFallbackToHudDial()
    {
        if (!_skinController.TryPrepare(
                SkinSelectionKey.HudDial,
                out var safe,
                out var prepareFailure))
        {
            ShowErrors(
            [
                new SkinValidationError(
                    "remove.fallback-prepare",
                    "$selectionKey",
                    $"HUD 科技仪表 could not be prepared ({prepareFailure?.ErrorCode}).")
            ]);
            return false;
        }

        if (!_viewModel.TrySelectSkinKey(SkinSelectionKey.HudDial))
        {
            ShowErrors(
            [
                new SkinValidationError(
                    "remove.fallback-save",
                    "$settings.selectedSkinKey",
                    "HUD 科技仪表 could not be saved before removal.")
            ]);
            return false;
        }

        _skinController.Activate(safe!);
        return true;
    }

    private void RaiseCatalogChanged()
    {
        void Raise() => CatalogChanged?.Invoke(this, EventArgs.Empty);
        if (_dispatcher.CheckAccess())
        {
            Raise();
        }
        else
        {
            _dispatcher.Post(Raise);
        }
    }
}
