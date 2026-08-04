using System.Windows;
using CodexQuotaHud.Skins.Storage;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WpfMessageBox = System.Windows.MessageBox;

namespace CodexQuotaHud.App.UI.SkinManagement;

public interface ISkinManagementDialogs
{
    string? ChoosePackagePath();

    SkinCollisionDecision ShowImportPreview(SkinInstallPreview preview);

    bool ConfirmRemoval(SkinMenuEntry entry);

    void ShowError(string message);
}

internal sealed class SkinManagementDialogs(Func<Window?> owner) :
    ISkinManagementDialogs
{
    private readonly Func<Window?> _owner = owner ?? throw new ArgumentNullException(
        nameof(owner));

    public string? ChoosePackagePath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入皮肤",
            Filter = "Codex Quota HUD 皮肤 (*.cqskin)|*.cqskin",
            InitialDirectory = SkinPackageExchangeDirectory.EnsureExists(),
            CheckFileExists = true,
            Multiselect = false
        };
        var window = _owner();
        var accepted = window is null
            ? dialog.ShowDialog()
            : dialog.ShowDialog(window);
        return accepted == true ? dialog.FileName : null;
    }

    public SkinCollisionDecision ShowImportPreview(SkinInstallPreview preview)
    {
        var dialog = new SkinImportPreviewWindow(preview);
        var window = _owner();
        if (window is not null)
        {
            dialog.Owner = window;
        }

        _ = dialog.ShowDialog();
        return dialog.Decision;
    }

    public bool ConfirmRemoval(SkinMenuEntry entry)
    {
        var message = $"确定删除自定义皮肤“{entry.DisplayName}”吗？";
        var window = _owner();
        var result = window is null
            ? WpfMessageBox.Show(
                message,
                "Codex Quota HUD",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning)
            : WpfMessageBox.Show(
                window,
                message,
                "Codex Quota HUD",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
        return result == MessageBoxResult.Yes;
    }

    public void ShowError(string message)
    {
        var window = _owner();
        if (window is null)
        {
            _ = WpfMessageBox.Show(
                message,
                "Codex Quota HUD",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        _ = WpfMessageBox.Show(
            window,
            message,
            "Codex Quota HUD",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
