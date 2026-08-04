using System.Windows;

namespace CodexQuotaHud.SkinDesigner.UI.Dialogs;

public interface IDesignerDialogService
{
    string Show(Window? owner, DesignerDialogRequest request);
}
