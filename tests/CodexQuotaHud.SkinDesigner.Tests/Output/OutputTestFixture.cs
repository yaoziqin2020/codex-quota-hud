using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.SkinDesigner.Tests.Output;

internal static class OutputTestFixture
{
    internal static readonly SemanticVersion HudVersion =
        SemanticVersion.Parse("1.1.1");

    internal static SkinDraftDocument CompleteDraft(
        Guid? skinId = null,
        string packageVersion = "1.2.3") =>
        SkinDraftFactory.CreateNew(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            skinId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
            DateTimeOffset.Parse("2026-08-02T00:00:00Z"),
            HudVersion) with
        {
            ProjectName = "Private project name",
            DisplayName = "Ocean / Ring",
            Author = "Alice",
            PackageVersion = SemanticVersion.Parse(packageVersion),
            Description = "A deterministic package"
        };

    internal static IReadOnlyDictionary<SkinAssetSlot, SkinAsset> Assets(
        params SkinAssetSlot[] slots)
    {
        var assets = new Dictionary<SkinAssetSlot, SkinAsset>();
        foreach (var slot in slots)
        {
            var isJpeg = slot == SkinAssetSlot.Center;
            var path = slot switch
            {
                SkinAssetSlot.Background => "assets/background.png",
                SkinAssetSlot.Center => "assets/center.jpg",
                SkinAssetSlot.Decoration => "assets/decoration.png",
                _ => throw new ArgumentOutOfRangeException(nameof(slots))
            };
            var content = isJpeg ? OneByOneJpeg : AlphaPng;
            assets.Add(
                slot,
                new SkinAsset(slot, path, [.. content], 1, 1, !isJpeg));
        }

        return assets;
    }

    internal static SkinDraftDocument WithReferences(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets) =>
        draft with
        {
            Assets = assets.ToDictionary(
                pair => pair.Key,
                pair => new DraftAssetReference(
                    pair.Key,
                    pair.Value.RelativePath,
                    Path.GetFileName(pair.Value.RelativePath)))
        };

    internal static readonly byte[] AlphaPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAANSURBVBhXY/j//z8DAAj8Av6IXwbgAAAAAElFTkSuQmCC");

    internal static readonly byte[] OneByOneJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAMCAgMCAgMDAwMEAwMEBQgFBQQEBQoHBwYIDAoMDAsKCwsNDhIQDQ4RDgsLEBYQERMUFRUVDA8XGBYUGBIUFRT/2wBDAQMEBAUEBQkFBQkUDQsNFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBQUFBT/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD9U6KKKAP/2Q==");
}
