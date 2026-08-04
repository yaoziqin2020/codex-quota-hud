using System.Windows.Media;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Skins.Templates;

namespace CodexQuotaHud.App.Tests.UI;

[Collection(WpfUiCollection.Name)]
public sealed class CustomQuotaSkinTests
{
    private const string SelectionKey =
        "custom:11111111-1111-1111-1111-111111111111";

    [Theory]
    [MemberData(nameof(RenderCases))]
    public void Render_MapsQuotaModesRefreshAndIndependentProductAlerts(
        QuotaSkinState input,
        Color expectedPrimary,
        Color? expectedSecondary) =>
        RunSta(() =>
        {
            var renderer = new RecordingRenderer();
            var skin = new CustomQuotaSkin(
                SelectionKey,
                HudSkinCatalogTests.Theme(),
                renderer);

            skin.Render(input);

            var actual = Assert.IsType<CustomSkinRenderState>(renderer.LastState);
            Assert.Equal(input.PrimaryPercent, actual.PrimaryPercent);
            Assert.Equal(input.SecondaryPercent, actual.SecondaryPercent);
            Assert.Equal(input.PrimaryLabel, actual.PrimaryLabel);
            Assert.Equal(input.Mode, actual.Mode);
            Assert.Equal(input.IsRefreshing, actual.IsRefreshing);
            Assert.Equal(expectedPrimary, actual.PrimaryRingColor);
            Assert.Equal(expectedSecondary, actual.SecondaryRingColor);
            Assert.Equal(SelectionKey, skin.SelectionKey);
            Assert.Same(renderer, skin.View);
        });

    [Theory]
    [InlineData(OrbAnimationState.Hidden, CustomSkinAnimationState.Hidden, true)]
    [InlineData(OrbAnimationState.Idle, CustomSkinAnimationState.Idle, true)]
    [InlineData(OrbAnimationState.Refreshing, CustomSkinAnimationState.Refreshing, true)]
    [InlineData(OrbAnimationState.Idle, CustomSkinAnimationState.Idle, false)]
    public void ApplyAnimationState_MapsExactStateAndPreservesGlobalFlag(
        OrbAnimationState input,
        CustomSkinAnimationState expected,
        bool enabled) =>
        RunSta(() =>
        {
            var renderer = new RecordingRenderer();
            var skin = new CustomQuotaSkin(
                SelectionKey,
                HudSkinCatalogTests.Theme(),
                renderer);

            skin.ApplyAnimationState(input, enabled);

            Assert.Equal((expected, enabled), renderer.LastAnimation);
        });

    [Theory]
    [InlineData(0d)]
    [InlineData(2d)]
    [InlineData(4d)]
    public void AnimationSettings_ProvideHoldDurationAndAbsoluteRefreshSpeed(
        double refreshSpeedMultiplier)
        => RunSta(() =>
        {
            var renderer = new RecordingRenderer();
            var theme = HudSkinCatalogTests.Theme() with
            {
                Animation = HudSkinCatalogTests.Theme().Animation with
                {
                    RefreshSpeedMultiplier = refreshSpeedMultiplier,
                    RefreshHoldSeconds = 2.75d
                }
            };
            var skin = new CustomQuotaSkin(SelectionKey, theme, renderer);

            skin.ApplyAnimationState(OrbAnimationState.Refreshing, true);
            skin.ApplyAnimationState(OrbAnimationState.Refreshing, true);

            Assert.Equal(TimeSpan.FromSeconds(2.75d), skin.RefreshHoldDuration);
            Assert.Equal(
                [refreshSpeedMultiplier, refreshSpeedMultiplier],
                renderer.RefreshSpeedMultipliers);
        });

    [Fact]
    public void Adapter_WrapsAndRendersTheRealTask6Renderer() =>
        RunSta(() =>
        {
            var package = HudSkinCatalogTests.Document();
            Assert.True(SkinTemplateRegistry.CreateDefault().TryResolve(
                package.Manifest.TemplateId,
                package.Manifest.SchemaVersion,
                out var template));
            var renderer = template.CreateRenderer(package);
            var skin = new CustomQuotaSkin(
                SelectionKey,
                package.Theme,
                renderer);

            skin.Render(new QuotaSkinState(
                68,
                34,
                "5 hours",
                QuotaDisplayMode.Dual,
                IsRefreshing: false,
                AnimationsEnabled: true));
            skin.ApplyAnimationState(OrbAnimationState.Idle, true);

            Assert.Equal(132, skin.View.Width);
            Assert.Equal(132, skin.View.Height);
            Assert.True(renderer.HasActiveAnimations);
            Assert.Equal(4, renderer.DesiredFrameRate);

            skin.ApplyAnimationState(OrbAnimationState.Hidden, true);
            Assert.False(renderer.HasActiveAnimations);
            Assert.Null(renderer.DesiredFrameRate);
        });

    public static TheoryData<QuotaSkinState, Color, Color?> RenderCases => new()
    {
        {
            State(68, 34, QuotaDisplayMode.Dual, refreshing: false, animations: true),
            Parse("#FF123456"),
            Parse("#FF654321")
        },
        {
            State(21, null, QuotaDisplayMode.Single, refreshing: true, animations: true),
            Parse("#FF123456"),
            null
        },
        {
            State(20, null, QuotaDisplayMode.Single, refreshing: false, animations: false),
            Parse("#FFFFB547"),
            null
        },
        {
            State(0, null, QuotaDisplayMode.Hidden, refreshing: false, animations: true),
            Parse("#FFFF5A67"),
            null
        },
        {
            State(11, 10, QuotaDisplayMode.Dual, refreshing: false, animations: true),
            Parse("#FFFFB547"),
            Parse("#FFFF5A67")
        },
        {
            State(10, 21, QuotaDisplayMode.Dual, refreshing: false, animations: true),
            Parse("#FFFF5A67"),
            Parse("#FF654321")
        }
    };

    private static QuotaSkinState State(
        double primary,
        double? secondary,
        QuotaDisplayMode mode,
        bool refreshing,
        bool animations) =>
        new(
            primary,
            secondary,
            "5 hours",
            mode,
            refreshing,
            animations);

    private static Color Parse(string value) =>
        (Color)ColorConverter.ConvertFromString(value)!;

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    private sealed class RecordingRenderer : CustomSkinRenderer
    {
        public CustomSkinRenderState? LastState { get; private set; }

        public (CustomSkinAnimationState State, bool Enabled)? LastAnimation
        {
            get;
            private set;
        }

        public List<double> RefreshSpeedMultipliers { get; } = [];

        public override void Render(CustomSkinRenderState state) =>
            LastState = state;

        public override void ApplyAnimationState(
            CustomSkinAnimationState state,
            bool globalAnimationsEnabled,
            double refreshSpeedMultiplier = 2d)
        {
            LastAnimation = (state, globalAnimationsEnabled);
            RefreshSpeedMultipliers.Add(refreshSpeedMultiplier);
        }
    }
}
