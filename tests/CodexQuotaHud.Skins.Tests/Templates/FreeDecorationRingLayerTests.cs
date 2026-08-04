using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Templates;
using CodexQuotaHud.Skins.Templates.FreeDecorationRing;

namespace CodexQuotaHud.Skins.Tests.Templates;

[Collection(WpfTestCollection.Name)]
public sealed class FreeDecorationRingLayerTests
{
    [Fact]
    public void ProtectedVisualTree_PreservesLayerOrderAndImageContainment()
    {
        WpfTestThread.Run(() =>
        {
            var renderer = CreateRenderer(AllSlots);
            FrameworkElement[] ordered =
            [
                renderer.BackgroundImage,
                renderer.BaseFill,
                renderer.DecorationImage,
                renderer.CenterImage,
                renderer.SecondaryTrack,
                renderer.SecondaryProgress,
                renderer.PrimaryTrack,
                renderer.PrimaryProgress,
                renderer.QuotaNumber,
                renderer.QuotaLabel
            ];

            Assert.All(ordered, element => Assert.Same(renderer.RootGrid, element.Parent));
            Assert.True(ordered
                .Select(Panel.GetZIndex)
                .Zip(ordered.Skip(1).Select(Panel.GetZIndex))
                .All(pair => pair.First < pair.Second));

            Assert.False(renderer.BackgroundImage.IsHitTestVisible);
            Assert.False(renderer.CenterImage.IsHitTestVisible);
            Assert.False(renderer.DecorationImage.IsHitTestVisible);
            Assert.True(renderer.RootGrid.ClipToBounds);
            Assert.NotNull(renderer.BackgroundImage.Clip);
            Assert.Null(renderer.DecorationImage.Clip);
            var centerClip = Assert.IsType<EllipseGeometry>(renderer.CenterImage.Clip);
            Assert.InRange(centerClip.Bounds.Left, 0, 132);
            Assert.InRange(centerClip.Bounds.Top, 0, 132);
            Assert.InRange(centerClip.Bounds.Right, 0, 132);
            Assert.InRange(centerClip.Bounds.Bottom, 0, 132);
            Assert.Null(renderer.RootGrid.OpacityMask);
            Assert.Null(renderer.PrimaryTrack.OpacityMask);
            Assert.Null(renderer.PrimaryProgress.OpacityMask);
            Assert.Null(renderer.SecondaryTrack.OpacityMask);
            Assert.Null(renderer.SecondaryProgress.OpacityMask);
            Assert.Null(renderer.QuotaNumber.OpacityMask);
            Assert.Null(renderer.QuotaLabel.OpacityMask);
        });
    }

    [Fact]
    public void ImageTransforms_AreIndependentAndApplyOnlyToTheirSlot()
    {
        WpfTestThread.Run(() =>
        {
            var renderer = CreateRenderer(
                AllSlots,
                theme => theme with
                {
                    Background = new SkinImageTransform(-50, 50, 0.25, -180, 0, 0, 1),
                    Center = new SkinImageTransform(11, 12, 2, 33, 0.5, 0.25, 0.75),
                    Decoration = new SkinImageTransform(50, -50, 3, 180, 1, 1, 0)
                });

            Assert.NotSame(renderer.BackgroundImage.RenderTransform, renderer.CenterImage.RenderTransform);
            Assert.NotSame(renderer.CenterImage.RenderTransform, renderer.DecorationImage.RenderTransform);
            AssertTransform(renderer.BackgroundImage, -50, 50, 0.25, -180, 0);
            AssertTransform(renderer.CenterImage, 11, 12, 2, 33, 0.5);
            AssertTransform(renderer.DecorationImage, 50, -50, 3, 180, 1);
            AssertCrop(renderer.BackgroundImage.Fill, 0, 0);
            AssertCrop(renderer.CenterImage.Fill, 0, 0.375);
            AssertCrop(renderer.DecorationImage.Fill, 0.5, 0);
        });
    }

