using System.Windows;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.App.Tests.UI;

public sealed class SkinControllerTests
{
    [Fact]
    public void BuiltInController_ResolvesEverySkinAndDefaultsToHudDial() =>
        RunSta(() =>
        {
            var controller = new SkinController();

            Assert.Equal(SkinId.HudDial, controller.CurrentSkin.Id);
            Assert.Equal(
                Enum.GetValues<SkinId>().Order(),
                controller.RegisteredIds.Order());

            foreach (var id in Enum.GetValues<SkinId>())
            {
                Assert.Equal(id, controller.Select(id).Id);
            }
        });

    [Fact]
    public void InvalidSelection_FallsBackToHudDial() =>
        RunSta(() =>
        {
            var controller = new SkinController();

            var selected = controller.Select((SkinId)999);

            Assert.Equal(SkinId.HudDial, selected.Id);
        });

    [Fact]
    public void Select_SwapsViewImmediatelyAndRendersLastState() =>
        RunSta(() =>
        {
            var skins = Enum.GetValues<SkinId>()
                .ToDictionary(id => id, id => new RecordingSkin(id));
            var hud = skins[SkinId.HudDial];
            var tank = skins[SkinId.LiquidTank];
            var controller = new SkinController(
                skins.ToDictionary(
                    pair => pair.Key,
                    pair => (Func<IQuotaSkin>)(() => pair.Value)),
                SkinId.HudDial);
            var state = new QuotaSkinState(
                67,
                82,
                "5 小时",
                QuotaDisplayMode.Dual,
                IsRefreshing: false,
                AnimationsEnabled: true);
            controller.Render(state);

            var selected = controller.Select(SkinId.LiquidTank);

            Assert.Same(tank, selected);
            Assert.Same(tank.View, controller.CurrentView);
            Assert.Equal(state, tank.LastState);
        });

    [Fact]
    public void QuotaSkinState_SingleModeRemovesSecondaryAndSanitizesPercentages()
    {
        var state = new QuotaSkinState(
            double.NaN,
            140,
            "每周",
            QuotaDisplayMode.Single,
            IsRefreshing: false,
            AnimationsEnabled: true);

        Assert.Equal(0, state.PrimaryPercent);
        Assert.Null(state.SecondaryPercent);

        var dual = state with
        {
            PrimaryPercent = -10,
            SecondaryPercent = double.PositiveInfinity,
            Mode = QuotaDisplayMode.Dual
        };
        Assert.Equal(0, dual.PrimaryPercent);
        Assert.Equal(0, dual.SecondaryPercent);
    }

    [Fact]
    public void QuotaSkinState_DerivesIndependentAlertLevels()
    {
        var dual = new QuotaSkinState(
            9,
            75,
            "5 灏忔椂",
            QuotaDisplayMode.Dual,
            IsRefreshing: false,
            AnimationsEnabled: true);

        Assert.Equal(QuotaAlertLevel.Critical, dual.PrimaryAlert);
        Assert.Equal(QuotaAlertLevel.Normal, dual.SecondaryAlert);

        var single = dual with
        {
            PrimaryPercent = 20,
            Mode = QuotaDisplayMode.Single
        };
        Assert.Equal(QuotaAlertLevel.Warning, single.PrimaryAlert);
        Assert.Null(single.SecondaryAlert);
    }

    [Fact]
    public void BuiltInSkins_AcceptDualAndSingleStatesAndReleaseAnimations() =>
        RunSta(() =>
        {
            var controller = new SkinController();
            var dual = new QuotaSkinState(
                61,
                84,
                "5 小时",
                QuotaDisplayMode.Dual,
                IsRefreshing: false,
                AnimationsEnabled: true);
            var single = new QuotaSkinState(
                84,
                null,
                "每周",
                QuotaDisplayMode.Single,
                IsRefreshing: true,
                AnimationsEnabled: true);

            foreach (var id in Enum.GetValues<SkinId>())
            {
                var skin = controller.Select(id);
                controller.Render(dual);
                controller.Render(single);
                var target = Assert.IsAssignableFrom<IOrbAnimationTarget>(skin);
                target.ApplyAnimationState(
                    OrbAnimationState.Idle,
                    animationsEnabled: true);
                target.ApplyAnimationState(
                    OrbAnimationState.Refreshing,
                    animationsEnabled: true);
                target.ApplyAnimationState(
                    OrbAnimationState.Hidden,
                    animationsEnabled: true);
                Assert.Equal(132, skin.View.Width);
                Assert.Equal(132, skin.View.Height);
            }
        });

    [Fact]
    public void BuiltInSkins_CapIdleAndRefreshingAnimationFrameRates() =>
        RunSta(() =>
        {
            var controller = new SkinController();

            foreach (var id in Enum.GetValues<SkinId>())
            {
                var skin = Assert.IsAssignableFrom<AnimatedQuotaSkin>(
                    controller.Select(id));

                skin.ApplyAnimationState(
                    OrbAnimationState.Idle,
                    animationsEnabled: true);
                Assert.NotEmpty(skin.ConfiguredFrameRates);
                Assert.All(
                    skin.ConfiguredFrameRates,
                    frameRate => Assert.Equal(4, frameRate));

                skin.ApplyAnimationState(
                    OrbAnimationState.Refreshing,
                    animationsEnabled: true);
                Assert.All(
                    skin.ConfiguredFrameRates,
                    frameRate => Assert.Equal(24, frameRate));

                skin.ApplyAnimationState(
                    OrbAnimationState.Hidden,
                    animationsEnabled: true);
            }
        });

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

    private sealed class RecordingSkin(SkinId id) : IQuotaSkin
    {
        public SkinId Id { get; } = id;

        public FrameworkElement View { get; } = new FrameworkElement();

        public QuotaSkinState? LastState { get; private set; }

        public void Render(QuotaSkinState state) => LastState = state;
    }
}
