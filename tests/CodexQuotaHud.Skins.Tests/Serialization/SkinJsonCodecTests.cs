using System.Text;
using CodexQuotaHud.Skins.Contracts;
using CodexQuotaHud.Skins.Serialization;

namespace CodexQuotaHud.Skins.Tests.Serialization;

public sealed class SkinJsonCodecTests
{
    private const string ValidManifestJson = """
        {"schemaVersion":1,"skinId":"11111111-1111-1111-1111-111111111111","displayName":"Ocean","author":"Alice","packageVersion":"1.2.3","description":"Ocean ring","templateId":"free-decoration-ring","minimumHudVersion":"1.1.1","originSkinId":null,"assets":[{"slot":"background","path":"assets/background.png","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},{"slot":"center","path":"assets/center.jpg","sha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"},{"slot":"decoration","path":"assets/decoration.png","sha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}]}
        """;

    private const string ValidThemeJson = """
        {"schemaVersion":1,"templateId":"free-decoration-ring","background":{"offsetX":0,"offsetY":0,"scale":1,"rotation":0,"opacity":1,"cropFocusX":0.5,"cropFocusY":0.5},"center":{"offsetX":0,"offsetY":0,"scale":1,"rotation":0,"opacity":1,"cropFocusX":0.5,"cropFocusY":0.5},"decoration":{"offsetX":0,"offsetY":0,"scale":1,"rotation":0,"opacity":1,"cropFocusX":0.5,"cropFocusY":0.5},"primaryRingColor":"#FF53DCF8","secondaryRingColor":"#FF9A68FF","baseBackgroundColor":"#FF0A1622","baseBackgroundOpacity":0.9,"ringDiameter":96,"ringThickness":8,"ringGap":6,"startAngle":270,"glowColor":"#FF24CFF2","glowIntensity":0.5,"numberTextSize":28,"labelTextSize":12,"textWeight":"semiBold","textPlacement":"numberAboveLabel","animation":{"rotationIntensity":0.25,"breathingIntensity":0.5,"glowIntensity":0.75,"floatingIntensity":1}}
        """;

