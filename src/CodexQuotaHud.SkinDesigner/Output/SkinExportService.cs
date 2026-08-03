using System.IO;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;

namespace CodexQuotaHud.SkinDesigner.Output;

public sealed class SkinExportService
{
    private readonly DraftPackageBuilder _builder;
    private readonly Func<
        string,
        SkinPackageBuildRequest,
        bool,
        CancellationToken,
        SkinValidationResult<SkinManifest>> _writeFile;

    public SkinExportService(
        DraftPackageBuilder builder,
        SkinPackageWriter writer)
        : this(builder, writer.WriteFile)
    {
    }

    internal SkinExportService(
        DraftPackageBuilder builder,
        Func<
            string,
            SkinPackageBuildRequest,
            bool,
            CancellationToken,
            SkinValidationResult<SkinManifest>> writeFile)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        _writeFile = writeFile ?? throw new ArgumentNullException(nameof(writeFile));
    }

    public async Task<DesignerOutputResult> ExportAsync(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets,
        string destinationPath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(assets);
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            return Failed(
                "export.destination.invalid",
                "The export destination is invalid.");
        }

        SkinValidationResult<SkinPackageBuildRequest> build;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            build = _builder.Build(draft, assets);
        }
        catch (OperationCanceledException)
        {
            return Cancelled();
        }

        if (!build.IsValid)
        {
            return new DesignerOutputResult(
                DesignerOutputDisposition.Failed,
                null,
                null,
                build.Errors,
                "The draft could not be exported.");
        }

        try
        {
            var fullPath = Path.GetFullPath(destinationPath);
            var result = await Task.Run(
                () => _writeFile(
                    fullPath,
                    build.Value!,
                    overwrite,
                    cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
            return result.IsValid
                ? new DesignerOutputResult(
                    DesignerOutputDisposition.Exported,
                    null,
                    fullPath,
                    [],
                    "Skin package exported.")
                : new DesignerOutputResult(
                    DesignerOutputDisposition.Failed,
                    null,
                    null,
                    result.Errors,
                    "The skin package was not exported.");
        }
        catch (OperationCanceledException)
        {
            return Cancelled();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or InvalidDataException)
        {
            return Failed(
                "export.failed",
                "The skin package could not be exported safely.");
        }
    }

    private static DesignerOutputResult Cancelled() =>
        new(
            DesignerOutputDisposition.Cancelled,
            null,
            null,
            [],
            "Export cancelled.");

    private static DesignerOutputResult Failed(string code, string message) =>
        new(
            DesignerOutputDisposition.Failed,
            null,
            null,
            [new SkinValidationError(code, "$destination", message)],
            message);
}
