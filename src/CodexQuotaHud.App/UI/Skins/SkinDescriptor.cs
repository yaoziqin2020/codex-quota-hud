using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.App.UI.Skins;

public sealed record SkinDescriptor(
    string SelectionKey,
    string DisplayName,
    bool IsBuiltIn,
    SkinId? BuiltInId,
    InstalledSkinRecord? Installed)
{
    public bool CanRemove => !IsBuiltIn;
}
