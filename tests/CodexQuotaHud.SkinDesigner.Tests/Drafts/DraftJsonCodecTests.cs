using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexQuotaHud.SkinDesigner.Drafts;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Serialization;

namespace CodexQuotaHud.SkinDesigner.Tests.Drafts;

public sealed class DraftJsonCodecTests
{
    private const string AddressedCenterPath =
        "assets/sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png";

    private static readonly string[] DraftPropertyOrder =
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

    [Fact]
    public void WriteParseWrite_AllAssetsProduceStableCanonicalBytes()
    {
        var draft = ValidDraft();

        var firstBytes = DraftJsonCodec.Write(draft);
        var parsed = DraftJsonCodec.Parse(firstBytes);

        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        var parsedDraft = Assert.IsType<SkinDraftDocument>(parsed.Value);
        Assert.Equal(draft with { Assets = parsedDraft.Assets }, parsedDraft);
        Assert.Equal(
            draft.Assets.OrderBy(pair => pair.Key),
            parsedDraft.Assets.OrderBy(pair => pair.Key));
        Assert.Equal(firstBytes, DraftJsonCodec.Write(parsedDraft));

        using var document = JsonDocument.Parse(firstBytes);
        Assert.Equal(
            DraftPropertyOrder,
            document.RootElement.EnumerateObject().Select(property => property.Name));
        using var canonicalTheme = JsonDocument.Parse(
            SkinJsonCodec.WriteTheme(draft.Theme));
        Assert.Equal(
            canonicalTheme.RootElement.EnumerateObject().Select(property => property.Name),
            document.RootElement.GetProperty("theme")
                .EnumerateObject()
                .Select(property => property.Name));
    }

    [Fact]
    public void ParseWrite_LegacyLiteralPreservesSchemaOneBytesAndNullStoragePath()
    {
        var expected = CanonicalUtf8(LegacyLiteralDraftJson);

        var parsed = DraftJsonCodec.Parse(expected);

        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        var draft = Assert.IsType<SkinDraftDocument>(parsed.Value);
        Assert.Equal(1, draft.DraftSchemaVersion);
        Assert.Null(draft.Assets[SkinAssetSlot.Center].StorageRelativePath);
        Assert.Equal(expected, DraftJsonCodec.Write(draft));
    }

    [Fact]
    public void ParseWrite_AddressedLiteralPreservesCanonicalAssetPropertyOrder()
    {
        var expected = CanonicalUtf8(AddressedLiteralDraftJson);

        var parsed = DraftJsonCodec.Parse(expected);

        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        var draft = Assert.IsType<SkinDraftDocument>(parsed.Value);
        Assert.Equal(
            AddressedCenterPath,
            draft.Assets[SkinAssetSlot.Center].StorageRelativePath);
        Assert.Equal(expected, DraftJsonCodec.Write(draft));
        using var document = JsonDocument.Parse(expected);
        Assert.Equal(
            ["slot", "relativePath", "storageRelativePath", "originalFileName"],
            document.RootElement.GetProperty("assets")[0]
                .EnumerateObject()
                .Select(property => property.Name));
    }

    [Fact]
    public void Parse_RejectsDuplicateStorageRelativePath()
    {
        var canonical = AddressedLiteralDraftJson.Replace("\r\n", "\n");
        var duplicate = canonical.Replace(
            $"      \"storageRelativePath\": \"{AddressedCenterPath}\",",
            $"      \"storageRelativePath\": \"{AddressedCenterPath}\",\n" +
            $"      \"storageRelativePath\": \"{AddressedCenterPath}\",",
            StringComparison.Ordinal);

        Assert.NotEqual(canonical, duplicate);
        AssertError(
            DraftJsonCodec.Parse(Encoding.UTF8.GetBytes(duplicate)),
            "json.duplicate-property",
            "$.assets[0].storageRelativePath");
    }

    [Theory]
    [InlineData(null)]
    [InlineData(17)]
    public void Parse_RejectsNullOrNonStringStorageRelativePath(object? value)
    {
        var bytes = Mutate(root =>
            root["assets"]!.AsArray()[0]!["storageRelativePath"] =
                JsonValue.Create(value));

        AssertError(
            DraftJsonCodec.Parse(bytes),
            "json.invalid-value",
            "$.assets[0].storageRelativePath");
    }

