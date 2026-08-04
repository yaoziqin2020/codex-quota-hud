using System.Windows;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.UI.Dialogs;

namespace CodexQuotaHud.SkinDesigner.Documents;

public enum UnsavedCloseChoice
{
    Save,
    Discard,
    Cancel
}

public interface IUnsavedChangesDialog
{
    UnsavedCloseChoice Show(SkinDraftDocument draft);
}

public sealed class WindowsUnsavedChangesDialog : IUnsavedChangesDialog
{
    private readonly IDesignerDialogService _dialogs;
    private readonly Func<Window?> _owner;

    public WindowsUnsavedChangesDialog()
        : this(new DesignerDialogService(), () => null)
    {
    }

    public WindowsUnsavedChangesDialog(
        IDesignerDialogService dialogs,
        Func<Window?> owner)
    {
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public UnsavedCloseChoice Show(SkinDraftDocument draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var result = _dialogs.Show(
            _owner(),
            new DesignerDialogRequest(
                "Unsaved skin draft",
                $"Save changes to '{draft.ProjectName}'?\n\nYes: Save   No: Discard   Cancel: Keep editing",
                DesignerDialogIcon.Warning,
                [
                    new DesignerDialogAction("save", "Save"),
                    new DesignerDialogAction("discard", "Discard"),
                    new DesignerDialogAction(
                        "cancel",
                        "Keep editing",
                        IsDefault: true,
                        IsCancel: true)
                ]));
        return result switch
        {
            "save" => UnsavedCloseChoice.Save,
            "discard" => UnsavedCloseChoice.Discard,
            _ => UnsavedCloseChoice.Cancel
        };
    }
}
