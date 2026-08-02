using System.Globalization;
using System.IO;
using System.Text;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Validation;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public static class SkinDraftValidator
{
    private const int DraftSchemaVersion = 1;
    private const int MaximumOriginalFileNameScalars = 255;

    public static SkinValidationResult<SkinDraftDocument> Validate(
        SkinDraftDocument draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var errors = new List<SkinValidationError>();
        if (draft.DraftSchemaVersion != DraftSchemaVersion)
        {
            Add(
                errors,
                "draft.schema.unsupported",
                "$.draftSchemaVersion",
                "Only draft schema version 1 is supported.");
        }

        ValidateIdentities(draft, errors);
        if (draft.Revision < 0)
        {
            Add(
                errors,
                "draft.revision.invalid",
                "$.revision",
                "Draft revision must be non-negative.");
        }

        ValidateRequiredMetadata(
            draft.ProjectName,
            SkinPackageLimits.MaximumDisplayNameScalars,
            "$.projectName",
            errors);
        ValidateRequiredMetadata(
            draft.DisplayName,
            SkinPackageLimits.MaximumDisplayNameScalars,
            "$.displayName",
            errors);
        ValidateEditableMetadata(
            draft.Author,
            SkinPackageLimits.MaximumAuthorScalars,
            "$.author",
            errors);
        ValidateEditableMetadata(
            draft.Description,
            SkinPackageLimits.MaximumDescriptionScalars,
            "$.description",
            errors);

        ValidateTheme(draft.Theme, errors);
        ValidateAssets(draft.Assets, errors);
        ValidateTimestamp(draft.CreatedAtUtc, "$.createdAtUtc", errors);
        ValidateTimestamp(draft.UpdatedAtUtc, "$.updatedAtUtc", errors);
        if (draft.CreatedAtUtc.Offset == TimeSpan.Zero &&
            draft.UpdatedAtUtc.Offset == TimeSpan.Zero &&
            draft.UpdatedAtUtc < draft.CreatedAtUtc)
        {
            Add(
                errors,
                "draft.timestamp.order",
                "$.updatedAtUtc",
                "The updated timestamp must not precede creation.");
        }

        return errors.Count == 0
            ? new SkinValidationResult<SkinDraftDocument>(draft, [])
            : new SkinValidationResult<SkinDraftDocument>(default, errors);
    }

    private static void ValidateIdentities(
        SkinDraftDocument draft,
        ICollection<SkinValidationError> errors)
    {
        if (draft.DraftId == Guid.Empty)
        {
            Add(
                errors,
                "draft.id.invalid",
                "$.draftId",
                "The draft ID must not be empty.");
        }

        if (draft.SkinId == Guid.Empty)
        {
            Add(
                errors,
                "draft.id.invalid",
                "$.skinId",
                "The skin ID must not be empty.");
        }
        else if (draft.SkinId == draft.DraftId)
        {
            Add(
                errors,
                "draft.id.collision",
                "$.skinId",
                "Draft and skin IDs must be distinct.");
        }

        if (draft.OriginSkinId is not { } originSkinId)
        {
            return;
        }

        if (originSkinId == Guid.Empty ||
            originSkinId == draft.DraftId ||
            originSkinId == draft.SkinId)
        {
            Add(
                errors,
                "draft.origin-id.invalid",
                "$.originSkinId",
                "The optional origin ID must be non-empty and distinct.");
        }
    }

    private static void ValidateRequiredMetadata(
        string? value,
        int maximumScalars,
        string location,
        ICollection<SkinValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            !IsControlFreeWithinLimit(value, maximumScalars))
        {
            AddMetadataError(location, errors);
        }
    }

    private static void ValidateEditableMetadata(
        string? value,
        int maximumScalars,
        string location,
        ICollection<SkinValidationError> errors)
    {
        if (value is null ||
            (value.Length != 0 && string.IsNullOrWhiteSpace(value)) ||
            !IsControlFreeWithinLimit(value, maximumScalars))
        {
            AddMetadataError(location, errors);
        }
    }

    private static bool IsControlFreeWithinLimit(
        string value,
        int maximumScalars) =>
        value.EnumerateRunes().Count() <= maximumScalars &&
        !value.EnumerateRunes().Any(
            rune => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control);

    private static void AddMetadataError(
        string location,
        ICollection<SkinValidationError> errors) =>
        Add(
            errors,
            "draft.metadata.invalid",
            location,
            "Draft metadata is empty where required, contains a control character, or exceeds its scalar limit.");

    private static void ValidateTheme(
        SkinTheme? theme,
        ICollection<SkinValidationError> errors)
    {
        if (theme is null)
        {
            AddThemeError(errors);
            return;
        }

        var result = SkinContractValidator.ValidateTheme(theme);
        if (!result.IsValid)
        {
            AddThemeError(errors);
        }
    }

    private static void AddThemeError(
        ICollection<SkinValidationError> errors) =>
        Add(
            errors,
            "draft.theme.invalid",
            "$.theme",
            "The draft theme does not satisfy the shared skin theme contract.");

    private static void ValidateAssets(
        IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>? assets,
        ICollection<SkinValidationError> errors)
    {
        if (assets is null)
        {
            Add(
                errors,
                "draft.asset.invalid",
                "$.assets",
                "Draft assets must be a slot-keyed collection.");
            return;
        }

        var index = 0;
        foreach (var pair in assets.OrderBy(pair => pair.Key))
        {
            var location = $"$.assets[{index}]";
            var asset = pair.Value;
            if (!Enum.IsDefined(pair.Key) || asset is null)
            {
                Add(
                    errors,
                    "draft.asset.slot.invalid",
                    $"{location}.slot",
                    "The asset slot is not defined by draft schema version 1.");
                index++;
                continue;
            }

            if (asset.Slot != pair.Key)
            {
                Add(
                    errors,
                    "draft.asset.slot.mismatch",
                    $"{location}.slot",
                    "The asset slot must match its dictionary key.");
            }

            if (!IsValidAssetPath(asset.Slot, asset.RelativePath))
            {
                Add(
                    errors,
                    "draft.asset.path.invalid",
                    $"{location}.relativePath",
                    "The draft asset path is not a fixed relative name for its slot.");
            }

            if (!IsValidOriginalFileName(asset.Slot, asset.OriginalFileName))
            {
                Add(
                    errors,
                    "draft.asset.original-name.invalid",
                    $"{location}.originalFileName",
                    "The original file name must be a bounded leaf PNG or JPEG name.");
            }

            index++;
        }
    }

    private static bool IsValidAssetPath(
        SkinAssetSlot slot,
        string? path) => slot switch
        {
            SkinAssetSlot.Background => path is "assets/background.png",
            SkinAssetSlot.Center => path is "assets/center.jpg",
            SkinAssetSlot.Decoration => path is "assets/decoration.png",
            _ => false
        };

    private static bool IsValidOriginalFileName(
        SkinAssetSlot slot,
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value is "." or ".." ||
            value.IndexOfAny(['/', '\\', ':']) >= 0 ||
            !IsControlFreeWithinLimit(value, MaximumOriginalFileNameScalars))
        {
            return false;
        }

        var extension = Path.GetExtension(value);
        return slot == SkinAssetSlot.Decoration
            ? string.Equals(extension, ".png", StringComparison.OrdinalIgnoreCase)
            : extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateTimestamp(
        DateTimeOffset value,
        string location,
        ICollection<SkinValidationError> errors)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            Add(
                errors,
                "draft.timestamp.invalid",
                location,
                "Draft timestamps must use UTC.");
        }
    }

    private static void Add(
        ICollection<SkinValidationError> errors,
        string code,
        string location,
        string message) =>
        errors.Add(new SkinValidationError(code, location, message));
}