    [Theory]
    [MemberData(nameof(InvalidStorageRelativePaths))]
    public void Parse_RejectsInvalidStorageRelativePathMatrix(
        string relativePath,
        string storageRelativePath)
    {
        var bytes = Mutate(root =>
        {
            root["assets"]!.AsArray()[0]!["relativePath"] = relativePath;
            root["assets"]!.AsArray()[0]!["storageRelativePath"] =
                storageRelativePath;
        });

        AssertError(
            DraftJsonCodec.Parse(bytes),
            "draft.asset.storage-path.invalid",
            "$.assets[0].storageRelativePath");
    }

    public static TheoryData<string, string> InvalidStorageRelativePaths => new()
    {
        {
            "assets/background.png",
            "assets/sha256-AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.png"
        },
        {
            "assets/background.png",
            "assets/sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png"
        },
        {
            "assets/background.png",
            "assets/sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png"
        },
        {
            "assets/background.png",
            "assets/hash-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png"
        },
        {
            "assets/background.png",
            "assets/../sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png"
        },
        {
            "assets/background.png",
            "assets\\sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png"
        },
        {
            "assets/background.png",
            "C:/assets/sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png"
        },
        {
            "assets/background.png",
            "assets/sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpeg"
        },
        {
            "assets/background.png",
            "assets/sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.gif"
        },
        {
            "assets/background.png",
            "assets/sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg"
        }
    };

