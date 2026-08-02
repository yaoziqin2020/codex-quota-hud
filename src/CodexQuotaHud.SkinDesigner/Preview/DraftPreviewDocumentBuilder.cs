using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.SkinDesigner.Preview;

public static class DraftPreviewDocumentBuilder
{
    public static SkinValidationResult<SkinPackageDocument> Build(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(assets);

        var errors = new List<SkinValidationError>();
        errors.AddRange(SkinDraftValidator.Validate(draft).Errors);
        errors.AddRange(SkinContractValidator.ValidateTheme(draft.Theme).Errors);
        ValidateRelationships(draft, assets, errors);
        var ownedAssets = ValidateAndCloneAssets(draft, assets, errors);
        if (errors.Count > 0)
        {
            return new SkinValidationResult<SkinPackageDocument>(null, errors);
        }

        var declarations = Enum.GetValues<SkinAssetSlot>()
            .Where(ownedAssets.ContainsKey)
            .Select(slot =>
            {
                var asset = ownedAssets[slot];
                return new SkinAssetReference(
                    slot,
                    draft.Assets[slot].RelativePath,
                    Convert.ToHexString(SHA256.HashData(asset.Content))
                        .ToLowerInvariant());
            })
            .ToArray();
        var manifest = new SkinManifest(
            SkinPackageLimits.SchemaVersion,
            draft.SkinId,
            draft.DisplayName,
            draft.Author,
            draft.PackageVersion,
            draft.Description,
            draft.Theme.TemplateId,
            draft.MinimumHudVersion,
            OriginSkinId: null,
            declarations);

        return new SkinValidationResult<SkinPackageDocument>(
            new SkinPackageDocument(
                manifest,
                draft.Theme,
                new ReadOnlyDictionary<SkinAssetSlot, SkinAsset>(ownedAssets)),
            []);
    }

    private static void ValidateRelationships(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets,
        ICollection<SkinValidationError> errors)
    {
        foreach (var pair in draft.Assets.OrderBy(pair => pair.Key))
        {
            var location = $"$.assets[{(int)pair.Key}]";
            if (!assets.TryGetValue(pair.Key, out var asset))
            {
                Add(errors, "preview.asset.missing", location,
                    "A declared draft asset is missing from the immutable snapshot.");
                continue;
            }

            if (asset is null ||
                asset.Slot != pair.Key ||
                pair.Value.Slot != pair.Key)
            {
                Add(errors, "preview.asset.slot-mismatch", location,
                    "Draft, dictionary, and decoded asset slots must match.");
                continue;
            }

            if (!string.Equals(
                    asset.RelativePath,
                    pair.Value.RelativePath,
                    StringComparison.Ordinal))
            {
                Add(errors, "preview.asset.path-mismatch", location,
                    "The decoded asset path must match the draft reference.");
            }
        }

        foreach (var pair in assets.OrderBy(pair => pair.Key))
        {
            if (!draft.Assets.ContainsKey(pair.Key))
            {
                Add(errors, "preview.asset.extra", "$.assets",
                    $"Asset slot '{pair.Key}' is not declared by the draft.");
            }
        }
    }

    private static Dictionary<SkinAssetSlot, SkinAsset> ValidateAndCloneAssets(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets,
        ICollection<SkinValidationError> errors)
    {
        var owned = new Dictionary<SkinAssetSlot, SkinAsset>();
        long decodedPixels = 0;
        foreach (var slot in Enum.GetValues<SkinAssetSlot>())
        {
            if (!draft.Assets.ContainsKey(slot) ||
                !assets.TryGetValue(slot, out var asset) ||
                asset is null ||
                asset.Slot != slot ||
                !string.Equals(
                    asset.RelativePath,
                    draft.Assets[slot].RelativePath,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var location = $"$.assets[{(int)slot}]";
            try
            {
                var decoded = SkinImageDecoder.Decode(
                    slot,
                    asset.RelativePath,
                    asset.Content);
                if (decoded.PixelWidth != asset.PixelWidth ||
                    decoded.PixelHeight != asset.PixelHeight)
                {
                    Add(errors, "preview.asset.dimensions", location,
                        "Decoded dimensions do not match the immutable asset snapshot.");
                }

                if (decoded.HasAlpha != asset.HasAlpha)
                {
                    Add(errors, "preview.asset.alpha", location,
                        "Decoded alpha capability does not match the immutable asset snapshot.");
                }

                if (slot == SkinAssetSlot.Decoration && !decoded.HasAlpha)
                {
                    Add(errors, "preview.asset.decoration-alpha", location,
                        "Decoration images must be alpha-capable PNG content.");
                }

                decodedPixels = checked(decodedPixels +
                    ((long)decoded.PixelWidth * decoded.PixelHeight));
                if (decodedPixels > SkinPackageLimits.MaximumDecodedPixels)
                {
                    Add(errors, "image.pixel-budget", "$.assets",
                        "Decoded images exceed the supported pixel budget.");
                }

                owned.Add(slot, asset with { Content = [.. asset.Content] });
            }
            catch (Exception exception) when (
                exception is IOException or NotSupportedException or OverflowException)
            {
                Add(errors, "preview.asset.decode", location,
                    $"The immutable asset could not be decoded: {exception.Message}");
            }
        }

        return owned;
    }

    private static void Add(
        ICollection<SkinValidationError> errors,
        string code,
        string location,
        string message) =>
        errors.Add(new SkinValidationError(code, location, message));
}
