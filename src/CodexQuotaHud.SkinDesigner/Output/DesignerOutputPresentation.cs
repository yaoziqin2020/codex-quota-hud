using System.IO;
using CodexQuotaHud.SkinDesigner.UI.Dialogs;
using CodexQuotaHud.Skins.Storage;

namespace CodexQuotaHud.SkinDesigner.Output;

internal sealed record DesignerOutputPresentation(
    string Title,
    string Message,
    DesignerDialogIcon Icon)
{
    internal static DesignerOutputPresentation Create(
        DesignerOutputResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var presentation = result.Disposition switch
        {
            DesignerOutputDisposition.AppliedLive => new(
                "已应用到 HUD",
                WithIdentity(
                    "皮肤已安装并应用到正在运行的 HUD。",
                    result.Installed),
                DesignerDialogIcon.Information),
            DesignerOutputDisposition.InstalledAndHudStarted => new(
                "已安装并启动 HUD",
                WithIdentity(
                    "皮肤已安装。HUD 已带着此皮肤的启用请求启动。",
                    result.Installed),
                DesignerDialogIcon.Information),
            DesignerOutputDisposition.InstalledNotActivated => new(
                "皮肤已安装，但未启用",
                WithIdentity(
                    "皮肤已安装，但未能自动启用。请在 HUD 的皮肤菜单中手动选择。",
                    result.Installed),
                DesignerDialogIcon.Warning),
            DesignerOutputDisposition.Exported => Exported(result.ExportPath),
            DesignerOutputDisposition.Cancelled => new(
                "操作已取消",
                "未创建或更改任何输出。",
                DesignerDialogIcon.Information),
            _ => Failed(result)
        };

        if (result.Disposition is not DesignerOutputDisposition.Failed &&
            result.Errors.Count > 0)
        {
            return presentation with
            {
                Message = result.Disposition == DesignerOutputDisposition.Cancelled
                    ? AppendCancelledWarnings(
                        presentation.Message,
                        result.Errors.Select(error => error.Message))
                    : AppendCommittedWarnings(
                        presentation.Message,
                        result.Errors.Select(error => error.Message)),
                Icon = DesignerDialogIcon.Warning
            };
        }

        return presentation;
    }

    private static DesignerOutputPresentation Exported(string? exportPath)
    {
        var fullPath = string.IsNullOrWhiteSpace(exportPath)
            ? string.Empty
            : Path.GetFullPath(exportPath);
        var fileName = Path.GetFileName(fullPath);
        var directory = Path.GetDirectoryName(fullPath);
        return new DesignerOutputPresentation(
            "导出完成",
            $"皮肤包已导出。\n\n文件：{fileName}\n文件夹：{directory}",
            DesignerDialogIcon.Information);
    }

    private static DesignerOutputPresentation Failed(DesignerOutputResult result)
    {
        var details = result.Errors
            .Select(error => error.Message)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (details.Length == 0 && !string.IsNullOrWhiteSpace(result.Message))
        {
            details = [result.Message];
        }

        var message = "未能完成输出操作。请检查皮肤内容或目标位置后重试。";
        if (details.Length > 0)
        {
            message += "\n\n详细信息：\n" + string.Join("\n", details);
        }

        return new DesignerOutputPresentation(
            "操作失败",
            message,
            DesignerDialogIcon.Error);
    }

    private static string WithIdentity(
        string message,
        InstalledSkinRecord? installed) =>
        installed is null
            ? message
            : $"{message}\n\n名称：{installed.DisplayName}" +
                $"\n版本：{installed.PackageVersion}" +
                $"\n皮肤 ID：{installed.SkinId:D}";

    private static string AppendCommittedWarnings(
        string message,
        IEnumerable<string> warnings)
    {
        var details = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var warning = message +
            "\n\n注意：输出已完成，但后续清理或收尾未完全成功。";
        return details.Length == 0
            ? warning
            : warning + "\n详细信息：\n" + string.Join("\n", details);
    }

    private static string AppendCancelledWarnings(
        string message,
        IEnumerable<string> warnings)
    {
        var details = warnings
            .Where(warning => !string.IsNullOrWhiteSpace(warning))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var warning = message +
            "\n\n注意：操作已取消，但临时文件清理未完全成功。";
        return details.Length == 0
            ? warning
            : warning + "\n详细信息：\n" + string.Join("\n", details);
    }
}
