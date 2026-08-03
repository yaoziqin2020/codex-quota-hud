using System.IO;
using CodexQuotaHud.App.Infrastructure.LocalControl;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Output;

public sealed class SkinApplyService
{
    private readonly SkinStoragePaths _paths;
    private readonly SemanticVersion _hudVersion;
    private readonly DraftPackageBuilder _builder;
    private readonly IApplyStagingLeaseProvider _staging;
    private readonly Func<
        Stream,
        SkinPackageBuildRequest,
        CancellationToken,
        SkinManifest> _writePackage;
    private readonly Func<
        Stream,
        long,
        SemanticVersion,
        CancellationToken,
        SkinValidationResult<SkinPackageDocument>> _validatePackage;
    private readonly Func<
        SkinPackageDocument,
        SemanticVersion,
        CancellationToken,
        SkinValidationResult<SkinInstallPreview>> _inspectPackage;
    private readonly Func<
        SkinInstallPreview,
        SkinCollisionDecision,
        CancellationToken,
        SkinInstallResult> _install;
    private readonly Func<string, InstalledSkinRecord?> _reload;
    private readonly Func<
        string,
        CancellationToken,
        Task<HudActivationResult>> _activate;
    private readonly ISkinOutputDialogs _dialogs;
    private readonly Action<string> _observe;

    public SkinApplyService(
        SkinStoragePaths paths,
        SemanticVersion hudVersion,
        DraftPackageBuilder builder,
        SkinPackageWriter writer,
        SkinPackageReader reader,
        SkinPackageInstaller installer,
        InstalledSkinCatalog catalog,
        HudActivationRequester requester,
        ISkinOutputDialogs dialogs)
        : this(
            paths,
            hudVersion,
            builder,
            PhysicalApplyStagingLeaseProvider.Instance,
            writer.Write,
            reader.ValidateStream,
            installer.Inspect,
            installer.Install,
            catalog.TryLoadSelection,
            requester.ActivateAsync,
            dialogs,
            observe: null)
    {
    }

    internal SkinApplyService(
        SkinStoragePaths paths,
        SemanticVersion hudVersion,
        DraftPackageBuilder builder,
        IApplyStagingLeaseProvider staging,
        Func<
            Stream,
            SkinPackageBuildRequest,
            CancellationToken,
            SkinManifest> writePackage,
        Func<
            Stream,
            long,
            SemanticVersion,
            CancellationToken,
            SkinValidationResult<SkinPackageDocument>> validatePackage,
        Func<
            SkinPackageDocument,
            SemanticVersion,
            CancellationToken,
            SkinValidationResult<SkinInstallPreview>> inspectPackage,
        Func<
            SkinInstallPreview,
            SkinCollisionDecision,
            CancellationToken,
            SkinInstallResult> install,
        Func<string, InstalledSkinRecord?> reload,
        Func<string, CancellationToken, Task<HudActivationResult>> activate,
        ISkinOutputDialogs dialogs,
        Action<string>? observe = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _hudVersion = hudVersion;
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _staging = staging ?? throw new ArgumentNullException(nameof(staging));
        _writePackage = writePackage ??
            throw new ArgumentNullException(nameof(writePackage));
        _validatePackage = validatePackage ??
            throw new ArgumentNullException(nameof(validatePackage));
        _inspectPackage = inspectPackage ??
            throw new ArgumentNullException(nameof(inspectPackage));
        _install = install ?? throw new ArgumentNullException(nameof(install));
        _reload = reload ?? throw new ArgumentNullException(nameof(reload));
        _activate = activate ?? throw new ArgumentNullException(nameof(activate));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _observe = observe ?? (_ => { });
    }