    [Fact]
    public void ThemeParameters_ApplyAtBothValidatedBounds()
    {
        WpfTestThread.Run(() =>
        {
            foreach (var maximum in new[] { false, true })
            {
                var transform = new SkinImageTransform(
                    maximum ? 50 : -50,
                    maximum ? 50 : -50,
                    maximum ? 3 : 0.25,
                    maximum ? 180 : -180,
                    maximum ? 1 : 0,
                    maximum ? 1 : 0,
                    maximum ? 1 : 0);
                var renderer = CreateRenderer(
                    AllSlots,
                    theme => theme with
                    {
                        Background = transform,
                        Center = transform,
                        Decoration = transform,
                        BaseBackgroundOpacity = maximum ? 1 : 0,
                        RingDiameter = maximum ? 116 : 72,
                        RingThickness = maximum ? 16 : 2,
                        RingGap = maximum ? 24 : 2,
                        StartAngle = maximum ? 359 : 0,
                        GlowIntensity = maximum ? 1 : 0,
                        NumberTextSize = maximum ? 34 : 12,
                        LabelTextSize = maximum ? 34 : 12,
                        TextWeight = maximum ? SkinTextWeight.Bold : SkinTextWeight.Regular,
                        TextPlacement = maximum
                            ? SkinTextPlacement.LabelAboveNumber
                            : SkinTextPlacement.Centered,
                        Animation = new SkinAnimationSettings(
                            maximum ? 1 : 0,
                            maximum ? 1 : 0,
                            maximum ? 1 : 0,
                            maximum ? 1 : 0)
                    });

                var expectedDiameter = maximum ? 116 : 72;
                var expectedSecondaryDiameter = maximum ? 36 : 64;
                var expectedThickness = maximum ? 16 : 2;
                var expectedGap = maximum ? 24 : 2;
                var expectedStartAngle = maximum ? 359 : 0;
                Assert.Equal(maximum ? 1 : 0, renderer.BaseFill.Opacity);
                Assert.Equal(expectedDiameter, renderer.PrimaryTrack.Width);
                Assert.Equal(expectedDiameter, renderer.PrimaryTrack.Height);
                Assert.Equal(expectedThickness, renderer.PrimaryTrack.StrokeThickness);
                Assert.Equal(expectedStartAngle, renderer.PrimaryProgress.StartAngle);
                Assert.Equal(expectedSecondaryDiameter, renderer.SecondaryTrack.Width);
                Assert.Equal(expectedSecondaryDiameter, renderer.SecondaryTrack.Height);
                Assert.Equal(expectedThickness, renderer.SecondaryTrack.StrokeThickness);
                Assert.Equal(expectedSecondaryDiameter, renderer.SecondaryProgress.Width);
                Assert.Equal(expectedSecondaryDiameter, renderer.SecondaryProgress.Height);
                Assert.Equal(expectedThickness, renderer.SecondaryProgress.StrokeThickness);
                Assert.Equal(expectedStartAngle, renderer.SecondaryProgress.StartAngle);
                Assert.True(renderer.SecondaryTrack.Width < renderer.PrimaryTrack.Width);
                var trackEdgeGap =
                    ((renderer.PrimaryTrack.Width - renderer.PrimaryTrack.StrokeThickness) / 2) -
                    ((renderer.SecondaryTrack.Width + renderer.SecondaryTrack.StrokeThickness) / 2);
                var progressEdgeGap =
                    ((renderer.PrimaryProgress.Width - renderer.PrimaryProgress.StrokeThickness) / 2) -
                    ((renderer.SecondaryProgress.Width + renderer.SecondaryProgress.StrokeThickness) / 2);
                Assert.Equal(expectedGap, trackEdgeGap);
                Assert.Equal(expectedGap, progressEdgeGap);
                Assert.Equal(maximum ? 34 : 12, renderer.QuotaNumber.FontSize);
                Assert.Equal(maximum ? 34 : 12, renderer.QuotaLabel.FontSize);
                Assert.Equal(maximum ? FontWeights.Bold : FontWeights.Normal,
                    renderer.QuotaNumber.FontWeight);
                Assert.Equal(maximum ? 4 : 0, renderer.AnimationTrackCount);
            }
        });
    }

