using System.Security.Cryptography;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Preview;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Tests.Preview;

public sealed class DraftPreviewDocumentBuilderTests
{
    private static readonly Guid DraftId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SkinId =
        Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    public static TheoryData<SkinAssetSlot[]> AssetCombinations => new()
    {
        Array.Empty<SkinAssetSlot>(),
        new[] { SkinAssetSlot.Background },
        new[] { SkinAssetSlot.Background, SkinAssetSlot.Center },
        new[]
        {
            SkinAssetSlot.Background,
            SkinAssetSlot.Center,
            SkinAssetSlot.Decoration
        }
    };

    [Theory]
    [MemberData(nameof(AssetCombinations))]
    public void Build_UsesImmutableDraftIdentityAndOwnedAssets(
        SkinAssetSlot[] slots)
    {
        var draft = CreateDraft(slots);
        var assets = slots.ToDictionary(slot => slot, CreateAsset);

        var result = DraftPreviewDocumentBuilder.Build(draft, assets);

        Assert.True(result.IsValid, Format(result.Errors));
        var package = Assert.IsType<SkinPackageDocument>(result.Value);
        Assert.Equal(draft.SkinId, package.Manifest.SkinId);
        Assert.Equal(draft.DisplayName, package.Manifest.DisplayName);
        Assert.Equal(draft.Author, package.Manifest.Author);
        Assert.Equal(draft.PackageVersion, package.Manifest.PackageVersion);
        Assert.Equal(draft.Description, package.Manifest.Description);
        Assert.Equal(draft.MinimumHudVersion,
            package.Manifest.MinimumHudVersion);
        Assert.Null(package.Manifest.OriginSkinId);
        Assert.Same(draft.Theme, package.Theme);
        Assert.Equal(slots, package.Manifest.Assets.Select(item => item.Slot));

        foreach (var slot in slots)
        {
            var source = assets[slot];
            var built = package.Assets[slot];
            var declaration = Assert.Single(package.Manifest.Assets,
                item => item.Slot == slot);
            Assert.Equal(draft.Assets[slot].RelativePath,
                declaration.Path);
            Assert.Equal(draft.Assets[slot].RelativePath,
                built.RelativePath);
            Assert.Equal(
                Convert.ToHexString(SHA256.HashData(source.Content))
                    .ToLowerInvariant(),
                declaration.Sha256);
            Assert.Equal(source.Content, built.Content);
            Assert.NotSame(source.Content, built.Content);
        }
    }

    [Theory]
    [InlineData("assets/background.png", "assets/center.png")]
    [InlineData("assets/background.png", "assets/center.jpg")]
    [InlineData("assets/background.jpg", "assets/center.png")]
    [InlineData("assets/background.jpg", "assets/center.jpg")]
    public void Build_PreservesCanonicalPngJpgPathsAndContentHashes(
        string backgroundPath,
        string centerPath)
    {
        var draft = CreateDraftWithImagePaths(backgroundPath, centerPath);
        var assets = new Dictionary<SkinAssetSlot, SkinAsset>
        {
            [SkinAssetSlot.Background] = CreateAsset(
                SkinAssetSlot.Background,
                backgroundPath),
            [SkinAssetSlot.Center] = CreateAsset(
                SkinAssetSlot.Center,
                centerPath)
        };

        var result = DraftPreviewDocumentBuilder.Build(draft, assets);

        Assert.True(result.IsValid, Format(result.Errors));
        var package = Assert.IsType<SkinPackageDocument>(result.Value);
        Assert.Collection(
            package.Manifest.Assets,
            background =>
            {
                Assert.Equal(SkinAssetSlot.Background, background.Slot);
                Assert.Equal(backgroundPath, background.Path);
                Assert.Equal(ExpectedHash(backgroundPath), background.Sha256);
            },
            center =>
            {
                Assert.Equal(SkinAssetSlot.Center, center.Slot);
                Assert.Equal(centerPath, center.Path);
                Assert.Equal(ExpectedHash(centerPath), center.Sha256);
            });
        Assert.Equal(
            backgroundPath,
            package.Assets[SkinAssetSlot.Background].RelativePath);
        Assert.Equal(
            centerPath,
            package.Assets[SkinAssetSlot.Center].RelativePath);
    }

