using System.Windows;
using CodexQuotaHud.SkinDesigner.Drafts;

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
    public UnsavedCloseChoice Show(SkinDraftDocument draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var result = MessageBox.Show(
            $"Save changes to '{draft.ProjectName}'?\n\nYes: Save   No: Discard   Cancel: Keep editing",
            "Unsaved skin draft",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        return result switch
        {
            MessageBoxResult.Yes => UnsavedCloseChoice.Save,
            MessageBoxResult.No => UnsavedCloseChoice.Discard,
            _ => UnsavedCloseChoice.Cancel
        };
    }
}