    [Fact]
    public void ParseManifest_RejectsUnknownAndDuplicateProperties()
    {
        var unknown = Utf8(ValidManifestJson.Replace(
            "\"assets\":", "\"mystery\":true,\"assets\":"));
        var duplicate = Utf8(ValidManifestJson.Replace(
            "\"displayName\":\"Ocean\"",
            "\"displayName\":\"Ocean\",\"displayName\":\"Other\""));

        AssertError(
            SkinJsonCodec.ParseManifest(unknown),
            "json.unknown-property",
            "$.mystery");
        AssertError(
            SkinJsonCodec.ParseManifest(duplicate),
            "json.duplicate-property",
            "$.displayName");
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("1e999")]
    public void ParseTheme_RejectsNonFiniteNumbers(string token)
    {
        var json = Utf8(ValidThemeJson.Replace(
            "\"ringDiameter\":96",
            $"\"ringDiameter\":{token}"));

        Assert.False(SkinJsonCodec.ParseTheme(json).IsValid);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"manifest\"")]
    [InlineData("1")]
    public void ParseManifest_RejectsNonObjectRoot(string json) =>
        AssertError(
            SkinJsonCodec.ParseManifest(Utf8(json)),
            "json.wrong-kind",
            "$");

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"theme\"")]
    [InlineData("1")]
    public void ParseTheme_RejectsNonObjectRoot(string json) =>
        AssertError(
            SkinJsonCodec.ParseTheme(Utf8(json)),
            "json.wrong-kind",
            "$");

    [Fact]
    public void ParseManifest_RejectsMissingAndNestedExtraProperties()
    {
        var missing = ValidManifestJson.Replace("\"author\":\"Alice\",", "");
        var extraAsset = ValidManifestJson.Replace(
            "\"sha256\":\"aaaaaaaa",
            "\"note\":true,\"sha256\":\"aaaaaaaa");

        AssertError(
            SkinJsonCodec.ParseManifest(Utf8(missing)),
            "json.missing-property",
            "$.author");
        AssertError(
            SkinJsonCodec.ParseManifest(Utf8(extraAsset)),
            "json.unknown-property",
            "$.assets[0].note");
    }

    [Fact]
    public void ParseTheme_RejectsMissingAndNestedExtraProperties()
    {
        var missing = ValidThemeJson.Replace("\"ringGap\":6,", "");
        var extraTransform = ValidThemeJson.Replace(
            "\"background\":{\"offsetX\":0,",
            "\"background\":{\"extra\":0,\"offsetX\":0,");
        var extraAnimation = ValidThemeJson.Replace(
            "\"animation\":{",
            "\"animation\":{\"extra\":0,");

        AssertError(
            SkinJsonCodec.ParseTheme(Utf8(missing)),
            "json.missing-property",
            "$.ringGap");
        AssertError(
            SkinJsonCodec.ParseTheme(Utf8(extraTransform)),
            "json.unknown-property",
            "$.background.extra");
        AssertError(
            SkinJsonCodec.ParseTheme(Utf8(extraAnimation)),
            "json.unknown-property",
            "$.animation.extra");
    }

    [Fact]
    public void ParseManifest_RejectsWrongNestedKinds()
    {
        var assetsNotArray = ReplaceArrayValue(
            ValidManifestJson,
            "\"assets\":",
            "{}");
        var assetNotObject = ValidManifestJson.Replace(
            ValidManifestJson[ValidManifestJson.IndexOf("[{", StringComparison.Ordinal)..],
            "[true]}");

        AssertError(
            SkinJsonCodec.ParseManifest(Utf8(assetsNotArray)),
            "json.wrong-kind",
            "$.assets");
        AssertError(
            SkinJsonCodec.ParseManifest(Utf8(assetNotObject)),
            "json.wrong-kind",
            "$.assets[0]");
    }

    [Theory]
    [InlineData("background", "[]")]
    [InlineData("center", "null")]
    [InlineData("decoration", "1")]
    [InlineData("animation", "false")]
    public void ParseTheme_RejectsWrongNestedKinds(
        string property,
        string replacement)
    {
        var start = $"\"{property}\":";
        var json = ReplaceObjectValue(ValidThemeJson, start, replacement);

        AssertError(
            SkinJsonCodec.ParseTheme(Utf8(json)),
            "json.wrong-kind",
            $"$.{property}");
    }

    [Theory]
    [InlineData("skinId", "not-a-guid")]
    [InlineData("skinId", "11111111111111111111111111111111")]
    [InlineData("packageVersion", "01.2.3")]
    [InlineData("packageVersion", "1.2")]
    [InlineData("minimumHudVersion", "1.2.3-beta")]
    [InlineData("originSkinId", "not-a-guid")]
    public void ParseManifest_RejectsMalformedTypedStrings(
        string property,
        string replacement)
    {
        var oldValue = property switch
        {
            "skinId" => "11111111-1111-1111-1111-111111111111",
            "packageVersion" => "1.2.3",
            "minimumHudVersion" => "1.1.1",
            "originSkinId" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(property))
        };
        var json = property == "originSkinId"
            ? ValidManifestJson.Replace(
                "\"originSkinId\":null",
                $"\"originSkinId\":\"{replacement}\"")
            : ValidManifestJson.Replace(
                $"\"{property}\":\"{oldValue}\"",
                $"\"{property}\":\"{replacement}\"");

        AssertError(
            SkinJsonCodec.ParseManifest(Utf8(json)),
            "json.invalid-value",
            $"$.{property}");
    }

    [Theory]
    [InlineData("Background")]
    [InlineData("outer")]
    [InlineData("")]
    public void ParseManifest_RejectsUnknownAssetSlotStrings(string slot)
    {
        var json = ValidManifestJson.Replace(
            "\"slot\":\"background\"",
            $"\"slot\":\"{slot}\"");

        AssertError(
            SkinJsonCodec.ParseManifest(Utf8(json)),
            "json.invalid-value",
            "$.assets[0].slot");
    }

    [Theory]
    [InlineData("textWeight", "SemiBold")]
    [InlineData("textWeight", "medium")]
    [InlineData("textPlacement", "NumberAboveLabel")]
    [InlineData("textPlacement", "free")]
    public void ParseTheme_RejectsUnknownEnumStrings(
        string property,
        string value)
    {
        var oldValue = property == "textWeight"
            ? "semiBold"
            : "numberAboveLabel";
        var json = ValidThemeJson.Replace(
            $"\"{property}\":\"{oldValue}\"",
            $"\"{property}\":\"{value}\"");

        AssertError(
            SkinJsonCodec.ParseTheme(Utf8(json)),
            "json.invalid-value",
            $"$.{property}");
    }

    [Theory]
    [InlineData("{/* comment */\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,}")]
    public void Parser_RejectsCommentsAndTrailingCommas(string json)
    {
        AssertError(
            SkinJsonCodec.ParseManifest(Utf8(json)),
            "json.invalid",
            "$");
    }

    [Fact]
    public void Writers_UseCanonicalUtf8AndRoundTripByteForByte()
    {
        var manifest = AssertValid(SkinJsonCodec.ParseManifest(Utf8(ValidManifestJson)));
        var theme = AssertValid(SkinJsonCodec.ParseTheme(Utf8(ValidThemeJson)));

        var manifestBytes = SkinJsonCodec.WriteManifest(manifest);
        var themeBytes = SkinJsonCodec.WriteTheme(theme);

        Assert.Equal(Utf8(CanonicalManifestJson), manifestBytes);
        Assert.Equal(Utf8(CanonicalThemeJson), themeBytes);
        Assert.DoesNotContain((byte)'\r', manifestBytes);
        Assert.DoesNotContain((byte)'\r', themeBytes);
        Assert.False(manifestBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));
        Assert.False(themeBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }));

        var reparsedManifest = AssertValid(SkinJsonCodec.ParseManifest(manifestBytes));
        var reparsedTheme = AssertValid(SkinJsonCodec.ParseTheme(themeBytes));
        Assert.Equal(manifestBytes, SkinJsonCodec.WriteManifest(reparsedManifest));
        Assert.Equal(themeBytes, SkinJsonCodec.WriteTheme(reparsedTheme));
    }

    private static readonly string CanonicalManifestJson = """
        {
          "schemaVersion": 1,
          "skinId": "11111111-1111-1111-1111-111111111111",
          "displayName": "Ocean",
          "author": "Alice",
          "packageVersion": "1.2.3",
          "description": "Ocean ring",
          "templateId": "free-decoration-ring",
          "minimumHudVersion": "1.1.1",
          "originSkinId": null,
          "assets": [
            {
              "slot": "background",
              "path": "assets/background.png",
              "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
            },
            {
              "slot": "center",
              "path": "assets/center.jpg",
              "sha256": "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"
            },
            {
              "slot": "decoration",
              "path": "assets/decoration.png",
              "sha256": "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"
            }
          ]
        }
        """;

    private static readonly string CanonicalThemeJson = """
        {
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
          "animation": {
            "rotationIntensity": 0.25,
            "breathingIntensity": 0.5,
            "glowIntensity": 0.75,
            "floatingIntensity": 1
          }
        }
        """;

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);

    private static T AssertValid<T>(SkinValidationResult<T> result)
    {
        Assert.True(result.IsValid, string.Join("; ", result.Errors));
        return Assert.IsType<T>(result.Value);
    }

    private static void AssertError<T>(
        SkinValidationResult<T> result,
        string code,
        string location)
    {
        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            error => error.Code == code && error.Location == location);
    }

    private static string ReplaceObjectValue(
        string json,
        string propertyPrefix,
        string replacement)
    {
        var propertyIndex = json.IndexOf(propertyPrefix, StringComparison.Ordinal);
        Assert.True(propertyIndex >= 0);
        var objectStart = propertyIndex + propertyPrefix.Length;
        Assert.Equal('{', json[objectStart]);

        var depth = 0;
        for (var index = objectStart; index < json.Length; index++)
        {
            depth += json[index] switch
            {
                '{' => 1,
                '}' => -1,
                _ => 0
            };
            if (depth == 0)
            {
                return string.Concat(
                    json.AsSpan(0, objectStart),
                    replacement,
                    json.AsSpan(index + 1));
            }
        }

        throw new InvalidOperationException("Object terminator not found.");
    }

    private static string ReplaceArrayValue(
        string json,
        string propertyPrefix,
        string replacement)
    {
        var propertyIndex = json.IndexOf(propertyPrefix, StringComparison.Ordinal);
        Assert.True(propertyIndex >= 0);
        var arrayStart = propertyIndex + propertyPrefix.Length;
        Assert.Equal('[', json[arrayStart]);

        var depth = 0;
        for (var index = arrayStart; index < json.Length; index++)
        {
            depth += json[index] switch
            {
                '[' => 1,
                ']' => -1,
                _ => 0
            };
            if (depth == 0)
            {
                return string.Concat(
                    json.AsSpan(0, arrayStart),
                    replacement,
                    json.AsSpan(index + 1));
            }
        }

        throw new InvalidOperationException("Array terminator not found.");
    }
}
