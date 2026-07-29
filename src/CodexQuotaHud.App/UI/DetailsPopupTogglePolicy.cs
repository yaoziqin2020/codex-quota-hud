namespace CodexQuotaHud.App.UI;

internal sealed class DetailsPopupTogglePolicy(
    Func<DateTimeOffset>? now = null)
{
    private readonly Func<DateTimeOffset> _now =
        now ?? (() => DateTimeOffset.UtcNow);
    private DateTimeOffset _suppressUntil = DateTimeOffset.MinValue;

    public void ObserveClosed(
        bool pointerOverOrb,
        bool programmaticClose,
        TimeSpan dismissalWindow)
    {
        if (!programmaticClose &&
            pointerOverOrb)
        {
            SuppressFor(dismissalWindow);
        }
    }

    public bool ShouldDismissPointerDown(
        bool popupOpen,
        TimeSpan dismissalWindow)
    {
        if (popupOpen)
        {
            SuppressFor(dismissalWindow);
            return true;
        }

        return IsOpenSuppressed;
    }

    public bool IsOpenSuppressed => _now() <= _suppressUntil;

    private void SuppressFor(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration));
        }

        _suppressUntil = _now() + duration;
    }
}
