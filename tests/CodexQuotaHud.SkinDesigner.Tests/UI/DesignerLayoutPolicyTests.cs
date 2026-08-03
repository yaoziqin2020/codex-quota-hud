using System.Windows;
using CodexQuotaHud.SkinDesigner.UI;

namespace CodexQuotaHud.SkinDesigner.Tests.UI;

public sealed class DesignerLayoutPolicyTests
{
    [Fact]
    public void WindowsMonitorSource_ConvertsNegativePhysicalMixedDpiWorkAreaToDips()
    {
        var converted = WindowsDesignerMonitorWorkAreaSource.ToDipWorkArea(
            left: -1920,
            top: 0,
            right: 0,
            bottom: 1080,
            new DpiScale(2, 2));

        Assert.Equal(new Rect(-960, 0, 960, 540), converted);
    }

    public static TheoryData<Rect, double> WorkAreas => new()
    {
        { new Rect(0, 0, 1920, 1080), 1.0 },
        { new Rect(0, 0, 1920, 1080), 1.25 },
        { new Rect(0, 0, 1920, 1080), 1.5 },
        { new Rect(0, 0, 1920, 1080), 2.0 },
        { new Rect(40, 30, 1280, 720), 1.0 },
        { new Rect(40, 30, 1280, 720), 1.25 },
        { new Rect(40, 30, 1280, 720), 1.5 },
        { new Rect(40, 30, 1280, 720), 2.0 },
        { new Rect(-960, 0, 960, 540), 1.0 },
        { new Rect(-960, 0, 960, 540), 1.25 },
        { new Rect(-960, 0, 960, 540), 1.5 },
        { new Rect(-960, 0, 960, 540), 2.0 }
    };

    [Theory]
    [MemberData(nameof(WorkAreas))]
    public void Calculate_KeepsWindowInsideWorkAreaAndPreviewAtLeast280Dip(
        Rect workArea,
        double scale)
    {
        var layout = DesignerLayoutPolicy.Calculate(
            workArea,
            new DpiScale(scale, scale));

        Assert.True(workArea.Contains(layout.WindowBounds.TopLeft));
        Assert.True(workArea.Contains(layout.WindowBounds.BottomRight));
        Assert.True(layout.PreviewWidth >= 280);
        Assert.True(layout.EditorWidth >= 320);
        Assert.True(layout.EditorWidth + layout.PreviewWidth <=
            layout.WindowBounds.Width);
        Assert.Equal(workArea.Width < 1180 || workArea.Height < 700,
            layout.Compact);
    }

    [Theory]
    [InlineData(double.NaN, 720)]
    [InlineData(1280, 0)]
    [InlineData(279, 540)]
    public void Calculate_RejectsInvalidOrTooSmallWorkArea(
        double width,
        double height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DesignerLayoutPolicy.Calculate(
                new Rect(0, 0, width, height),
                new DpiScale(1, 1)));
    }
}
