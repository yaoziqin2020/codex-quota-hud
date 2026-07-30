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
            new AppSettings(SelectedSkin: SkinId.HudDial));

        store.Save(store.Load() with
        {
            SelectedSkin = SkinId.Aurora,
            Left = 420,
            Top = 240
        });

        Assert.Equal(SkinId.Aurora, store.Current.SelectedSkin);
        Assert.Equal(420, store.Load().Left);
        Assert.Equal(240, store.Load().Top);
    }

    [Fact]
    public void Save_DoesNotShareStateBetweenStores()
    {
        var first = new InMemorySettingsStore(new AppSettings());
        var second = new InMemorySettingsStore(new AppSettings());

        first.Save(first.Load() with { SelectedSkin = SkinId.LiquidTank });

        Assert.Equal(SkinId.LiquidTank, first.Load().SelectedSkin);
        Assert.Equal(SkinId.HudDial, second.Load().SelectedSkin);
    }
}
