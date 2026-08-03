using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Output;

public enum DesignerOutputDisposition
{
    AppliedLive,
    InstalledAndHudStarted,
    InstalledNotActivated,
    Exported,
    Cancelled,
    Failed
}

public sealed record DesignerOutputResult(
    DesignerOutputDisposition Disposition,
    InstalledSkinRecord? Installed,
    string? ExportPath,
    IReadOnlyList<SkinValidationError> Errors,
    string? Message);

public sealed record DesignerOutputServices(
    SkinApplyService Apply,
    SkinExportService Export,
    ISkinOutputDialogs Dialogs);
