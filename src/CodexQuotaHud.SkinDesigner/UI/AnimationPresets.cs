using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.UI;

public enum AnimationPresetKind
{
    Still,
    Gentle,
    Noticeable
}

public static class AnimationPresets
{
    private const double MatchTolerance = 1e-6;

    public static SkinAnimationSettings Resolve(
        AnimationPresetKind preset,
        bool hasDecoration) => preset switch
        {
            AnimationPresetKind.Still => new(0, 0, 0, 0),
            AnimationPresetKind.Gentle when hasDecoration =>
                new(.45, .45, .55, .15),
            AnimationPresetKind.Gentle => new(0, .55, .65, 0),
            AnimationPresetKind.Noticeable when hasDecoration =>
                new(.8, .9, .9, .25),
            AnimationPresetKind.Noticeable => new(0, .9, .9, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };

    public static string DisplayName(
        SkinAnimationSettings settings,
        bool hasDecoration)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var preset in Enum.GetValues<AnimationPresetKind>())
        {
            if (!EqualsWithinTolerance(
                    settings,
                    Resolve(preset, hasDecoration)))
            {
                continue;
            }

            return preset switch
            {
                AnimationPresetKind.Still => "静止",
                AnimationPresetKind.Gentle => "柔和",
                AnimationPresetKind.Noticeable => "明显",
                _ => throw new ArgumentOutOfRangeException(nameof(preset))
            };
        }

        return "自定义";
    }

    private static bool EqualsWithinTolerance(
        SkinAnimationSettings left,
        SkinAnimationSettings right) =>
        Close(left.RotationIntensity, right.RotationIntensity) &&
        Close(left.BreathingIntensity, right.BreathingIntensity) &&
        Close(left.GlowIntensity, right.GlowIntensity) &&
        Close(left.FloatingIntensity, right.FloatingIntensity);

    private static bool Close(double left, double right) =>
        Math.Abs(left - right) <= MatchTolerance;
}
