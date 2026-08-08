using System.Collections;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Templates.FreeDecorationRing;
using CodexQuotaHud.Skins.Tests.Fixtures;

namespace CodexQuotaHud.Skins.Tests.Templates;

[Collection(WpfTestCollection.Name)]
public sealed class FreeDecorationRingRendererTests
{
    public static TheoryData<SkinAssetSlot[]> ImageSlotCombinations => new()
    {
        Array.Empty<SkinAssetSlot>(),
        new[] { SkinAssetSlot.Background },
        new[] { SkinAssetSlot.Center },
        new[] { SkinAssetSlot.Decoration },
        new[] { SkinAssetSlot.Background, SkinAssetSlot.Center },
        new[] { SkinAssetSlot.Background, SkinAssetSlot.Decoration },
        new[] { SkinAssetSlot.Center, SkinAssetSlot.Decoration },
        new[]
        {
            SkinAssetSlot.Background,
            SkinAssetSlot.Center,
            SkinAssetSlot.Decoration
        }
    };

    [Theory]
    [MemberData(nameof(ImageSlotCombinations))]
    public void Constructor_UsesOnlyFrozenOwnedImageBytes(SkinAssetSlot[] slots) =>
        WpfTestThread.Run(() =>
        {
            var renderer = Assert.IsType<FreeDecorationRingRenderer>(
                new FreeDecorationRingTemplate().CreateRenderer(CreateDocument(slots)));

            Assert.Equal(132, renderer.Width);
            Assert.Equal(132, renderer.Height);

            Assert.Equal(
                slots.Contains(SkinAssetSlot.Background),
                renderer.BackgroundImage.Fill is ImageBrush);
            Assert.Equal(
                slots.Contains(SkinAssetSlot.Center),
                renderer.CenterImage.Fill is ImageBrush);
            Assert.Equal(
                slots.Contains(SkinAssetSlot.Decoration),
                renderer.DecorationImage.Fill is ImageBrush);

            foreach (var brush in new[]
                     {
                         renderer.BackgroundImage.Fill,
                         renderer.CenterImage.Fill,
                         renderer.DecorationImage.Fill
                     }.OfType<ImageBrush>())
            {
                var bitmap = Assert.IsAssignableFrom<BitmapSource>(brush.ImageSource);
                Assert.True(bitmap.IsFrozen);
                Assert.True(brush.IsFrozen);
            }

            foreach (var value in EnumerateDependencyValues(renderer))
            {
                Assert.DoesNotContain("assets/", value?.ToString() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("C:\\", value?.ToString() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }
        });

    [Fact]
    public void Constructor_AppliesSharedTextLayoutToBothTextMargins() =>
        WpfTestThread.Run(() =>
        {
            var document = CreateDocument(
                transformTheme: theme => theme with
                {
                    TextPlacement = SkinTextPlacement.LabelAboveNumber,
                    TextOffsetY = -4,
                    TextLineGap = 6
                });

            var renderer = Assert.IsType<FreeDecorationRingRenderer>(
                new FreeDecorationRingTemplate().CreateRenderer(document));

            Assert.Equal(new Thickness(0, 17, 0, 0), renderer.QuotaNumber.Margin);
            Assert.Equal(new Thickness(0, -29, 0, 0), renderer.QuotaLabel.Margin);
        });

    internal static SkinPackageDocument CreateDocument(
        IEnumerable<SkinAssetSlot>? slots = null,
        Func<SkinTheme, SkinTheme>? transformTheme = null)
    {
        var slotSet = (slots ?? []).ToHashSet();
        var identity = new SkinImageTransform(0, 0, 1, 0, 1, 0.5, 0.5);
        var theme = new SkinTheme(
            1,
            SkinPackageLimits.FreeDecorationRingTemplateId,
            identity,
            identity,
            identity,
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
        theme = transformTheme?.Invoke(theme) ?? theme;

        var assets = slotSet.ToDictionary(
            slot => slot,
            slot => new SkinAsset(
                slot,
                slot switch
                {
                    SkinAssetSlot.Background => "assets/background.png",
                    SkinAssetSlot.Center => "assets/center.png",
                    SkinAssetSlot.Decoration => "assets/decoration.png",
                    _ => throw new ArgumentOutOfRangeException(nameof(slot))
                },
                slot == SkinAssetSlot.Center
                    ? SkinPackageFixture.CreateGrayscalePng(1, 2)
                    : SkinPackageFixture.CreateGrayscalePng(2, 1),
                slot == SkinAssetSlot.Center ? 1 : 2,
                slot == SkinAssetSlot.Center ? 2 : 1,
                true));
        var manifest = new SkinManifest(
            1,
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Ocean",
            "Alice",
            SemanticVersion.Parse("1.2.3"),
            "Ocean ring",
            SkinPackageLimits.FreeDecorationRingTemplateId,
            SemanticVersion.Parse("1.1.1"),
            null,
            assets.Values.Select(asset => new SkinAssetReference(
                asset.Slot,
                asset.RelativePath,
                new string('0', 64))).ToArray());

        return new SkinPackageDocument(manifest, theme, assets);
    }

    private static IEnumerable<object?> EnumerateDependencyValues(DependencyObject root)
    {
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            var values = current.GetLocalValueEnumerator();
            while (values.MoveNext())
            {
                yield return values.Current.Value;
            }

            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(current); index++)
            {
                queue.Enqueue(VisualTreeHelper.GetChild(current, index));
            }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfTestCollection
{
    public const string Name = "WPF renderer tests";
}

internal static class WpfTestThread
{
    public static void Run(Action action)
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
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
