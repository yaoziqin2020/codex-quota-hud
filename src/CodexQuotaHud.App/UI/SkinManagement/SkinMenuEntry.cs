namespace CodexQuotaHud.App.UI.SkinManagement;

public sealed record SkinMenuEntry(
    string SelectionKey,
    string DisplayName,
    bool IsSelected,
    bool CanRemove);
