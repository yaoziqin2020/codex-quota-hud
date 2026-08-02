using System.Text.Json;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private const string InstalledCustomKey =
        "custom:11111111-1111-1111-1111-111111111111";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "CodexQuotaHud.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(SkinId.HudDial, "builtin:HudDial")]
    [InlineData(SkinId.EnergyRing, "builtin:EnergyRing")]
    [InlineData(SkinId.LiquidGlass, "builtin:LiquidGlass")]
    [InlineData(SkinId.Aurora, "builtin:Aurora")]
    [InlineData(SkinId.LiquidTank, "builtin:LiquidTank")]
    public void BuiltInSelectionKeys_RoundTripStableEnumIds(
        SkinId skin,
        string expected)
    {
        Assert.Equal(expected, SkinSelectionKey.FromBuiltIn(skin));
        Assert.True(SkinSelectionKey.TryGetBuiltIn(expected, out var parsed));
        Assert.Equal(skin, parsed);
    }

    [Theory]
    [InlineData("custom:11111111-1111-1111-1111-111111111111", true)]
    [InlineData("custom:11111111111111111111111111111111", false)]
    [InlineData("custom:11111111-1111-1111-1111-11111111111A", false)]
    [InlineData("CUSTOM:11111111-1111-1111-1111-111111111111", false)]
    [InlineData("builtin:NotReal", false)]
    [InlineData("Builtin:HudDial", false)]
    public void SelectionKeySyntax_RequiresExactNamespaceAndCanonicalId(
        string value,
        bool expected) =>
        Assert.Equal(expected, SkinSelectionKey.IsSyntacticallyValid(value));

    [Fact]
    public void CustomSelectionKey_ParsesOnlyCanonicalLowercaseGuidD()
    {
        Assert.True(SkinSelectionKey.TryGetCustomId(InstalledCustomKey, out var id));
        Assert.Equal(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            id);
        Assert.False(SkinSelectionKey.TryGetCustomId(
            "custom:11111111-1111-1111-1111-11111111111A",
            out _));
    }

    [Fact]
    public void DefaultPath_UsesCurrentUsersLocalApplicationData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaHud",
            "settings.json");

        Assert.Equal(expected, new SettingsStore().SettingsPath);
    }

    [Fact]
    public void Load_WhenFileIsMissing_ReturnsDefaults()
    {
        var store = CreateStore();

        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void LoadWithMigration_WhenFileIsMissing_ReturnsHudDialWithoutWriteBack()
    {
        var result = CreateStore().LoadWithMigration();

        Assert.Equal(SkinSelectionKey.HudDial, result.Settings.SelectedSkinKey);
        Assert.False(result.RequiresWriteBack);
        Assert.Null(result.SelectionErrorCode);
    }

    [Fact]
    public void Load_WhenJsonIsCorrupt_ReturnsDefaults()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.SettingsPath, "{ definitely-not-json");

        Assert.Equal(new AppSettings(), store.Load());
    }

    public static TheoryData<string, string, bool, string?> MigrationCases => new()
    {
        {
            """
            {
              "Left": 125.5,
              "Top": -48.25,
              "AnimationsEnabled": false,
              "SelectedSkinKey": "custom:11111111-1111-1111-1111-111111111111",
              "SelectedSkin": "LiquidTank",
              "LastSuccessfulRefresh": "2026-07-29T01:02:03.4567890+09:00"
            }
            """,
            InstalledCustomKey,
            false,
            null
        },
        {
            """
            {
              "Left": 125.5,
              "Top": -48.25,
              "AnimationsEnabled": false,
              "SelectedSkinKey": "custom:22222222-2222-2222-2222-222222222222",
              "SelectedSkin": "LiquidTank",
              "LastSuccessfulRefresh": "2026-07-29T01:02:03.4567890+09:00"
            }
            """,
            "builtin:LiquidTank",
            true,
            "skin.selection.invalid"
        },
        {
            """
            {
              "Left": 125.5,
              "Top": -48.25,
              "AnimationsEnabled": false,
              "SelectedSkin": "Aurora",
              "LastSuccessfulRefresh": "2026-07-29T01:02:03.4567890+09:00"
            }
            """,
            "builtin:Aurora",
            true,
            null
        },
        {
            """
            {
              "Left": 125.5,
              "Top": -48.25,
              "AnimationsEnabled": false,
              "SelectedSkin": 1,
              "LastSuccessfulRefresh": "2026-07-29T01:02:03.4567890+09:00"
            }
            """,
            "builtin:EnergyRing",
            true,
            null
        },
        {
            """
            {
              "Left": 125.5,
              "Top": -48.25,
              "AnimationsEnabled": false,
              "SelectedSkinKey": "builtin:not-real",
              "SelectedSkin": "NotARealSkin",
              "LastSuccessfulRefresh": "2026-07-29T01:02:03.4567890+09:00"
            }
            """,
            "builtin:HudDial",
            true,
            "skin.selection.invalid"
        },
        {
            """
            {
              "Left": 125.5,
              "Top": -48.25,
              "AnimationsEnabled": false,
              "LastSuccessfulRefresh": "2026-07-29T01:02:03.4567890+09:00"
            }
            """,
            "builtin:HudDial",
            true,
            null
        }
    };

    [Theory]
    [MemberData(nameof(MigrationCases))]
    public void LoadWithMigration_UsesSelectionPrecedenceAndPreservesOtherSettings(
        string json,
        string expectedSelectionKey,
        bool expectedWriteBack,
        string? expectedErrorCode)
    {
        var store = CreateStore(key => key == InstalledCustomKey);
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.SettingsPath, json);

        var result = store.LoadWithMigration();

        Assert.Equal(expectedSelectionKey, result.Settings.SelectedSkinKey);
        Assert.Equal(expectedWriteBack, result.RequiresWriteBack);
        Assert.Equal(expectedErrorCode, result.SelectionErrorCode);
        Assert.Equal(125.5, result.Settings.Left);
        Assert.Equal(-48.25, result.Settings.Top);
        Assert.False(result.Settings.AnimationsEnabled);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-29T01:02:03.4567890+09:00"),
            result.Settings.LastSuccessfulRefresh);
    }

    [Fact]
    public void Save_WritesOnlyTheApprovedSettingsFieldsAndLeavesNoTempFile()
    {
        var store = CreateStore();
        var settings = new AppSettings(
            Left: 12.25,
            Top: 78.5,
            AnimationsEnabled: false,
            SelectedSkinKey: SkinSelectionKey.LiquidTank,
            LastSuccessfulRefresh: DateTimeOffset.Parse("2026-07-29T03:04:05+00:00"));

        store.Save(settings);

        using var document = JsonDocument.Parse(File.ReadAllText(store.SettingsPath));
        var names = document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            [
                "AnimationsEnabled",
                "LastSuccessfulRefresh",
                "Left",
                "SelectedSkinKey",
                "Top"
            ],
            names);
        Assert.Equal(12.25, document.RootElement.GetProperty("Left").GetDouble());
        Assert.Equal(78.5, document.RootElement.GetProperty("Top").GetDouble());
        Assert.False(document.RootElement.GetProperty("AnimationsEnabled").GetBoolean());
        Assert.Equal(
            "builtin:LiquidTank",
            document.RootElement.GetProperty("SelectedSkinKey").GetString());
        Assert.False(document.RootElement.TryGetProperty("SelectedSkin", out _));
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-29T03:04:05+00:00"),
            document.RootElement.GetProperty("LastSuccessfulRefresh").GetDateTimeOffset());
        Assert.Empty(TemporaryFiles());
        Assert.DoesNotContain(
            document.RootElement.EnumerateObject(),
            property =>
                property.Name.Contains("account", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("quota", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Contains("rateLimit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SaveAndLoad_CustomSelectionKey_RoundTripsExactly()
    {
        var store = CreateStore(key => key == InstalledCustomKey);
        var settings = new AppSettings(SelectedSkinKey: InstalledCustomKey);

        store.Save(settings);

        var result = store.LoadWithMigration();
        Assert.Equal(InstalledCustomKey, result.Settings.SelectedSkinKey);
        Assert.False(result.RequiresWriteBack);
        Assert.Null(result.SelectionErrorCode);
    }

    [Fact]
    public void Save_WhenAtomicMoveFails_DoesNotDamageExistingSettings()
    {
        var baselineLocks = SettingsStore.ActiveSaveLockCount;
        var store = CreateStore();
        var original = new AppSettings(SelectedSkinKey: SkinSelectionKey.Aurora);
        store.Save(original);

        using var lockedTarget = new FileStream(
            store.SettingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var exception = Record.Exception(
            () => store.Save(new AppSettings(SelectedSkinKey: SkinSelectionKey.EnergyRing)));
        Assert.True(
            exception is IOException or UnauthorizedAccessException,
            $"Expected an atomic replacement failure, but received {exception?.GetType().Name ?? "no exception"}.");

        lockedTarget.Dispose();
        Assert.Equal(original, store.Load());
        Assert.Empty(TemporaryFiles());
        Assert.Equal(baselineLocks, SettingsStore.ActiveSaveLockCount);
    }

    [Fact]
    public async Task Save_ConcurrentCallsEachCompleteAndLeaveOneWholeSettingsDocument()
    {
        const int writerCount = 16;
        var baselineLocks = SettingsStore.ActiveSaveLockCount;
        var store = CreateStore();
        using var start = new Barrier(writerCount);
        var candidates = Enumerable.Range(1, writerCount)
            .Select(index => new AppSettings(
                Left: index,
                Top: index * 10,
                AnimationsEnabled: index % 2 == 0,
                SelectedSkinKey: SkinSelectionKey.FromBuiltIn(
                    (SkinId)(index % Enum.GetValues<SkinId>().Length)),
                LastSuccessfulRefresh: DateTimeOffset.Parse("2026-07-29T00:00:00+00:00")
                    .AddMinutes(index)))
            .ToArray();

        var saves = candidates
            .Select(settings => Task.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait();
                    new SettingsStore(store.SettingsPath).Save(settings);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray();

        await Task.WhenAll(saves).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Contains(store.Load(), candidates);
        Assert.Empty(TemporaryFiles());
        Assert.Equal(baselineLocks, SettingsStore.ActiveSaveLockCount);
    }

    [Fact]
    public void Save_ManyShortLivedPaths_DoNotRemainInTheActiveLockPool()
    {
        const int pathCount = 64;
        var baseline = SettingsStore.ActiveSaveLockCount;

        for (var index = 0; index < pathCount; index++)
        {
            var path = Path.Combine(_directory, $"short-lived-{index}", "settings.json");
            new SettingsStore(path).Save(new AppSettings(Left: index));
        }

        Assert.Equal(baseline, SettingsStore.ActiveSaveLockCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private SettingsStore CreateStore(Func<string, bool>? selectionExists = null) =>
        new(Path.Combine(_directory, "settings.json"), selectionExists);

    private string[] TemporaryFiles() =>
        Directory.Exists(_directory)
            ? Directory.GetFiles(_directory, "*.tmp")
            : [];
}