    public async Task<DesignerOutputResult> ApplyAsync(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(assets);

        IApplyStagingLease? staging = null;
        InstalledSkinRecord? promoted = null;
        DesignerOutputResult result;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var build = _builder.Build(draft, assets);
            Checkpoint("build", cancellationToken);
            if (!build.IsValid)
            {
                result = Failed(build.Errors, "The draft could not be packaged.");
            }
            else
            {
                staging = _staging.Create(_paths);
                Checkpoint("prewrite", cancellationToken);
                await Task.Run(
                    () =>
                    {
                        _ = _writePackage(
                            staging.PackageStream,
                            build.Value!,
                            cancellationToken);
                        staging.FlushPackageToDisk();
                    },
                    CancellationToken.None).ConfigureAwait(false);
                Checkpoint("postwrite", cancellationToken);
                Checkpoint("prevalidate", cancellationToken);
                var validated = _validatePackage(
                    staging.PackageStream,
                    staging.PackageStream.Length,
                    _hudVersion,
                    cancellationToken);
                Checkpoint("postvalidate", cancellationToken);
                if (!validated.IsValid)
                {
                    result = Failed(
                        validated.Errors,
                        "The staged skin package did not pass validation.");
                }
                else
                {
                    result = await InspectInstallActivateAsync(
                        validated.Value!,
                        cancellationToken,
                        installed => promoted = installed)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            result = promoted is null
                ? Cancelled()
                : NotActivated(
                    promoted,
                    [new SkinValidationError(
                        "apply.cancelled",
                        "$apply",
                        "Activation was cancelled after the skin was installed.")]);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidOperationException)
        {
            result = promoted is null
                ? Failed(
                    [new SkinValidationError(
                        "apply.failed",
                        "$apply",
                        "The skin could not be applied safely.")],
                    "The skin could not be applied safely.")
                : NotActivated(
                    promoted,
                    [new SkinValidationError(
                        "apply.activation-failed",
                        "$activation",
                        "The installed skin could not be activated.")]);
        }

        _observe("report");
        if (staging is not null)
        {
            try
            {
                staging.DeleteOwnedOperation();
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                var operationId = Path.GetFileName(staging.OperationPath);
                result = result with
                {
                    Errors = result.Errors.Concat(
                    [
                        new SkinValidationError(
                            "apply.cleanup-failed",
                            "$operation",
                            "The apply staging operation could not be cleaned up. " +
                            $"Recovery operation: {operationId}.")
                    ]).ToArray(),
                    Message = (result.Message ?? "The output operation completed.") +
                        " Temporary apply files could not be cleaned up; " +
                        $"recovery operation: {operationId}."
                };
            }
            finally
            {
                staging.Dispose();
            }
        }

        _observe("cleanup");
        return result;
    }

    private async Task<DesignerOutputResult> InspectInstallActivateAsync(
        SkinPackageDocument stagedPackage,
        CancellationToken cancellationToken,
        Action<InstalledSkinRecord> recordPromotion)
    {
        Checkpoint("precollision", cancellationToken);
        var inspected = _inspectPackage(
            stagedPackage,
            _hudVersion,
            cancellationToken);
        Checkpoint("postcollision", cancellationToken);
        if (!inspected.IsValid)
        {
            return Failed(inspected.Errors, "The staged package could not be inspected.");
        }

        var preview = inspected.Value!;
        if (preview.IsDowngrade)
        {
            return Failed(
                [new SkinValidationError(
                    "install.downgrade",
                    "$install",
                    "The installed skin is newer than this draft.")],
                "The installed skin is newer than this draft.");
        }

        var decision = preview.Existing is null
            ? SkinCollisionDecision.Replace
            : _dialogs.ChooseApplyCollision(preview);
        Checkpoint("postdecision", cancellationToken);
        if (decision == SkinCollisionDecision.Cancel)
        {
            return Cancelled();
        }

        if (preview.Existing is not null &&
            !preview.AllowedDecisions.Contains(decision))
        {
            return Failed(
                [new SkinValidationError(
                    "install.decision.invalid",
                    "$install",
                    "The collision decision is not allowed.")],
                "The selected collision action is not allowed.");
        }

        Checkpoint("preinstall", cancellationToken);
        var install = _install(preview, decision, cancellationToken);
        if (install.Installed is not null)
        {
            recordPromotion(install.Installed);
        }

        Checkpoint("postinstall", cancellationToken);
        if (install.Installed is null)
        {
            return install.Errors.Count == 0
                ? Cancelled()
                : Failed(install.Errors, "The skin was not installed.");
        }

        Checkpoint("prereload", cancellationToken);
        var reloaded = _reload(install.Installed.SelectionKey);
        Checkpoint("postreload", cancellationToken);
        if (reloaded is null ||
            !string.Equals(
                reloaded.SelectionKey,
                install.Installed.SelectionKey,
                StringComparison.Ordinal))
        {
            return NotActivated(
                install.Installed,
                [new SkinValidationError(
                    "install.reload.failed",
                    "$install",
                    "The installed skin could not be reloaded from the catalog.")]);
        }

        Checkpoint("preactivation", cancellationToken);
        var activation = await _activate(
            reloaded.SelectionKey,
            cancellationToken).ConfigureAwait(false);
        _observe("postactivation");
        return activation.Disposition switch
        {
            HudActivationDisposition.ActivatedLive => new DesignerOutputResult(
                DesignerOutputDisposition.AppliedLive,
                reloaded,
                null,
                install.Errors,
                "Skin installed and applied to the running HUD."),
            HudActivationDisposition.StartedHud => new DesignerOutputResult(
                DesignerOutputDisposition.InstalledAndHudStarted,
                reloaded,
                null,
                install.Errors,
                "Skin installed and the HUD was started."),
            _ => NotActivated(
                reloaded,
                activation.ErrorCode is null
                    ? install.Errors
                    : install.Errors.Concat(
                    [
                        new SkinValidationError(
                            activation.ErrorCode,
                            "$activation",
                            activation.Message ?? "The HUD did not activate the skin.")
                    ]).ToArray())
        };
    }

    private void Checkpoint(
        string name,
        CancellationToken cancellationToken)
    {
        _observe(name);
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static DesignerOutputResult Cancelled() =>
        new(
            DesignerOutputDisposition.Cancelled,
            null,
            null,
            [],
            "Apply cancelled.");

    private static DesignerOutputResult Failed(
        IReadOnlyList<SkinValidationError> errors,
        string message) =>
        new(
            DesignerOutputDisposition.Failed,
            null,
            null,
            errors,
            message);

    private static DesignerOutputResult NotActivated(
        InstalledSkinRecord installed,
        IReadOnlyList<SkinValidationError> errors) =>
        new(
            DesignerOutputDisposition.InstalledNotActivated,
            installed,
            null,
            errors,
            "皮肤已安装，但未能自动激活；可在 HUD 皮肤菜单中手动应用。" );
}
