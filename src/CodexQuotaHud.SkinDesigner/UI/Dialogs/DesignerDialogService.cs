using System.Windows;

namespace CodexQuotaHud.SkinDesigner.UI.Dialogs;

public sealed class DesignerDialogService : IDesignerDialogService
{
    public string Show(Window? owner, DesignerDialogRequest request)
    {
        var dialog = new DesignerDialogWindow(owner, request);
        dialog.ShowDialog();
        return dialog.SelectedActionId
            ?? throw new InvalidOperationException("The dialog closed without an action.");
    }
}
