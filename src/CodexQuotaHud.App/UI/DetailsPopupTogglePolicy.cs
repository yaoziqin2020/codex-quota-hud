namespace CodexQuotaHud.App.UI;

internal sealed class DetailsPopupTogglePolicy
{
    private bool _suppressNextOpen;

    public void ObserveClosed(
        bool pointerOverOrb,
        bool programmaticClose)
    {
        if (!programmaticClose &&
            pointerOverOrb)
        {
            _suppressNextOpen = true;
        }
    }

    public bool ConsumeSuppressedOpen()
    {
        var suppress = _suppressNextOpen;
        _suppressNextOpen = false;
        return suppress;
    }
}
