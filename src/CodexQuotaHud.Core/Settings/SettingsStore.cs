using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.Settings;

public interface ISettingsStore
{
    AppSettings Load();

    void Save(AppSettings settings);

    void Save(AppSettings settings, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(settings);
        cancellationToken.ThrowIfCancellationRequested();
    }
}

public sealed record SettingsLoadResult(
    AppSettings Settings,
    bool RequiresWriteBack,
    string? SelectionErrorCode);

public sealed class SettingsStore : ISettingsStore
{
    private static readonly object DefaultPathSync = new();
    private static readonly ConcurrentDictionary<string, SaveLockEntry> SaveLocks =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _saveLockKey;
    private readonly Func<string, bool> _selectionExists;
    private readonly Action? _beforeAtomicCommit;
    private static string? _defaultPathOverrideForTests;
    private static int _defaultConstructorCountForTests;
    private static int _defaultPathOverrideOwnerThreadId;

    internal static int ActiveSaveLockCount => SaveLocks.Count;

    public SettingsStore()
        : this(ResolveDefaultSettingsPath())
    {
    }

    public SettingsStore(
        string settingsPath,
        Func<string, bool>? selectionExists = null)
        : this(settingsPath, selectionExists, beforeAtomicCommit: null)
    {
    }

    internal SettingsStore(
        string settingsPath,
        Func<string, bool>? selectionExists,
        Action? beforeAtomicCommit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        SettingsPath = settingsPath;
        _saveLockKey = Path.GetFullPath(settingsPath);
        _selectionExists = selectionExists ?? DefaultSelectionExists;
        _beforeAtomicCommit = beforeAtomicCommit;
    }

    public string SettingsPath { get; }

    internal static DefaultPathOverrideScope OverrideDefaultPathForTests(
        string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        var fullPath = Path.GetFullPath(settingsPath);
        Monitor.Enter(DefaultPathSync);
        if (_defaultPathOverrideForTests is not null)
        {
            Monitor.Exit(DefaultPathSync);
            throw new InvalidOperationException(
                "A default settings path override is already active.");
        }

        _defaultPathOverrideForTests = fullPath;
        _defaultConstructorCountForTests = 0;
        _defaultPathOverrideOwnerThreadId = Environment.CurrentManagedThreadId;
        return new DefaultPathOverrideScope(
            _defaultPathOverrideOwnerThreadId);
    }

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

    public void Save(AppSettings settings) =>
        Save(settings, CancellationToken.None);

    public void Save(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (AcquireSaveLock(_saveLockKey))
        {
            cancellationToken.ThrowIfCancellationRequested();
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

                _beforeAtomicCommit?.Invoke();
                CommitUnlessCancelled(
                    cancellationToken,
                    () => File.Move(
                        temporaryPath,
                        SettingsPath,
                        overwrite: true));
            }
            catch
            {
                TryDelete(temporaryPath);
                throw;
            }
        }
    }

    private static void CommitUnlessCancelled(
        CancellationToken cancellationToken,
        Action commit)
    {
        var state = new CommitGateState(cancellationToken.IsCancellationRequested);
        using var registration = cancellationToken.UnsafeRegister(
            static value =>
            {
                var gate = (CommitGateState)value!;
                lock (gate.Sync)
                {
                    gate.Cancelled = true;
                }
            },
            state);
        lock (state.Sync)
        {
            if (state.Cancelled || cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            commit();
        }
    }

    private static string ResolveDefaultSettingsPath()
    {
        lock (DefaultPathSync)
        {
            if (_defaultPathOverrideForTests is { } overridePath)
            {
                _defaultConstructorCountForTests++;
                return overridePath;
            }
        }

        return Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaHud",
            "settings.json");
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
        if (!root.TryGetProperty("SelectedSkin", out var value))
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

    private sealed class CommitGateState(bool cancelled)
    {
        public object Sync { get; } = new();

        public bool Cancelled { get; set; } = cancelled;
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

    internal sealed class DefaultPathOverrideScope : IDisposable
    {
        private readonly int _ownerThreadId;
        private int _disposed;

        internal DefaultPathOverrideScope(int ownerThreadId)
        {
            _ownerThreadId = ownerThreadId;
        }

        internal int ConstructionCount
        {
            get
            {
                ThrowIfUnavailable();
                return _defaultConstructorCountForTests;
            }
        }

        public void Dispose()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "The default settings path override must be disposed " +
                    "on the thread that created it.");
            }

            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _defaultPathOverrideForTests = null;
            _defaultConstructorCountForTests = 0;
            _defaultPathOverrideOwnerThreadId = 0;
            Monitor.Exit(DefaultPathSync);
        }

        private void ThrowIfUnavailable()
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _disposed) != 0,
                this);
            if (Environment.CurrentManagedThreadId != _ownerThreadId ||
                _defaultPathOverrideOwnerThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "The default settings path override is owned by another " +
                    "thread.");
            }
        }
    }
}
