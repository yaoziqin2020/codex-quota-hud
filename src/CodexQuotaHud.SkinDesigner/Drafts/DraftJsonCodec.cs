using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Serialization;

namespace CodexQuotaHud.SkinDesigner.Drafts;

public static class DraftJsonCodec
{
    private const string CanonicalTimestampFormat =
        "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    private static readonly string[] DraftProperties =
    [
        "draftSchemaVersion",
        "draftId",
        "skinId",
        "revision",
        "projectName",
        "displayName",
        "author",
        "packageVersion",
        "description",
        "minimumHudVersion",
        "originSkinId",
        "theme",
        "assets",
        "createdAtUtc",
        "updatedAtUtc"
    ];

    private static readonly string[] AssetRequiredProperties =
    [
        "slot",
        "relativePath",
        "originalFileName"
    ];

    private static readonly string[] AssetOptionalProperties =
    [
        "storageRelativePath"
    ];

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    public static SkinValidationResult<SkinDraftDocument> Parse(
        ReadOnlySpan<byte> utf8)
    {
        try
        {
            using var document = JsonDocument.Parse(
                utf8.ToArray(),
                DocumentOptions);
            var root = document.RootElement;
            var errors = new List<SkinValidationError>();
            if (!ValidateObject(root, "$", DraftProperties, errors))
            {
                return Invalid(errors);
            }

            var assetsElement = root.GetProperty("assets");
            if (assetsElement.ValueKind != JsonValueKind.Array)
            {
                AddWrongKind(errors, "$.assets");
                return Invalid(errors);
            }

            var assetIndex = 0;
            foreach (var asset in assetsElement.EnumerateArray())
            {
                ValidateObject(
                    asset,
                    $"$.assets[{assetIndex}]",
                    AssetRequiredProperties,
                    errors,
                    AssetOptionalProperties);
                assetIndex++;
            }

            if (errors.Count != 0)
            {
                return Invalid(errors);
            }

            var schemaVersion = ReadInt32(
                root.GetProperty("draftSchemaVersion"),
                "$.draftSchemaVersion",
                errors);
            var draftId = ReadGuid(
                root.GetProperty("draftId"),
                "$.draftId",
                errors);
            var skinId = ReadGuid(
                root.GetProperty("skinId"),
                "$.skinId",
                errors);
            var revision = ReadInt64(
                root.GetProperty("revision"),
                "$.revision",
                errors);
            var projectName = ReadString(
                root.GetProperty("projectName"),
                "$.projectName",
                errors);
            var displayName = ReadString(
                root.GetProperty("displayName"),
                "$.displayName",
                errors);
            var author = ReadString(
                root.GetProperty("author"),
                "$.author",
                errors);
            var packageVersion = ReadVersion(
                root.GetProperty("packageVersion"),
                "$.packageVersion",
                errors);
            var description = ReadString(
                root.GetProperty("description"),
                "$.description",
                errors);
            var minimumHudVersion = ReadVersion(
                root.GetProperty("minimumHudVersion"),
                "$.minimumHudVersion",
                errors);
            var originSkinId = ReadNullableGuid(
                root.GetProperty("originSkinId"),
                "$.originSkinId",
                errors);
            var theme = ReadTheme(root.GetProperty("theme"), errors);
            var assets = ReadAssets(assetsElement, errors);
            var createdAtUtc = ReadTimestamp(
                root.GetProperty("createdAtUtc"),
                "$.createdAtUtc",
                errors);
            var updatedAtUtc = ReadTimestamp(
                root.GetProperty("updatedAtUtc"),
                "$.updatedAtUtc",
                errors);

            if (errors.Count != 0 || theme is null)
            {
                return Invalid(errors);
            }

            var draft = new SkinDraftDocument(
                schemaVersion,
                draftId,
                skinId,
                revision,
                projectName!,
                displayName!,
                author!,
                packageVersion,
                description!,
                minimumHudVersion,
                originSkinId,
                theme,
                assets,
                createdAtUtc,
                updatedAtUtc);
            return SkinDraftValidator.Validate(draft);
        }
        catch (JsonException)
        {
            return Invalid(
            [
                new SkinValidationError(
                    "json.invalid",
                    "$",
                    "The draft document is not valid strict JSON.")
            ]);
        }
    }

