using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.Settings;

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);
}

public sealed record SettingsLoadResult(
    AppSettings Settings,
    bool RequiresWriteBack,
    string? SelectionErrorCode);

public sealed class SettingsStore : ISettingsStore
{
    private static readonly ConcurrentDictionary<string, SaveLockEntry> SaveLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _saveLockKey;
    private readonly Func<string, bool> _selectionExists;

    internal static int ActiveSaveLockCount => SaveLocks.Count;

    public SettingsStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaHud",
            "settings.json"))
    {
    }

    public SettingsStore(
        string settingsPath,
        Func<string, bool>? selectionExists = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        SettingsPath = settingsPath;
        _saveLockKey = Path.GetFullPath(settingsPath);
        _selectionExists = selectionExists ?? DefaultSelectionExists;
    }

    public string SettingsPath { get; }

    public AppSettings Load() => LoadWithMigration().Settings;

    public SettingsLoadResult LoadWithMigration()
    {
        if (!File.Exists(SettingsPath))
        {
            return DefaultLoadResult();
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
                return DefaultLoadResult();
            }

            var root = document.RootElement;
            var selection = ReadSelection(root);
            var settings = new AppSettings(
                Left: ReadNullableDouble(root, nameof(AppSettings.Left)),
                Top: ReadNullableDouble(root, nameof(AppSettings.Top)),
                AnimationsEnabled: ReadBoolean(
                    root,
                    nameof(AppSettings.AnimationsEnabled),
                    defaultValue: true),
                SelectedSkinKey: selection.Key,
                LastSuccessfulRefresh: ReadTimestamp(
                    root,
                    nameof(AppSettings.LastSuccessfulRefresh)));
            return new SettingsLoadResult(
                settings,
                selection.RequiresWriteBack,
                selection.ErrorCode);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return DefaultLoadResult();
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

        using (AcquireSaveLock(_saveLockKey))
        {
            var temporaryPath =
                $"{SettingsPath}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
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

    private SelectionReadResult ReadSelection(JsonElement root)
    {
        var hasSelectedSkinKey = root.TryGetProperty(
            nameof(AppSettings.SelectedSkinKey),
            out var selectedSkinKey);
        if (hasSelectedSkinKey &&
            selectedSkinKey.ValueKind == JsonValueKind.String)
        {
            var key = selectedSkinKey.GetString();
            if (key is not null &&
                SkinSelectionKey.IsSyntacticallyValid(key) &&
                _selectionExists(key))
            {
                return new SelectionReadResult(
                    key,
                    RequiresWriteBack: false,
                    ErrorCode: null);
            }
        }

        var legacyKey = ReadLegacySkin(root) is { } legacySkin
            ? SkinSelectionKey.FromBuiltIn(legacySkin)
            : SkinSelectionKey.HudDial;
        return new SelectionReadResult(
            legacyKey,
            RequiresWriteBack: true,
            ErrorCode: hasSelectedSkinKey ? "skin.selection.invalid" : null);
    }

    private static SkinId? ReadLegacySkin(JsonElement root)
    {
        if (!root.TryGetProperty(nameof(AppSettings.SelectedSkin), out var value))
        {
            return null;
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

        return null;
    }

    private static bool DefaultSelectionExists(string key) =>
        SkinSelectionKey.TryGetBuiltIn(key, out _);

    private static SettingsLoadResult DefaultLoadResult() =>
        new(new AppSettings(), RequiresWriteBack: false, SelectionErrorCode: null);

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

    private static SaveLockLease AcquireSaveLock(string path)
    {
        while (true)
        {
            var entry = SaveLocks.GetOrAdd(path, static _ => new SaveLockEntry());
            lock (entry.LifecycleSync)
            {
                if (entry.IsRetired)
                {
                    continue;
                }

                entry.ReferenceCount++;
            }

            try
            {
                Monitor.Enter(entry.SaveSync);
                return new SaveLockLease(path, entry);
            }
            catch
            {
                ReleaseSaveLockReference(path, entry);
                throw;
            }
        }
    }

    private static void ReleaseSaveLockReference(string path, SaveLockEntry entry)
    {
        lock (entry.LifecycleSync)
        {
            entry.ReferenceCount--;
            if (entry.ReferenceCount != 0)
            {
                return;
            }

            entry.IsRetired = true;
            ((ICollection<KeyValuePair<string, SaveLockEntry>>)SaveLocks).Remove(
                new KeyValuePair<string, SaveLockEntry>(path, entry));
        }
    }

    private sealed class SaveLockEntry
    {
        public object LifecycleSync { get; } = new();

        public object SaveSync { get; } = new();

        public int ReferenceCount { get; set; }

        public bool IsRetired { get; set; }
    }

    private sealed record SelectionReadResult(
        string Key,
        bool RequiresWriteBack,
        string? ErrorCode);

    private sealed class SaveLockLease : IDisposable
    {
        private readonly string _path;
        private SaveLockEntry? _entry;

        public SaveLockLease(string path, SaveLockEntry entry)
        {
            _path = path;
            _entry = entry;
        }

        public void Dispose()
        {
            var entry = Interlocked.Exchange(ref _entry, null);
            if (entry is null)
            {
                return;
            }

            try
            {
                Monitor.Exit(entry.SaveSync);
            }
            finally
            {
                ReleaseSaveLockReference(_path, entry);
            }
        }
    }
}
