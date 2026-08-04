using System.Windows;
using System.Windows.Threading;
using CodexQuotaHud.SkinDesigner.UI.Dialogs;
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

internal sealed class WindowsSkinOutputDialogs : ISkinOutputDialogs
{
    private readonly Func<Window?> _owner;
    private readonly IDesignerDialogService _dialogs;
    private readonly Func<Window?, string, string?> _chooseExportPath;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    internal WindowsSkinOutputDialogs(
        Func<Window?> owner,
        IDesignerDialogService dialogs)
        : this(owner, dialogs, ChooseExportPath)
    {
    }

    internal WindowsSkinOutputDialogs(
        Func<Window?> owner,
        IDesignerDialogService dialogs,
        Func<Window?, string, string?> chooseExportPath)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _chooseExportPath = chooseExportPath ??
            throw new ArgumentNullException(nameof(chooseExportPath));
    }

    public string? ChooseExportPath(string suggestedFileName) =>
        InvokeOnDesigner(ownerWindow =>
            _chooseExportPath(
                ownerWindow,
                SkinPackageExchangeDirectory.SuggestedExportPath(
                    suggestedFileName)));

    public bool ConfirmExportReplace(string destinationPath) =>
        _dialogs.Show(
            _owner(),
            new DesignerDialogRequest(
                "Export skin package",
                $"Replace the existing package '{System.IO.Path.GetFileName(destinationPath)}'?",
                DesignerDialogIcon.Warning,
                [
                    new DesignerDialogAction("replace", "Replace"),
                    new DesignerDialogAction(
                        "cancel",
                        "Cancel",
                        IsDefault: true,
                        IsCancel: true)
                ])) == "replace";

    public SkinCollisionDecision ChooseApplyCollision(SkinInstallPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return _dialogs.Show(
            _owner(),
            new DesignerDialogRequest(
                "Apply skin",
                "A skin with this ID is already installed.\n\nYes: Replace   No: Keep a copy   Cancel: Stop",
                DesignerDialogIcon.Question,
                [
                    new DesignerDialogAction("replace", "Replace"),
                    new DesignerDialogAction("keep-copy", "Keep a copy"),
                    new DesignerDialogAction(
                        "cancel",
                        "Stop",
                        IsDefault: true,
                        IsCancel: true)
                ])) switch
        {
            "replace" => SkinCollisionDecision.Replace,
            "keep-copy" => SkinCollisionDecision.KeepCopy,
            _ => SkinCollisionDecision.Cancel
        };
    }

    public void ShowResult(DesignerOutputResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var isWarning = result.Errors.Count > 0 || result.Disposition is
            DesignerOutputDisposition.Failed or
            DesignerOutputDisposition.InstalledNotActivated;
        _ = _dialogs.Show(
            _owner(),
            new DesignerDialogRequest(
                "Skin Designer",
                result.Message ?? (isWarning
                    ? "The output operation did not complete cleanly."
                    : "The output operation completed."),
                isWarning
                    ? DesignerDialogIcon.Warning
                    : DesignerDialogIcon.Information,
                [new DesignerDialogAction(
                    "ok",
                    "OK",
                    IsDefault: true,
                    IsCancel: true)]));
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

    private static string? ChooseExportPath(
        Window? owner,
        string suggestedFileName)
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
    }
}