    [Theory]
    [InlineData(SkinAssetSlot.Background, "assets/background.png", false)]
    [InlineData(SkinAssetSlot.Background, "assets/background.jpg", true)]
    [InlineData(SkinAssetSlot.Center, "assets/center.png", false)]
    [InlineData(SkinAssetSlot.Center, "assets/center.jpg", true)]
    public void Build_RejectsCanonicalPathWhoseDecodedFormatDoesNotMatch(
        SkinAssetSlot slot,
        string path,
        bool usePngContent)
    {
        var draft = CreateDraftWithPath(slot, path);
        var content = usePngContent ? AlphaPng : OneByOneJpeg;
        var assets = new Dictionary<SkinAssetSlot, SkinAsset>
        {
            [slot] = new SkinAsset(
                slot,
                path,
                content,
                1,
                1,
                HasAlpha: usePngContent)
        };

        var result = DraftPreviewDocumentBuilder.Build(draft, assets);

        Assert.False(result.IsValid);
        var error = Assert.Single(result.Errors);
        Assert.Equal("preview.asset.decode", error.Code);
        Assert.Equal($"$.assets[{(int)slot}]", error.Location);
    }

    [Fact]
    public void Build_DoesNotReadOriginalAssetPaths()
    {
        var draft = CreateDraft([SkinAssetSlot.Background]);
        var reference = draft.Assets[SkinAssetSlot.Background] with
        {
            OriginalFileName = "definitely-not-present.jpg"
        };
        draft = draft with
        {
            Assets = new Dictionary<SkinAssetSlot, DraftAssetReference>
            {
                [SkinAssetSlot.Background] = reference
            }
        };

        var result = DraftPreviewDocumentBuilder.Build(
            draft,
            new Dictionary<SkinAssetSlot, SkinAsset>
            {
                [SkinAssetSlot.Background] =
                    CreateAsset(SkinAssetSlot.Background)
            });

        Assert.True(result.IsValid, Format(result.Errors));
    }

