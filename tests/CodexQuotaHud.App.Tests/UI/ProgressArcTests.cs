using CodexQuotaHud.App.UI.Controls;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class ProgressArcTests
{
    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(25, 90)]
    [InlineData(50, 180)]
    [InlineData(100, 359.999)]
    [InlineData(140, 359.999)]
    public void SweepAngle_ClampsProgressToRenderableRange(
        double progress,
        double expected)
    {
        Assert.Equal(expected, ProgressArc.CalculateSweepAngle(progress), 3);
    }
}
