using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexQuotaHud.App.Preview;
using CodexQuotaHud.App.UI;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Models;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Templates.FreeDecorationRing;
using ShapeEllipse = System.Windows.Shapes.Ellipse;
using ShapeLine = System.Windows.Shapes.Line;
using ShapeRectangle = System.Windows.Shapes.Rectangle;

namespace CodexQuotaHud.App.Tests.Preview;

[Collection(PreviewWpfCollection.Name)]
public sealed class SyntheticPreviewCompositionTests
{
    [Fact]
    public void DesignerGuides_DefaultCollapsedAndMapSharedGeometry()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            var overlay = Assert.IsType<Grid>(
                composition.HudWindow.FindName("DesignerGuideOverlay"));

            Assert.Equal(Visibility.Collapsed, overlay.Visibility);
            Assert.False(overlay.IsHitTestVisible);

            var theme = CreateTheme() with
            {
                Center = IdentityTransform() with
                {
                    OffsetX = 7,
                    OffsetY = -5,
                    Scale = 1.2
                },
                TextOffsetY = 3,
                TextLineGap = 8,
                Animation = new SkinAnimationSettings(0, 0.75, 0, 0)
            };
            var expected = FreeDecorationRingGeometry.CalculateGuideGeometry(
                theme);

            composition.SetDesignerGuides(theme, visible: true);

            Assert.Equal(Visibility.Visible, overlay.Visibility);
            Assert.False(overlay.IsHitTestVisible);
            var primary = Assert.IsType<ShapeEllipse>(
                composition.HudWindow.FindName("DesignerGuidePrimaryRing"));
            var secondary = Assert.IsType<ShapeEllipse>(
                composition.HudWindow.FindName("DesignerGuideSecondaryRing"));
            Assert.Equal(expected.PrimaryDiameter, primary.Width);
            Assert.Equal(expected.PrimaryDiameter, primary.Height);
            Assert.Equal(expected.SecondaryDiameter, secondary.Width);
            Assert.Equal(expected.SecondaryDiameter, secondary.Height);

            var center = Assert.IsType<ShapeRectangle>(
                composition.HudWindow.FindName("DesignerGuideCenterPeak"));
            Assert.Equal(expected.CenterPeakSize, center.Width);
            Assert.Equal(expected.CenterPeakSize, center.Height);
            var centerOffset = Assert.IsType<TranslateTransform>(
                center.RenderTransform);
            Assert.Equal(expected.CenterPeakOffsetX, centerOffset.X);
            Assert.Equal(expected.CenterPeakOffsetY, centerOffset.Y);

            var numberLine = Assert.IsType<ShapeLine>(
                composition.HudWindow.FindName("DesignerGuideNumberLine"));
            var labelLine = Assert.IsType<ShapeLine>(
                composition.HudWindow.FindName("DesignerGuideLabelLine"));
            Assert.Equal(
                expected.Text.NumberY,
                Assert.IsType<TranslateTransform>(numberLine.RenderTransform).Y);
            Assert.Equal(
                expected.Text.LabelY,
                Assert.IsType<TranslateTransform>(labelLine.RenderTransform).Y);

            composition.SetDesignerGuides(theme, visible: false);

