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
    [InlineData(.55, .08, .2875, 1.85)]
    [InlineData(.9, .08, .375, 1.5)]
    [InlineData(1, .08, .4, 1.4)]
    public void Glow_KeepsFullRingVisuallyBelowSolidProgressArc(
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

    [Theory]
    [InlineData(3, 0, 3, 3, 3.2)]
    [InlineData(3, .15, 1.8, 4.2, 2.96)]
    [InlineData(3, .25, 1, 5, 2.8)]
    [InlineData(3, 1, -5, 11, 1.6)]
    public void Floating_TurnsSliderIntoVisibleVerticalTravel(
        double baseOffset,
        double intensity,
        double from,
        double to,
        double halfCycle)
    {
        var range = FreeDecorationRingMotionProfile.Floating(
            baseOffset,
            intensity);

        Assert.Equal(from, range.From, 6);
        Assert.Equal(to, range.To, 6);
        Assert.Equal(halfCycle, range.HalfCycleSeconds, 6);
    }
}
