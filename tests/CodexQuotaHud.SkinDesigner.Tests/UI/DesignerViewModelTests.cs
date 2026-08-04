using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Images;
using CodexQuotaHud.SkinDesigner.UI;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Tests.UI;

public sealed class DesignerViewModelTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-02T00:00:00Z");

    [Fact]
    public void Constructor_ExposesExactlySixOrderedEditorSections()
    {
        var sut = CreateViewModel(out _, out _);

        Assert.Equal(
            ["基本信息", "图片", "额度环", "颜色与效果", "文字", "动画"],
            sut.Sections.Select(section => section.Header));
        Assert.Same(sut.BasicInformation, sut.Sections[0]);
        Assert.Same(sut.Images, sut.Sections[1]);
        Assert.Same(sut.QuotaRings, sut.Sections[2]);
        Assert.Same(sut.ColorsAndEffects, sut.Sections[3]);
        Assert.Same(sut.Text, sut.Sections[4]);
        Assert.Same(sut.Animation, sut.Sections[5]);
    }

    [Fact]
    public void AcceptedEdits_ChangeOnlyRequestedFieldAndPublishExactlyOnce()
    {
        var sut = CreateViewModel(out var session, out var previewed);
        var meaningful = 0;
        session.MeaningfulChange += (_, _) => meaningful++;

        AssertAccepted(() => sut.BasicInformation.SetProjectName("项目甲"),
            draft => Assert.Equal("项目甲", draft.ProjectName));
        AssertAccepted(() => sut.BasicInformation.SetDisplayName("皮肤甲"),
            draft => Assert.Equal("皮肤甲", draft.DisplayName));
        AssertAccepted(() => sut.BasicInformation.SetAuthor("作者甲"),
            draft => Assert.Equal("作者甲", draft.Author));
        AssertAccepted(() => sut.BasicInformation.SetPackageVersion("2.3.4"),
            draft => Assert.Equal(SemanticVersion.Parse("2.3.4"), draft.PackageVersion));
        AssertAccepted(() => sut.BasicInformation.SetDescription("说明"),
            draft => Assert.Equal("说明", draft.Description));

        AssertAccepted(() => sut.QuotaRings.SetRingDiameter(100),
            draft => Assert.Equal(100, draft.Theme.RingDiameter));
        AssertAccepted(() => sut.QuotaRings.SetRingThickness(7),
            draft => Assert.Equal(7, draft.Theme.RingThickness));
        AssertAccepted(() => sut.QuotaRings.SetRingGap(9),
            draft => Assert.Equal(9, draft.Theme.RingGap));
        AssertAccepted(() => sut.QuotaRings.SetStartAngle(123),
            draft => Assert.Equal(123, draft.Theme.StartAngle));

        AssertAccepted(() => sut.ColorsAndEffects.SetPrimaryRingColor("#FF112233"),
            draft => Assert.Equal("#FF112233", draft.Theme.PrimaryRingColor));
        AssertAccepted(() => sut.ColorsAndEffects.SetSecondaryRingColor("#FF445566"),
            draft => Assert.Equal("#FF445566", draft.Theme.SecondaryRingColor));
        AssertAccepted(() => sut.ColorsAndEffects.SetBaseBackgroundColor("#FF778899"),
            draft => Assert.Equal("#FF778899", draft.Theme.BaseBackgroundColor));
        AssertAccepted(() => sut.ColorsAndEffects.SetBaseBackgroundOpacity(0.4),
            draft => Assert.Equal(0.4, draft.Theme.BaseBackgroundOpacity));
        AssertAccepted(() => sut.ColorsAndEffects.SetGlowColor("#FFAABBCC"),
            draft => Assert.Equal("#FFAABBCC", draft.Theme.GlowColor));
        AssertAccepted(() => sut.ColorsAndEffects.SetGlowIntensity(0.6),
            draft => Assert.Equal(0.6, draft.Theme.GlowIntensity));

        AssertAccepted(() => sut.Text.SetNumberTextSize(31),
            draft => Assert.Equal(31, draft.Theme.NumberTextSize));
        AssertAccepted(() => sut.Text.SetLabelTextSize(15),
            draft => Assert.Equal(15, draft.Theme.LabelTextSize));
        AssertAccepted(() => sut.Text.SetTextWeight(SkinTextWeight.Bold),
            draft => Assert.Equal(SkinTextWeight.Bold, draft.Theme.TextWeight));
        AssertAccepted(() => sut.Text.SetTextPlacement(SkinTextPlacement.LabelAboveNumber),
            draft => Assert.Equal(SkinTextPlacement.LabelAboveNumber, draft.Theme.TextPlacement));

        AssertAccepted(() => sut.Animation.SetRotationIntensity(0.1),
            draft => Assert.Equal(0.1, draft.Theme.Animation.RotationIntensity));
        AssertAccepted(() => sut.Animation.SetBreathingIntensity(0.2),
            draft => Assert.Equal(0.2, draft.Theme.Animation.BreathingIntensity));
        AssertAccepted(() => sut.Animation.SetGlowIntensity(0.3),
            draft => Assert.Equal(0.3, draft.Theme.Animation.GlowIntensity));
        AssertAccepted(() => sut.Animation.SetFloatingIntensity(0.4),
            draft => Assert.Equal(0.4, draft.Theme.Animation.FloatingIntensity));

        Assert.Equal(session.Current.Revision, meaningful);
        Assert.Equal(session.Current.Revision, previewed.Count);
        Assert.Equal(session.Current, previewed[^1]);

        void AssertAccepted(
            Func<EditorMutationResult> mutate,
            Action<SkinDraftDocument> assertField)
        {
            var before = session.Current;
            var revision = before.Revision;
            var eventsBefore = meaningful;
            var previewsBefore = previewed.Count;

            var result = mutate();

            Assert.True(result.Succeeded, Format(result.Errors));
            Assert.Equal(revision + 1, session.Current.Revision);
            Assert.Equal(eventsBefore + 1, meaningful);
            Assert.Equal(previewsBefore + 1, previewed.Count);
            assertField(session.Current);
        }
    }

    [Theory]
    [MemberData(nameof(NumericBoundaries))]
    public void NumericEditors_AcceptExactBoundsAndRejectOutsideOrNonFinite(
        string field,
        double minimum,
        double maximum,
        double below,
        double above)
    {
        var sut = CreateViewModel(out var session, out var previewed);

        Assert.True(ApplyNumeric(sut, field, minimum).Succeeded);
        Assert.True(ApplyNumeric(sut, field, maximum).Succeeded);
        var revision = session.Current.Revision;
        var previewCount = previewed.Count;
        var expectedLocation = "$." + field["theme.".Length..];

        foreach (var invalid in new[]
                 {
                     below,
                     above,
                     double.NaN,
                     double.PositiveInfinity,
                     double.NegativeInfinity
                 })
        {
            var result = ApplyNumeric(sut, field, invalid);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Errors, error => error.Location == expectedLocation);
            Assert.Equal(revision, session.Current.Revision);
            Assert.Equal(previewCount, previewed.Count);
        }
    }

    [Fact]
    public void InvalidMetadataColorVersionAndEnumsChangeNothing()
    {
        var sut = CreateViewModel(out var session, out var previewed);
        var invalid = new Func<EditorMutationResult>[]
        {
            () => sut.BasicInformation.SetDisplayName(new string('界', 81)),
            () => sut.BasicInformation.SetAuthor("safe\u0001author"),
            () => sut.BasicInformation.SetPackageVersion("01.2.3"),
            () => sut.ColorsAndEffects.SetPrimaryRingColor("#GG112233"),
            () => sut.Text.SetTextWeight((SkinTextWeight)99),
            () => sut.Text.SetTextPlacement((SkinTextPlacement)99)
        };

        foreach (var mutate in invalid)
        {
            var before = session.Current;
            var result = mutate();

            Assert.False(result.Succeeded);
            Assert.NotEmpty(result.Errors);
            Assert.Same(before, session.Current);
            Assert.Empty(previewed);
        }
    }

    [Fact]
    public void ApplyPreset_WithoutDecorationPublishesOneEffectiveUpdate()
    {
        using var sut = CreateViewModel(out var session, out var previewed);
        var beforeRevision = session.Current.Revision;

        var result = sut.Animation.ApplyPreset(AnimationPresetKind.Noticeable);

        Assert.True(result.Succeeded, Format(result.Errors));
        Assert.Equal(beforeRevision + 1, session.Current.Revision);
        Assert.Equal(
            new SkinAnimationSettings(0, .9, .9, 0),
            session.Current.Theme.Animation);
        Assert.Single(previewed);
        Assert.Equal("明显", sut.Animation.CurrentPresetName);
        Assert.False(sut.Animation.CanEditDecorationAnimation);
        Assert.Contains("透明装饰图", sut.Animation.DecorationAnimationHint);
    }

    [Fact]
    public void DecorationAvailability_RefreshesWithoutSilentlyRewritingAnimation()
    {
        var asset = new SkinAsset(
            SkinAssetSlot.Decoration,
            "assets/decoration.png",
            [1, 2, 3],
            PixelWidth: 1,
            PixelHeight: 1,
            HasAlpha: true);
        var next = CreatedAt;
        var session = new SkinDraftSession(
            SkinDraftFactory.CreateNew(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CreatedAt,
                SemanticVersion.Parse("1.1.1")),
            () => next = next.AddSeconds(1));
        using var sut = new DesignerViewModel(
            session,
            new Dictionary<SkinAssetSlot, SkinAsset>
            {
                [SkinAssetSlot.Decoration] = asset
            },
            previewUpdate: null);
        var changedProperties = new List<string?>();
        sut.Animation.PropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName);

        var preset = sut.Animation.ApplyPreset(AnimationPresetKind.Noticeable);
        Assert.True(preset.Succeeded, Format(preset.Errors));
        var configured = session.Current.Theme.Animation;
        Assert.Equal(new SkinAnimationSettings(.8, .9, .9, .25), configured);
        Assert.True(sut.Animation.CanEditDecorationAnimation);
        Assert.Equal("明显", sut.Animation.CurrentPresetName);

        changedProperties.Clear();
        var committer = (IDesignerImageMutationCommitter)sut;
        Assert.True(committer.TryRemove(SkinAssetSlot.Decoration));

        Assert.Equal(configured, session.Current.Theme.Animation);
        Assert.False(sut.Animation.CanEditDecorationAnimation);
        Assert.Equal("自定义", sut.Animation.CurrentPresetName);
        Assert.Contains(nameof(sut.Animation.CanEditDecorationAnimation),
            changedProperties);
        Assert.Contains(nameof(sut.Animation.DecorationAnimationHint),
            changedProperties);
        Assert.Contains(nameof(sut.Animation.CurrentPresetName),
            changedProperties);

        changedProperties.Clear();
        Assert.True(committer.TryCommit(
            asset,
            new DraftAssetReference(
                SkinAssetSlot.Decoration,
                asset.RelativePath,
                "decoration.png")));

        Assert.True(sut.Animation.CanEditDecorationAnimation);
        Assert.Equal("明显", sut.Animation.CurrentPresetName);
        Assert.Contains(nameof(sut.Animation.CanEditDecorationAnimation),
            changedProperties);
    }

    public static IEnumerable<object[]> NumericBoundaries()
    {
        yield return ["theme.ringDiameter", 72d, 116d, 71.999d, 116.001d];
        yield return ["theme.ringThickness", 2d, 16d, 1.999d, 16.001d];
        yield return ["theme.ringGap", 2d, 24d, 1.999d, 24.001d];
        yield return ["theme.startAngle", 0d, 359d, -0.001d, 359.001d];
        yield return ["theme.baseBackgroundOpacity", 0d, 1d, -0.001d, 1.001d];
        yield return ["theme.glowIntensity", 0d, 1d, -0.001d, 1.001d];
        yield return ["theme.numberTextSize", 12d, 34d, 11.999d, 34.001d];
        yield return ["theme.labelTextSize", 12d, 34d, 11.999d, 34.001d];
        yield return ["theme.animation.rotationIntensity", 0d, 1d, -0.001d, 1.001d];
        yield return ["theme.animation.breathingIntensity", 0d, 1d, -0.001d, 1.001d];
        yield return ["theme.animation.glowIntensity", 0d, 1d, -0.001d, 1.001d];
        yield return ["theme.animation.floatingIntensity", 0d, 1d, -0.001d, 1.001d];
    }

    private static EditorMutationResult ApplyNumeric(
        DesignerViewModel sut,
        string field,
        double value) => field switch
    {
        "theme.ringDiameter" => sut.QuotaRings.SetRingDiameter(value),
        "theme.ringThickness" => sut.QuotaRings.SetRingThickness(value),
        "theme.ringGap" => sut.QuotaRings.SetRingGap(value),
        "theme.startAngle" => sut.QuotaRings.SetStartAngle(value),
        "theme.baseBackgroundOpacity" => sut.ColorsAndEffects.SetBaseBackgroundOpacity(value),
        "theme.glowIntensity" => sut.ColorsAndEffects.SetGlowIntensity(value),
        "theme.numberTextSize" => sut.Text.SetNumberTextSize(value),
        "theme.labelTextSize" => sut.Text.SetLabelTextSize(value),
        "theme.animation.rotationIntensity" => sut.Animation.SetRotationIntensity(value),
        "theme.animation.breathingIntensity" => sut.Animation.SetBreathingIntensity(value),
        "theme.animation.glowIntensity" => sut.Animation.SetGlowIntensity(value),
        "theme.animation.floatingIntensity" => sut.Animation.SetFloatingIntensity(value),
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private static DesignerViewModel CreateViewModel(
        out SkinDraftSession session,
        out List<SkinDraftDocument> previewed)
    {
        var next = CreatedAt;
        session = new SkinDraftSession(
            SkinDraftFactory.CreateNew(
                Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                CreatedAt,
                SemanticVersion.Parse("1.1.1")),
            () => next = next.AddSeconds(1));
        previewed = [];
        return new DesignerViewModel(session, previewed.Add);
    }

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join("; ", errors.Select(error =>
            $"{error.Code}@{error.Location}"));
}
