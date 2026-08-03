using CodexQuotaHud.App.UI.About;

namespace CodexQuotaHud.App.Tests.UI;

[Collection(WpfUiCollection.Name)]
public sealed class AboutWindowTests
{
    [Fact]
    public void Constructor_LoadsAppIconWhenHostedOutsideAppExecutable()
    {
        RunSta(() =>
        {
            var window = new AboutWindow(AboutInformation.Current);
            try
            {
                Assert.NotNull(window.Icon);
                Assert.Equal("Codex Quota HUD", window.ProductNameText.Text);
                Assert.Equal("版本 1.0.0", window.VersionValueText.Text);
            }
            finally
            {
                window.Close();
            }
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
