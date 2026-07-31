using System.Windows.Media;
using CodexQuotaHud.App.UI;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class QuotaAlertPresentationTests
{
    [Theory]
    [InlineData(20.1, QuotaAlertLevel.Normal)]
    [InlineData(20, QuotaAlertLevel.Warning)]
    [InlineData(10.1, QuotaAlertLevel.Warning)]
    [InlineData(10, QuotaAlertLevel.Critical)]
    [InlineData(0, QuotaAlertLevel.Critical)]
    [InlineData(-5, QuotaAlertLevel.Critical)]
    [InlineData(101, QuotaAlertLevel.Normal)]
    [InlineData(double.NaN, QuotaAlertLevel.Critical)]
    [InlineData(double.PositiveInfinity, QuotaAlertLevel.Critical)]
    public void Classify_UsesFixedNormalizedBoundaries(
        double percent,
        QuotaAlertLevel expected)
    {
        Assert.Equal(expected, QuotaAlertPolicy.Classify(percent));
    }

    [Fact]
    public void Palette_UsesSpecifiedWarningAndCriticalColors()
    {
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xB5, 0x47), QuotaAlertPalette.WarningMediaColor);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0x5A, 0x67), QuotaAlertPalette.CriticalMediaColor);
        Assert.Equal(System.Drawing.Color.FromArgb(0xFF, 0xB5, 0x47), QuotaAlertPalette.WarningDrawingColor);
        Assert.Equal(System.Drawing.Color.FromArgb(0xFF, 0x5A, 0x67), QuotaAlertPalette.CriticalDrawingColor);
    }

    [Fact]
    public void Palette_ProvidesFrozenSharedBrushes()
    {
        Assert.True(QuotaAlertPalette.WarningBrush.IsFrozen);
        Assert.True(QuotaAlertPalette.CriticalBrush.IsFrozen);
    }

    [Fact]
    public void ResolveBrush_PreservesNormalBrushIdentity()
    {
        var normal = new SolidColorBrush(Colors.AliceBlue);

        Assert.Same(normal, QuotaAlertPalette.ResolveBrush(QuotaAlertLevel.Normal, normal));
    }

    [Fact]
    public void ResolveColors_PreserveNormalColorValues()
    {
        var normalMedia = Color.FromArgb(0xFF, 0x12, 0x34, 0x56);
        var normalDrawing = System.Drawing.Color.FromArgb(0x12, 0x34, 0x56);

        Assert.Equal(normalMedia, QuotaAlertPalette.ResolveMediaColor(QuotaAlertLevel.Normal, normalMedia));
        Assert.Equal(normalDrawing, QuotaAlertPalette.ResolveDrawingColor(QuotaAlertLevel.Normal, normalDrawing));
    }
}
