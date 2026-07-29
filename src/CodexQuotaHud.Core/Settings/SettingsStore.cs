using System.Text.Json;
using System.Text.Json.Serialization;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.Settings;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaHud",
            "settings.json"))
    {
    }

    public SettingsStore(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        SettingsPath = settingsPath;
    }

    public string SettingsPath { get; }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = new FileStream(
                SettingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new AppSettings();
            }

            var root = document.RootElement;
            return new AppSettings(
                Left: ReadNullableDouble(root, nameof(AppSettings.Left)),
                Top: ReadNullableDouble(root, nameof(AppSettings.Top)),
                AnimationsEnabled: ReadBoolean(
                    root,
                    nameof(AppSettings.AnimationsEnabled),
                    defaultValue: true),
                SelectedSkin: ReadSkin(root),
                LastSuccessfulRefresh: ReadTimestamp(
                    root,
                    nameof(AppSettings.LastSuccessfulRefresh)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = SettingsPath + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, SerializerOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private static double? ReadNullableDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var result) &&
               double.IsFinite(result)
            ? result
            : null;
    }

    private static bool ReadBoolean(
        JsonElement root,
        string propertyName,
        bool defaultValue)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return defaultValue;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue
        };
    }

    private static SkinId ReadSkin(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(AppSettings.SelectedSkin), out var value))
        {
            return SkinId.HudDial;
        }

        if (value.ValueKind == JsonValueKind.String &&
            Enum.TryParse<SkinId>(value.GetString(), ignoreCase: false, out var stringSkin) &&
            Enum.IsDefined(stringSkin))
        {
            return stringSkin;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var numericSkin) &&
            Enum.IsDefined(typeof(SkinId), numericSkin))
        {
            return (SkinId)numericSkin;
        }

        return SkinId.HudDial;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String &&
               value.TryGetDateTimeOffset(out var result)
            ? result
            : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
