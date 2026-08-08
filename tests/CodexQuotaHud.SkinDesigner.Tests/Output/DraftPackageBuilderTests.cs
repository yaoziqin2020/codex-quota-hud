using System.IO.Compression;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.SkinDesigner.Output;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;

namespace CodexQuotaHud.SkinDesigner.Tests.Output;

public sealed class DraftPackageBuilderTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Build_MapsCompleteDraftAndEveryOptionalAssetCount(int assetCount)
    {
        var orderedSlots = new[]
        {
            SkinAssetSlot.Background,
            SkinAssetSlot.Center,
            SkinAssetSlot.Decoration
        };
        var assets = OutputTestFixture.Assets(orderedSlots[..assetCount]);
        var draft = OutputTestFixture.WithReferences(
            OutputTestFixture.CompleteDraft(),
            assets) with
        {
            MinimumHudVersion = SemanticVersion.Parse("1.3.0")
        };

        var result = new DraftPackageBuilder(SemanticVersion.Parse("1.3.0"))
            .Build(draft, assets);

        Assert.True(result.IsValid, Format(result.Errors));
        var request = Assert.IsType<CodexQuotaHud.Skins.Packaging.SkinPackageBuildRequest>(
            result.Value);
        Assert.Equal(draft.SkinId, request.Manifest.SkinId);
        Assert.Equal("Ocean / Ring", request.Manifest.DisplayName);
        Assert.Equal("Alice", request.Manifest.Author);
        Assert.Equal(SemanticVersion.Parse("1.2.3"), request.Manifest.PackageVersion);
        Assert.Equal("A deterministic package", request.Manifest.Description);
        Assert.Equal(draft.MinimumHudVersion, request.Manifest.MinimumHudVersion);
        Assert.Equal(
            SemanticVersion.Parse("1.3.0"),
            request.Manifest.MinimumHudVersion);
        Assert.Equal(draft.Theme, request.Theme);
        Assert.Equal(2d, request.Theme.Animation.RefreshSpeedMultiplier);
        Assert.Equal(1.5d, request.Theme.Animation.RefreshHoldSeconds);
        Assert.Null(request.Manifest.OriginSkinId);
        Assert.Empty(request.Manifest.Assets);
        Assert.Equal(
            orderedSlots[..assetCount],
            request.Assets.Keys.OrderBy(slot => slot).ToArray());
        Assert.DoesNotContain(
            draft.ProjectName,
            string.Join('|', request.Assets.Values.Select(asset => asset.RelativePath)),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("author")]
    [InlineData("description")]
    public void Build_RejectsMissingMandatoryPackageMetadata(string field)
    {
        var draft = OutputTestFixture.CompleteDraft();
        draft = field == "author"
            ? draft with { Author = string.Empty }
            : draft with { Description = string.Empty };

        var result = new DraftPackageBuilder(OutputTestFixture.HudVersion)
            .Build(draft, OutputTestFixture.Assets());

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Location == $"$.{field}");
    }

    [Fact]
    public void Build_RejectsAssetDictionaryThatDoesNotMatchDraftReferences()
    {
        var referenced = OutputTestFixture.Assets(SkinAssetSlot.Background);
        var draft = OutputTestFixture.WithReferences(
            OutputTestFixture.CompleteDraft(),
            referenced);
        var extra = OutputTestFixture.Assets(
            SkinAssetSlot.Background,
            SkinAssetSlot.Center);

        var result = new DraftPackageBuilder(OutputTestFixture.HudVersion)
            .Build(draft, extra);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == "draft.asset.mismatch");
    }

    [Fact]
    public void Build_RejectsMinimumHudVersionAboveCompositionVersion()
    {
        var draft = OutputTestFixture.CompleteDraft() with
        {
            MinimumHudVersion = SemanticVersion.Parse("9.0.0")
        };

        var result = new DraftPackageBuilder(OutputTestFixture.HudVersion)
            .Build(draft, OutputTestFixture.Assets());

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == "version.incompatible");
    }

    [Fact]
    public void Build_AddressedDraftWritesOnlyCanonicalPackagePath()
    {
        var assets = OutputTestFixture.Assets(SkinAssetSlot.Background);
        var draft = OutputTestFixture.WithReferences(
            OutputTestFixture.CompleteDraft(),
            assets);
        var reference = draft.Assets[SkinAssetSlot.Background];
        var storageRelativePath = DraftAssetStorage.CreateContentRelativePath(
            reference.RelativePath,
            assets[SkinAssetSlot.Background].Content);
        draft = draft with
        {
            Assets = new Dictionary<SkinAssetSlot, DraftAssetReference>
            {
                [SkinAssetSlot.Background] = reference with
                {
                    StorageRelativePath = storageRelativePath
                }
            }
        };

        var built = new DraftPackageBuilder(OutputTestFixture.HudVersion)
            .Build(draft, assets);

        Assert.True(built.IsValid, Format(built.Errors));
        var request = Assert.IsType<SkinPackageBuildRequest>(built.Value);
        Assert.Equal(
            "assets/background.png",
            request.Assets[SkinAssetSlot.Background].RelativePath);
        using var package = new MemoryStream();
        new SkinPackageWriter().Write(
            package,
            request,
            CancellationToken.None);
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        Assert.Contains(archive.Entries,
            entry => entry.FullName == "assets/background.png");
        Assert.DoesNotContain(archive.Entries,
            entry => entry.FullName.Contains("sha256-", StringComparison.Ordinal));
        using var manifestReader = new StreamReader(
            Assert.Single(archive.Entries, entry => entry.FullName == "manifest.json")
                .Open());
        Assert.DoesNotContain(
            storageRelativePath,
            manifestReader.ReadToEnd(),
            StringComparison.Ordinal);
    }

    private static string Format(IReadOnlyList<SkinValidationError> errors) =>
        string.Join(Environment.NewLine, errors.Select(error =>
            $"{error.Code} {error.Location}: {error.Message}"));
}