    [Fact]
    public void CrossExtremeRingMetrics_KeepTheInnerRingVisibleWithoutOverlap()
    {
        WpfTestThread.Run(() =>
        {
            var renderer = CreateRenderer(
                AllSlots,
                theme => theme with
                {
                    RingDiameter = 72,
                    RingThickness = 16,
                    RingGap = 24
                });

            renderer.Render(new CustomSkinRenderState(
                68,
                34,
                "5-hour",
                QuotaDisplayMode.Dual,
                false,
                Colors.Cyan,
                Colors.MediumPurple));

            Assert.Equal(32, renderer.SecondaryTrack.Width);
            Assert.Equal(32, renderer.SecondaryTrack.Height);
            Assert.Equal(32, renderer.SecondaryProgress.Width);
            Assert.Equal(32, renderer.SecondaryProgress.Height);
            Assert.True(
                renderer.SecondaryTrack.Width >
                renderer.SecondaryTrack.StrokeThickness);
            Assert.True(
                renderer.SecondaryProgress.Width >
                renderer.SecondaryProgress.StrokeThickness);

            var trackEdgeGap =
                ((renderer.PrimaryTrack.Width - renderer.PrimaryTrack.StrokeThickness) / 2) -
                ((renderer.SecondaryTrack.Width + renderer.SecondaryTrack.StrokeThickness) / 2);
            var progressEdgeGap =
                ((renderer.PrimaryProgress.Width - renderer.PrimaryProgress.StrokeThickness) / 2) -
                ((renderer.SecondaryProgress.Width + renderer.SecondaryProgress.StrokeThickness) / 2);
            Assert.Equal(4, trackEdgeGap);
            Assert.Equal(4, progressEdgeGap);
            Assert.InRange(trackEdgeGap, double.Epsilon, 24);
            Assert.InRange(progressEdgeGap, double.Epsilon, 24);
            Assert.Equal(Visibility.Visible, renderer.SecondaryTrack.Visibility);
            Assert.Equal(Visibility.Visible, renderer.SecondaryProgress.Visibility);
        });
    }

    [Fact]
    public void Render_UsesIndependentRingsAndDoesNotMutateDualState()
    {
        WpfTestThread.Run(() =>
        {
            var renderer = CreateRenderer(AllSlots);
            var state = new CustomSkinRenderState(
                68,
                34,
                "5-hour",
                QuotaDisplayMode.Dual,
                false,
                Colors.OrangeRed,
                Colors.MediumPurple);

            renderer.Render(state);

            Assert.Equal(244.8, renderer.PrimaryProgress.SweepAngle, 6);
            Assert.Equal(122.4, renderer.SecondaryProgress.SweepAngle, 6);
            Assert.Equal(68, state.PrimaryPercent);
            Assert.Equal(34, state.SecondaryPercent);
            Assert.Equal(Colors.OrangeRed,
                Assert.IsType<SolidColorBrush>(renderer.PrimaryProgress.Stroke).Color);
            Assert.Equal(Colors.MediumPurple,
                Assert.IsType<SolidColorBrush>(renderer.SecondaryProgress.Stroke).Color);
            Assert.Equal(Visibility.Visible, renderer.SecondaryTrack.Visibility);
            Assert.Equal(Visibility.Visible, renderer.SecondaryProgress.Visibility);
            Assert.Equal("68%", renderer.QuotaNumber.Text);
            Assert.Equal("5-hour", renderer.QuotaLabel.Text);
        });
    }

    [Theory]
    [InlineData("5-hour")]
    [InlineData("Weekly")]
    public void Render_SingleStateRemovesSecondaryRing(string label)
    {
        WpfTestThread.Run(() =>
        {
            var renderer = CreateRenderer(AllSlots);

            renderer.Render(new CustomSkinRenderState(
                100,
                null,
                label,
                QuotaDisplayMode.Single,
                true,
                Colors.Cyan,
                null));

            Assert.Equal(360, renderer.PrimaryProgress.SweepAngle);
            Assert.Equal(Visibility.Collapsed, renderer.SecondaryTrack.Visibility);
            Assert.Equal(Visibility.Collapsed, renderer.SecondaryProgress.Visibility);
            Assert.Equal(label, renderer.QuotaLabel.Text);
        });
    }

    [Fact]
    public void Render_HiddenStateCollapsesAllQuotaContent()
    {
        WpfTestThread.Run(() =>
        {
            var renderer = CreateRenderer(AllSlots);

            renderer.Render(new CustomSkinRenderState(
                0,
                null,
                string.Empty,
                QuotaDisplayMode.Hidden,
                false,
                Colors.Transparent,
                null));

            Assert.All(
                new FrameworkElement[]
                {
                    renderer.SecondaryTrack,
                    renderer.SecondaryProgress,
                    renderer.PrimaryTrack,
                    renderer.PrimaryProgress,
                    renderer.QuotaNumber,
                    renderer.QuotaLabel
                },
                element => Assert.Equal(Visibility.Collapsed, element.Visibility));
        });
    }