    [Theory]
    [InlineData("assets/background.png", "assets/center.png")]
    [InlineData("assets/background.png", "assets/center.jpg")]
    [InlineData("assets/background.jpg", "assets/center.png")]
    [InlineData("assets/background.jpg", "assets/center.jpg")]
    public void ValidateWriteParseWrite_AcceptsCanonicalPngJpgPathMatrix(
        string backgroundPath,
        string centerPath)
    {
        var draft = WithImagePaths(
            ValidDraft(),
            backgroundPath,
            centerPath);

        var validation = SkinDraftValidator.Validate(draft);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors));

        var firstBytes = DraftJsonCodec.Write(draft);
        var parsed = DraftJsonCodec.Parse(firstBytes);

        Assert.True(parsed.IsValid, string.Join("; ", parsed.Errors));
        var parsedDraft = Assert.IsType<SkinDraftDocument>(parsed.Value);
        Assert.Equal(
            backgroundPath,
            parsedDraft.Assets[SkinAssetSlot.Background].RelativePath);
        Assert.Equal(
            centerPath,
            parsedDraft.Assets[SkinAssetSlot.Center].RelativePath);
        Assert.Equal(firstBytes, DraftJsonCodec.Write(parsedDraft));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void Parse_RejectsUnsupportedDraftSchemaAtExactLocation(int schema)
    {
        var bytes = Mutate(root => root["draftSchemaVersion"] = schema);

        AssertError(
            DraftJsonCodec.Parse(bytes),
            "draft.schema.unsupported",
            "$.draftSchemaVersion");
    }

    [Fact]
    public void Parse_RejectsUnknownDuplicateAndMissingOuterProperties()
    {
        AssertError(
            DraftJsonCodec.Parse(Mutate(root => root["unexpected"] = true)),
            "json.unknown-property",
            "$.unexpected");
        AssertError(
            DraftJsonCodec.Parse(Mutate(root => root.Remove("projectName"))),
            "json.missing-property",
            "$.projectName");

        var canonical = Encoding.UTF8.GetString(
            DraftJsonCodec.Write(ValidDraft()));
        var duplicate = canonical.Replace(
            "  \"projectName\": \"Ocean project\",",
            "  \"projectName\": \"Ocean project\",\n  \"projectName\": \"Duplicate\",",
            StringComparison.Ordinal);
        Assert.NotEqual(canonical, duplicate);
        AssertError(
            DraftJsonCodec.Parse(Encoding.UTF8.GetBytes(duplicate)),
            "json.duplicate-property",
            "$.projectName");
    }

    [Fact]
    public void Parse_RejectsUnknownAndDuplicateAssetPropertiesAndSlots()
    {
        AssertError(
            DraftJsonCodec.Parse(Mutate(root =>
                root["assets"]!.AsArray()[0]!["unexpected"] = true)),
            "json.unknown-property",
            "$.assets[0].unexpected");

        var duplicateSlot = Mutate(root =>
        {
            var assets = root["assets"]!.AsArray();
            assets.Add(assets[0]!.DeepClone());
        });
        AssertError(
            DraftJsonCodec.Parse(duplicateSlot),
            "draft.asset.duplicate-slot",
            "$.assets[3].slot");

        var canonical = Encoding.UTF8.GetString(
            DraftJsonCodec.Write(ValidDraft()));
        var duplicateProperty = canonical.Replace(
            "      \"relativePath\": \"assets/background.png\",",
            "      \"relativePath\": \"assets/background.png\",\n      \"relativePath\": \"assets/background.jpg\",",
            StringComparison.Ordinal);
        Assert.NotEqual(canonical, duplicateProperty);
        AssertError(
            DraftJsonCodec.Parse(Encoding.UTF8.GetBytes(duplicateProperty)),
            "json.duplicate-property",
            "$.assets[0].relativePath");
    }

    [Fact]
    public void Parse_RejectsMissingAssetPropertyAndUnknownSlot()
    {
        AssertError(
            DraftJsonCodec.Parse(Mutate(root =>
                root["assets"]!.AsArray()[1]!.AsObject()
                    .Remove("originalFileName"))),
            "json.missing-property",
            "$.assets[1].originalFileName");
        AssertError(
            DraftJsonCodec.Parse(Mutate(root =>
                root["assets"]!.AsArray()[1]!["slot"] = "avatar")),
            "json.invalid-value",
            "$.assets[1].slot");
    }

    [Theory]
    [InlineData("draftId")]
    [InlineData("skinId")]
    [InlineData("originSkinId")]
    public void Parse_RejectsNonCanonicalGuidText(string field)
    {
        var uppercase = field == "originSkinId"
            ? "CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCCC"
            : field == "draftId"
                ? "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA"
                : "BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB";

        AssertError(
            DraftJsonCodec.Parse(Mutate(root => root[field] = uppercase)),
            "json.invalid-value",
            $"$.{field}");
    }

    [Theory]
    [InlineData("packageVersion", "01.2.3")]
    [InlineData("minimumHudVersion", "1.01.1")]
    public void Parse_RejectsNonCanonicalSemanticVersionText(
        string field,
        string value) =>
        AssertError(
            DraftJsonCodec.Parse(Mutate(root => root[field] = value)),
            "json.invalid-value",
            $"$.{field}");

    [Theory]
    [InlineData("createdAtUtc", "2026-08-02T00:00:00+00:00")]
    [InlineData("updatedAtUtc", "2026-08-02T09:01:02.0000000+09:00")]
    [InlineData("updatedAtUtc", "2026-08-02T00:00:00Z")]
    public void Parse_RejectsNonCanonicalOrOutOfOrderUtcTimestamps(
        string field,
        string value)
    {
        var result = DraftJsonCodec.Parse(Mutate(root => root[field] = value));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Location == $"$.{field}");
    }

    [Fact]
    public void Parse_RejectsNegativeRevision()
    {
        AssertError(
            DraftJsonCodec.Parse(Mutate(root => root["revision"] = -1)),
            "draft.revision.invalid",
            "$.revision");
    }

    [Theory]
    [InlineData("projectName", "")]
    [InlineData("displayName", "")]
    [InlineData("projectName", "safe\u0001name")]
    [InlineData("author", "safe\u0001author")]
    [InlineData("description", "safe\u0001description")]
    public void Parse_RejectsEmptyRequiredOrControlMetadata(
        string field,
        string value) =>
        AssertError(
            DraftJsonCodec.Parse(Mutate(root => root[field] = value)),
            "draft.metadata.invalid",
            $"$.{field}");

    [Theory]
    [InlineData("projectName", 81)]
    [InlineData("displayName", 81)]
    [InlineData("author", 81)]
    [InlineData("description", 501)]
    public void Parse_RejectsMetadataAboveSharedScalarLimits(
        string field,
        int length) =>
        AssertError(
            DraftJsonCodec.Parse(Mutate(root => root[field] = new string('x', length))),
            "draft.metadata.invalid",
            $"$.{field}");

    [Fact]
    public void Parse_AllowsEmptyEditableAuthorAndDescription()
    {
        var result = DraftJsonCodec.Parse(Mutate(root =>
        {
            root["author"] = string.Empty;
            root["description"] = string.Empty;
        }));

        Assert.True(result.IsValid, string.Join("; ", result.Errors));
    }

    [Theory]
    [InlineData(0, "C:/outside.png")]
    [InlineData(0, "assets/../background.png")]
    [InlineData(0, "assets/background.gif")]
    [InlineData(0, "assets/background.webp")]
    [InlineData(0, "assets/Background.png")]
    [InlineData(0, "assets/background.PNG")]
    [InlineData(1, "assets/background.png")]
    [InlineData(1, "assets/background.jpg")]
    [InlineData(1, "assets/Center.png")]
    [InlineData(1, "assets/center.JPG")]
    [InlineData(2, "assets/decoration.jpg")]
    [InlineData(2, "assets/Decoration.png")]
    public void Parse_RejectsAbsoluteTraversalUnsupportedOrWrongSlotPaths(
        int assetIndex,
        string path) =>
        AssertError(
            DraftJsonCodec.Parse(Mutate(root =>
                root["assets"]!.AsArray()[assetIndex]!["relativePath"] = path)),
            "draft.asset.path.invalid",
            $"$.assets[{assetIndex}].relativePath");

    [Theory]
    [InlineData(0, "assets/background.jpeg")]
    [InlineData(1, "assets/center.jpeg")]
    public void Parse_RejectsJpegExtensionWithoutImportNormalization(
        int assetIndex,
        string path) =>
        AssertError(
            DraftJsonCodec.Parse(Mutate(root =>
                root["assets"]!.AsArray()[assetIndex]!["relativePath"] = path)),
            "draft.asset.path.invalid",
            $"$.assets[{assetIndex}].relativePath");

    [Theory]
    [InlineData("C:\\images\\background.png")]
    [InlineData("../background.png")]
    [InlineData("folder/background.png")]
    [InlineData("safe\u0001name.png")]
    public void Parse_RejectsOriginalFileNameThatLeaksAPathOrControlText(
        string originalFileName) =>
        AssertError(
            DraftJsonCodec.Parse(Mutate(root =>
                root["assets"]!.AsArray()[0]!["originalFileName"] = originalFileName)),
            "draft.asset.original-name.invalid",
            "$.assets[0].originalFileName");

    [Fact]
    public void Parse_RejectsThemeThroughTheSharedValidatorAtThemeLocation()
    {
        var bytes = Mutate(root =>
            root["theme"]!["ringThickness"] = 16.001);

        AssertError(
            DraftJsonCodec.Parse(bytes),
            "draft.theme.invalid",
            "$.theme");
    }

    [Fact]
    public void Parse_RejectsUnknownThemePropertyAtThemeLocation()
    {
        var bytes = Mutate(root => root["theme"]!["script"] = "run.exe");

        AssertError(
            DraftJsonCodec.Parse(bytes),
            "draft.theme.invalid",
            "$.theme");
    }

    [Fact]
    public void Parse_RejectsMissingOrDuplicateThemePropertiesAtThemeLocation()
    {
        AssertError(
            DraftJsonCodec.Parse(Mutate(root =>
                root["theme"]!.AsObject().Remove("ringThickness"))),
            "draft.theme.invalid",
            "$.theme");

        var canonical = Encoding.UTF8.GetString(
            DraftJsonCodec.Write(ValidDraft()));
        var duplicate = canonical.Replace(
            "    \"ringThickness\": 8,",
            "    \"ringThickness\": 8,\n    \"ringThickness\": 9,",
            StringComparison.Ordinal);
        Assert.NotEqual(canonical, duplicate);
        AssertError(
            DraftJsonCodec.Parse(Encoding.UTF8.GetBytes(duplicate)),
            "draft.theme.invalid",
            "$.theme");
    }

    [Fact]
    public void Validate_RejectsEmptyOrCollidingDraftIdentity()
    {
        var draft = ValidDraft();
        AssertError(
            SkinDraftValidator.Validate(draft with { DraftId = Guid.Empty }),
            "draft.id.invalid",
            "$.draftId");
        AssertError(
            SkinDraftValidator.Validate(draft with { SkinId = Guid.Empty }),
            "draft.id.invalid",
            "$.skinId");
        AssertError(
            SkinDraftValidator.Validate(draft with { SkinId = draft.DraftId }),
            "draft.id.collision",
            "$.skinId");
    }

    [Fact]
    public void Validate_RejectsNonUtcAndOutOfOrderTimestamps()
    {
        var draft = ValidDraft();
        AssertError(
            SkinDraftValidator.Validate(draft with
            {
                UpdatedAtUtc = draft.UpdatedAtUtc.ToOffset(TimeSpan.FromHours(9))
            }),
            "draft.timestamp.invalid",
            "$.updatedAtUtc");
        AssertError(
            SkinDraftValidator.Validate(draft with
            {
                UpdatedAtUtc = draft.CreatedAtUtc.AddTicks(-1)
            }),
            "draft.timestamp.order",
            "$.updatedAtUtc");
    }

    [Fact]
    public void Validate_RejectsAssetDictionaryKeyAndSlotMismatch()
    {
        var draft = ValidDraft();
        var assets = draft.Assets.ToDictionary(pair => pair.Key, pair => pair.Value);
        assets[SkinAssetSlot.Background] = assets[SkinAssetSlot.Background] with
        {
            Slot = SkinAssetSlot.Center
        };

        AssertError(
            SkinDraftValidator.Validate(draft with { Assets = ReadOnly(assets) }),
            "draft.asset.slot.mismatch",
            "$.assets[0].slot");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"draftSchemaVersion\":1,}")]
    [InlineData("{\"draftSchemaVersion\":1/*comment*/}")]
    public void Parse_RejectsNonObjectOrIllegalJson(string json)
    {
        var result = DraftJsonCodec.Parse(Encoding.UTF8.GetBytes(json));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Location == "$");
    }

    [Fact]
    public void Write_RejectsUnsafeDraftBeforeSerializingIt()
    {
        var draft = ValidDraft();
        var unsafeAsset = draft.Assets[SkinAssetSlot.Background] with
        {
            RelativePath = "C:/private/account/background.png"
        };
        var assets = draft.Assets.ToDictionary(pair => pair.Key, pair => pair.Value);
        assets[SkinAssetSlot.Background] = unsafeAsset;

        var exception = Assert.Throws<ArgumentException>(
            () => DraftJsonCodec.Write(draft with { Assets = ReadOnly(assets) }));

        Assert.Contains("$.assets[0].relativePath", exception.Message);
    }

    private static SkinDraftDocument ValidDraft()
    {
        var created = new DateTimeOffset(
            2026,
            8,
            2,
            0,
            0,
            0,
            TimeSpan.Zero);
        var draft = SkinDraftFactory.CreateNew(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            created,
            SemanticVersion.Parse("1.1.1"));
        return draft with
        {
            Revision = 7,
            ProjectName = "Ocean project",
            DisplayName = "Ocean",
            Author = "Alice",
            PackageVersion = SemanticVersion.Parse("1.2.3"),
            Description = "Ocean ring",
            OriginSkinId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
            Assets = ReadOnly(new Dictionary<SkinAssetSlot, DraftAssetReference>
            {
                [SkinAssetSlot.Background] = new(
                    SkinAssetSlot.Background,
                    "assets/background.png",
                    "ocean background.png"),
                [SkinAssetSlot.Center] = new(
                    SkinAssetSlot.Center,
                    "assets/center.jpg",
                    "avatar.jpg"),
                [SkinAssetSlot.Decoration] = new(
                    SkinAssetSlot.Decoration,
                    "assets/decoration.png",
                    "outer.png")
            }),
            UpdatedAtUtc = created.AddMinutes(1)
        };
    }

    private static SkinDraftDocument WithImagePaths(
        SkinDraftDocument draft,
        string backgroundPath,
        string centerPath)
    {
        var assets = draft.Assets.ToDictionary(pair => pair.Key, pair => pair.Value);
        assets[SkinAssetSlot.Background] = assets[SkinAssetSlot.Background] with
        {
            RelativePath = backgroundPath,
            OriginalFileName = Path.GetFileName(backgroundPath)
        };
        assets[SkinAssetSlot.Center] = assets[SkinAssetSlot.Center] with
        {
            RelativePath = centerPath,
            OriginalFileName = Path.GetFileName(centerPath)
        };
        return draft with { Assets = ReadOnly(assets) };
    }

    private static byte[] Mutate(Action<JsonObject> mutation)
    {
        var root = JsonNode.Parse(DraftJsonCodec.Write(ValidDraft()))!.AsObject();
        mutation(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private static IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference> ReadOnly(
        IDictionary<SkinAssetSlot, DraftAssetReference> assets) =>
        new ReadOnlyDictionary<SkinAssetSlot, DraftAssetReference>(assets);

    private static byte[] CanonicalUtf8(string json) =>
        Encoding.UTF8.GetBytes(json.Replace("\r\n", "\n"));

    private static void AssertError(
        SkinValidationResult<SkinDraftDocument> result,
        string code,
        string location)
    {
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == code && error.Location == location);
    }

    private const string LegacyLiteralDraftJson = """
        {
          "draftSchemaVersion": 1,
          "draftId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "skinId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
          "revision": 7,
          "projectName": "Ocean project",
          "displayName": "Ocean",
          "author": "Alice",
          "packageVersion": "1.2.3",
          "description": "Ocean ring",
          "minimumHudVersion": "1.1.1",
          "originSkinId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
          "theme": {
            "schemaVersion": 1,
            "templateId": "free-decoration-ring",
            "background": {
              "offsetX": 0,
              "offsetY": 0,
              "scale": 1,
              "rotation": 0,
              "opacity": 1,
              "cropFocusX": 0.5,
              "cropFocusY": 0.5
            },
            "center": {
              "offsetX": 0,
              "offsetY": 0,
              "scale": 1,
              "rotation": 0,
              "opacity": 1,
              "cropFocusX": 0.5,
              "cropFocusY": 0.5
            },
            "decoration": {
              "offsetX": 0,
              "offsetY": 0,
              "scale": 1,
              "rotation": 0,
              "opacity": 1,
              "cropFocusX": 0.5,
              "cropFocusY": 0.5
            },
            "primaryRingColor": "#FF53DCF8",
            "secondaryRingColor": "#FF9A68FF",
            "baseBackgroundColor": "#FF0A1622",
            "baseBackgroundOpacity": 0.9,
            "ringDiameter": 96,
            "ringThickness": 8,
            "ringGap": 6,
            "startAngle": 270,
            "glowColor": "#FF24CFF2",
            "glowIntensity": 0.5,
            "numberTextSize": 28,
            "labelTextSize": 12,
            "textWeight": "semiBold",
            "textPlacement": "numberAboveLabel",
            "textOffsetY": 0,
            "textLineGap": 0,
            "animation": {
              "rotationIntensity": 0,
              "breathingIntensity": 0.55,
              "glowIntensity": 0.65,
              "floatingIntensity": 0,
              "refreshSpeedMultiplier": 2,
              "refreshHoldSeconds": 1.5
            }
          },
          "assets": [
            {
              "slot": "center",
              "relativePath": "assets/center.png",
              "originalFileName": "center.png"
            }
          ],
          "createdAtUtc": "2026-08-02T00:00:00.0000000Z",
          "updatedAtUtc": "2026-08-02T00:01:00.0000000Z"
        }
        """;

    private const string AddressedLiteralDraftJson = """
        {
          "draftSchemaVersion": 1,
          "draftId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
          "skinId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
          "revision": 7,
          "projectName": "Ocean project",
          "displayName": "Ocean",
          "author": "Alice",
          "packageVersion": "1.2.3",
          "description": "Ocean ring",
          "minimumHudVersion": "1.1.1",
          "originSkinId": "cccccccc-cccc-cccc-cccc-cccccccccccc",
          "theme": {
            "schemaVersion": 1,
            "templateId": "free-decoration-ring",
            "background": {
              "offsetX": 0,
              "offsetY": 0,
              "scale": 1,
              "rotation": 0,
              "opacity": 1,
              "cropFocusX": 0.5,
              "cropFocusY": 0.5
            },
            "center": {
              "offsetX": 0,
              "offsetY": 0,
              "scale": 1,
              "rotation": 0,
              "opacity": 1,
              "cropFocusX": 0.5,
              "cropFocusY": 0.5
            },
            "decoration": {
              "offsetX": 0,
              "offsetY": 0,
              "scale": 1,
              "rotation": 0,
              "opacity": 1,
              "cropFocusX": 0.5,
              "cropFocusY": 0.5
            },
            "primaryRingColor": "#FF53DCF8",
            "secondaryRingColor": "#FF9A68FF",
            "baseBackgroundColor": "#FF0A1622",
            "baseBackgroundOpacity": 0.9,
            "ringDiameter": 96,
            "ringThickness": 8,
            "ringGap": 6,
            "startAngle": 270,
            "glowColor": "#FF24CFF2",
            "glowIntensity": 0.5,
            "numberTextSize": 28,
            "labelTextSize": 12,
            "textWeight": "semiBold",
            "textPlacement": "numberAboveLabel",
            "textOffsetY": 0,
            "textLineGap": 0,
            "animation": {
              "rotationIntensity": 0,
              "breathingIntensity": 0.55,
              "glowIntensity": 0.65,
              "floatingIntensity": 0,
              "refreshSpeedMultiplier": 2,
              "refreshHoldSeconds": 1.5
            }
          },
          "assets": [
            {
              "slot": "center",
              "relativePath": "assets/center.png",
              "storageRelativePath": "assets/sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png",
              "originalFileName": "center.png"
            }
          ],
          "createdAtUtc": "2026-08-02T00:00:00.0000000Z",
          "updatedAtUtc": "2026-08-02T00:01:00.0000000Z"
        }
        """;
}
