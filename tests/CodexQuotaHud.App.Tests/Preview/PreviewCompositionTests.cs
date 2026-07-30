using System.Windows.Threading;
using CodexQuotaHud.App.Preview;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.Preview;

public sealed class PreviewCompositionTests
{
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
