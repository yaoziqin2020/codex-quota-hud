using System.Windows;
using System.Windows.Threading;

namespace CodexQuotaHud.SkinDesigner.UI.Dialogs;

public sealed class DesignerDialogService : IDesignerDialogService
{
    public string Show(Window? owner, DesignerDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var ownerDispatcher = owner?.Dispatcher;
        if (ownerDispatcher is null || IsUnavailable(ownerDispatcher))
        {
            return ShowOnCurrentDispatcher(owner: null, request);
        }

        if (ownerDispatcher.CheckAccess())
        {
            return ShowOnOwnerDispatcher(owner!, request);
        }

        try
        {
            return ownerDispatcher.Invoke(
                () => ShowOnOwnerDispatcher(owner!, request));
        }
        catch (InvalidOperationException) when (IsUnavailable(ownerDispatcher))
        {
            return ShowOnCurrentDispatcher(owner: null, request);
        }
        catch (TaskCanceledException) when (IsUnavailable(ownerDispatcher))
        {
            return ShowOnCurrentDispatcher(owner: null, request);
        }
    }

    private static string ShowOnOwnerDispatcher(
        Window owner,
        DesignerDialogRequest request) =>
        owner.IsLoaded
            ? ShowOnCurrentDispatcher(owner, request)
            : ShowOnCurrentDispatcher(owner: null, request);

    private static string ShowOnCurrentDispatcher(
        Window? owner,
        DesignerDialogRequest request)
    {
        var dialog = new DesignerDialogWindow(owner, request);
        dialog.ShowDialog();
        return dialog.SelectedActionId
            ?? throw new InvalidOperationException("The dialog closed without an action.");
    }

    private static bool IsUnavailable(Dispatcher dispatcher) =>
        dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished;
}
