using CodexQuotaHud.App.Preview;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Tests.Preview;

public sealed class InMemorySettingsStoreTests
{
    [Fact]
    public void Save_UpdatesOnlyTheStoreInstance()
    {
        var store = new InMemorySettingsStore(
            new AppSettings(SelectedSkinKey: SkinSelectionKey.HudDial));

        store.Save(store.Load() with
        {
            SelectedSkinKey = SkinSelectionKey.Aurora,
            Left = 420,
            Top = 240
        });

        Assert.Equal(SkinSelectionKey.Aurora, store.Current.SelectedSkinKey);
        Assert.Equal(420, store.Load().Left);
        Assert.Equal(240, store.Load().Top);
    }

    [Fact]
    public void Save_DoesNotShareStateBetweenStores()
    {
        var first = new InMemorySettingsStore(new AppSettings());
        var second = new InMemorySettingsStore(new AppSettings());

        first.Save(first.Load() with
        {
            SelectedSkinKey = SkinSelectionKey.LiquidTank
        });

        Assert.Equal(SkinSelectionKey.LiquidTank, first.Load().SelectedSkinKey);
        Assert.Equal(SkinSelectionKey.HudDial, second.Load().SelectedSkinKey);
    }

    [Fact]
    public void Save_ChangesOnlyInMemoryAndDoesNotTouchFormalSettingsFile()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"CodexQuotaHud-PreviewSettings-{Guid.NewGuid():N}");
        var settingsPath = Path.Combine(directory, "settings.json");
        try
        {
            var formalStore = new SettingsStore(settingsPath);
            formalStore.Save(new AppSettings(
                SelectedSkinKey: SkinSelectionKey.EnergyRing));
            var formalJson = File.ReadAllText(settingsPath);
            var previewStore = new InMemorySettingsStore(new AppSettings());

            previewStore.Save(previewStore.Load() with
            {
                SelectedSkinKey = SkinSelectionKey.LiquidTank
            });

            Assert.Equal(
                SkinSelectionKey.LiquidTank,
                previewStore.Current.SelectedSkinKey);
            Assert.Equal(formalJson, File.ReadAllText(settingsPath));
            Assert.Equal(
                SkinSelectionKey.EnergyRing,
                formalStore.Load().SelectedSkinKey);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