    [Theory]
    [InlineData("missing", "preview.asset.missing")]
    [InlineData("extra", "preview.asset.extra")]
    [InlineData("path", "preview.asset.path-mismatch")]
    [InlineData("dimensions", "preview.asset.dimensions")]
    [InlineData("decoration-alpha", "preview.asset.decoration-alpha")]
    [InlineData("theme", "number.out-of-range")]
    public void Build_RejectsInvalidSnapshotWithSpecificError(
        string scenario,
        string expectedCode)
    {
        var slots = scenario == "extra"
            ? Array.Empty<SkinAssetSlot>()
            : scenario == "decoration-alpha"
                ? new[] { SkinAssetSlot.Decoration }
                : new[] { SkinAssetSlot.Background };
        var draft = CreateDraft(slots);
        var assets = slots.ToDictionary(slot => slot, CreateAsset);

        switch (scenario)
        {
            case "missing":
                assets.Clear();
                break;
            case "extra":
                assets[SkinAssetSlot.Background] =
                    CreateAsset(SkinAssetSlot.Background);
                break;
            case "path":
                assets[SkinAssetSlot.Background] =
                    assets[SkinAssetSlot.Background] with
                    {
                        RelativePath = "assets/background.jpg"
                    };
                break;
            case "dimensions":
                assets[SkinAssetSlot.Background] =
                    assets[SkinAssetSlot.Background] with { PixelWidth = 2 };
                break;
            case "decoration-alpha":
                assets[SkinAssetSlot.Decoration] = new SkinAsset(
                    SkinAssetSlot.Decoration,
                    "assets/decoration.png",
                    NonAlphaPng,
                    1,
                    1,
                    HasAlpha: false);
                break;
            case "theme":
                draft = draft with
                {
                    Theme = draft.Theme with { RingThickness = 17 }
                };
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        var result = DraftPreviewDocumentBuilder.Build(draft, assets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == expectedCode);
    }

    internal static SkinDraftDocument CreateDraft(
        IEnumerable<SkinAssetSlot>? slots = null)
    {
        var draft = SkinDraftFactory.CreateNew(
            DraftId,
            SkinId,
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            SemanticVersion.Parse("1.1.1")) with
        {
            DisplayName = "Preview Ocean",
            Author = string.Empty,
            Description = string.Empty,
            OriginSkinId = Guid.Parse(
                "cccccccc-cccc-cccc-cccc-cccccccccccc")
        };
        var references = (slots ?? []).ToDictionary(
            slot => slot,
            slot => new DraftAssetReference(
                slot,
                PathFor(slot),
                $"missing-{slot}{Path.GetExtension(PathFor(slot))}"));
        return draft with { Assets = references };
    }

    internal static SkinDraftDocument CreateDraftWithImagePaths(
        string backgroundPath,
        string centerPath)
    {
        var draft = CreateDraft(
            [SkinAssetSlot.Background, SkinAssetSlot.Center]);
        var references = draft.Assets.ToDictionary(
            pair => pair.Key,
            pair => pair.Value);
        references[SkinAssetSlot.Background] =
            references[SkinAssetSlot.Background] with
            {
                RelativePath = backgroundPath,
                OriginalFileName = Path.GetFileName(backgroundPath)
            };
        references[SkinAssetSlot.Center] =
            references[SkinAssetSlot.Center] with
            {
                RelativePath = centerPath,
                OriginalFileName = Path.GetFileName(centerPath)
            };
        return draft with { Assets = references };
    }

    internal static SkinAsset CreateAsset(SkinAssetSlot slot) => new(
        slot,
        PathFor(slot),
        slot == SkinAssetSlot.Center ? OneByOneJpeg : AlphaPng,
        1,
        1,
        HasAlpha: slot != SkinAssetSlot.Center);

    internal static SkinAsset CreateAsset(
        SkinAssetSlot slot,
        string path)
    {
        var usesPng = path.EndsWith(".png", StringComparison.Ordinal);
        return new SkinAsset(
            slot,
            path,
            usesPng ? AlphaPng : OneByOneJpeg,
            1,
            1,
            HasAlpha: usesPng);
    }

    private static SkinDraftDocument CreateDraftWithPath(
        SkinAssetSlot slot,
        string path)
    {
        var draft = CreateDraft([slot]);
        return draft with
        {
            Assets = new Dictionary<SkinAssetSlot, DraftAssetReference>
            {
                [slot] = draft.Assets[slot] with
                {
                    RelativePath = path,
                    OriginalFileName = Path.GetFileName(path)
                }
            }
        };
    }

    private static string ExpectedHash(string path) =>
        path.EndsWith(".png", StringComparison.Ordinal)
            ? "4d9d398f5d40472b74ad49676f694d8ed2c29251b3df106e3b4df1b441ad4511"
            : "cb3811c1599a9871648272ceaaa21982c7e92deabc367facea3a6941e9804a9b";

    private static string PathFor(SkinAssetSlot slot) => slot switch
    {
        SkinAssetSlot.Background => "assets/background.png",
        SkinAssetSlot.Center => "assets/center.jpg",
        SkinAssetSlot.Decoration => "assets/decoration.png",
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    internal static readonly byte[] AlphaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/j//z8DAAj8Av6IXwbgAAAAAElFTkSuQmCC");

    private static readonly byte[] NonAlphaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAAAAAA6fptVAAAACklEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    internal static readonly byte[] OneByOneJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==");

    internal static string Format(
        IReadOnlyList<SkinValidationError> errors) =>
        string.Join("; ", errors.Select(error =>
            $"{error.Code}@{error.Location}"));
}
