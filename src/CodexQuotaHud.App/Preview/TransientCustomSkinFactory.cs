using System.IO;
using System.Security.Cryptography;
using CodexQuotaHud.App.UI.Skins;
using CodexQuotaHud.Core.Settings;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Packaging;
using CodexQuotaHud.Skins.Templates;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.App.Preview;

internal static class TransientCustomSkinFactory
{
    public static SkinValidationResult<SyntheticSkinCandidate> Create(
        SkinPackageDocument package,
        SkinTemplateRegistry? templates = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        templates ??= SkinTemplateRegistry.CreateDefault();

        var errors = new List<SkinValidationError>();
        var contract = SkinContractValidator.Validate(
            package.Manifest,
            package.Theme,
            package.Manifest.MinimumHudVersion);
        errors.AddRange(contract.Errors.Where(error =>
            !IsIncompleteEditableMetadata(package.Manifest, error)));
        ValidateAssets(package, errors);
        if (errors.Count > 0)
        {
            return new SkinValidationResult<SyntheticSkinCandidate>(null, errors);
        }

        if (!templates.TryResolve(
                package.Manifest.TemplateId,
                package.Manifest.SchemaVersion,
                out var template))
        {
            return Invalid(
                "template.unsupported",
                "$.templateId",
                "The preview template is unavailable.");
        }

        try
        {
            var skin = new CustomQuotaSkin(
                $"custom:{package.Manifest.SkinId:D}",
                package.Theme,
                template.CreateRenderer(package));
            return new SkinValidationResult<SyntheticSkinCandidate>(
                new SyntheticSkinCandidate(
                    skin,
                    SkinPresentation.ForCustom(package.Theme)),
                []);
        }
        catch (Exception exception)
        {
            return Invalid(
                "preview.renderer.invalid",
                "$",
                $"The production preview renderer could not be created: {exception.Message}");
        }
    }

    private static void ValidateAssets(
        SkinPackageDocument package,
        ICollection<SkinValidationError> errors)
    {
        if (package.Assets is null)
        {
            Add(errors, "preview.asset.collection", "$.assets",
                "Preview assets must be a slot-keyed collection.");
            return;
        }

        var declarations = package.Manifest.Assets ?? [];
        var declaredSlots = declarations
            .Where(item => item is not null && Enum.IsDefined(item.Slot))
            .Select(item => item.Slot)
            .ToHashSet();
        foreach (var slot in package.Assets.Keys.Where(
                     slot => !declaredSlots.Contains(slot)))
        {
            Add(errors, "preview.asset.extra", "$.assets",
                $"Asset slot '{slot}' is not declared by the manifest.");
        }

        long decodedPixels = 0;
        for (var index = 0; index < declarations.Count; index++)
        {
            var declaration = declarations[index];
            var location = $"$.assets[{index}]";
            if (declaration is null ||
                !package.Assets.TryGetValue(declaration.Slot, out var asset))
            {
                Add(errors, "preview.asset.missing", location,
                    "A declared preview asset is missing.");
                continue;
            }

            if (asset.Slot != declaration.Slot ||
                !string.Equals(
                    asset.RelativePath,
                    declaration.Path,
                    StringComparison.Ordinal))
            {
                Add(errors, "preview.asset.identity", location,
                    "The asset slot and path must match its declaration.");
                continue;
            }

            var hash = Convert.ToHexString(SHA256.HashData(asset.Content))
                .ToLowerInvariant();
            if (!string.Equals(hash, declaration.Sha256, StringComparison.Ordinal))
            {
                Add(errors, "preview.asset.hash", location,
                    "The preview asset hash does not match its content.");
                continue;
            }

            try
            {
                var decoded = SkinImageDecoder.Decode(
                    asset.Slot,
                    asset.RelativePath,
                    asset.Content);
                if (decoded.PixelWidth != asset.PixelWidth ||
                    decoded.PixelHeight != asset.PixelHeight)
                {
                    Add(errors, "preview.asset.dimensions", location,
                        "Decoded image dimensions do not match the asset snapshot.");
                }

                if (decoded.HasAlpha != asset.HasAlpha)
                {
                    Add(errors, "preview.asset.alpha", location,
                        "Decoded alpha capability does not match the asset snapshot.");
                }

                if (asset.Slot == SkinAssetSlot.Decoration &&
                    !decoded.HasAlpha)
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
            }
            catch (Exception exception) when (
                exception is IOException or NotSupportedException or OverflowException)
            {
                Add(errors, "preview.asset.decode", location,
                    $"The preview asset could not be decoded: {exception.Message}");
            }
        }
    }

    private static SkinValidationResult<SyntheticSkinCandidate> Invalid(
        string code,
        string location,
        string message) =>
        new(null, [new SkinValidationError(code, location, message)]);

    private static bool IsIncompleteEditableMetadata(
        SkinManifest manifest,
        SkinValidationError error) =>
        error.Code == "metadata.invalid" &&
        (error.Location == "$.author" && manifest.Author is { Length: 0 } ||
         error.Location == "$.description" &&
            manifest.Description is { Length: 0 });

    private static void Add(
        ICollection<SkinValidationError> errors,
        string code,
        string location,
        string message) =>
        errors.Add(new SkinValidationError(code, location, message));
}
