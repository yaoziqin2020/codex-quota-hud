namespace CodexQuotaHud.App.UI.About;

internal interface IAboutWindow
{
    event EventHandler? Closed;

    void Show();

    bool Activate();

    void Close();
}
