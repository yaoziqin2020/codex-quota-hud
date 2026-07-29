using System.Text.Json;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.Core.Tests.Settings;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "CodexQuotaHud.Tests", Guid.NewGuid().ToString("N"));

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
    public void Load_WhenJsonIsCorrupt_ReturnsDefaults()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.SettingsPath, "{ definitely-not-json");

        Assert.Equal(new AppSettings(), store.Load());
    }

    [Fact]
    public void Load_WhenSkinIsInvalid_PreservesOtherSettingsAndUsesHudDial()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            store.SettingsPath,
            """
            {
              "Left": 125.5,
              "Top": 48,
              "AnimationsEnabled": false,
              "SelectedSkin": "NotARealSkin",
              "LastSuccessfulRefresh": "2026-07-29T01:02:03+00:00"
            }
            """);

        var settings = store.Load();

        Assert.Equal(125.5, settings.Left);
        Assert.Equal(48, settings.Top);
        Assert.False(settings.AnimationsEnabled);
        Assert.Equal(SkinId.HudDial, settings.SelectedSkin);
        Assert.Equal(
            DateTimeOffset.Parse("2026-07-29T01:02:03+00:00"),
            settings.LastSuccessfulRefresh);
    }

    [Fact]
    public void Save_WritesOnlyTheApprovedSettingsFieldsAndLeavesNoTempFile()
    {
        var store = CreateStore();
        var settings = new AppSettings(
            Left: 12.25,
            Top: 78.5,
            AnimationsEnabled: false,
            SelectedSkin: SkinId.LiquidTank,
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
                "SelectedSkin",
                "Top"
            ],
            names);
        Assert.Equal(12.25, document.RootElement.GetProperty("Left").GetDouble());
        Assert.Equal(78.5, document.RootElement.GetProperty("Top").GetDouble());
        Assert.False(document.RootElement.GetProperty("AnimationsEnabled").GetBoolean());
        Assert.Equal("LiquidTank", document.RootElement.GetProperty("SelectedSkin").GetString());
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
    public void Save_WhenAtomicMoveFails_DoesNotDamageExistingSettings()
    {
        var store = CreateStore();
        var original = new AppSettings(SelectedSkin: SkinId.Aurora);
        store.Save(original);

        using var lockedTarget = new FileStream(
            store.SettingsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None);

        var exception = Record.Exception(
            () => store.Save(new AppSettings(SelectedSkin: SkinId.EnergyRing)));
        Assert.True(
            exception is IOException or UnauthorizedAccessException,
            $"Expected an atomic replacement failure, but received {exception?.GetType().Name ?? "no exception"}.");

        lockedTarget.Dispose();
        Assert.Equal(original, store.Load());
        Assert.Empty(TemporaryFiles());
    }

    [Fact]
    public async Task Save_ConcurrentCallsEachCompleteAndLeaveOneWholeSettingsDocument()
    {
        const int writerCount = 16;
        var store = CreateStore();
        using var start = new Barrier(writerCount);
        var candidates = Enumerable.Range(1, writerCount)
            .Select(index => new AppSettings(
                Left: index,
                Top: index * 10,
                AnimationsEnabled: index % 2 == 0,
                SelectedSkin: (SkinId)(index % Enum.GetValues<SkinId>().Length),
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
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private SettingsStore CreateStore() =>
        new(Path.Combine(_directory, "settings.json"));

    private string[] TemporaryFiles() =>
        Directory.Exists(_directory)
            ? Directory.GetFiles(_directory, "*.tmp")
            : [];
}
