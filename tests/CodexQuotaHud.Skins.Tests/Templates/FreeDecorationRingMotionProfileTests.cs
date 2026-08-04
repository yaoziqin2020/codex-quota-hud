using CodexQuotaHud.Skins.Templates.FreeDecorationRing;

namespace CodexQuotaHud.Skins.Tests.Templates;

public sealed class FreeDecorationRingMotionProfileTests
{
    [Theory]
    [InlineData(1, 1, .96, 1.12, 1.4)]
    [InlineData(1, .55, .978, 1.066, 1.85)]
    [InlineData(.8, .9, .7712, .8864, 1.5)]
    public void Breathing_UsesApprovedVisibleRange(
        double baseScale,
        double intensity,
        double from,
        double to,
        double halfCycle)
    {
        var range = FreeDecorationRingMotionProfile.Breathing(
            baseScale,
            intensity);

        Assert.Equal(from, range.From, 6);
        Assert.Equal(to, range.To, 6);
        Assert.Equal(halfCycle, range.HalfCycleSeconds, 6);
    }

    [Theory]
    [InlineData(.55, .08, .5625, 1.85)]
    [InlineData(.9, .08, .825, 1.5)]
    [InlineData(1, .08, .9, 1.4)]
    public void Glow_UsesApprovedVisibleOpacityRange(
        double intensity,
        double from,
        double to,
        double halfCycle)
    {
        var range = FreeDecorationRingMotionProfile.Glow(intensity);

        Assert.Equal(from, range.From, 6);
        Assert.Equal(to, range.To, 6);
        Assert.Equal(halfCycle, range.HalfCycleSeconds, 6);
    }
}
