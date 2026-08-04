using CodexQuotaHud.SkinDesigner.Drafts;
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

    [Theory]
    [InlineData("speed", 0d, "$.animation.refreshSpeedMultiplier")]
    [InlineData("speed", 4d, "$.animation.refreshSpeedMultiplier")]
    [InlineData("hold", 0d, "$.animation.refreshHoldSeconds")]
    [InlineData("hold", 3d, "$.animation.refreshHoldSeconds")]
    public void RefreshAnimationEditors_ValidateBoundsAndPublishDirtyPreviewDrafts(
        string field,
        double value,
        string errorLocation)
    {
        var timestamp = DateTimeOffset.Parse("2026-08-04T00:00:00Z");
        var session = new SkinDraftSession(
            SkinDraftFactory.CreateNew(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                timestamp,
                SemanticVersion.Parse("1.2.3")),
            () => timestamp = timestamp.AddSeconds(1));
        var previewed = new List<SkinDraftDocument>();
        using var sut = new DesignerViewModel(session, previewed.Add);
        var beforeRevision = session.Current.Revision;

        Assert.False(session.HasUnsavedChanges);
        var accepted = field == "speed"
            ? sut.Animation.SetRefreshSpeedMultiplier(value)
            : sut.Animation.SetRefreshHoldSeconds(value);

        Assert.True(accepted.Succeeded, Format(accepted.Errors));
        Assert.True(session.HasUnsavedChanges);
        Assert.Equal(beforeRevision + 1, session.Current.Revision);
        Assert.Single(previewed);
        Assert.Equal(
            value,
            field == "speed"
                ? session.Current.Theme.Animation.RefreshSpeedMultiplier
                : session.Current.Theme.Animation.RefreshHoldSeconds);
        Assert.Equal(session.Current, previewed[0]);

        var revision = session.Current.Revision;
        foreach (var invalid in new[]
                 {
                     -0.001d,
                     field == "speed" ? 4.001d : 3.001d,
                     double.NaN,
                     double.PositiveInfinity,
                     double.NegativeInfinity
                 })
        {
            var rejected = field == "speed"
                ? sut.Animation.SetRefreshSpeedMultiplier(invalid)
                : sut.Animation.SetRefreshHoldSeconds(invalid);

            Assert.False(rejected.Succeeded);
            Assert.Contains(rejected.Errors, error =>
                error.Location == errorLocation);
            Assert.Equal(revision, session.Current.Revision);
            Assert.Single(previewed);
        }
    }

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join("; ", errors.Select(error =>
            $"{error.Code}@{error.Location}"));
}
