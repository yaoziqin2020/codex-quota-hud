using System.Windows;
using CodexQuotaHud.App.UI.Animation;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;

namespace CodexQuotaHud.App.Tests.UI;

[Collection(WpfUiCollection.Name)]
public sealed class SkinControllerTests
{
    private const string CustomKey =
        "custom:11111111-1111-1111-1111-111111111111";

    [Fact]
    public void TryPrepare_CustomCandidateDoesNotSwapUntilActivate() =>
        RunSta(() =>
        {
            var catalog = CatalogWithCustom();
            var hud = new RecordingSkin(SkinId.HudDial);
            var custom = new RecordingSkin(CustomKey);
            var controller = new SkinController(
                catalog,
                descriptor => descriptor.SelectionKey == CustomKey ? custom : hud,
                SkinSelectionKey.HudDial);
            var before = controller.CurrentSkin;

            Assert.True(controller.TryPrepare(
                CustomKey,
                out var candidate,
                out var failure));

            Assert.Null(failure);
            Assert.NotNull(candidate);
            Assert.Same(custom, candidate.Skin);
            Assert.Same(before, controller.CurrentSkin);
            Assert.Equal(SkinSelectionKey.HudDial, controller.CurrentDescriptor.SelectionKey);

            controller.Activate(candidate);

            Assert.Same(custom, controller.CurrentSkin);
            Assert.Equal(CustomKey, controller.CurrentDescriptor.SelectionKey);
            Assert.Equal(CustomKey, controller.CurrentPresentation.Popup.Decoration == PopupDecorationKind.Custom
                ? controller.CurrentDescriptor.SelectionKey
                : string.Empty);
        });

    [Fact]
    public void TryPrepare_MissingOrFactoryFailurePreservesExactActiveInstance() =>
        RunSta(() =>
        {
            var catalog = CatalogWithCustom();
            var hud = new RecordingSkin(SkinId.HudDial);
            var controller = new SkinController(
                catalog,
                descriptor => descriptor.SelectionKey == CustomKey
                    ? throw new InvalidOperationException("renderer failed")
                    : hud,
                SkinSelectionKey.HudDial);
            var beforeDescriptor = controller.CurrentDescriptor;
            var beforeSkin = controller.CurrentSkin;

            Assert.False(controller.TryPrepare(
                "custom:99999999-9999-9999-9999-999999999999",
                out _,
                out var missing));
            Assert.Equal("skin.selection.missing", missing!.ErrorCode);
            Assert.Same(beforeSkin, controller.CurrentSkin);
            Assert.Same(beforeDescriptor, controller.CurrentDescriptor);

            Assert.False(controller.TryPrepare(CustomKey, out _, out var factory));
            Assert.Equal("skin.selection.factory", factory!.ErrorCode);
            Assert.Equal("Ocean", factory.DisplayNameOrId);
            Assert.DoesNotContain(@"C:\", factory.DisplayNameOrId, StringComparison.Ordinal);
            Assert.Same(beforeSkin, controller.CurrentSkin);
            Assert.Same(beforeDescriptor, controller.CurrentDescriptor);
        });

    [Fact]
    public void Activate_RejectsCandidateFromAnotherCatalogGeneration() =>
        RunSta(() =>
        {
            var first = new SkinController(
                CatalogWithCustom(),
                descriptor => new RecordingSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            var second = new SkinController(
                CatalogWithCustom(),
                descriptor => new RecordingSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            Assert.True(first.TryPrepare(CustomKey, out var stale, out _));
            var before = second.CurrentSkin;

            Assert.Throws<InvalidOperationException>(() => second.Activate(stale!));

            Assert.Same(before, second.CurrentSkin);
            Assert.Equal(SkinSelectionKey.HudDial, second.CurrentDescriptor.SelectionKey);
        });

    [Fact]
    public void Activate_RendersLastStateOnCandidateBeforeSwapping() =>
        RunSta(() =>
        {
            var custom = new RecordingSkin(CustomKey);
            var controller = new SkinController(
                CatalogWithCustom(),
                descriptor => descriptor.SelectionKey == CustomKey
                    ? custom
                    : new RecordingSkin(descriptor.SelectionKey),
                SkinSelectionKey.HudDial);
            var state = new QuotaSkinState(
                68,
                34,
                "5 hours",
                QuotaDisplayMode.Dual,
                IsRefreshing: false,
                AnimationsEnabled: true);
            controller.Render(state);
            Assert.True(controller.TryPrepare(CustomKey, out var candidate, out _));

            Assert.Equal(state, custom.LastState);

            controller.Activate(candidate!);

            Assert.Same(custom.View, controller.CurrentView);
        });

    [Fact]
    public void BuiltInController_ResolvesEverySkinAndDefaultsToHudDial() =>
        RunSta(() =>
        {
            var controller = new SkinController();

            Assert.Equal(
                SkinId.HudDial,
                Assert.IsAssignableFrom<AnimatedQuotaSkin>(controller.CurrentSkin).Id);
            Assert.Equal(
                Enum.GetValues<SkinId>().Order(),
                controller.RegisteredIds.Order());

            foreach (var id in Enum.GetValues<SkinId>())
            {
                Assert.Equal(
                    id,
                    Assert.IsAssignableFrom<AnimatedQuotaSkin>(controller.Select(id)).Id);
            }
        });

    [Fact]
    public void InvalidSelection_FallsBackToHudDial() =>
        RunSta(() =>
        {
            var controller = new SkinController();

            var selected = controller.Select((SkinId)999);

            Assert.Equal(
                SkinId.HudDial,
                Assert.IsAssignableFrom<AnimatedQuotaSkin>(selected).Id);
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
                HudSkinCatalog.CreateBuiltInOnly(),
                descriptor => skins[descriptor.BuiltInId!.Value],
                SkinSelectionKey.HudDial);
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

    private static HudSkinCatalog CatalogWithCustom() =>
        new(new CodexQuotaHud.Skins.Storage.InstalledSkinCatalogResult(
            [new CodexQuotaHud.Skins.Storage.InstalledSkinRecord(
                CustomKey,
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "Ocean",
                CodexQuotaHud.Skins.Contracts.SemanticVersion.Parse("1.0.0"),
                @"C:\Catalog\11111111-1111-1111-1111-111111111111",
                HudSkinCatalogTests.Document())],
            []));

    private sealed class RecordingSkin : IQuotaSkin
    {
        public RecordingSkin(SkinId id)
            : this(SkinSelectionKey.FromBuiltIn(id))
        {
            Id = id;
        }

        public RecordingSkin(string selectionKey)
        {
            SelectionKey = selectionKey;
        }

        public SkinId? Id { get; }

        public string SelectionKey { get; }

        public FrameworkElement View { get; } = new FrameworkElement();

        public QuotaSkinState? LastState { get; private set; }

        public void Render(QuotaSkinState state) => LastState = state;
    }
}
