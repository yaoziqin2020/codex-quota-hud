using System.Windows;
using System.Windows.Threading;
using CodexQuotaHud.Skins.Storage;
using Microsoft.Win32;

namespace CodexQuotaHud.SkinDesigner.Output;

public interface ISkinOutputDialogs
{
    string? ChooseExportPath(string suggestedFileName);

    bool ConfirmExportReplace(string destinationPath);

    SkinCollisionDecision ChooseApplyCollision(SkinInstallPreview preview);

    void ShowResult(DesignerOutputResult result);
}

internal sealed class WindowsSkinOutputDialogs(Func<Window?> owner) :
    ISkinOutputDialogs
{
    private readonly Func<Window?> _owner = owner ??
        throw new ArgumentNullException(nameof(owner));
    private readonly WindowsSkinOutputDialogActions _actions =
        WindowsSkinOutputDialogActions.CreateDefault();
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    internal WindowsSkinOutputDialogs(
        Func<Window?> owner,
        WindowsSkinOutputDialogActions actions)
        : this(owner)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public string? ChooseExportPath(string suggestedFileName) =>
        InvokeOnDesigner(ownerWindow =>
            _actions.ChooseExportPath(ownerWindow, suggestedFileName));

    public bool ConfirmExportReplace(string destinationPath) =>
        InvokeOnDesigner(ownerWindow =>
            _actions.ConfirmExportReplace(ownerWindow, destinationPath));

    public SkinCollisionDecision ChooseApplyCollision(SkinInstallPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return InvokeOnDesigner(ownerWindow =>
            _actions.ChooseApplyCollision(ownerWindow, preview));
    }

    public void ShowResult(DesignerOutputResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var isWarning = result.Errors.Count > 0 || result.Disposition is
            DesignerOutputDisposition.Failed or
            DesignerOutputDisposition.InstalledNotActivated;
        InvokeOnDesigner(ownerWindow =>
        {
            _actions.ShowResult(
                ownerWindow,
                result,
                isWarning ? MessageBoxImage.Warning : MessageBoxImage.Information);
            return true;
        });
    }

    private T InvokeOnDesigner<T>(Func<Window?, T> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return _dispatcher.CheckAccess()
            ? InvokeWithOwner(action)
            : _dispatcher.Invoke(() => InvokeWithOwner(action));
    }

    private T InvokeWithOwner<T>(Func<Window?, T> action)
    {
        var ownerWindow = _owner();
        if (ownerWindow is not null &&
            !ReferenceEquals(ownerWindow.Dispatcher, _dispatcher))
        {
            throw new InvalidOperationException(
                "The output dialog owner belongs to a different Dispatcher.");
        }

        return action(ownerWindow);
    }
}

internal sealed record WindowsSkinOutputDialogActions(
    Func<Window?, string, string?> ChooseExportPath,
    Func<Window?, string, bool> ConfirmExportReplace,
    Func<Window?, SkinInstallPreview, SkinCollisionDecision> ChooseApplyCollision,
    Action<Window?, DesignerOutputResult, MessageBoxImage> ShowResult)
{
    internal static WindowsSkinOutputDialogActions CreateDefault() => new(
        (owner, suggestedFileName) =>
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export skin package",
                Filter = "Codex Quota skin package (*.cqskin)|*.cqskin",
                AddExtension = true,
                DefaultExt = ".cqskin",
                FileName = suggestedFileName,
                OverwritePrompt = false
            };
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        },
        (owner, destinationPath) => MessageBox.Show(
            owner,
            $"Replace the existing package '{System.IO.Path.GetFileName(destinationPath)}'?",
            "Export skin package",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No) == MessageBoxResult.Yes,
        (owner, _) => MessageBox.Show(
            owner,
            "A skin with this ID is already installed.\n\nYes: Replace   No: Keep a copy   Cancel: Stop",
            "Apply skin",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel) switch
        {
            MessageBoxResult.Yes => SkinCollisionDecision.Replace,
            MessageBoxResult.No => SkinCollisionDecision.KeepCopy,
            _ => SkinCollisionDecision.Cancel
        },
        (owner, result, image) => MessageBox.Show(
            owner,
            result.Message ?? (image == MessageBoxImage.Warning
                ? "The output operation did not complete cleanly."
                : "The output operation completed."),
            "Skin Designer",
            MessageBoxButton.OK,
            image));
}
