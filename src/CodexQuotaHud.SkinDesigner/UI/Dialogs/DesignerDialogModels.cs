namespace CodexQuotaHud.SkinDesigner.UI.Dialogs;

public enum DesignerDialogIcon
{
    Information,
    Warning,
    Error,
    Question
}

public sealed record DesignerDialogAction(
    string Id,
    string Label,
    bool IsDefault = false,
    bool IsCancel = false);

public sealed record DesignerDialogRequest(
    string Title,
    string Message,
    DesignerDialogIcon Icon,
    IReadOnlyList<DesignerDialogAction> Actions);