    public static byte[] Write(SkinDraftDocument draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var validation = SkinDraftValidator.Validate(draft);
        if (!validation.IsValid)
        {
            var first = validation.Errors[0];
            throw new ArgumentException(
                $"Invalid skin draft at {first.Location}: {first.Message}",
                nameof(draft));
        }

        using var themeDocument = JsonDocument.Parse(
            SkinJsonCodec.WriteTheme(draft.Theme),
            DocumentOptions);
        return WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("draftSchemaVersion", draft.DraftSchemaVersion);
            writer.WriteString("draftId", WriteGuid(draft.DraftId));
            writer.WriteString("skinId", WriteGuid(draft.SkinId));
            writer.WriteNumber("revision", draft.Revision);
            writer.WriteString("projectName", draft.ProjectName);
            writer.WriteString("displayName", draft.DisplayName);
            writer.WriteString("author", draft.Author);
            writer.WriteString("packageVersion", draft.PackageVersion.ToString());
            writer.WriteString("description", draft.Description);
            writer.WriteString(
                "minimumHudVersion",
                draft.MinimumHudVersion.ToString());
            if (draft.OriginSkinId is { } originSkinId)
            {
                writer.WriteString("originSkinId", WriteGuid(originSkinId));
            }
            else
            {
                writer.WriteNull("originSkinId");
            }

            writer.WritePropertyName("theme");
            themeDocument.RootElement.WriteTo(writer);
            writer.WriteStartArray("assets");
            foreach (var asset in draft.Assets
                         .OrderBy(pair => pair.Key)
                         .Select(pair => pair.Value))
            {
                writer.WriteStartObject();
                writer.WriteString("slot", WriteAssetSlot(asset.Slot));
                writer.WriteString("relativePath", asset.RelativePath);
                if (asset.StorageRelativePath is not null)
                {
                    writer.WriteString(
                        "storageRelativePath",
                        asset.StorageRelativePath);
                }

                writer.WriteString("originalFileName", asset.OriginalFileName);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteString("createdAtUtc", WriteTimestamp(draft.CreatedAtUtc));
            writer.WriteString("updatedAtUtc", WriteTimestamp(draft.UpdatedAtUtc));
            writer.WriteEndObject();
        });
    }

    private static SkinTheme? ReadTheme(
        JsonElement element,
        ICollection<SkinValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            AddDraftThemeError(errors);
            return null;
        }

        var themeBytes = Encoding.UTF8.GetBytes(element.GetRawText());
        var parsed = SkinJsonCodec.ParseTheme(themeBytes);
        if (!parsed.IsValid)
        {
            AddDraftThemeError(errors);
            return null;
        }

        return parsed.Value;
    }

    private static void AddDraftThemeError(
        ICollection<SkinValidationError> errors) =>
        errors.Add(new SkinValidationError(
            "draft.theme.invalid",
            "$.theme",
            "The embedded theme is not a strict schema-v1 skin theme."));

    private static IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>
        ReadAssets(
            JsonElement element,
            ICollection<SkinValidationError> errors)
    {
        var assets = new Dictionary<SkinAssetSlot, DraftAssetReference>();
        var index = 0;
        foreach (var assetElement in element.EnumerateArray())
        {
            var location = $"$.assets[{index}]";
            var slot = ReadAssetSlot(
                assetElement.GetProperty("slot"),
                $"{location}.slot",
                errors);
            var relativePath = ReadString(
                assetElement.GetProperty("relativePath"),
                $"{location}.relativePath",
                errors);
            var storageRelativePath = assetElement.TryGetProperty(
                "storageRelativePath",
                out var storageRelativePathElement)
                ? ReadString(
                    storageRelativePathElement,
                    $"{location}.storageRelativePath",
                    errors)
                : null;
            var originalFileName = ReadString(
                assetElement.GetProperty("originalFileName"),
                $"{location}.originalFileName",
                errors);
            if (slot is { } definedSlot &&
                relativePath is not null &&
                originalFileName is not null)
            {
                var reference = new DraftAssetReference(
                    definedSlot,
                    relativePath,
                    originalFileName,
                    storageRelativePath);
                if (!assets.TryAdd(definedSlot, reference))
                {
                    errors.Add(new SkinValidationError(
                        "draft.asset.duplicate-slot",
                        $"{location}.slot",
                        "Each draft asset slot may be declared only once."));
                }
            }

            index++;
        }

        return new ReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>(assets);
    }

    private static bool ValidateObject(
        JsonElement element,
        string path,
        IReadOnlyList<string> expectedProperties,
        ICollection<SkinValidationError> errors,
        IReadOnlyList<string>? optionalProperties = null)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            AddWrongKind(errors, path);
            return false;
        }

        var startingErrorCount = errors.Count;
        var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
        if (optionalProperties is not null)
        {
            expected.UnionWith(optionalProperties);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                errors.Add(new SkinValidationError(
                    "json.duplicate-property",
                    $"{path}.{property.Name}",
                    "JSON properties must not be duplicated."));
            }

            if (!expected.Contains(property.Name))
            {
                errors.Add(new SkinValidationError(
                    "json.unknown-property",
                    $"{path}.{property.Name}",
                    "The JSON property is not defined by draft schema version 1."));
            }
        }

        foreach (var propertyName in expectedProperties)
        {
            if (!seen.Contains(propertyName))
            {
                errors.Add(new SkinValidationError(
                    "json.missing-property",
                    $"{path}.{propertyName}",
                    "A required JSON property is missing."));
            }
        }

        return errors.Count == startingErrorCount;
    }

    private static int ReadInt32(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out var value))
        {
            return value;
        }

        AddInvalidValue(errors, path);
        return default;
    }

    private static long ReadInt64(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt64(out var value))
        {
            return value;
        }

        AddInvalidValue(errors, path);
        return default;
    }

    private static string? ReadString(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        AddInvalidValue(errors, path);
        return null;
    }

    private static Guid ReadGuid(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        var value = ReadString(element, path, errors);
        if (value is not null &&
            Guid.TryParseExact(value, "D", out var guid) &&
            string.Equals(value, WriteGuid(guid), StringComparison.Ordinal))
        {
            return guid;
        }

        if (value is not null)
        {
            AddInvalidValue(errors, path);
        }

        return default;
    }

    private static Guid? ReadNullableGuid(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors) =>
        element.ValueKind == JsonValueKind.Null
            ? null
            : ReadGuid(element, path, errors);

    private static SemanticVersion ReadVersion(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        var value = ReadString(element, path, errors);
        if (value is not null &&
            SemanticVersion.TryParse(value, out var version) &&
            string.Equals(value, version.ToString(), StringComparison.Ordinal))
        {
            return version;
        }

        if (value is not null)
        {
            AddInvalidValue(errors, path);
        }

        return default;
    }

    private static DateTimeOffset ReadTimestamp(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        var value = ReadString(element, path, errors);
        if (value is not null &&
            DateTimeOffset.TryParseExact(
                value,
                CanonicalTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp) &&
            timestamp.Offset == TimeSpan.Zero &&
            string.Equals(value, WriteTimestamp(timestamp), StringComparison.Ordinal))
        {
            return timestamp;
        }

        if (value is not null)
        {
            AddInvalidValue(errors, path);
        }

        return default;
    }

    private static SkinAssetSlot? ReadAssetSlot(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        var value = ReadString(element, path, errors);
        var slot = value switch
        {
            "background" => SkinAssetSlot.Background,
            "center" => SkinAssetSlot.Center,
            "decoration" => SkinAssetSlot.Decoration,
            _ => (SkinAssetSlot?)null
        };
        if (slot is null && value is not null)
        {
            AddInvalidValue(errors, path);
        }

        return slot;
    }

    private static string WriteGuid(Guid value) =>
        value.ToString("D").ToLowerInvariant();

    private static string WriteTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString(
            CanonicalTimestampFormat,
            CultureInfo.InvariantCulture);

    private static string WriteAssetSlot(SkinAssetSlot slot) => slot switch
    {
        SkinAssetSlot.Background => "background",
        SkinAssetSlot.Center => "center",
        SkinAssetSlot.Decoration => "decoration",
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    private static byte[] WriteJson(Action<Utf8JsonWriter> write)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(
                   buffer,
                   new JsonWriterOptions
                   {
                       Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                       Indented = true,
                       IndentCharacter = ' ',
                       IndentSize = 2,
                       NewLine = "\n"
                   }))
        {
            write(writer);
        }

        return buffer.ToArray();
    }

    private static SkinValidationResult<SkinDraftDocument> Invalid(
        IReadOnlyList<SkinValidationError> errors) => new(default, errors);

    private static void AddWrongKind(
        ICollection<SkinValidationError> errors,
        string path) =>
        errors.Add(new SkinValidationError(
            "json.wrong-kind",
            path,
            "The JSON value has the wrong kind."));

    private static void AddInvalidValue(
        ICollection<SkinValidationError> errors,
        string path) =>
        errors.Add(new SkinValidationError(
            "json.invalid-value",
            path,
            "The JSON value cannot be represented by the draft contract."));
}
