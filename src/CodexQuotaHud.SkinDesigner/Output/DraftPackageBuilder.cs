using System.Collections.ObjectModel;
using System.IO;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.SkinDesigner.Output;

public sealed class DraftPackageBuilder
{
    private readonly SemanticVersion _hudVersion;

    public DraftPackageBuilder(SemanticVersion hudVersion) =>
        _hudVersion = hudVersion;

    public SkinValidationResult<SkinPackageBuildRequest> Build(
        SkinDraftDocument draft,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(assets);

        var draftValidation = SkinDraftValidator.Validate(draft);
        if (!draftValidation.IsValid)
        {
            return Invalid(draftValidation.Errors);
        }

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
            Assets: []);
        var contract = SkinContractValidator.Validate(
            manifest,
            draft.Theme,
            _hudVersion);
        if (!contract.IsValid)
        {
            return Invalid(contract.Errors);
        }

        var assetErrors = ValidateAssetMatch(draft.Assets, assets);
        if (assetErrors.Count > 0)
        {
            return Invalid(assetErrors);
        }

        var owned = new SortedDictionary<SkinAssetSlot, SkinAsset>();
        long decodedPixels = 0;
        try
        {
            foreach (var reference in draft.Assets.OrderBy(pair => pair.Key))
            {
                var source = assets[reference.Key];
                var content = source.Content.ToArray();
                if (content.LongLength > SkinPackageLimits.MaximumImageBytes)
                {
                    return Error(
                        "archive.entry-size",
                        "$.assets",
                        "An owned image exceeds the supported size.");
                }

                var decoded = SkinImageDecoder.Decode(
                    reference.Key,
                    reference.Value.RelativePath,
                    content);
                decodedPixels = checked(
                    decodedPixels +
                    (long)decoded.PixelWidth * decoded.PixelHeight);
                if (decodedPixels > SkinPackageLimits.MaximumDecodedPixels ||
                    source.PixelWidth != decoded.PixelWidth ||
                    source.PixelHeight != decoded.PixelHeight ||
                    source.HasAlpha != decoded.HasAlpha)
                {
                    return Error(
                        "draft.asset.invalid",
                        "$.assets",
                        "Owned image metadata does not match its decoded content.");
                }

                owned.Add(
                    reference.Key,
                    new SkinAsset(
                        reference.Key,
                        reference.Value.RelativePath,
                        content,
                        decoded.PixelWidth,
                        decoded.PixelHeight,
                        decoded.HasAlpha));
            }
        }
        catch (Exception exception) when (
            exception is IOException or ArgumentException or OverflowException)
        {
            return Error(
                "draft.asset.invalid",
                "$.assets",
                "An owned image does not satisfy the package image contract.");
        }

        return new SkinValidationResult<SkinPackageBuildRequest>(
            new SkinPackageBuildRequest(
                manifest,
                draft.Theme,
                new ReadOnlyDictionary<SkinAssetSlot, SkinAsset>(owned)),
            []);
    }

    private static IReadOnlyList<SkinValidationError> ValidateAssetMatch(
        IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference> references,
        IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets)
    {
        if (references.Count != assets.Count)
        {
            return [Mismatch()];
        }

        foreach (var pair in references)
        {
            if (!assets.TryGetValue(pair.Key, out var asset) ||
                asset is null ||
                asset.Slot != pair.Key ||
                !string.Equals(
                    pair.Value.RelativePath,
                    asset.RelativePath,
                    StringComparison.Ordinal) ||
                asset.Content is null)
            {
                return [Mismatch()];
            }
        }

        return [];
    }

    private static SkinValidationError Mismatch() =>
        new(
            "draft.asset.mismatch",
            "$.assets",
            "The owned images do not match the draft asset references.");

    private static SkinValidationResult<SkinPackageBuildRequest> Error(
        string code,
        string location,
        string message) =>
        Invalid([new SkinValidationError(code, location, message)]);

    private static SkinValidationResult<SkinPackageBuildRequest> Invalid(
        IReadOnlyList<SkinValidationError> errors) => new(null, errors);
}
