using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using CodexQuotaHud.Skins.Contracts;

namespace CodexQuotaHud.Skins.Serialization;

public static class SkinJsonCodec
{
    private static readonly string[] ManifestProperties =
    [
        "schemaVersion",
        "skinId",
        "displayName",
        "author",
        "packageVersion",
        "description",
        "templateId",
        "minimumHudVersion",
        "originSkinId",
        "assets"
    ];

    private static readonly string[] AssetProperties =
    [
        "slot",
        "path",
        "sha256"
    ];

    private static readonly string[] ThemeProperties =
    [
        "schemaVersion",
        "templateId",
        "background",
        "center",
        "decoration",
        "primaryRingColor",
        "secondaryRingColor",
        "baseBackgroundColor",
        "baseBackgroundOpacity",
        "ringDiameter",
        "ringThickness",
        "ringGap",
        "startAngle",
        "glowColor",
        "glowIntensity",
        "numberTextSize",
        "labelTextSize",
        "textWeight",
        "textPlacement",
        "animation"
    ];

    private static readonly string[] TransformProperties =
    [
        "offsetX",
        "offsetY",
        "scale",
        "rotation",
        "opacity",
        "cropFocusX",
        "cropFocusY"
    ];

    private static readonly string[] AnimationProperties =
    [
        "rotationIntensity",
        "breathingIntensity",
        "glowIntensity",
        "floatingIntensity"
    ];

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow
    };

    public static SkinValidationResult<SkinManifest> ParseManifest(
        ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(
                utf8Json.ToArray(),
                DocumentOptions);
            var errors = new List<SkinValidationError>();
            var root = document.RootElement;
            if (!ValidateObject(root, "$", ManifestProperties, errors))
            {
                return Invalid<SkinManifest>(errors);
            }

            var assetsElement = root.GetProperty("assets");
            if (assetsElement.ValueKind != JsonValueKind.Array)
            {
                AddWrongKind(errors, "$.assets");
                return Invalid<SkinManifest>(errors);
            }

            var assetIndex = 0;
            foreach (var assetElement in assetsElement.EnumerateArray())
            {
                ValidateObject(
                    assetElement,
                    $"$.assets[{assetIndex}]",
                    AssetProperties,
                    errors);
                assetIndex++;
            }

            if (errors.Count != 0)
            {
                return Invalid<SkinManifest>(errors);
            }

            var schemaVersion = ReadInt32(
                root.GetProperty("schemaVersion"),
                "$.schemaVersion",
                errors);
            var skinId = ReadGuid(
                root.GetProperty("skinId"),
                "$.skinId",
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
            var templateId = ReadString(
                root.GetProperty("templateId"),
                "$.templateId",
                errors);
            var minimumHudVersion = ReadVersion(
                root.GetProperty("minimumHudVersion"),
                "$.minimumHudVersion",
                errors);
            var originSkinId = ReadNullableGuid(
                root.GetProperty("originSkinId"),
                "$.originSkinId",
                errors);
            var assets = ReadAssets(assetsElement, errors);

            if (errors.Count != 0)
            {
                return Invalid<SkinManifest>(errors);
            }

            return Valid(new SkinManifest(
                schemaVersion,
                skinId,
                displayName!,
                author!,
                packageVersion,
                description!,
                templateId!,
                minimumHudVersion,
                originSkinId,
                assets));
        }
        catch (JsonException)
        {
            return JsonInvalid<SkinManifest>();
        }
    }

    public static SkinValidationResult<SkinTheme> ParseTheme(
        ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            using var document = JsonDocument.Parse(
                utf8Json.ToArray(),
                DocumentOptions);
            var errors = new List<SkinValidationError>();
            var root = document.RootElement;
            if (!ValidateObject(root, "$", ThemeProperties, errors))
            {
                return Invalid<SkinTheme>(errors);
            }

            ValidateObject(
                root.GetProperty("background"),
                "$.background",
                TransformProperties,
                errors);
            ValidateObject(
                root.GetProperty("center"),
                "$.center",
                TransformProperties,
                errors);
            ValidateObject(
                root.GetProperty("decoration"),
                "$.decoration",
                TransformProperties,
                errors);
            ValidateObject(
                root.GetProperty("animation"),
                "$.animation",
                AnimationProperties,
                errors);

            if (errors.Count != 0)
            {
                return Invalid<SkinTheme>(errors);
            }

            var schemaVersion = ReadInt32(
                root.GetProperty("schemaVersion"),
                "$.schemaVersion",
                errors);
            var templateId = ReadString(
                root.GetProperty("templateId"),
                "$.templateId",
                errors);
            var background = ReadTransform(
                root.GetProperty("background"),
                "$.background",
                errors);
            var center = ReadTransform(
                root.GetProperty("center"),
                "$.center",
                errors);
            var decoration = ReadTransform(
                root.GetProperty("decoration"),
                "$.decoration",
                errors);
            var primaryRingColor = ReadString(
                root.GetProperty("primaryRingColor"),
                "$.primaryRingColor",
                errors);
            var secondaryRingColor = ReadString(
                root.GetProperty("secondaryRingColor"),
                "$.secondaryRingColor",
                errors);
            var baseBackgroundColor = ReadString(
                root.GetProperty("baseBackgroundColor"),
                "$.baseBackgroundColor",
                errors);
            var baseBackgroundOpacity = ReadDouble(
                root.GetProperty("baseBackgroundOpacity"),
                "$.baseBackgroundOpacity",
                errors);
            var ringDiameter = ReadDouble(
                root.GetProperty("ringDiameter"),
                "$.ringDiameter",
                errors);
            var ringThickness = ReadDouble(
                root.GetProperty("ringThickness"),
                "$.ringThickness",
                errors);
            var ringGap = ReadDouble(
                root.GetProperty("ringGap"),
                "$.ringGap",
                errors);
            var startAngle = ReadDouble(
                root.GetProperty("startAngle"),
                "$.startAngle",
                errors);
            var glowColor = ReadString(
                root.GetProperty("glowColor"),
                "$.glowColor",
                errors);
            var glowIntensity = ReadDouble(
                root.GetProperty("glowIntensity"),
                "$.glowIntensity",
                errors);
            var numberTextSize = ReadDouble(
                root.GetProperty("numberTextSize"),
                "$.numberTextSize",
                errors);
            var labelTextSize = ReadDouble(
                root.GetProperty("labelTextSize"),
                "$.labelTextSize",
                errors);
            var textWeight = ReadTextWeight(
                root.GetProperty("textWeight"),
                "$.textWeight",
                errors);
            var textPlacement = ReadTextPlacement(
                root.GetProperty("textPlacement"),
                "$.textPlacement",
                errors);
            var animation = ReadAnimation(
                root.GetProperty("animation"),
                "$.animation",
                errors);

            if (errors.Count != 0)
            {
                return Invalid<SkinTheme>(errors);
            }

            return Valid(new SkinTheme(
                schemaVersion,
                templateId!,
                background,
                center,
                decoration,
                primaryRingColor!,
                secondaryRingColor!,
                baseBackgroundColor!,
                baseBackgroundOpacity,
                ringDiameter,
                ringThickness,
                ringGap,
                startAngle,
                glowColor!,
                glowIntensity,
                numberTextSize,
                labelTextSize,
                textWeight,
                textPlacement,
                animation));
        }
        catch (JsonException)
        {
            return JsonInvalid<SkinTheme>();
        }
    }

    public static byte[] WriteManifest(SkinManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", manifest.SchemaVersion);
            writer.WriteString("skinId", manifest.SkinId.ToString("D").ToLowerInvariant());
            writer.WriteString("displayName", manifest.DisplayName);
            writer.WriteString("author", manifest.Author);
            writer.WriteString("packageVersion", manifest.PackageVersion.ToString());
            writer.WriteString("description", manifest.Description);
            writer.WriteString("templateId", manifest.TemplateId);
            writer.WriteString("minimumHudVersion", manifest.MinimumHudVersion.ToString());
            if (manifest.OriginSkinId is { } originSkinId)
            {
                writer.WriteString(
                    "originSkinId",
                    originSkinId.ToString("D").ToLowerInvariant());
            }
            else
            {
                writer.WriteNull("originSkinId");
            }

            writer.WriteStartArray("assets");
            foreach (var asset in manifest.Assets.OrderBy(asset => asset.Slot))
            {
                writer.WriteStartObject();
                writer.WriteString("slot", WriteAssetSlot(asset.Slot));
                writer.WriteString("path", asset.Path);
                writer.WriteString("sha256", asset.Sha256.ToLowerInvariant());
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        });
    }

    public static byte[] WriteTheme(SkinTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);

        return WriteJson(writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", theme.SchemaVersion);
            writer.WriteString("templateId", theme.TemplateId);
            WriteTransform(writer, "background", theme.Background);
            WriteTransform(writer, "center", theme.Center);
            WriteTransform(writer, "decoration", theme.Decoration);
            writer.WriteString("primaryRingColor", theme.PrimaryRingColor);
            writer.WriteString("secondaryRingColor", theme.SecondaryRingColor);
            writer.WriteString("baseBackgroundColor", theme.BaseBackgroundColor);
            writer.WriteNumber("baseBackgroundOpacity", theme.BaseBackgroundOpacity);
            writer.WriteNumber("ringDiameter", theme.RingDiameter);
            writer.WriteNumber("ringThickness", theme.RingThickness);
            writer.WriteNumber("ringGap", theme.RingGap);
            writer.WriteNumber("startAngle", theme.StartAngle);
            writer.WriteString("glowColor", theme.GlowColor);
            writer.WriteNumber("glowIntensity", theme.GlowIntensity);
            writer.WriteNumber("numberTextSize", theme.NumberTextSize);
            writer.WriteNumber("labelTextSize", theme.LabelTextSize);
            writer.WriteString("textWeight", WriteTextWeight(theme.TextWeight));
            writer.WriteString("textPlacement", WriteTextPlacement(theme.TextPlacement));
            writer.WriteStartObject("animation");
            writer.WriteNumber(
                "rotationIntensity",
                theme.Animation.RotationIntensity);
            writer.WriteNumber(
                "breathingIntensity",
                theme.Animation.BreathingIntensity);
            writer.WriteNumber("glowIntensity", theme.Animation.GlowIntensity);
            writer.WriteNumber(
                "floatingIntensity",
                theme.Animation.FloatingIntensity);
            writer.WriteEndObject();
            writer.WriteEndObject();
        });
    }

    private static IReadOnlyList<SkinAssetReference> ReadAssets(
        JsonElement assetsElement,
        ICollection<SkinValidationError> errors)
    {
        var assets = new List<SkinAssetReference>();
        var index = 0;
        foreach (var element in assetsElement.EnumerateArray())
        {
            var path = $"$.assets[{index}]";
            var slot = ReadAssetSlot(
                element.GetProperty("slot"),
                $"{path}.slot",
                errors);
            var relativePath = ReadString(
                element.GetProperty("path"),
                $"{path}.path",
                errors);
            var sha256 = ReadString(
                element.GetProperty("sha256"),
                $"{path}.sha256",
                errors);
            if (relativePath is not null && sha256 is not null)
            {
                assets.Add(new SkinAssetReference(slot, relativePath, sha256));
            }

            index++;
        }

        return assets;
    }

    private static SkinImageTransform ReadTransform(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors) =>
        new(
            ReadDouble(element.GetProperty("offsetX"), $"{path}.offsetX", errors),
            ReadDouble(element.GetProperty("offsetY"), $"{path}.offsetY", errors),
            ReadDouble(element.GetProperty("scale"), $"{path}.scale", errors),
            ReadDouble(element.GetProperty("rotation"), $"{path}.rotation", errors),
            ReadDouble(element.GetProperty("opacity"), $"{path}.opacity", errors),
            ReadDouble(element.GetProperty("cropFocusX"), $"{path}.cropFocusX", errors),
            ReadDouble(element.GetProperty("cropFocusY"), $"{path}.cropFocusY", errors));

    private static SkinAnimationSettings ReadAnimation(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors) =>
        new(
            ReadDouble(
                element.GetProperty("rotationIntensity"),
                $"{path}.rotationIntensity",
                errors),
            ReadDouble(
                element.GetProperty("breathingIntensity"),
                $"{path}.breathingIntensity",
                errors),
            ReadDouble(
                element.GetProperty("glowIntensity"),
                $"{path}.glowIntensity",
                errors),
            ReadDouble(
                element.GetProperty("floatingIntensity"),
                $"{path}.floatingIntensity",
                errors));

    private static bool ValidateObject(
        JsonElement element,
        string path,
        IReadOnlyList<string> expectedProperties,
        ICollection<SkinValidationError> errors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            AddWrongKind(errors, path);
            return false;
        }

        var startingErrorCount = errors.Count;
        var expected = new HashSet<string>(expectedProperties, StringComparer.Ordinal);
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
                    "The JSON property is not defined by schema version 1."));
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

    private static double ReadDouble(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetDouble(out var value) &&
            double.IsFinite(value))
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
        if (value is not null && Guid.TryParseExact(value, "D", out var guid))
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
        ICollection<SkinValidationError> errors)
    {
        if (element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return ReadGuid(element, path, errors);
    }

    private static SemanticVersion ReadVersion(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        var value = ReadString(element, path, errors);
        if (value is not null && SemanticVersion.TryParse(value, out var version))
        {
            return version;
        }

        if (value is not null)
        {
            AddInvalidValue(errors, path);
        }

        return default;
    }

    private static SkinAssetSlot ReadAssetSlot(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        var value = ReadString(element, path, errors);
        var parsed = value switch
        {
            "background" => SkinAssetSlot.Background,
            "center" => SkinAssetSlot.Center,
            "decoration" => SkinAssetSlot.Decoration,
            _ => (SkinAssetSlot?)null
        };
        if (parsed is { } slot)
        {
            return slot;
        }

        if (value is not null)
        {
            AddInvalidValue(errors, path);
        }

        return default;
    }

    private static SkinTextWeight ReadTextWeight(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        var value = ReadString(element, path, errors);
        var parsed = value switch
        {
            "regular" => SkinTextWeight.Regular,
            "semiBold" => SkinTextWeight.SemiBold,
            "bold" => SkinTextWeight.Bold,
            _ => (SkinTextWeight?)null
        };
        if (parsed is { } weight)
        {
            return weight;
        }

        if (value is not null)
        {
            AddInvalidValue(errors, path);
        }

        return default;
    }

    private static SkinTextPlacement ReadTextPlacement(
        JsonElement element,
        string path,
        ICollection<SkinValidationError> errors)
    {
        var value = ReadString(element, path, errors);
        var parsed = value switch
        {
            "centered" => SkinTextPlacement.Centered,
            "numberAboveLabel" => SkinTextPlacement.NumberAboveLabel,
            "labelAboveNumber" => SkinTextPlacement.LabelAboveNumber,
            _ => (SkinTextPlacement?)null
        };
        if (parsed is { } placement)
        {
            return placement;
        }

        if (value is not null)
        {
            AddInvalidValue(errors, path);
        }

        return default;
    }

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

    private static void WriteTransform(
        Utf8JsonWriter writer,
        string propertyName,
        SkinImageTransform transform)
    {
        writer.WriteStartObject(propertyName);
        writer.WriteNumber("offsetX", transform.OffsetX);
        writer.WriteNumber("offsetY", transform.OffsetY);
        writer.WriteNumber("scale", transform.Scale);
        writer.WriteNumber("rotation", transform.Rotation);
        writer.WriteNumber("opacity", transform.Opacity);
        writer.WriteNumber("cropFocusX", transform.CropFocusX);
        writer.WriteNumber("cropFocusY", transform.CropFocusY);
        writer.WriteEndObject();
    }

    private static string WriteAssetSlot(SkinAssetSlot slot) => slot switch
    {
        SkinAssetSlot.Background => "background",
        SkinAssetSlot.Center => "center",
        SkinAssetSlot.Decoration => "decoration",
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    private static string WriteTextWeight(SkinTextWeight weight) => weight switch
    {
        SkinTextWeight.Regular => "regular",
        SkinTextWeight.SemiBold => "semiBold",
        SkinTextWeight.Bold => "bold",
        _ => throw new ArgumentOutOfRangeException(nameof(weight))
    };

    private static string WriteTextPlacement(SkinTextPlacement placement) =>
        placement switch
        {
            SkinTextPlacement.Centered => "centered",
            SkinTextPlacement.NumberAboveLabel => "numberAboveLabel",
            SkinTextPlacement.LabelAboveNumber => "labelAboveNumber",
            _ => throw new ArgumentOutOfRangeException(nameof(placement))
        };

    private static SkinValidationResult<T> Valid<T>(T value) => new(value, []);

    private static SkinValidationResult<T> Invalid<T>(
        IReadOnlyList<SkinValidationError> errors) => new(default, errors);

    private static SkinValidationResult<T> JsonInvalid<T>() =>
        Invalid<T>(
        [
            new SkinValidationError(
                "json.invalid",
                "$",
                "The document is not valid strict JSON.")
        ]);

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
            "The JSON value cannot be represented by the schema contract."));
}
