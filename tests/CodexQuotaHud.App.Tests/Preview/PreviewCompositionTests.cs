using System.Windows.Threading;
using CodexQuotaHud.App.Preview;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.App.Infrastructure;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.Preview;

[Collection(PreviewWpfCollection.Name)]
public sealed class PreviewCompositionTests
{
    [Fact]
    public void Composition_UsesOnlyBuiltInCatalogAndInMemorySettings() =>
        RunSta(() =>
        {
            using var composition = new PreviewComposition(
                System.Windows.Threading.Dispatcher.CurrentDispatcher,
                () => { });

            Assert.Equal(
                [
                    SkinSelectionKey.HudDial,
                    SkinSelectionKey.EnergyRing,
                    SkinSelectionKey.LiquidGlass,
                    SkinSelectionKey.Aurora,
                    SkinSelectionKey.LiquidTank
                ],
                composition.HudWindow.SkinController.RegisteredKeys);
            Assert.IsType<InMemorySettingsStore>(composition.SettingsStore);
            Assert.Same(composition.Session, composition.Synthetic.Session);
            Assert.Same(composition.HudWindow, composition.Synthetic.HudWindow);
        });

    [Fact]
    public void Composition_StartsDualAndDisposesIdempotently()
    {
        RunSta(() =>
        {
            var exits = 0;
            using var composition = new PreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => exits++);

            Assert.Equal(QuotaDisplayMode.Dual,
                composition.ViewModel.DisplayMode);
            Assert.Equal(68, composition.ViewModel.PrimaryPercent);
            Assert.Equal(34, composition.ViewModel.SecondaryPercent);
            Assert.IsType<InMemorySettingsStore>(composition.SettingsStore);

            composition.Dispose();
            composition.Dispose();
            Assert.Equal(0, exits);
        });
    }

    [Fact]
    public void Composition_ForwardsInstalledHandoffOnce()
    {
        RunSta(() =>
        {
            using var composition = new PreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { },
                new InstalledAppLauncher(
                    @"C:\Present",
                    _ => true,
                    _ => true));
            var requests = 0;
            composition.OpenInstalledRequested += (_, _) => requests++;

            composition.ControlWindow.RequestOpenInstalled();
            composition.ControlWindow.RequestOpenInstalled();

            Assert.Equal(1, requests);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }
}
