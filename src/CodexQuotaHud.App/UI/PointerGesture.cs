namespace CodexQuotaHud.App.UI;

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

}
