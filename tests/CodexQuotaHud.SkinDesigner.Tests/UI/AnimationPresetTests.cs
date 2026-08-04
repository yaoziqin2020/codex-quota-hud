using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Tests.UI;

public sealed class AnimationPresetTests
{
    [Theory]
    [InlineData(AnimationPresetKind.Still, false, 0, 0, 0, 0, "静止")]
    [InlineData(AnimationPresetKind.Gentle, false, 0, .55, .65, 0, "柔和")]
    [InlineData(AnimationPresetKind.Noticeable, false, 0, .9, .9, 0, "明显")]
    [InlineData(AnimationPresetKind.Gentle, true, .45, .45, .55, .15, "柔和")]
    [InlineData(AnimationPresetKind.Noticeable, true, .8, .9, .9, .25, "明显")]
    public void Resolve_UsesExactApprovedValues(
        AnimationPresetKind kind,
        bool hasDecoration,
        double rotation,
        double breathing,
        double glow,
        double floating,
        string displayName)
    {
        var settings = AnimationPresets.Resolve(kind, hasDecoration);

        Assert.Equal(
            new SkinAnimationSettings(rotation, breathing, glow, floating),
            settings);
        Assert.Equal(
            displayName,
            AnimationPresets.DisplayName(settings, hasDecoration));
    }

    [Fact]
    public void DisplayName_ReturnsCustomForManualValues()
    {
        var settings = new SkinAnimationSettings(.123, .456, .789, .111);

        Assert.Equal(
            "自定义",
            AnimationPresets.DisplayName(settings, hasDecoration: true));
    }

    [Fact]
    public void DisplayName_ToleratesSliderRoundTripNoise()
    {
        var settings = new SkinAnimationSettings(
            .4500004,
            .4499996,
            .5500004,
            .1499996);

        Assert.Equal(
            "柔和",
            AnimationPresets.DisplayName(settings, hasDecoration: true));
    }
}
