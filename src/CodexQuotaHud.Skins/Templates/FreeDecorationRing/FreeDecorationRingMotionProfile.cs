namespace CodexQuotaHud.Skins.Templates.FreeDecorationRing;

internal readonly record struct AnimationRange(
    double From,
    double To,
    double HalfCycleSeconds);

internal static class FreeDecorationRingMotionProfile
{
    public static AnimationRange Breathing(
        double baseScale,
        double intensity)
    {
        var value = Math.Clamp(intensity, 0, 1);
        return new AnimationRange(
            baseScale * (1 - (.04 * value)),
            baseScale * (1 + (.12 * value)),
            2.4 - value);
    }

    public static AnimationRange Glow(double intensity)
    {
        var value = Math.Clamp(intensity, 0, 1);
        return new AnimationRange(
            .08,
            .15 + (.25 * value),
            2.4 - value);
    }

    public static AnimationRange Floating(
        double baseOffset,
        double intensity)
    {
        var value = Math.Clamp(intensity, 0, 1);
        var travel = 8 * value;
        return new AnimationRange(
            baseOffset - travel,
            baseOffset + travel,
            3.2 - (1.6 * value));
    }
}
