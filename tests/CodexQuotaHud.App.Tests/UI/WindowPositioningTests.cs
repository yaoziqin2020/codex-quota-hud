using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class WindowPositioningTests
{
    [Theory]
    [InlineData(-40, 120, 0, 0, 1920, 1040, 0, 120)]
    [InlineData(1900, 120, 0, 0, 1920, 1040, 1788, 120)]
    [InlineData(300, -80, 0, 0, 1920, 1040, 300, 0)]
    [InlineData(300, 1000, 0, 0, 1920, 1040, 300, 908)]
    [InlineData(2100, 100, 1920, 0, 1920, 1040, 2100, 100)]
    public void Clamp_KeepsEntireOrbInsideSelectedWorkArea(
        double left,
        double top,
        double workLeft,
        double workTop,
        double workWidth,
        double workHeight,
        double expectedLeft,
        double expectedTop)
    {
        var result = WindowPositioning.Clamp(
            left,
            top,
            width: 132,
            height: 132,
            new WorkArea(workLeft, workTop, workWidth, workHeight));

        Assert.Equal(expectedLeft, result.Left);
        Assert.Equal(expectedTop, result.Top);
    }

    [Fact]
    public void Clamp_NonFinitePositionUsesWorkAreaCenter()
    {
        var result = WindowPositioning.Clamp(
            double.NaN,
            double.PositiveInfinity,
            width: 132,
            height: 132,
            new WorkArea(100, 50, 1000, 800));

        Assert.Equal(534, result.Left);
        Assert.Equal(384, result.Top);
    }
}
