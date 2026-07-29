using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class PointerGestureTests
{
    [Theory]
    [InlineData(100, 100, 100, 100, true)]
    [InlineData(100, 100, 103, 102, true)]
    [InlineData(100, 100, 105, 100, false)]
    [InlineData(100, 100, 100, 108, false)]
    public void IsClick_DistinguishesStationaryClickFromWindowDrag(
        double startLeft,
        double startTop,
        double endLeft,
        double endTop,
        bool expected)
    {
        Assert.Equal(
            expected,
            PointerGesture.IsClick(
                startLeft,
                startTop,
                endLeft,
                endTop));
    }
}
