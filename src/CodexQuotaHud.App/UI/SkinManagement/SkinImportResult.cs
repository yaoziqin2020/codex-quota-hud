using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.App.UI.SkinManagement;

public sealed record SkinImportResult(
    bool Succeeded,
    bool Cancelled,
    InstalledSkinRecord? Installed,
    IReadOnlyList<SkinValidationError> Errors);