    [Fact]
    public void AnimationState_EnforcesPrecedenceAndFrameRateCaps()
    {
        WpfTestThread.Run(() =>
        {
            var renderer = CreateRenderer(AllSlots);

            renderer.ApplyAnimationState(CustomSkinAnimationState.Idle, true);
            Assert.Equal(4, renderer.DesiredFrameRate);
            Assert.True(renderer.HasActiveAnimations);
            Assert.All(renderer.ConfiguredFrameRates, rate => Assert.Equal(4, rate));

            renderer.ApplyAnimationState(CustomSkinAnimationState.Refreshing, true);
            Assert.Equal(24, renderer.DesiredFrameRate);
            Assert.True(renderer.HasActiveAnimations);
            Assert.All(renderer.ConfiguredFrameRates, rate => Assert.Equal(24, rate));

            renderer.ApplyAnimationState(CustomSkinAnimationState.Refreshing, false);
            Assert.False(renderer.HasActiveAnimations);
            Assert.Null(renderer.DesiredFrameRate);
            AssertTransformsReset(renderer);

            renderer.ApplyAnimationState(CustomSkinAnimationState.Idle, true);
            renderer.ApplyAnimationState(CustomSkinAnimationState.Hidden, true);
            Assert.False(renderer.HasActiveAnimations);
            Assert.Null(renderer.DesiredFrameRate);
            AssertTransformsReset(renderer);
        });
    }

    [Fact]
    public void ZeroAnimationIntensities_CreateNoTracks()
    {
        WpfTestThread.Run(() =>
        {
            var renderer = CreateRenderer(
                AllSlots,
                theme => theme with
                {
                    Animation = new SkinAnimationSettings(0, 0, 0, 0)
                });

            Assert.Equal(0, renderer.AnimationTrackCount);
            renderer.ApplyAnimationState(CustomSkinAnimationState.Refreshing, true);
            Assert.False(renderer.HasActiveAnimations);
            Assert.Null(renderer.DesiredFrameRate);
        });
    }

    [Theory]
    [InlineData(AnimationChannel.Rotation)]
    [InlineData(AnimationChannel.Breathing)]
    [InlineData(AnimationChannel.Glow)]
    [InlineData(AnimationChannel.Floating)]
    public void SoleNonzeroAnimationChannel_AnimatesOnlyItsDependencyPropertyAndResets(
        AnimationChannel channel)
    {
        WpfTestThread.Run(() =>
        {
            foreach (var stopWithHidden in new[] { false, true })
            {
                var renderer = CreateRenderer(
                    AllSlots,
                    theme => theme with
                    {
                        Animation = CreateAnimationSettings(channel, 1, 0)
                    });

                Assert.Equal(1, renderer.AnimationTrackCount);
                renderer.ApplyAnimationState(CustomSkinAnimationState.Refreshing, true);
                AssertAnimationSources(renderer, channel, expectedAnimated: true);
                Assert.False(DependencyPropertyHelper
                    .GetValueSource(
                        renderer.PrimaryProgress,
                        UIElement.OpacityProperty)
                    .IsAnimated);

                if (stopWithHidden)
                {
                    renderer.ApplyAnimationState(CustomSkinAnimationState.Hidden, true);
                }
                else
                {
                    renderer.ApplyAnimationState(CustomSkinAnimationState.Refreshing, false);
                }

                AssertAnimationSources(renderer, null, expectedAnimated: false);
                AssertAnimationBaseValues(renderer);
            }
        });
    }

    [Theory]
    [InlineData(AnimationChannel.Rotation)]
    [InlineData(AnimationChannel.Breathing)]
    [InlineData(AnimationChannel.Glow)]
    [InlineData(AnimationChannel.Floating)]
    public void SoleZeroAnimationChannel_CreatesTheOtherThreeCorrectTracks(
        AnimationChannel zeroChannel)
    {
        WpfTestThread.Run(() =>
        {
            var renderer = CreateRenderer(
                AllSlots,
                theme => theme with
                {
                    Animation = CreateAnimationSettings(zeroChannel, 0, 1)
                });

            Assert.Equal(3, renderer.AnimationTrackCount);
            renderer.ApplyAnimationState(CustomSkinAnimationState.Idle, true);
            AssertAnimationSources(renderer, zeroChannel, expectedAnimated: false);
        });
    }

    private static readonly SkinAssetSlot[] AllSlots =
    [
        SkinAssetSlot.Background,
        SkinAssetSlot.Center,
        SkinAssetSlot.Decoration
    ];

    private static FreeDecorationRingRenderer CreateRenderer(
        IEnumerable<SkinAssetSlot> slots,
        Func<SkinTheme, SkinTheme>? transformTheme = null) =>
        Assert.IsType<FreeDecorationRingRenderer>(
            new FreeDecorationRingTemplate().CreateRenderer(
                FreeDecorationRingRendererTests.CreateDocument(slots, transformTheme)));

