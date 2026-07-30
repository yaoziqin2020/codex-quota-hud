using System.IO;
using System.Text.Json;

namespace CodexQuotaHud.App.Preview;

internal sealed class PreviewWindowStateStore
{
    public PreviewWindowStateStore()
        : this(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData))
    {
    }

    internal PreviewWindowStateStore(string localAppData)
    {
        StatePath = Path.GetFullPath(Path.Combine(
            localAppData,
            "CodexQuotaHud",
            "preview-window.json"));
    }

    public string StatePath { get; }

    public PreviewWindowState Load()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return PreviewWindowState.Default;
            }

            var state = JsonSerializer.Deserialize<PreviewWindowState>(
                File.ReadAllText(StatePath));
            return state?.IsValid == true
                ? state
                : PreviewWindowState.Default;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            JsonException)
        {
            return PreviewWindowState.Default;
        }
    }

    public void Save(PreviewWindowState state)
    {
        if (state?.IsValid != true)
        {
            return;
        }

        string? temporaryPath = null;
        try
        {
            var directory = Path.GetDirectoryName(StatePath)!;
            Directory.CreateDirectory(directory);
            temporaryPath = Path.Combine(
                directory,
                $".preview-window.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(state));
            File.Move(temporaryPath, StatePath, overwrite: true);
            temporaryPath = null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
