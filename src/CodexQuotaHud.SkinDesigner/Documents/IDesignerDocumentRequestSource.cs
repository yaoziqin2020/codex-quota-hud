using System.IO;
using System.Windows;
using CodexQuotaHud.Skins.Storage;
using Microsoft.Win32;

namespace CodexQuotaHud.SkinDesigner.Documents;

internal interface IDesignerDocumentRequestSource
{
    Guid? SelectDraftId(Window owner);

    string? SelectInstalledSelectionKey(Window owner);

    string? SelectPackagePath(Window owner);
}

internal sealed class WindowsDesignerDocumentRequestSource(SkinStoragePaths paths) :
    IDesignerDocumentRequestSource
{
    public Guid? SelectDraftId(Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 Designer 草稿",
            Filter = "Designer draft (draft.json;recovery.json)|draft.json;recovery.json",
            InitialDirectory = paths.DraftsRoot,
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(owner) == true
            ? ProjectIdFromDocument(dialog.FileName)
            : null;
    }

    public string? SelectInstalledSelectionKey(Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择已安装自定义皮肤",
            Filter = "Installed skin manifest (manifest.json)|manifest.json",
            InitialDirectory = paths.InstalledSkinsRoot,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(owner) != true)
        {
            return null;
        }

        var id = ProjectIdFromDocument(dialog.FileName);
        return id is null ? null : $"custom:{id:D}";
    }

    public string? SelectPackagePath(Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入皮肤包以编辑",
            Filter = "Codex Quota skin package (*.cqskin)|*.cqskin",
            CheckFileExists = true,
            Multiselect = false
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private static Guid? ProjectIdFromDocument(string fileName)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(fileName));
        var leaf = directory is null ? null : Path.GetFileName(directory);
        return Guid.TryParseExact(leaf, "D", out var id) && id != Guid.Empty
            ? id
            : null;
    }
}