    private static void AssertTransform(
        FrameworkElement element,
        double x,
        double y,
        double scale,
        double rotation,
        double opacity)
    {
        var group = Assert.IsType<TransformGroup>(element.RenderTransform);
        var scaleTransform = Assert.IsType<ScaleTransform>(group.Children[0]);
        var rotateTransform = Assert.IsType<RotateTransform>(group.Children[1]);
        var translateTransform = Assert.IsType<TranslateTransform>(group.Children[2]);
        Assert.Equal(scale, scaleTransform.ScaleX);
        Assert.Equal(scale, scaleTransform.ScaleY);
        Assert.Equal(rotation, rotateTransform.Angle);
        Assert.Equal(x, translateTransform.X);
        Assert.Equal(y, translateTransform.Y);
        Assert.Equal(opacity, element.Opacity);
    }

    private static void AssertCrop(Brush fill, double x, double y)
    {
        var brush = Assert.IsType<ImageBrush>(fill);
        Assert.Equal(x, brush.Viewbox.X);
        Assert.Equal(y, brush.Viewbox.Y);
    }

    private static void AssertTransformsReset(FreeDecorationRingRenderer renderer)
    {
        AssertTransform(renderer.BackgroundImage, 0, 0, 1, 0, 1);
        AssertTransform(renderer.CenterImage, 0, 0, 1, 0, 1);
        AssertTransform(renderer.DecorationImage, 0, 0, 1, 0, 1);
        Assert.Equal(0, renderer.AnimatedGlow.Opacity);
    }

    private static SkinAnimationSettings CreateAnimationSettings(
        AnimationChannel selectedChannel,
        double selectedValue,
        double otherValue) =>
        new(
            selectedChannel == AnimationChannel.Rotation ? selectedValue : otherValue,
            selectedChannel == AnimationChannel.Breathing ? selectedValue : otherValue,
            selectedChannel == AnimationChannel.Glow ? selectedValue : otherValue,
            selectedChannel == AnimationChannel.Floating ? selectedValue : otherValue);

    private static void AssertAnimationSources(
        FreeDecorationRingRenderer renderer,
        AnimationChannel? selectedChannel,
        bool expectedAnimated)
    {
        foreach (var (channel, properties) in GetAnimationProperties(renderer))
        {
            var expected = selectedChannel is null
                ? expectedAnimated
                : channel == selectedChannel
                    ? expectedAnimated
                    : !expectedAnimated;
            foreach (var property in properties)
            {
                Assert.Equal(
                    expected,
                    DependencyPropertyHelper
                        .GetValueSource(property.Target, property.Property)
                        .IsAnimated);
            }
        }
    }

    private static void AssertAnimationBaseValues(
        FreeDecorationRingRenderer renderer)
    {
        foreach (var (_, properties) in GetAnimationProperties(renderer))
        {
            foreach (var property in properties)
            {
                Assert.Equal(
                    property.BaseValue,
                    Assert.IsType<double>(property.Target.GetValue(property.Property)));
            }
        }
    }

    private static IReadOnlyDictionary<AnimationChannel, AnimationProperty[]>
        GetAnimationProperties(FreeDecorationRingRenderer renderer)
    {
        var decoration = Assert.IsType<TransformGroup>(
            renderer.DecorationImage.RenderTransform);
        var center = Assert.IsType<TransformGroup>(
            renderer.CenterImage.RenderTransform);
        var decorationRotate = Assert.IsType<RotateTransform>(
            decoration.Children[1]);
        var decorationTranslate = Assert.IsType<TranslateTransform>(
            decoration.Children[2]);
        var centerScale = Assert.IsType<ScaleTransform>(center.Children[0]);

        return new Dictionary<AnimationChannel, AnimationProperty[]>
        {
            [AnimationChannel.Rotation] =
            [
                new AnimationProperty(
                    decorationRotate,
                    RotateTransform.AngleProperty,
                    0)
            ],
            [AnimationChannel.Breathing] =
            [
                new AnimationProperty(
                    centerScale,
                    ScaleTransform.ScaleXProperty,
                    1),
                new AnimationProperty(
                    centerScale,
                    ScaleTransform.ScaleYProperty,
                    1)
            ],
            [AnimationChannel.Glow] =
            [
                new AnimationProperty(
                    renderer.AnimatedGlow,
                    UIElement.OpacityProperty,
                    0)
            ],
            [AnimationChannel.Floating] =
            [
                new AnimationProperty(
                    decorationTranslate,
                    TranslateTransform.YProperty,
                    0)
            ]
        };
    }

    public enum AnimationChannel
    {
        Rotation,
        Breathing,
        Glow,
        Floating
    }

    private sealed record AnimationProperty(
        DependencyObject Target,
        DependencyProperty Property,
        double BaseValue);
}
