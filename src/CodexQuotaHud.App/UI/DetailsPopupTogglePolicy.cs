namespace CodexQuotaHud.App.UI;

internal sealed class DetailsPopupTogglePolicy
{
    private bool _suppressNextOpen;

    public void ObserveClosed(
        bool pointerOverOrb,
        bool leftButtonPressed,
        bool programmaticClose)
    {
        if (!programmaticClose &&
            pointerOverOrb &&
            leftButtonPressed)
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