            Assert.Equal(Visibility.Collapsed, overlay.Visibility);
            Assert.Equal(0, primary.Width);
            Assert.Equal(0, secondary.Width);
            Assert.Equal(0, center.Width);
        });
    }

    [Fact]
    public void PublicComposition_DoesNotCreateAMonitorBackedWindowHandle()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            Assert.Equal(
                IntPtr.Zero,
                new WindowInteropHelper(composition.HudWindow).Handle);
        });
    }

    [Fact]
    public void Composition_RendersProductionOutputAcrossSyntheticStates()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });

            var package = CreatePackage();
            var result = composition.SetCustomPackage(package);

            Assert.True(result.IsValid, Format(result.Errors));
            Assert.Same(package, result.Value);
            var customSkin = Assert.IsType<CustomQuotaSkin>(
                composition.HudWindow.ActiveSyntheticSkin);
            var renderer = Assert.IsType<FreeDecorationRingRenderer>(
                customSkin.View);
            Assert.Same(renderer, composition.HudWindow.SkinHost.Content);
            Assert.Equal("68%", renderer.QuotaNumber.Text);
            Assert.Equal("5 小时", renderer.QuotaLabel.Text);
            Assert.Equal(Visibility.Visible,
                renderer.SecondaryProgress.Visibility);
            Assert.Equal(68 * 3.6, renderer.PrimaryProgress.SweepAngle);
            Assert.Equal(34 * 3.6, renderer.SecondaryProgress.SweepAngle);

            composition.ShowHud();
            composition.Session.Apply(SyntheticPreviewState.Default with
            {
                DisplayChoice = PreviewDisplayChoice.FiveHourOnly,
                FiveHourPercent = 19,
                IsRefreshing = true,
                DetailsOpen = true
            });
            Assert.Equal("19%", renderer.QuotaNumber.Text);
            Assert.Equal("5 小时", renderer.QuotaLabel.Text);
            Assert.Equal(Visibility.Collapsed,
                renderer.SecondaryProgress.Visibility);
            Assert.Equal(
                QuotaAlertPalette.WarningMediaColor,
                Assert.IsType<SolidColorBrush>(renderer.PrimaryProgress.Stroke)
                    .Color);
            Assert.True(composition.HudWindow.DetailsPopup.IsOpen);
            Assert.Single(composition.HudWindow.DetailsItems.Items);
            Assert.True(renderer.HasActiveAnimations);
            Assert.Equal(24, renderer.DesiredFrameRate);

            composition.Session.Apply(SyntheticPreviewState.Default with
            {
                DisplayChoice = PreviewDisplayChoice.WeeklyOnly,
                WeeklyPercent = 9,
                IsRefreshing = false,
                DetailsOpen = false
            });
            Assert.Equal("9%", renderer.QuotaNumber.Text);
            Assert.Equal("每周", renderer.QuotaLabel.Text);
            Assert.Equal(
                QuotaAlertPalette.CriticalMediaColor,
                Assert.IsType<SolidColorBrush>(renderer.PrimaryProgress.Stroke)
                    .Color);
            Assert.False(composition.HudWindow.DetailsPopup.IsOpen);

            composition.Session.Apply(SyntheticPreviewState.Default with
            {
                FiveHourPercent = 19,
                WeeklyPercent = 9
            });
            Assert.Equal(Visibility.Visible,
                renderer.SecondaryProgress.Visibility);
            Assert.Equal(
                QuotaAlertPalette.WarningMediaColor,
                Assert.IsType<SolidColorBrush>(renderer.PrimaryProgress.Stroke)
                    .Color);
            Assert.Equal(
                QuotaAlertPalette.CriticalMediaColor,
                Assert.IsType<SolidColorBrush>(renderer.SecondaryProgress.Stroke)
                    .Color);

            composition.Session.Apply(SyntheticPreviewState.Default with
            {
                DisplayChoice = PreviewDisplayChoice.NoQuota,
                DetailsOpen = true
            });
            Assert.Equal(Visibility.Collapsed,
                renderer.PrimaryProgress.Visibility);
            Assert.Equal(Visibility.Collapsed,
                renderer.SecondaryProgress.Visibility);
            Assert.False(composition.HudWindow.IsVisible);
            Assert.False(composition.HudWindow.DetailsPopup.IsOpen);
            Assert.False(renderer.HasActiveAnimations);
        });
    }

    [Fact]
    public void WorkAreaUpdate_DoesNotPreventQuotaReturningAfterNoQuota()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            composition.ShowHud();
            composition.SetPreviewWorkArea(new Rect(100, 100, 600, 500));

            composition.Session.SetDisplayChoice(PreviewDisplayChoice.NoQuota);
            Assert.False(composition.HudWindow.IsVisible);

            composition.Session.SetDisplayChoice(PreviewDisplayChoice.Dual);

            Assert.True(composition.HudWindow.IsVisible);
        });
    }

    [Fact]
    public void SwitchingFromSyntheticToBuiltIn_RendersLatestQuotaState()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            Assert.True(composition.SetCustomPackage(CreatePackage()).IsValid);

            composition.Session.Apply(SyntheticPreviewState.Default with
            {
                DisplayChoice = PreviewDisplayChoice.WeeklyOnly,
                FiveHourPercent = 73,
                WeeklyPercent = 17,
                IsRefreshing = true,
                DetailsOpen = true
            });

            Assert.True(composition.Session.SetBuiltInSkin(SkinId.HudDial));
            var builtIn = Assert.IsType<HudDialSkin>(
                composition.HudWindow.SkinHost.Content);
            Assert.Equal("17%", builtIn.PercentText.Text);
            Assert.Equal("每周", builtIn.LabelText.Text);
            Assert.Equal("SYNC", builtIn.ModeText.Text);
            Assert.Equal(17d, builtIn.PrimaryArc.Progress);
            Assert.Equal(Visibility.Collapsed,
                builtIn.SecondaryArc.Visibility);
        });
    }

    [Fact]
    public void PreviewWorkArea_BoundsAllProductionEdgePositions()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            var area = new Rect(400, 200, 520, 420);
            composition.SetPreviewWorkArea(area);
            composition.HudWindow.Left = 560;
            composition.HudWindow.Top = 300;

            foreach (var side in new[]
                     {
                         EdgeDockSide.Left,
                         EdgeDockSide.Right,
                         EdgeDockSide.Top,
                         EdgeDockSide.Bottom
                     })
            {
                composition.Session.PreviewEdge(side);
                AssertVisibleHandleInside(
                    composition.HudWindow,
                    area,
                    side);

                composition.Session.ForceExpanded();
                AssertWindowInside(composition.HudWindow, area);
            }
        });
    }

    [Fact]
    public void SameIdUpdate_ReplacesRendererAndPreservesQuotaState()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            composition.Session.Apply(SyntheticPreviewState.Default with
            {
                FiveHourPercent = 33,
                WeeklyPercent = 22
            });
            Assert.True(composition.SetCustomPackage(CreatePackage()).IsValid);
            var first = composition.HudWindow.SkinHost.Content;

            var updated = CreatePackage() with
            {
                Theme = CreateTheme() with
                {
                    PrimaryRingColor = "#FF00FF00"
                }
            };
            var result = composition.SetCustomPackage(updated);

            Assert.True(result.IsValid, Format(result.Errors));
            Assert.NotSame(first, composition.HudWindow.SkinHost.Content);
            var viewModel = Assert.IsType<QuotaOrbViewModel>(
                composition.HudWindow.DataContext);
            Assert.Equal(33, viewModel.PrimaryPercent);
            Assert.Equal(22, viewModel.SecondaryPercent);
        });
    }

    [Fact]
    public void InvalidUpdate_LeavesLastRendererAndPresentationExact()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            Assert.True(composition.SetCustomPackage(CreatePackage()).IsValid);
            var renderer = composition.HudWindow.SkinHost.Content;
            var presentation = composition.HudWindow.ActiveSyntheticPresentation;
            var invalid = CreatePackage() with
            {
                Theme = CreateTheme() with { RingThickness = 17 }
            };

            var result = composition.SetCustomPackage(invalid);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors,
                error => error.Code == "number.out-of-range" &&
                    error.Location == "$.ringThickness");
            Assert.Same(renderer, composition.HudWindow.SkinHost.Content);
            Assert.Same(presentation,
                composition.HudWindow.ActiveSyntheticPresentation);
        });
    }

    [Fact]
    public void ProductionAnimations_RespectGlobalSwitchAndZeroIntensities()
    {
        RunSta(() =>
        {
            using var composition = new SyntheticPreviewComposition(
                Dispatcher.CurrentDispatcher,
                () => { });
            Assert.True(composition.SetCustomPackage(CreatePackage()).IsValid);
            composition.ShowHud();
            var activeRenderer = Assert.IsType<FreeDecorationRingRenderer>(
                composition.HudWindow.SkinHost.Content);

            Assert.True(activeRenderer.HasActiveAnimations);
            Assert.Equal(4, activeRenderer.DesiredFrameRate);
            composition.Session.SetAnimationsEnabled(false);
            Assert.Equal(4, activeRenderer.AnimationTrackCount);
            Assert.False(activeRenderer.HasActiveAnimations);
            composition.Session.SetAnimationsEnabled(true);
            Assert.Equal(4, activeRenderer.AnimationTrackCount);
            Assert.True(activeRenderer.HasActiveAnimations);
            Assert.Equal(4, activeRenderer.DesiredFrameRate);
            composition.Session.SetRefreshing(true);
            Assert.True(activeRenderer.HasActiveAnimations);
            Assert.Equal(24, activeRenderer.DesiredFrameRate);

            var still = CreatePackage() with
            {
                Theme = CreateTheme() with
                {
                    Animation = new SkinAnimationSettings(0, 0, 0, 0)
                }
            };
            Assert.True(composition.SetCustomPackage(still).IsValid);
            var stillRenderer = Assert.IsType<FreeDecorationRingRenderer>(
                composition.HudWindow.SkinHost.Content);

            Assert.Equal(0, stillRenderer.AnimationTrackCount);
            Assert.False(stillRenderer.HasActiveAnimations);
            Assert.Null(stillRenderer.DesiredFrameRate);
        });
    }

    [Fact]
    public void Factory_RejectsUntrustedDecodedAssetMetadata()
    {
        RunSta(() =>
        {
            var content = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/j//z8DAAj8Av6IXwbgAAAAAElFTkSuQmCC");
            const string path = "assets/decoration.png";
            var asset = new SkinAsset(
                SkinAssetSlot.Decoration,
                path,
                content,
                PixelWidth: 2,
                PixelHeight: 1,
                HasAlpha: true);
            var package = CreatePackage() with
            {
                Manifest = CreatePackage().Manifest with
                {
                    Assets =
                    [
                        new SkinAssetReference(
                            SkinAssetSlot.Decoration,
                            path,
                            Convert.ToHexString(SHA256.HashData(content))
                                .ToLowerInvariant())
                    ]
                },
                Assets = new Dictionary<SkinAssetSlot, SkinAsset>
                {
                    [SkinAssetSlot.Decoration] = asset
                }
            };

            var result = TransientCustomSkinFactory.Create(package);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors,
                error => error.Code == "preview.asset.dimensions" &&
                    error.Location == "$.assets[0]");
        });
    }

    [Fact]
    public void Composition_LeavesFormalSettingsSentinelByteExact()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"CodexQuotaHud-SyntheticIsolation-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "settings.json");
        var sentinel = new byte[] { 0x7B, 0x22, 0x73, 0x65, 0x6E, 0x74, 0x69,
            0x6E, 0x65, 0x6C, 0x22, 0x3A, 0x74, 0x72, 0x75, 0x65, 0x7D };
        File.WriteAllBytes(path, sentinel);
        var timestamp = DateTime.UtcNow.AddDays(-2);
        File.SetLastWriteTimeUtc(path, timestamp);
        var exactTimestamp = File.GetLastWriteTimeUtc(path);
        try
        {
            var formalStoreConstructorCount = -1;
            RunSta(() =>
            {
                using var formalSettingsScope =
                    SettingsStore.OverrideDefaultPathForTests(path);
                Assert.Throws<InvalidOperationException>(() =>
                    SettingsStore.OverrideDefaultPathForTests(path));
                using (var composition = new SyntheticPreviewComposition(
                           Dispatcher.CurrentDispatcher,
                           () => { }))
                {
                    Assert.True(composition.SetCustomPackage(CreatePackage())
                        .IsValid);
                    composition.SetPreviewWorkArea(
                        new Rect(100, 100, 600, 500));
                    composition.Session.Apply(SyntheticPreviewState.Default with
                    {
                        DisplayChoice = PreviewDisplayChoice.WeeklyOnly,
                        FiveHourPercent = 10,
                        WeeklyPercent = 21,
                        AnimationsEnabled = false,
                        IsRefreshing = true,
                        DetailsOpen = true,
                        EdgeSide = EdgeDockSide.Right
                    });
                    Assert.True(composition.Session.SetBuiltInSkin(
                        SkinId.EnergyRing));
                    Assert.True(composition.SetCustomPackage(CreatePackage())
                        .IsValid);
                    composition.Session.SetAnimationsEnabled(true);
                    composition.Session.SetRefreshing(false);
                    composition.Session.SetDetailsOpen(false);
                    composition.Session.ForceExpanded();
                    Assert.Equal(
                        SkinSelectionKey.FromBuiltIn(SkinId.EnergyRing),
                        composition.CurrentInMemorySettings.SelectedSkinKey);
                }

                formalStoreConstructorCount =
                    formalSettingsScope.ConstructionCount;
            });

            Assert.Equal(0, formalStoreConstructorCount);
            RunSta(() =>
            {
                using var resetScope =
                    SettingsStore.OverrideDefaultPathForTests(path);
                Assert.Equal(0, resetScope.ConstructionCount);
            });
            Assert.Equal(sentinel, File.ReadAllBytes(path));
            Assert.Equal(exactTimestamp, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    internal static SkinPackageDocument CreatePackage() => new(
        new SkinManifest(
            1,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Ocean",
            "Alice",
            SemanticVersion.Parse("1.2.3"),
            "Ocean ring",
            SkinPackageLimits.FreeDecorationRingTemplateId,
            SemanticVersion.Parse("1.1.1"),
            null,
            []),
        CreateTheme(),
        new Dictionary<SkinAssetSlot, SkinAsset>());

    internal static SkinTheme CreateTheme() => new(
        1,
        SkinPackageLimits.FreeDecorationRingTemplateId,
        IdentityTransform(),
        IdentityTransform(),
        IdentityTransform(),
        "#FF53DCF8",
        "#FF9A68FF",
        "#FF0A1622",
        0.9,
        96,
        8,
        6,
        270,
        "#FF24CFF2",
        0.5,
        28,
        12,
        SkinTextWeight.SemiBold,
        SkinTextPlacement.NumberAboveLabel,
        new SkinAnimationSettings(0.25, 0.5, 0.75, 1));

    private static SkinImageTransform IdentityTransform() =>
        new(0, 0, 1, 0, 1, 0.5, 0.5);

    private static void AssertVisibleHandleInside(
        Window window,
        Rect area,
        EdgeDockSide side)
    {
        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
        var visible = EdgeAutoHideGeometry.VisibleHandleWidth;
        switch (side)
        {
            case EdgeDockSide.Left:
                Assert.Equal(area.Left + visible, window.Left + width, 3);
                Assert.InRange(window.Top, area.Top, area.Bottom - height);
                break;
            case EdgeDockSide.Right:
                Assert.Equal(area.Right - visible, window.Left, 3);
                Assert.InRange(window.Top, area.Top, area.Bottom - height);
                break;
            case EdgeDockSide.Top:
                Assert.Equal(area.Top + visible, window.Top + height, 3);
                Assert.InRange(window.Left, area.Left, area.Right - width);
                break;
            case EdgeDockSide.Bottom:
                Assert.Equal(area.Bottom - visible, window.Top, 3);
                Assert.InRange(window.Left, area.Left, area.Right - width);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(side));
        }
    }

    private static void AssertWindowInside(Window window, Rect area)
    {
        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;
        Assert.InRange(window.Left, area.Left, area.Right - width);
        Assert.InRange(window.Top, area.Top, area.Bottom - height);
    }

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join("; ", errors.Select(error =>
            $"{error.Code}@{error.Location}"));

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
}
