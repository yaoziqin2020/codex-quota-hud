namespace CodexQuotaHud.App.UI;

public enum OrbPointerAction
{
    None,
    ToggleDetails,
    Refresh
}

public static class PointerGesture
{
    private const double ClickMovementThreshold = 4;

    public static bool IsClick(
        double startLeft,
        double startTop,
        double endLeft,
        double endTop) =>
        Math.Abs(endLeft - startLeft) <= ClickMovementThreshold &&
        Math.Abs(endTop - startTop) <= ClickMovementThreshold;

    public static OrbPointerAction SelectAction(int clickCount, bool moved)
    {
        if (clickCount >= 2)
        {
            return OrbPointerAction.Refresh;
        }

        return moved
            ? OrbPointerAction.None
            : OrbPointerAction.ToggleDetails;
    }
}
