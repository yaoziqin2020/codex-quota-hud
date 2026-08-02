# Optional Skin Designer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a safe data-only `.cqskin` runtime to every Codex Quota HUD installation and an opt-in, separately installed Skin Designer that creates, previews, applies, edits, and exports the first `自由装饰环` template.

**Architecture:** A new `CodexQuotaHud.Skins` WPF class library owns versioned contracts, strict JSON/archive validation, deterministic packaging, atomic installed-skin storage, the template registry, and the production renderer. `CodexQuotaHud.App` adapts that shared renderer into the existing `QuotaOrbWindow`, menus, settings, tray, edge, and per-user control path; `CodexQuotaHud.SkinDesigner` is a separate executable with its own mutex and draft lifecycle, references the App project only to reuse the existing synthetic Preview composition, and is never referenced by the normal HUD. Inno Setup always installs the runtime/import path and conditionally installs the designer under an unchecked `designer` component.

**Tech Stack:** C# 13, .NET SDK 9.0.316, `net9.0`/`net9.0-windows`, WPF, Windows Forms tray integration, `System.Text.Json`, `System.IO.Compression`, named pipes and named mutexes, xUnit 2.9.2, PowerShell 5.1, Inno Setup 6; no new NuGet runtime dependency.

## Global Constraints

- Windows 10/11 x64 remains the supported runtime; source builds use exactly .NET SDK `9.0.316`.
- The normal Setup path stays lightweight: `安装皮肤设计器` / `Install Skin Designer` is visible but not selected by default.
- Every installation always contains the five built-in skins, custom-skin runtime, `.cqskin` import/select/remove UI, startup behavior, details, tray, edge docking, and existing quota acquisition.
- The normal HUD must not reference or load `CodexQuotaHud.SkinDesigner`; the designer has a separate executable and single-instance identity and never connects to `codex app-server` or registers startup.
- Setup creates no designer desktop shortcut, no designer startup entry, and no raw `--preview` shortcut; it creates a designer Start-menu entry only when the optional component is selected.
- The hidden `CodexQuotaHud.App.exe --preview` diagnostic remains source/ZIP-only and keeps synthetic in-memory data isolated from formal settings.
- `.cqskin` is data-only: no XAML, DLL, EXE, JavaScript, PowerShell, arbitrary element tree, executable expression, remote URL, symlink, or reparse-point content is accepted.
- Package limits are exact: compressed file size `<= 50 MiB`, extracted bytes `<= 64 MiB`, entry count `<= 64`, each encoded image `<= 16 MiB`, decoded dimensions `<= 8192 × 8192`, and combined decoded pixels `<= 67,108,864`.
- The accepted image formats are decoded PNG and JPEG only; background/center accept PNG or JPEG, while outer decoration requires transparent-capable PNG.
- Display name and author are limited to 80 Unicode scalar values; description is limited to 500; all numbers must be finite and every JSON property must be known for schema version `1`.
- Free Decoration Ring bounds are exact: image offsets `-50..50` DIP, scale `0.25..3.0`, rotation `-180..180`, opacity/crop focus/effect intensity `0..1`, ring diameter `72..116` DIP, ring thickness `2..16` DIP, gap `2..24` DIP, start angle `0..359`, and text size `12..34` DIP.
- System-owned rings, labels, and numbers render above every package image layer. Existing `QuotaAlertPolicy` overrides package ring colors at `>10%..20%` with `#FFFFB547` and at `<=10%` with `#FFFF5A67`, independently for primary and secondary quota.
- Custom selection keys are `custom:<lowercase-guid-d>`; built-ins remain exactly `builtin:HudDial`, `builtin:EnergyRing`, `builtin:LiquidGlass`, `builtin:Aurora`, and `builtin:LiquidTank`.
- Imported assets are copied into owned storage. Staging is operation-scoped and cleaned after success or failure. Replacement is staged and rollback-safe; failed activation never changes the formal selection.
- Normal uninstall preserves settings, installed skins, designer drafts, and recovery state. Existing explicit purge removes the exact `%LOCALAPPDATA%\CodexQuotaHud` root, including `skins`, `designer`, and `imports`, through the current path/reparse safety checks.
- The existing five `SkinId` enum values and renderers remain stable. Existing click/double-click, 60-second refresh, monitor placement, edge geometry, tray behavior, app-server protocol, startup, and two-direction Preview handoff semantics do not change.
- No tag, upload, release, local production install, GUI launch, or public release claim occurs while executing implementation tasks unless the user separately authorizes that external action.

---

## File and ownership map

| Unit | Responsibility |
|---|---|
| `src/CodexQuotaHud.Skins/Contracts/*` | Schema-v1 manifest/theme/asset/version/result types and exact constants; no App or designer types. |
| `src/CodexQuotaHud.Skins/Serialization/*` | Strict known-property JSON parsing and canonical UTF-8 serialization. |
| `src/CodexQuotaHud.Skins/Packaging/*` | Secure archive inspection, decoded-image verification, deterministic `.cqskin` writing, and SHA-256 checking. |
| `src/CodexQuotaHud.Skins/Storage/*` | Exact Local App Data paths, installed catalog, staging, collision policy, atomic promote/rollback/remove. |
| `src/CodexQuotaHud.Skins/Templates/*` | `ISkinTemplate`, versioned registry, Free Decoration Ring bounds, renderer, and animation surface. |
| `src/CodexQuotaHud.App/UI/Skins/*` | Adapter from string selection keys and installed packages to existing `IQuotaSkin`/animation contracts; built-in factories stay here. |
| `src/CodexQuotaHud.App/UI/SkinManagement/*` | Import preview, Replace/Keep a copy/Cancel, custom removal, designer discovery/launch, and shared menu models. |
| `src/CodexQuotaHud.App/Infrastructure/LocalControl/*` | Typed per-user named-pipe command transport; shutdown compatibility event remains intact. |
| `src/CodexQuotaHud.SkinDesigner/*` | Separate composition root, draft domain, recovery/undo, split-view editor, image workflow, Preview bridge, apply/export, and designer-only mutex. |
| `installer/` and `scripts/` | Publish two executables, package normal ZIP fallback, add/remove optional Inno component, preserve data, and run isolated installer smoke. |
| `tests/CodexQuotaHud.Skins.Tests/` | Contract, security, packaging, storage, and renderer tests. |
| `tests/CodexQuotaHud.SkinDesigner.Tests/` | Draft/history/recovery/UI/composition/apply tests. |
| Existing Core/App test projects | Settings migration, HUD integration, IPC, menus, packaging, and built-in regression tests. |

## Task dependency order

1. Tasks 1–3 establish immutable contracts and reject unsafe input before any install/render path exists.
2. Tasks 4–6 add deterministic output, owned storage, and the one registered template/renderer.
3. Tasks 7–10 migrate formal settings, integrate custom skins into the HUD, add management UI, then add typed activation IPC.
4. Tasks 11–15 build designer state, recovery/history, Preview bridge, editor workflow, and its independent application lifetime on the stable runtime.
5. Tasks 16–17 publish/package both executables and prove optional-component add/remove/preserve behavior.
6. Task 18 records documentation, full automated evidence, and explicit real-Windows acceptance without making an unauthorized release claim.

---

### Task 1: Shared skin project and exact schema-v1 contracts

**Files:**
- Create: `src/CodexQuotaHud.Skins/CodexQuotaHud.Skins.csproj`
- Create: `src/CodexQuotaHud.Skins/Contracts/SemanticVersion.cs`
- Create: `src/CodexQuotaHud.Skins/Contracts/SkinPackageLimits.cs`
- Create: `src/CodexQuotaHud.Skins/Contracts/SkinContracts.cs`
- Create: `src/CodexQuotaHud.Skins/Storage/SkinStoragePaths.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/CodexQuotaHud.Skins.Tests.csproj`
- Create: `tests/CodexQuotaHud.Skins.Tests/Contracts/SkinContractTests.cs`
- Modify: `CodexQuotaHud.sln`

**Interfaces:**
- Produces: `SemanticVersion.Parse(string)`, `TryParse(string, out SemanticVersion)`, `CompareTo(SemanticVersion)`, and canonical `ToString()` as `major.minor.patch`.
- Produces: `SkinManifest`, `SkinTheme`, `SkinAssetReference`, `SkinImageTransform`, `SkinAnimationSettings`, `SkinPackageDocument`, `SkinValidationError`, and `SkinValidationResult<T>` with the exact declarations below.
- Produces: `SkinStoragePaths(string localAppDataRoot)` and exact `SettingsRoot`, `InstalledSkinsRoot`, `DraftsRoot`, and `ImportsRoot` paths.
- Consumes: `CodexQuotaHud.Core` only; the new library must not reference `CodexQuotaHud.App` or the designer.

- [ ] **Step 1: Write the failing contract tests**

Create tests with literal boundary and identity assertions:

```csharp
[Theory]
[InlineData("0.0.0", 0, 0, 0)]
[InlineData("12.34.56", 12, 34, 56)]
public void SemanticVersion_RoundTripsCanonicalTriplets(
    string text, int major, int minor, int patch)
{
    var version = SemanticVersion.Parse(text);
    Assert.Equal((major, minor, patch),
        (version.Major, version.Minor, version.Patch));
    Assert.Equal(text, version.ToString());
}

[Theory]
[InlineData("1")]
[InlineData("1.2")]
[InlineData("1.2.3.4")]
[InlineData("1.2.3-beta")]
[InlineData("01.2.3")]
[InlineData("-1.2.3")]
public void SemanticVersion_RejectsAnythingOutsideUnsignedCanonicalTriplet(
    string text) =>
    Assert.False(SemanticVersion.TryParse(text, out _));

[Fact]
public void StoragePaths_AreExactChildrenOfLocalAppData()
{
    var paths = new SkinStoragePaths(@"C:\Users\Test\AppData\Local");
    Assert.Equal(@"C:\Users\Test\AppData\Local\CodexQuotaHud", paths.SettingsRoot);
    Assert.Equal(@"C:\Users\Test\AppData\Local\CodexQuotaHud\skins", paths.InstalledSkinsRoot);
    Assert.Equal(@"C:\Users\Test\AppData\Local\CodexQuotaHud\designer\drafts", paths.DraftsRoot);
    Assert.Equal(@"C:\Users\Test\AppData\Local\CodexQuotaHud\imports", paths.ImportsRoot);
}

[Fact]
public void ContractConstants_MatchApprovedSchemaAndLimits()
{
    Assert.Equal(1, SkinPackageLimits.SchemaVersion);
    Assert.Equal("free-decoration-ring", SkinPackageLimits.FreeDecorationRingTemplateId);
    Assert.Equal(50L * 1024 * 1024, SkinPackageLimits.MaximumPackageBytes);
    Assert.Equal(64L * 1024 * 1024, SkinPackageLimits.MaximumExtractedBytes);
    Assert.Equal(64, SkinPackageLimits.MaximumEntries);
    Assert.Equal(16L * 1024 * 1024, SkinPackageLimits.MaximumImageBytes);
    Assert.Equal(8192, SkinPackageLimits.MaximumImageDimension);
    Assert.Equal(67_108_864L, SkinPackageLimits.MaximumDecodedPixels);
}
```

Also construct one complete `SkinManifest` and `SkinTheme` and assert their
record equality. The fixture uses skin ID
`11111111-1111-1111-1111-111111111111`, package version `1.2.3`, minimum HUD
`1.1.1`, and all three slots so later tasks share one stable literal.

- [ ] **Step 2: Run the focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release
```

Expected: MSBuild fails because the test project and contract types do not yet
exist.

- [ ] **Step 3: Add projects and exact contract declarations**

Use this project boundary:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\CodexQuotaHud.Core\CodexQuotaHud.Core.csproj" />
  </ItemGroup>
  <PropertyGroup>
    <TargetFramework>net9.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

Define these exact public types in `SkinContracts.cs`:

```csharp
public enum SkinAssetSlot { Background, Center, Decoration }
public enum SkinTextWeight { Regular, SemiBold, Bold }
public enum SkinTextPlacement { Centered, NumberAboveLabel, LabelAboveNumber }

public sealed record SkinAssetReference(
    SkinAssetSlot Slot, string Path, string Sha256);

public sealed record SkinImageTransform(
    double OffsetX, double OffsetY, double Scale, double Rotation,
    double Opacity, double CropFocusX, double CropFocusY);

public sealed record SkinAnimationSettings(
    double RotationIntensity, double BreathingIntensity,
    double GlowIntensity, double FloatingIntensity);

public sealed record SkinManifest(
    int SchemaVersion, Guid SkinId, string DisplayName, string Author,
    SemanticVersion PackageVersion, string Description, string TemplateId,
    SemanticVersion MinimumHudVersion, Guid? OriginSkinId,
    IReadOnlyList<SkinAssetReference> Assets);

public sealed record SkinTheme(
    int SchemaVersion, string TemplateId,
    SkinImageTransform Background, SkinImageTransform Center,
    SkinImageTransform Decoration, string PrimaryRingColor,
    string SecondaryRingColor, string BaseBackgroundColor,
    double BaseBackgroundOpacity, double RingDiameter,
    double RingThickness, double RingGap, double StartAngle,
    string GlowColor, double GlowIntensity, double NumberTextSize,
    double LabelTextSize, SkinTextWeight TextWeight,
    SkinTextPlacement TextPlacement, SkinAnimationSettings Animation);

public sealed record SkinAsset(
    SkinAssetSlot Slot, string RelativePath, byte[] Content,
    int PixelWidth, int PixelHeight, bool HasAlpha);

public sealed record SkinPackageDocument(
    SkinManifest Manifest, SkinTheme Theme,
    IReadOnlyDictionary<SkinAssetSlot, SkinAsset> Assets);

public sealed record SkinValidationError(
    string Code, string Location, string Message);

public sealed record SkinValidationResult<T>(
    T? Value, IReadOnlyList<SkinValidationError> Errors)
{
    public bool IsValid => Errors.Count == 0 && Value is not null;
}
```

`SkinPackageLimits` contains every literal asserted in Step 1 plus string
limits `80/80/500`, template geometry bounds from Global Constraints, and
fixed names `manifest.json`, `theme.json`, and `assets/`. `SemanticVersion`
uses invariant `int.TryParse`, rejects leading zeroes except literal `0`, and
compares major, minor, then patch. `SkinStoragePaths` normalizes the supplied
Local App Data root with `Path.GetFullPath` and combines only the exact child
segments asserted above.

- [ ] **Step 4: Add all four projects to the solution and verify GREEN**

Add `CodexQuotaHud.Skins` under the solution `src` folder and
`CodexQuotaHud.Skins.Tests` under `tests`; preserve existing project GUIDs.
Then run the Step 2 command.

Expected: all `SkinContractTests` pass and the new library has no App/designer
project reference.

- [ ] **Step 5: Commit**

```powershell
git add CodexQuotaHud.sln src/CodexQuotaHud.Skins tests/CodexQuotaHud.Skins.Tests
git commit -m "feat: define skin package contracts"
```

---

### Task 2: Strict canonical manifest and theme JSON

**Files:**
- Create: `src/CodexQuotaHud.Skins/Serialization/SkinJsonCodec.cs`
- Create: `src/CodexQuotaHud.Skins/Validation/SkinContractValidator.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Serialization/SkinJsonCodecTests.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Validation/SkinContractValidatorTests.cs`

**Interfaces:**
- Produces: `SkinJsonCodec.ParseManifest(ReadOnlySpan<byte>)`, `ParseTheme(ReadOnlySpan<byte>)`, `WriteManifest(SkinManifest)`, and `WriteTheme(SkinTheme)`.
- Produces: `SkinContractValidator.Validate(SkinManifest, SkinTheme, SemanticVersion installedHudVersion)` returning `SkinValidationResult<(SkinManifest Manifest, SkinTheme Theme)>`.
- Consumes: schema-v1 contracts from Task 1; no filesystem, ZIP, WPF image, or installation work.

- [ ] **Step 1: Write failing strict-parser tests**

Use hand-authored UTF-8 JSON, not the production writer, for rejection tests:

```csharp
[Fact]
public void ParseManifest_RejectsUnknownAndDuplicateProperties()
{
    var unknown = Utf8(ValidManifestJson.Replace(
        "\"assets\":", "\"mystery\":true,\"assets\":"));
    var duplicate = Utf8(ValidManifestJson.Replace(
        "\"displayName\":\"Ocean\"",
        "\"displayName\":\"Ocean\",\"displayName\":\"Other\""));

    AssertError(SkinJsonCodec.ParseManifest(unknown), "json.unknown-property", "$.mystery");
    AssertError(SkinJsonCodec.ParseManifest(duplicate), "json.duplicate-property", "$.displayName");
}

[Theory]
[InlineData("NaN")]
[InlineData("Infinity")]
[InlineData("1e999")]
public void ParseTheme_RejectsNonFiniteNumbers(string token)
{
    var json = Utf8(ValidThemeJson.Replace("\"ringDiameter\":96", $"\"ringDiameter\":{token}"));
    Assert.False(SkinJsonCodec.ParseTheme(json).IsValid);
}
```

Add literal tests for wrong JSON kind, missing/extra properties, strings over
80/80/500 Unicode scalar values (include surrogate-pair emoji), malformed
GUID/hash/version/color, undeclared path forms,
unknown enum strings, schema `0/2`, template mismatch, reserved built-in IDs,
minimum HUD `9.0.0`, and every lower/upper geometry boundary plus one value on
each invalid side.

The `assets` property itself is required. Add explicit valid manifests with
`0`, `1`, `2`, and `3` distinct slots and one invalid manifest containing the
same slot twice. No test expects a missing optional slot to be rejected.

- [ ] **Step 2: Run the parser/contract tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinJsonCodecTests|FullyQualifiedName~SkinContractValidatorTests"
```

Expected: compilation fails because `SkinJsonCodec` and
`SkinContractValidator` do not exist.

- [ ] **Step 3: Implement explicit known-property parsing**

Parse with `JsonDocumentOptions` that disallow comments and trailing commas.
Before reading a value, enumerate each object into a case-sensitive
`HashSet<string>` and compare with the exact property set. Return one or more
stable errors rather than throwing package-controlled text. Use these root
names exactly:

```text
manifest: schemaVersion, skinId, displayName, author, packageVersion,
description, templateId, minimumHudVersion, originSkinId, assets
asset: slot, path, sha256
theme: schemaVersion, templateId, background, center, decoration,
primaryRingColor, secondaryRingColor, baseBackgroundColor,
baseBackgroundOpacity, ringDiameter, ringThickness, ringGap, startAngle,
glowColor, glowIntensity, numberTextSize, labelTextSize, textWeight,
textPlacement, animation
transform: offsetX, offsetY, scale, rotation, opacity, cropFocusX, cropFocusY
animation: rotationIntensity, breathingIntensity, glowIntensity,
floatingIntensity
```

Writer output uses lower camel case, two-space indentation, LF newlines, UTF-8
without BOM, the property order above, lowercase GUID `D`, lowercase SHA-256,
and enum names `background|center|decoration`,
`regular|semiBold|bold`, and
`centered|numberAboveLabel|labelAboveNumber`. Do not use a permissive
reflection serializer for package input.

- [ ] **Step 4: Implement exact semantic validation**

`SkinContractValidator` rejects:

```csharp
private static readonly HashSet<Guid> ReservedIds =
[
    Guid.Parse("10000000-0000-0000-0000-000000000001"), // HudDial
    Guid.Parse("10000000-0000-0000-0000-000000000002"), // EnergyRing
    Guid.Parse("10000000-0000-0000-0000-000000000003"), // LiquidGlass
    Guid.Parse("10000000-0000-0000-0000-000000000004"), // Aurora
    Guid.Parse("10000000-0000-0000-0000-000000000005")  // LiquidTank
];
```

It requires schema `1`, template `free-decoration-ring`, non-empty non-control
metadata within scalar limits, canonical semantic versions, minimum HUD no
greater than the injected installed version, exact `#AARRGGBB` colors,
an `assets` collection with each slot present zero or one time, fixed relative
names under `assets/` for the slots that are present, decoration `.png`,
background/center `.png|.jpg|.jpeg`, 64 lowercase hex hash characters, and all
Global Constraint numeric bounds. JSON numbers outside bounds are errors; no
package value is clamped. `OriginSkinId` may be non-null only in an installed
local copy and must differ from `SkinId`; package-reader validation in Task 3
passes `allowLocalProvenance: false` and installed-store validation in Task 5
passes `true`.

- [ ] **Step 5: Verify canonical round-trip and GREEN**

Add a round-trip test that parses writer output, writes it again, and compares
bytes exactly. Run the Step 2 command.

Expected: every parser/validator test passes; error `Location` identifies the
field such as `$.ringThickness` or `$.assets[1].sha256`.

- [ ] **Step 6: Commit**

```powershell
git add src/CodexQuotaHud.Skins/Serialization src/CodexQuotaHud.Skins/Validation tests/CodexQuotaHud.Skins.Tests
git commit -m "feat: validate skin schema strictly"
```

---

### Task 3: Secure `.cqskin` archive and decoded-image validation

**Files:**
- Create: `src/CodexQuotaHud.Skins/Packaging/SkinPackageReader.cs`
- Create: `src/CodexQuotaHud.Skins/Packaging/SkinImageDecoder.cs`
- Create: `src/CodexQuotaHud.Skins/Packaging/ZipEntryPolicy.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Packaging/SkinPackageReaderTests.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Packaging/SkinPackageAttackTests.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Fixtures/SkinPackageFixture.cs`

**Interfaces:**
- Produces: `SkinPackageReader.ValidateFile(string packagePath, SemanticVersion installedHudVersion, CancellationToken)` and `ValidateStream(Stream package, long packageLength, SemanticVersion installedHudVersion, CancellationToken)` returning `SkinValidationResult<SkinPackageDocument>`.
- Produces: `SkinImageDecoder.Decode(SkinAssetSlot slot, string relativePath, ReadOnlyMemory<byte> encoded)` returning dimensions, alpha capability, and a frozen `BitmapSource` only after content decode succeeds.
- Consumes: strict Task 2 parsers/validator; does not create an installed directory or mutate settings.

- [ ] **Step 1: Write a valid package fixture and failing acceptance test**

The fixture builds archives entirely in a temporary directory with literal
1×1 PNG and JPEG byte arrays, `manifest.json`, and `theme.json`. Its happy-path
test asserts:

```csharp
var result = reader.ValidateFile(packagePath, SemanticVersion.Parse("1.1.1"), CancellationToken.None);
Assert.True(result.IsValid, string.Join(" | ", result.Errors));
Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), result.Value!.Manifest.SkinId);
Assert.Equal(3, result.Value.Assets.Count);
Assert.All(result.Value.Assets.Values, asset =>
{
    Assert.Equal(1, asset.PixelWidth);
    Assert.Equal(1, asset.PixelHeight);
});
```

Add three more valid packages containing zero, one, and two declared image
slots; together with the all-three fixture these prove every slot is
independently optional. Add a duplicate-slot manifest that is rejected before
archive promotion. The `assets` JSON property remains present even when its
array is empty.

- [ ] **Step 2: Write the archive attack matrix**

Create one test case per rejection, with expected stable code:

```text
package.too-large                 50 MiB + 1 compressed input
archive.entry-count               65 entries
archive.extracted-size            aggregate uncompressed bytes > 64 MiB
archive.entry-size                one image > 16 MiB
archive.path.absolute             C:/escape.png or /escape.png
archive.path.traversal            ../escape.png and assets/../../escape.png
archive.path.separator            assets\background.png
archive.path.duplicate            case/Unicode-normalized duplicate names
archive.entry.encrypted           encrypted ZIP flag
archive.compression.unsupported   method other than stored/deflate
archive.entry.not-regular         directory/symlink/reparse/external-attribute entry
archive.file.undeclared           any file not manifest/theme/declared asset
archive.file.forbidden            .exe/.dll/.xaml/.js/.ps1 by content path
asset.hash.mismatch               declared SHA-256 differs
image.signature                   extension/content spoof
image.decode                      truncated/corrupt PNG or JPEG
image.dimension                   width or height 8193
image.pixel-budget                aggregate decoded pixels 67,108,865
image.decoration-format           JPEG in decoration slot
```

For duplicate-name tests include `assets/center.png` twice, a case variant,
and NFC/NFD-equivalent Unicode names. Verify no test writes outside its unique
temporary root.

- [ ] **Step 3: Run secure-reader tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinPackageReaderTests|FullyQualifiedName~SkinPackageAttackTests"
```

Expected: compilation fails because the reader and decoder do not exist.

- [ ] **Step 4: Implement streaming archive inspection before extraction**

Open with `FileShare.Read`; reject a file length over the package cap before
constructing `ZipArchive`. Normalize names to Unicode Form C, require `/` as
the only separator, require `Path.GetFullPath(Path.Combine(root, name))` to be
a strict descendant of a synthetic root, and compare normalized names with
`StringComparer.OrdinalIgnoreCase`. Permit only regular, non-encrypted,
stored (`0`) or deflate (`8`) entries. Read each entry through a counting stream
that aborts before the per-entry or aggregate uncompressed limit; never call
`ExtractToDirectory`.

Read and parse `manifest.json`/`theme.json` first, calculate each asset hash
while copying into bounded memory, require exact declared files, then decode.
Cancellation must be checked before each entry and during every 64 KiB copy.
Catch only `InvalidDataException`, `IOException`, `UnauthorizedAccessException`,
`JsonException`, and WPF image codec exceptions and translate them to stable
errors; do not expose a package-controlled absolute path in user messages.

- [ ] **Step 5: Decode content and enforce pixel budgets**

Create `BitmapDecoder` with `BitmapCacheOption.OnLoad`, demand PNG signature or
JPEG SOI plus a matching WPF decoder codec, use the first frame only, require
positive dimensions within `8192`, calculate pixels in checked `long`, and
freeze the frame before closing the stream. Decoration must decode as PNG;
`HasAlpha` records a pixel format with alpha but transparent pixels are not a
requirement. Sum decoded pixels before returning the document.

- [ ] **Step 6: Run focused tests twice and verify GREEN**

Run the Step 3 command twice.

Expected: all valid/attack cases pass on both runs, every operation closes the
package file, and the temporary directory can be deleted immediately.

- [ ] **Step 7: Commit**

```powershell
git add src/CodexQuotaHud.Skins/Packaging tests/CodexQuotaHud.Skins.Tests
git commit -m "feat: reject unsafe skin archives"
```

---

### Task 4: Deterministic `.cqskin` writer and hash contract

**Files:**
- Create: `src/CodexQuotaHud.Skins/Packaging/SkinPackageWriter.cs`
- Create: `src/CodexQuotaHud.Skins/Packaging/SkinPackageBuildRequest.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Packaging/SkinPackageWriterTests.cs`

**Interfaces:**
- Produces: `SkinPackageBuildRequest(SkinManifest Manifest, SkinTheme Theme, IReadOnlyDictionary<SkinAssetSlot, SkinAsset> Assets)`; the supplied manifest has an empty `Assets` collection and the writer owns the final references/hashes.
- Produces: `SkinPackageWriter.Write(Stream destination, SkinPackageBuildRequest request, CancellationToken)` returning the finalized `SkinManifest`.
- Produces: `SkinPackageWriter.WriteFile(string destinationPath, SkinPackageBuildRequest request, bool overwrite, CancellationToken)` using a sibling temporary file and atomic final move.
- Consumes: Task 2 canonical JSON writer and Task 3 decoded `SkinAsset` values.

- [ ] **Step 1: Write failing deterministic-output tests**

Build the same request twice into separate `MemoryStream` instances and assert:

```csharp
Assert.Equal(first.ToArray(), second.ToArray());
using var archive = new ZipArchive(first, ZipArchiveMode.Read, leaveOpen: true);
Assert.Equal(
    ["assets/background.png", "assets/center.jpg", "assets/decoration.png",
     "manifest.json", "theme.json"],
    archive.Entries.Select(entry => entry.FullName).ToArray());
Assert.All(archive.Entries, entry =>
    Assert.Equal(new DateTimeOffset(1980, 1, 1, 0, 0, 0, TimeSpan.Zero), entry.LastWriteTime));
```

Parse `manifest.json` and independently calculate SHA-256 for every asset.
Assert the declared lowercase hashes match. Verify the request's original
empty `Assets` list and input byte arrays were not mutated.

- [ ] **Step 2: Write failing destination-safety tests**

Add exact cases proving:

- `overwrite: false` preserves an existing destination and returns
  `export.destination-exists`;
- a simulated final-move failure preserves the previous destination;
- a cancelled write removes its `.<guid>.tmp` sibling;
- no `manifest.json`, `theme.json`, or ZIP entry contains the source image's
  absolute path;
- reopening writer output through `SkinPackageReader` succeeds.

- [ ] **Step 3: Run writer tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter FullyQualifiedName~SkinPackageWriterTests
```

Expected: compilation fails because the writer/build request do not exist.

- [ ] **Step 4: Implement canonical build and ZIP order**

Before opening the output, re-run contract and image validation, map slots to
fixed logical filenames while preserving only `.png`, `.jpg`, or `.jpeg`, and
calculate hashes with `SHA256.HashData`. Replace the manifest's `Assets` with
references sorted by slot. Create stored files in ordinal path order, assign
the fixed DOS epoch timestamp, use `CompressionLevel.Optimal`, and write the
canonical JSON bytes from Task 2. Do not serialize source paths or metadata not
declared by schema v1.

For file output, require a `.cqskin` extension, normalize the parent and target,
write/flush a unique sibling temporary file, validate that temporary package
with Task 3, then use `File.Move(temp, target, overwrite)` only after complete
success. Always delete the exact temporary path in `finally`.

- [ ] **Step 5: Run focused reader/writer tests and verify GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinPackageWriterTests|FullyQualifiedName~SkinPackageReaderTests"
```

Expected: deterministic bytes, independent hashes, re-import, cancellation,
and cleanup tests all pass.

- [ ] **Step 6: Commit**

```powershell
git add src/CodexQuotaHud.Skins/Packaging tests/CodexQuotaHud.Skins.Tests/Packaging
git commit -m "feat: export deterministic skin packages"
```

---

### Task 5: Installed catalog, collision decisions, atomic promote, rollback, and removal

**Files:**
- Create: `src/CodexQuotaHud.Skins/Storage/InstalledSkinCatalog.cs`
- Create: `src/CodexQuotaHud.Skins/Storage/InstalledSkinReader.cs`
- Create: `src/CodexQuotaHud.Skins/Storage/SkinPackageInstaller.cs`
- Create: `src/CodexQuotaHud.Skins/Storage/SkinInstallModels.cs`
- Create: `src/CodexQuotaHud.Skins/Storage/SafeOwnedDirectory.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Storage/InstalledSkinCatalogTests.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Storage/SkinPackageInstallerTests.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Storage/SkinInstallRollbackTests.cs`

**Interfaces:**
- Produces: `SkinCollisionDecision { Replace, KeepCopy, Cancel }` and `SkinInstallDisposition { Installed, Replaced, KeptCopy, Cancelled }`.
- Produces: `SkinInstallPreview(SkinPackageDocument Package, InstalledSkinRecord? Existing, bool IsDowngrade, IReadOnlyList<SkinCollisionDecision> AllowedDecisions)`.
- Produces: `SkinInstallResult(SkinInstallDisposition Disposition, InstalledSkinRecord? Installed, IReadOnlyList<SkinValidationError> Errors)`.
- Produces: `SkinPackageInstaller.Inspect(string packagePath, SemanticVersion hudVersion, CancellationToken)`, `Install(SkinInstallPreview preview, SkinCollisionDecision decision, CancellationToken)`, and `Remove(Guid skinId)`.
- Produces: `InstalledSkinCatalog.LoadAll()`, `Find(Guid)`, and `TryLoadSelection(string)`; corrupt records are returned separately as `CorruptInstalledSkin` and never constructed as renderable records.
- Produces: `InstalledSkinRecord(string SelectionKey, Guid SkinId, string DisplayName, SemanticVersion PackageVersion, string DirectoryPath, SkinPackageDocument Package)`.
- Consumes: `%LOCALAPPDATA%\CodexQuotaHud\skins` and operation roots under `%LOCALAPPDATA%\CodexQuotaHud\imports` only.

- [ ] **Step 1: Write failing clean-install and collision tests**

Cover the exact decision table:

```text
no existing ID                 -> Installed; Replace/KeepCopy not requested
same ID, imported > installed  -> Replace, KeepCopy, Cancel allowed
same ID, imported = installed  -> Replace, KeepCopy, Cancel allowed
same ID, imported < installed  -> install.downgrade; no install decision allowed
Replace                        -> same custom:<id>, imported package version
KeepCopy                       -> new GUID, custom:<new-id>, OriginSkinId=<package-id>
Cancel                         -> no filesystem change
```

Assert `KeepCopy` does not modify the source `.cqskin` bytes/hash and that all
successful directories contain exactly `manifest.json`, `theme.json`, and
declared assets.

- [ ] **Step 2: Write failing rollback, cleanup, and removal tests**

Inject filesystem operations behind `ISkinFileSystem` and fail each transition:

1. staging write;
2. staged revalidation;
3. existing-to-backup move;
4. stage-to-final move;
5. backup cleanup.

For steps 1–4 assert the old installed directory bytes remain exact and no new
record is visible. A backup-cleanup failure reports `install.cleanup-failed`
but retains the successfully promoted skin and the recoverable operation
directory. Every normal success/failure removes its operation directory.

Removal tests reject unknown IDs, a path not named as lowercase GUID `D`,
ancestor/reparse-point escapes, and every reserved built-in ID; successful
removal deletes exactly one custom directory and preserves all siblings.

- [ ] **Step 3: Run storage tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~InstalledSkinCatalogTests|FullyQualifiedName~SkinPackageInstallerTests|FullyQualifiedName~SkinInstallRollbackTests"
```

Expected: compilation fails because installed storage types do not exist.

- [ ] **Step 4: Implement owned-directory validation and installed reader**

`SafeOwnedDirectory` resolves every root and candidate with `Path.GetFullPath`,
requires strict descendant relationship, walks each existing ancestor from the
owned root to reject `FileAttributes.ReparsePoint`, and accepts a final skin
directory name only when `Guid.TryParseExact(name, "D")` succeeds and the name
is lowercase canonical text.

Installed directories are unpacked data, not executable content. Read exact
files with the same byte/image/hash/contract checks as Task 3, allowing
`OriginSkinId` only for local Keep Copy records. `LoadAll` sorts by display name
then GUID and reports corrupt records without stopping healthy-skin discovery.

- [ ] **Step 5: Implement operation-scoped atomic install**

Create `%imports%\<operation-guid>\candidate\<skin-guid>` after validating
the imports root. Write canonical manifest/theme/assets, flush files, and read
the staged directory again. For Replace, move existing to
`%imports%\<operation-guid>\backup\<skin-guid>`, move candidate to final, and
restore backup if promotion fails. For Keep Copy, generate the new UUID before
staging, set `OriginSkinId` to the imported package ID, rewrite only the local
manifest, and leave package bytes unchanged.

Never expose a half-staged directory from `InstalledSkinCatalog`. A retained
cleanup-failure operation is named in the result by operation ID only, not by a
package-controlled path.

- [ ] **Step 6: Run storage plus attack tests and verify GREEN**

Run the Step 3 command followed by:

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinPackageAttackTests|FullyQualifiedName~SkinInstall"
```

Expected: atomic install/replace/keep-copy/rollback/remove/cleanup and all
archive attacks pass.

- [ ] **Step 7: Commit**

```powershell
git add src/CodexQuotaHud.Skins/Storage tests/CodexQuotaHud.Skins.Tests/Storage
git commit -m "feat: install custom skins atomically"
```

---

### Task 6: Versioned template registry and Free Decoration Ring renderer

**Files:**
- Create: `src/CodexQuotaHud.Skins/Templates/ISkinTemplate.cs`
- Create: `src/CodexQuotaHud.Skins/Templates/SkinTemplateRegistry.cs`
- Create: `src/CodexQuotaHud.Skins/Templates/CustomSkinRenderer.cs`
- Create: `src/CodexQuotaHud.Skins/Templates/CustomSkinRenderState.cs`
- Create: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingTemplate.cs`
- Create: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingRenderer.xaml`
- Create: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingRenderer.xaml.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Templates/SkinTemplateRegistryTests.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Templates/FreeDecorationRingRendererTests.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Templates/FreeDecorationRingLayerTests.cs`

**Interfaces:**
- Produces: `ISkinTemplate.TemplateId`, `SchemaVersion`, and `CreateRenderer(SkinPackageDocument)`.
- Produces: `SkinTemplateRegistry.CreateDefault()`, `TryResolve(string templateId, int schemaVersion, out ISkinTemplate)`, and immutable registered-key enumeration.
- Produces: `CustomSkinRenderState(double PrimaryPercent, double? SecondaryPercent, string PrimaryLabel, QuotaDisplayMode Mode, bool IsRefreshing, Color PrimaryRingColor, Color? SecondaryRingColor)`.
- Produces: abstract `CustomSkinRenderer : UserControl` with `Render(CustomSkinRenderState)` and `ApplyAnimationState(CustomSkinAnimationState state, bool globalAnimationsEnabled)`.
- Produces: `CustomSkinAnimationState { Hidden, Idle, Refreshing }`.
- Consumes: validated `SkinPackageDocument`; package colors arrive as normal-state colors and the App adapter in Task 8 supplies alert-overridden colors.

- [ ] **Step 1: Write failing registry and construction tests**

```csharp
var registry = SkinTemplateRegistry.CreateDefault();
Assert.True(registry.TryResolve("free-decoration-ring", 1, out var template));
Assert.IsType<FreeDecorationRingTemplate>(template);
Assert.False(registry.TryResolve("unknown", 1, out _));
Assert.False(registry.TryResolve("free-decoration-ring", 2, out _));
Assert.Equal([("free-decoration-ring", 1)], registry.RegisteredKeys);
```

Construct the renderer on an STA thread from zero, one, and all-three image-slot
packages. Assert its `Width/Height` are `132`, assets are frozen/owned, and no
local source path appears in any dependency-object string value.

- [ ] **Step 2: Write failing protected-layer and state tests**

Give XAML elements stable internal names and assert visual Z-order:

```text
BackgroundImage < BaseFill < DecorationImage < CenterImage <
SecondaryTrack/SecondaryProgress < PrimaryTrack/PrimaryProgress <
QuotaNumber/QuotaLabel
```

Assert transforms are applied to only their image slot; all three images have
`IsHitTestVisible=false`; the body images are clipped; center uses its bounded
mask; rings/text are never descendants of an image container or opacity mask.

Render exact dual `68/34`, single-five-hour, single-weekly, and hidden states.
Assert hidden collapses all quota content, single removes the secondary ring,
dual uses independent colors, and percent arcs use `percent * 3.6` without
changing the underlying state. Exercise every parameter at both valid bounds.

- [ ] **Step 3: Write failing animation-precedence tests**

For nonzero package intensities, call:

```csharp
renderer.ApplyAnimationState(CustomSkinAnimationState.Idle, true);
Assert.Equal(4, renderer.DesiredFrameRate);
renderer.ApplyAnimationState(CustomSkinAnimationState.Refreshing, true);
Assert.Equal(24, renderer.DesiredFrameRate);
renderer.ApplyAnimationState(CustomSkinAnimationState.Refreshing, false);
Assert.False(renderer.HasActiveAnimations);
renderer.ApplyAnimationState(CustomSkinAnimationState.Hidden, true);
Assert.False(renderer.HasActiveAnimations);
```

Also verify every zero intensity creates no animation track and that disabling
the global switch removes all storyboards immediately.

- [ ] **Step 4: Run renderer tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinTemplateRegistryTests|FullyQualifiedName~FreeDecorationRing"
```

Expected: compilation fails because registry/renderer types do not exist.

- [ ] **Step 5: Implement the fixed protected visual tree**

Use one `132 × 132` root grid. Load images only from validated owned bytes with
`BitmapCacheOption.OnLoad`; use `TranslateTransform`, `ScaleTransform`, and
`RotateTransform` centered on each slot; use `ImageBrush` viewbox alignment for
crop focus `0..1`. Draw ring tracks and progress with WPF `Path`/`ArcSegment`
or the existing equivalent geometry, and keep system number/label in the final
grid layer. The renderer never accepts XAML text or constructs a package-owned
element.

Create animation tracks only for nonzero rotation/breathing/glow/floating
intensity. Cap desired frame rate at existing idle `4` and refreshing `24`;
`globalAnimationsEnabled=false` and `Hidden` remove storyboards and reset
transforms. Refreshing may scale speed by the validated intensity but cannot
exceed the 24 fps cap.

- [ ] **Step 6: Verify renderer, reader, and alert boundary compatibility GREEN**

Run the Step 4 command and the existing alert tests:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~QuotaAlert|FullyQualifiedName~ProgressArc"
```

Expected: all new renderer tests and existing `21/20/11/10/0` policy tests
pass; no App code has yet changed.

- [ ] **Step 7: Commit**

```powershell
git add src/CodexQuotaHud.Skins/Templates tests/CodexQuotaHud.Skins.Tests/Templates
git commit -m "feat: render free decoration ring skins"
```

---

### Task 7: String selection key and legacy settings migration

**Files:**
- Create: `src/CodexQuotaHud.Core/Settings/SkinSelectionKey.cs`
- Modify: `src/CodexQuotaHud.Core/Settings/AppSettings.cs`
- Modify: `src/CodexQuotaHud.Core/Settings/SettingsStore.cs`
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbViewModel.cs`
- Modify: `tests/CodexQuotaHud.Core.Tests/Settings/SettingsStoreTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/QuotaOrbViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/QuotaOrbWindowStartupTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/InMemorySettingsStoreTests.cs`

**Interfaces:**
- Produces: built-in constants `SkinSelectionKey.HudDial`, `EnergyRing`, `LiquidGlass`, `Aurora`, and `LiquidTank` with the exact `builtin:<SkinId>` values from Global Constraints.
- Produces: `SkinSelectionKey.FromBuiltIn(SkinId)`, `TryGetBuiltIn(string, out SkinId)`, `TryGetCustomId(string, out Guid)`, and `IsSyntacticallyValid(string)`; custom output is always `custom:<lowercase-guid-d>`.
- Changes: `AppSettings` persists `string SelectedSkinKey = SkinSelectionKey.HudDial`; its `[JsonIgnore] SkinId SelectedSkin` read-only compatibility projection exists only until Task 8 finishes the UI migration.
- Produces: `SettingsLoadResult(AppSettings Settings, bool RequiresWriteBack, string? SelectionErrorCode)` and `SettingsStore.LoadWithMigration()`.
- Changes: `SettingsStore(string settingsPath, Func<string, bool>? selectionExists = null)`; the default validator accepts built-ins only, and Task 8 injects the healthy installed-custom catalog predicate.
- Preserves: `ISettingsStore.Load()` and `Save(AppSettings)` so Preview's in-memory store remains filesystem-free.

- [ ] **Step 1: Write failing selection-key syntax tests**

Add literal, case-sensitive expectations:

```csharp
[Theory]
[InlineData(SkinId.HudDial, "builtin:HudDial")]
[InlineData(SkinId.EnergyRing, "builtin:EnergyRing")]
[InlineData(SkinId.LiquidGlass, "builtin:LiquidGlass")]
[InlineData(SkinId.Aurora, "builtin:Aurora")]
[InlineData(SkinId.LiquidTank, "builtin:LiquidTank")]
public void BuiltInSelectionKeys_RoundTripStableEnumIds(
    SkinId skin, string expected)
{
    Assert.Equal(expected, SkinSelectionKey.FromBuiltIn(skin));
    Assert.True(SkinSelectionKey.TryGetBuiltIn(expected, out var parsed));
    Assert.Equal(skin, parsed);
}

[Theory]
[InlineData("custom:11111111-1111-1111-1111-111111111111", true)]
[InlineData("custom:11111111111111111111111111111111", false)]
[InlineData("custom:11111111-1111-1111-1111-11111111111A", false)]
[InlineData("CUSTOM:11111111-1111-1111-1111-111111111111", false)]
[InlineData("builtin:NotReal", false)]
public void SelectionKeySyntax_RequiresExactNamespaceAndCanonicalId(
    string value, bool expected) =>
    Assert.Equal(expected, SkinSelectionKey.IsSyntacticallyValid(value));
```

The breaks caught are changing a stable built-in identifier, accepting a
non-canonical custom UUID, and case-folding a persisted key.

- [ ] **Step 2: Write the failing migration precedence matrix**

Use hand-authored settings JSON and an injected predicate that recognizes only
`custom:11111111-1111-1111-1111-111111111111`. Assert this exact order:

```text
valid SelectedSkinKey + conflicting legacy SelectedSkin -> new key, no rewrite
invalid/uninstalled SelectedSkinKey + valid legacy enum  -> mapped builtin key, rewrite
missing SelectedSkinKey + valid legacy string enum       -> mapped builtin key, rewrite
missing SelectedSkinKey + valid legacy numeric enum      -> mapped builtin key, rewrite
invalid/missing both                                      -> builtin:HudDial, rewrite
missing settings file                                     -> builtin:HudDial, no rewrite
```

Every row includes non-default `Left`, `Top`, `AnimationsEnabled`, and
`LastSuccessfulRefresh` and asserts those unrelated fields are unchanged.
Invalid new selections return `skin.selection.invalid`; pure legacy migration
returns no user-facing error.

- [ ] **Step 3: Write failing save-shape and atomic-migration tests**

Update the existing approved-field test to require exactly:

```text
AnimationsEnabled
LastSuccessfulRefresh
Left
SelectedSkinKey
Top
```

and to reject a serialized `SelectedSkin` property. Save
`custom:11111111-1111-1111-1111-111111111111`, re-read it with the accepting
predicate, and assert exact preservation. Retain the existing atomic move,
concurrent save, temporary-file cleanup, and active-lock-pool tests with string
keys.

- [ ] **Step 4: Run Core settings tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.Core.Tests\CodexQuotaHud.Core.Tests.csproj -c Release --filter FullyQualifiedName~SettingsStoreTests
```

Expected: compilation fails because `SkinSelectionKey`, `SelectedSkinKey`, and
`LoadWithMigration` do not exist.

- [ ] **Step 5: Implement exact key parsing and migration without a Skins dependency**

Keep `SkinSelectionKey` in Core and map built-ins with an exhaustive switch:

```csharp
public static string FromBuiltIn(SkinId skin) => skin switch
{
    SkinId.HudDial => HudDial,
    SkinId.EnergyRing => EnergyRing,
    SkinId.LiquidGlass => LiquidGlass,
    SkinId.Aurora => Aurora,
    SkinId.LiquidTank => LiquidTank,
    _ => throw new ArgumentOutOfRangeException(nameof(skin))
};
```

`TryGetCustomId` requires the literal `custom:` prefix, exactly 36 lowercase
GUID-D characters, `Guid.TryParseExact(..., "D")`, and equality with
`id.ToString("D")`. `SettingsStore` reads `SelectedSkinKey` only when it is a
JSON string and `selectionExists(key)` returns true; otherwise it attempts the
existing legacy string/numeric enum parser, then falls back to HudDial. Never
change another setting because selection validation failed.

`AppSettings` uses the new primary-constructor field and marks the temporary
built-in projection `[JsonIgnore]`. Update old test fixtures and Preview's
in-memory initial settings to construct with `SelectedSkinKey:`. Do not add a
Skins project reference to Core.

- [ ] **Step 6: Persist migration best-effort at normal startup**

In `App.OnStartup`, replace the plain load with:

```csharp
var loadResult = settingsStore.LoadWithMigration();
var settings = loadResult.Settings;
if (loadResult.RequiresWriteBack)
{
    try { settingsStore.Save(settings); }
    catch (Exception exception) when (
        exception is IOException or UnauthorizedAccessException or SecurityException)
    {
        Trace.TraceWarning("Could not persist settings migration: {0}", exception);
    }
}
```

Keep normal startup alive when migration persistence fails. In
`QuotaOrbViewModel`, retain the temporary built-in `SelectedSkin` API but read
it from `SkinSelectionKey.TryGetBuiltIn`; its setter writes
`SelectedSkinKey = SkinSelectionKey.FromBuiltIn(value)`. Task 8 replaces this
compatibility surface with the string API.

- [ ] **Step 7: Run migration, view-model, and Preview isolation tests GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.Core.Tests\CodexQuotaHud.Core.Tests.csproj -c Release --filter FullyQualifiedName~SettingsStoreTests
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~QuotaOrbViewModelTests|FullyQualifiedName~InMemorySettingsStoreTests|FullyQualifiedName~PreviewSessionTests"
```

Expected: all selected tests pass; legacy built-in users retain their exact
skin, Preview writes only its in-memory key, and no formal settings field other
than the selection key is migrated.

- [ ] **Step 8: Commit**

```powershell
git add src/CodexQuotaHud.Core/Settings src/CodexQuotaHud.App/App.xaml.cs src/CodexQuotaHud.App/UI/QuotaOrbViewModel.cs tests/CodexQuotaHud.Core.Tests/Settings tests/CodexQuotaHud.App.Tests
git commit -m "feat: migrate skin selection keys"
```

---

### Task 8: Healthy custom catalog and production HUD renderer integration

**Files:**
- Modify: `src/CodexQuotaHud.Core/Settings/AppSettings.cs`
- Modify: `src/CodexQuotaHud.App/CodexQuotaHud.App.csproj`
- Create: `src/CodexQuotaHud.App/UI/Skins/HudSkinCatalog.cs`
- Create: `src/CodexQuotaHud.App/UI/Skins/SkinDescriptor.cs`
- Create: `src/CodexQuotaHud.App/UI/Skins/SkinPresentation.cs`
- Create: `src/CodexQuotaHud.App/UI/Skins/SkinActivationCandidate.cs`
- Create: `src/CodexQuotaHud.App/UI/Skins/CustomQuotaSkin.cs`
- Modify: `src/CodexQuotaHud.App/UI/Skins/IQuotaSkin.cs`
- Modify: `src/CodexQuotaHud.App/UI/Skins/AnimatedQuotaSkin.cs`
- Modify: `src/CodexQuotaHud.App/UI/Skins/SkinController.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbViewModel.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/PopupPresentation.cs`
- Modify: `src/CodexQuotaHud.App/UI/TrayController.cs`
- Modify: `src/CodexQuotaHud.App/UI/TrayIconRenderer.cs`
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/HudSkinCatalogTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/CustomQuotaSkinTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/SkinControllerTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/QuotaOrbViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/QuotaOrbWindowStartupTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/QuotaAlertSkinTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/TrayIconRendererTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewCompositionTests.cs`

**Interfaces:**
- Adds: App project reference to `CodexQuotaHud.Skins`; no Core-to-Skins or Skins-to-App reference is allowed.
- Produces: `SkinDescriptor(string SelectionKey, string DisplayName, bool IsBuiltIn, SkinId? BuiltInId, InstalledSkinRecord? Installed)` and `bool CanRemove => !IsBuiltIn`.
- Produces: `HudSkinCatalogSnapshot(IReadOnlyList<SkinDescriptor> Healthy, IReadOnlyList<CorruptInstalledSkin> Corrupt)`, `HudSkinCatalog.Load()`, and `TryGet(string selectionKey, out SkinDescriptor)`; it returns the five built-ins first in stable enum order, followed by healthy Task 5 records sorted by display name/GUID, and exposes corrupt records separately.
- Produces: `SkinPresentation(PopupTheme Popup, EdgeProgressTheme Edge, System.Drawing.Color TrayAccent)` and `SkinPresentation.ForBuiltIn(SkinId)` / `ForCustom(SkinTheme)`.
- Changes: `IQuotaSkin.SelectionKey` is the controller identity. `AnimatedQuotaSkin.Id : SkinId` remains unchanged for built-in compatibility and implements `SelectionKey` through `SkinSelectionKey.FromBuiltIn(Id)`.
- Produces: `CustomQuotaSkin : IQuotaSkin, IOrbAnimationTarget`, wrapping the Task 6 `CustomSkinRenderer`.
- Produces: `SkinActivationCandidate(SkinDescriptor Descriptor, IQuotaSkin Skin, SkinPresentation Presentation)` and `SkinSelectionFailure(string RequestedSelectionKey, string DisplayNameOrId, string ErrorCode)`.
- Changes: `SkinController.TryPrepare(string selectionKey, out SkinActivationCandidate? candidate, out SkinSelectionFailure? failure)`, `Activate(SkinActivationCandidate candidate)`, `RegisteredKeys`, `CurrentDescriptor`, and `CurrentPresentation`; prepare failure never changes the active instance.
- Changes: formal view-model selection becomes `string SelectedSkinKey`; its constructor receives `Func<string, bool> selectionExists`, and `bool TrySelectSkinKey(string selectionKey)` returns false without changing memory when validation or settings persistence fails.
- Removes: Task 7's temporary `[JsonIgnore] AppSettings.SelectedSkin` compatibility projection after the last App caller and test use the string key; the serialized settings shape remains unchanged.
- Produces: `bool QuotaOrbWindow.TryActivateSkinKey(string selectionKey)`, ordered as controller prepare, durable view-model save, then controller activation. `SelectSkinCommand` and both menu surfaces call only this coordinator with catalog-approved keys.
- Consumes: Task 6 `CustomSkinRenderState` colors as `System.Windows.Media.Color`; Task 8 does not introduce a second color type at the shared renderer boundary.

- [ ] **Step 1: Write failing catalog ordering and health tests**

Create a temporary Task 5 catalog with two valid installed packages and one
corrupt directory. Assert literal order and filtering:

```csharp
Assert.Equal(
    ["builtin:HudDial", "builtin:EnergyRing", "builtin:LiquidGlass",
     "builtin:Aurora", "builtin:LiquidTank",
     "custom:11111111-1111-1111-1111-111111111111",
     "custom:22222222-2222-2222-2222-222222222222"],
    catalog.Load().Healthy.Select(item => item.SelectionKey));
Assert.Single(catalog.Load().Corrupt);
Assert.False(catalog.TryGet("custom:33333333-3333-3333-3333-333333333333", out _));
Assert.All(catalog.Load().Healthy.Take(5), item => Assert.False(item.CanRemove));
Assert.All(catalog.Load().Healthy.Skip(5), item => Assert.True(item.CanRemove));
```

Use display names that sort opposite their UUIDs to prove custom ordering is
display name then GUID, not directory enumeration order.

- [ ] **Step 2: Write failing custom adapter state and alert tests**

Construct a real Task 6 renderer on STA and render these exact states through
`CustomQuotaSkin.Render(QuotaSkinState)`:

```text
dual 68/34 idle animations on
five-hour-only 21 refreshing
weekly-only 20 animations off
hidden
dual 11/10 mixed Warning/Critical
dual 10/21 mixed Critical/Normal
```

Assert the renderer receives the same mode/percent/label/refresh state. For
normal quota it receives the package primary/secondary colors; at Warning it
receives `#FFFFB547`; at Critical it receives `#FFFF5A67`. Secondary alert is
independent. Map App `OrbAnimationState.Hidden/Idle/Refreshing` to the exact
Task 6 enum, and prove the global animation flag always reaches the renderer.

- [ ] **Step 3: Write failing controller/fallback/persistence tests**

Refactor existing controller tests to use stable string keys while continuing
to assert every built-in `Id`. Add:

```csharp
Assert.True(controller.TryPrepare(
    "custom:11111111-1111-1111-1111-111111111111",
    out var candidate,
    out var failure));
Assert.Null(failure);
Assert.IsType<CustomQuotaSkin>(candidate!.Skin);
controller.Activate(candidate);
Assert.Equal(candidate.Descriptor.SelectionKey,
    controller.CurrentDescriptor.SelectionKey);

var previous = controller.CurrentDescriptor;
Assert.False(controller.TryPrepare(
    "custom:99999999-9999-9999-9999-999999999999",
    out _,
    out failure));
Assert.Equal(previous, controller.CurrentDescriptor);
Assert.Equal("skin.selection.missing", failure!.ErrorCode);
```

Build a settings file selecting a now-corrupt custom skin. Start the normal
composition with recording error presentation and assert: HudDial is selected,
`settings.json` is rewritten to `builtin:HudDial`, and exactly one actionable
message names the custom display name/ID without exposing an absolute path.
Repeat a render after fallback and prove no second message is shown. For an
interactive custom-skin factory exception, assert the previous formal key and
active renderer both remain exact because prepare occurs before settings save.
For a startup factory exception on the persisted custom key, assert HudDial is
prepared, persisted, and activated in that order; a fallback save failure keeps
the safe HudDial instance already active for the current run and does not claim
that fallback persistence succeeded.

- [ ] **Step 4: Run HUD integration tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~HudSkinCatalogTests|FullyQualifiedName~CustomQuotaSkinTests|FullyQualifiedName~SkinControllerTests|FullyQualifiedName~QuotaOrbViewModelTests"
```

Expected: compilation fails because the App has no Skins reference, catalog,
custom adapter, or string controller surface.

- [ ] **Step 5: Implement catalog, presentation, and renderer adapter**

`HudSkinCatalog` combines hard-coded built-in descriptors with one immutable
snapshot from `InstalledSkinCatalog.LoadAll()`. It never creates a descriptor
for a corrupt record. `SkinPresentation.ForCustom` parses only the already
validated theme colors and derives:

```text
popup background = BaseBackgroundColor with BaseBackgroundOpacity
popup border/accent = PrimaryRingColor
popup shadow = GlowColor
popup decoration = Custom (all five built-in decoration canvases collapsed)
edge track = popup background
edge border/accent/glow = PrimaryRingColor/GlowColor
edge fill gradient = PrimaryRingColor -> SecondaryRingColor
edge material = TechHighlight
tray accent = PrimaryRingColor
```

`CustomQuotaSkin` maps `QuotaSkinState` into `CustomSkinRenderState`. Resolve
each ring color with existing `QuotaAlertPolicy`/`QuotaAlertPalette` before
calling the shared renderer, so a package cannot disable or redefine product
alerts. It owns no package file handle and executes no package content.

`SkinController.TryPrepare` resolves the descriptor, builds the candidate, and
contains package/factory exceptions as one `SkinSelectionFailure`; it does not
replace the current renderer. `Activate` accepts only a candidate produced by
the controller's current catalog generation and performs the single visual
swap after persistence has succeeded.

- [ ] **Step 6: Integrate the string controller into the production window and tray**

Construct `SkinStoragePaths`, `InstalledSkinCatalog`, `HudSkinCatalog`, and
`SkinTemplateRegistry.CreateDefault()` once in normal `App.OnStartup`, before
settings load. Pass `key => hudCatalog.TryGet(key, out _)` to Task 7's
`SettingsStore`. Inject one `SkinController` into `QuotaOrbWindow`; Preview
injects a built-in-only catalog and keeps its in-memory settings.

Delete the temporary `AppSettings.SelectedSkin` projection and replace every
App/test access with `SelectedSkinKey`; retain the stable `SkinId` enum only in
built-in descriptor/factory compatibility paths.

Change all window/controller comparisons to `SelectedSkinKey`. Use
`SkinController.CurrentPresentation` for popup and edge themes. Change
`TrayIconRenderer.CreateState` to accept the resolved normal
`System.Drawing.Color` rather than a `SkinId`; keep alert override inside the
renderer. `TrayController` receives the same catalog/controller presentation,
so full HUD, edge, popup, and tray cannot disagree about a custom skin.

For an interactive selection, `QuotaOrbWindow.TryActivateSkinKey` prepares the
candidate first, calls `QuotaOrbViewModel.TrySelectSkinKey`, and activates only
after the save returns true. A prepare or save failure leaves both the previous
formal key and previous visual instance unchanged.

On startup selection failure, keep the controller's initial HudDial instance,
call `QuotaOrbViewModel.TrySelectSkinKey(SkinSelectionKey.HudDial)`, and prepare
and activate a fresh HudDial candidate only after its settings save succeeds.
Display one App-owned error such as:
`自定义皮肤“<name-or-id>”已损坏，已切换到 HUD 科技仪表。请重新导入或删除该皮肤。`

- [ ] **Step 7: Verify production states and built-in regressions GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~HudSkinCatalogTests|FullyQualifiedName~CustomQuotaSkinTests|FullyQualifiedName~SkinControllerTests|FullyQualifiedName~QuotaOrbWindowStartupTests|FullyQualifiedName~QuotaAlert|FullyQualifiedName~TrayIconRendererTests|FullyQualifiedName~PreviewCompositionTests"
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter FullyQualifiedName~FreeDecorationRing
```

Expected: custom single/dual/hidden/refresh/animation/alert states pass; all
five built-in skins retain their current output; Preview constructs no real
catalog path, app-server, or formal settings store.

- [ ] **Step 8: Run the complete .NET regression and commit**

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

Expected: no failed tests, zero build warnings, and zero build errors.

```powershell
git add src/CodexQuotaHud.Core/Settings/AppSettings.cs src/CodexQuotaHud.App tests/CodexQuotaHud.App.Tests
git commit -m "feat: render installed custom skins"
```

---

### Task 9: HUD import, custom removal, and optional-designer management UI

**Files:**
- Create: `src/CodexQuotaHud.App/UI/SkinManagement/SkinMenuEntry.cs`
- Create: `src/CodexQuotaHud.App/UI/SkinManagement/SkinImportResult.cs`
- Create: `src/CodexQuotaHud.App/UI/SkinManagement/ISkinManagementDialogs.cs`
- Create: `src/CodexQuotaHud.App/UI/SkinManagement/SkinManagementController.cs`
- Create: `src/CodexQuotaHud.App/UI/SkinManagement/SkinImportPreviewWindow.xaml`
- Create: `src/CodexQuotaHud.App/UI/SkinManagement/SkinImportPreviewWindow.xaml.cs`
- Create: `src/CodexQuotaHud.App/UI/SkinManagement/DesignerLauncher.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/TrayController.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbViewModel.cs`
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/SkinManagement/SkinManagementControllerTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/SkinManagement/SkinImportPreviewWindowTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/SkinManagement/DesignerLauncherTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/QuotaOrbWindowStartupTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/TraySkinMenuTests.cs`

**Interfaces:**
- Produces: `SkinMenuEntry(string SelectionKey, string DisplayName, bool IsSelected, bool CanRemove)` from the Task 8 healthy catalog only.
- Produces: `SkinImportResult(bool Succeeded, bool Cancelled, InstalledSkinRecord? Installed, IReadOnlyList<SkinValidationError> Errors)`.
- Produces: `IReadOnlyList<SkinMenuEntry> SkinManagementController.Entries`, `bool DesignerAvailable`, `Task<SkinImportResult> ImportAsync(string packagePath, CancellationToken cancellationToken = default)`, `Task<bool> RemoveAsync(string selectionKey, CancellationToken cancellationToken = default)`, `bool OpenDesigner()`, and `event EventHandler? CatalogChanged`.
- Consumes: Task 5 `SkinPackageInstaller.Inspect/Install/Remove`, Task 8 `HudSkinCatalog`, formal `QuotaOrbViewModel.TrySelectSkinKey`, and Task 6 preview renderer.
- Produces: `DesignerLauncher.ExpectedExecutablePath`, `IsAvailable`, and `TryLaunch(out string? error)`; the exact installed path is `%LOCALAPPDATA%\Programs\CodexQuotaHud\designer\CodexQuotaHud.SkinDesigner.exe`.
- Produces: `string? ISkinManagementDialogs.ChoosePackagePath()`, `SkinCollisionDecision ShowImportPreview(SkinInstallPreview preview)`, `bool ConfirmRemoval(SkinMenuEntry entry)`, and `void ShowError(string message)` as the only OS/UI boundaries. Tests use a recording adapter only for these external dialogs and exercise real package/catalog/controller behavior.
- Adds in this task: `HudSkinCatalogSnapshot HudSkinCatalog.Refresh()` and `bool SkinController.ReplaceCatalog(HudSkinCatalogSnapshot snapshot, out SkinSelectionFailure? failure)`; replacement keeps the current instance when its key remains healthy, returns false without swapping when it is absent, and lets the caller use Task 8's durable fallback order.

- [ ] **Step 1: Write failing import-before-activation tests**

Use the real Task 4 fixture/writer and Task 5 installer in a unique Local App
Data root. `ImportAsync` must follow this observable order:

```text
inspect and fully validate -> show safe preview -> obtain collision decision
-> install -> reload healthy catalog -> raise CatalogChanged
```

Assert a new package becomes an available `SkinMenuEntry` but the formal
`SelectedSkinKey` remains unchanged. Invalid, cancelled, incompatible,
downgrade, and preview-closed imports create no installed directory, raise no
catalog event, and show the Task 2/3 specific field-or-asset reason. Simulate
same-ID equal/newer packages and assert the dialog's exact three choices map to
Task 5 Replace/KeepCopy/Cancel. Keep Copy makes both the original and the new
`custom:<new-id>` entry visible while leaving the formal selection unchanged.
Map Task 2/3 inspection failures and Task 5 install outcomes into the exact
`SkinImportResult` fields; do not use exceptions as normal invalid/cancel flow.

- [ ] **Step 2: Write failing safe-removal ordering tests**

Cover:

```text
built-in key                    -> removal rejected, no confirmation
unknown/corrupt key             -> removal rejected, actionable error
unselected custom + confirm     -> exact directory removed, catalog event once
unselected custom + cancel      -> no change
selected custom + fallback save -> persist builtin:HudDial, switch view, remove
selected custom + save failure  -> abort removal, custom directory/view retained
selected custom + remove failure-> HudDial remains selected, custom remains listed
```

Assert siblings, settings, drafts, imports outside the operation, and all five
built-ins are preserved. The break caught is deleting the active skin before a
durable safe fallback exists.

- [ ] **Step 3: Write failing designer discovery and launch tests**

Construct `DesignerLauncher` with a normal app directory and controlled file/
process delegates. Assert:

```csharp
Assert.Equal(
    Path.Combine(appDirectory, "designer", "CodexQuotaHud.SkinDesigner.exe"),
    launcher.ExpectedExecutablePath);
```

Missing file means `DesignerAvailable == false` and no menu entry. A present
file launches that exact full path with `UseShellExecute=true`, empty arguments,
and no working-directory search. A false `Process.Start` result, Win32 error,
or file disappearing between check/start is contained and reported; the HUD
continues running and no fallback process is started.

- [ ] **Step 4: Write failing WPF/WinForms menu parity tests**

On STA, construct a HUD window and tray controller from the same management
controller. Open/rebuild both skin menus and assert identical ordered entries:

```text
five built-ins
healthy custom skins
separator
导入皮肤…
打开皮肤设计器   (only when exact executable exists)
```

Each custom entry exposes Select and Remove; built-ins expose Select only.
Invoke real menu click handlers and assert the matching management/view-model
operation, selected check mark, and catalog refresh. The raw `--preview`
diagnostic never appears. Designer discovery must not create a desktop link,
startup entry, process, or settings write.

For both menus, make the import click call `ChoosePackagePath()` exactly once;
a null result performs no inspection or UI refresh, while a returned path is
passed unchanged to `ImportAsync`.

- [ ] **Step 5: Run management tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~SkinManagementControllerTests|FullyQualifiedName~SkinImportPreviewWindowTests|FullyQualifiedName~DesignerLauncherTests|FullyQualifiedName~TraySkinMenuTests"
```

Expected: compilation fails because management controller, preview window,
designer launcher, and dynamic menu surfaces do not exist.

- [ ] **Step 6: Implement safe preview and collision UI**

`SkinImportPreviewWindow` receives only a fully validated
`SkinInstallPreview`; it shows display name, informational author (label it
`作者（未验证）` / `Author (unverified)`), version, description, template, asset
summary, compatibility result, and a real Task 6 renderer in fixed dual
`68/34` idle state. It never reads a path from manifest/theme, never installs
on load, and cannot select the formal skin.

For no collision, expose `安装` and `取消`. For same-ID equal/newer collision,
expose exactly `替换`, `保留副本`, and `取消`; an older version disables install
and displays the downgrade reason. Return a `SkinCollisionDecision` only from a
button click. Close/Escape maps to Cancel. Because Task 5 deliberately has no
public `Install` collision decision, the no-collision `安装` confirmation passes
`SkinCollisionDecision.Replace` only as the installer's internal promote token;
the preview never labels that clean-install action as Replace.

- [ ] **Step 7: Implement management controller and dynamic menus**

After a successful install/remove, reload Task 8's immutable catalog snapshot,
call `SkinController.ReplaceCatalog`, and raise `CatalogChanged` once on the WPF
dispatcher. Import never calls `TrySelectSkinKey`. Removal of the current key
first prepares HudDial, calls `TrySelectSkinKey(SkinSelectionKey.HudDial)`, and
activates that candidate; it aborts before deletion unless all three operations
succeed. If a refresh detects an externally missing active custom key,
`ReplaceCatalog` leaves its current instance alive while the composition root
runs the same durable HudDial fallback sequence.

Replace the static five-item WPF/WinForms implementations with builders that
consume `Entries`; preserve the current five labels and order. Add
`导入皮肤…` to the tray skin menu and the orb skin menu. Add
`打开皮肤设计器` only when `DesignerLauncher.IsAvailable`; launch failures use
one actionable App-owned message. Rebuild on menu open and `CatalogChanged` so
both surfaces remain synchronized without restarting the HUD.

- [ ] **Step 8: Verify focused management and existing interaction GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~SkinManagement|FullyQualifiedName~DesignerLauncher|FullyQualifiedName~TraySkinMenu|FullyQualifiedName~QuotaOrbWindowStartupTests|FullyQualifiedName~OrbClickControllerTests|FullyQualifiedName~DetailsPopup|FullyQualifiedName~EdgeAutoHide"
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinPackageAttackTests|FullyQualifiedName~SkinPackageInstallerTests"
```

Expected: import/remove/discovery/menu tests pass; click/double-click, details,
edge geometry, package security, and atomic install tests remain green.

- [ ] **Step 9: Run full .NET regression and commit**

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

Expected: no failed tests, zero warnings/errors, and no whitespace errors.

```powershell
git add src/CodexQuotaHud.App tests/CodexQuotaHud.App.Tests
git commit -m "feat: manage custom skins from the HUD"
```

---

### Task 10: Typed per-user LocalControl and bounded activation launch

**Files:**
- Create: `src/CodexQuotaHud.App/Infrastructure/LocalControl/LocalControlContracts.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/LocalControl/LocalControlProtocol.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/LocalControl/LocalControlPipeFactory.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/LocalControl/LocalControlServer.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/LocalControl/LocalControlClient.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/LocalControl/LocalControlActivationHandler.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/LocalControl/HudActivationRequester.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/AppLaunchRequest.cs`
- Modify: `src/CodexQuotaHud.App/Infrastructure/InstalledAppLauncher.cs`
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/LocalControl/LocalControlProtocolTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/LocalControl/LocalControlServerTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/LocalControl/LocalControlActivationHandlerTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/LocalControl/HudActivationRequesterTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppLauncherTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppShutdownListenerTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/AppLaunchModeTests.cs`

**Interfaces:**
- Produces: `LocalControlCommandKind { ActivateSkin }`, `LocalControlRequest(int ProtocolVersion, LocalControlCommandKind Command, string SelectionKey)`, and `LocalControlResponse(bool Succeeded, string? ErrorCode, string? Message)`.
- Produces: constants `LocalControlProtocol.PipeName = "CodexQuotaHud.LocalControl.v1"`, `ProtocolVersion = 1`, `MaximumPayloadBytes = 4096`, `ConnectTimeout = TimeSpan.FromSeconds(2)`, and `ResponseTimeout = TimeSpan.FromSeconds(2)`.
- Produces: `Task LocalControlProtocol.WriteRequestAsync(Stream stream, LocalControlRequest request, CancellationToken cancellationToken)`, `Task<LocalControlRequest> ReadRequestAsync(Stream stream, CancellationToken cancellationToken)`, `Task WriteResponseAsync(Stream stream, LocalControlResponse response, CancellationToken cancellationToken)`, and `Task<LocalControlResponse> ReadResponseAsync(Stream stream, CancellationToken cancellationToken)` using strict length-prefixed UTF-8 frames.
- Produces: `ILocalControlPipeFactory.AcceptAsync(string pipeName, CancellationToken cancellationToken)` returning `Task<Stream>` and `ConnectAsync(string pipeName, TimeSpan timeout, CancellationToken cancellationToken)` returning `Task<Stream?>`; null means no current-user server. `CurrentUserLocalControlPipeFactory` is the only production implementation and applies `PipeOptions.CurrentUserOnly` at both ends.
- Produces: `LocalControlServer(string pipeName, Func<LocalControlRequest, CancellationToken, Task<LocalControlResponse>> handle, ILocalControlPipeFactory? pipes = null)` with `void Start()` and `ValueTask DisposeAsync()`, and `LocalControlClient(string pipeName, ILocalControlPipeFactory? pipes = null).SendAsync(LocalControlRequest request, CancellationToken cancellationToken = default)`.
- Changes: `LocalControlClient` and `InstalledAppLauncher` are public for the separate designer; `Task<LocalControlResponse> LocalControlClient.SendAsync(LocalControlRequest request, CancellationToken cancellationToken = default)` never throws for normal unavailable/timeout/rejected outcomes.
- Produces: `LocalControlActivationHandler(Func<string, bool> selectionExists, Func<string, CancellationToken, Task<bool>> activateOnUiThread).HandleAsync(LocalControlRequest request, CancellationToken cancellationToken)` returning `Task<LocalControlResponse>`; success means Task 8's prepare/save/activate transaction completed.
- Produces: `HudActivationDisposition { ActivatedLive, StartedHud, Rejected, Failed }`, `HudActivationResult(HudActivationDisposition Disposition, string? ErrorCode, string? Message)`, and `Task<HudActivationResult> HudActivationRequester.ActivateAsync(string selectionKey, CancellationToken cancellationToken = default)`.
- Produces: `AppLaunchRequest(bool IsPreview, bool IsBackground, string? ActivationSelectionKey)` and `bool AppLaunchRequest.TryParse(IReadOnlyList<string> arguments, out AppLaunchRequest? request, out string? error)`.
- Changes: `bool InstalledAppLauncher.TryLaunchActivation(string selectionKey, out string? error)` starts the exact installed HUD executable with two `ArgumentList` items: `--activate-skin` and one canonical `custom:<lowercase-guid-d>` key.
- Preserves: `InstalledAppShutdownListener.EventName == @"Local\CodexQuotaHud.ShutdownRequested"`, its named-event signaling API, and all installer/Preview replacement behavior. Typed LocalControl does not add or replace a shutdown command.
- Consumes: Task 7 `SkinSelectionKey.TryGetCustomId`, Task 8 `QuotaOrbWindow.TryActivateSkinKey`, and Task 9's healthy catalog predicate.

- [ ] **Step 1: Write failing strict protocol and payload-bound tests**

Serialize one literal activation frame and assert a four-byte little-endian
length prefix followed by canonical UTF-8 JSON containing only
`protocolVersion`, `command`, and `selectionKey`. Round-trip:

```csharp
var request = new LocalControlRequest(
    1,
    LocalControlCommandKind.ActivateSkin,
    "custom:11111111-1111-1111-1111-111111111111");
var decoded = await RoundTripAsync(request);
Assert.Equal(request, decoded);
```

Reject protocol `0/2`, zero/negative lengths, `4097` bytes, truncated prefixes,
truncated JSON, trailing second frames, invalid UTF-8, unknown JSON properties,
unknown command strings, built-in keys, uppercase/non-D GUIDs, and cancellation
during a partial read. Every failure is `control.protocol.invalid` or
`control.request.invalid`; no package-controlled text becomes an exception
message shown to the user.

- [ ] **Step 2: Write failing current-user pipe and activation-handler tests**

Use a unique pipe name under one test process. Assert the server creates
`NamedPipeServerStream` with one server instance and
`PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly`, accepts one frame per
connection, returns one response, and continues after malformed, disconnected,
or handler-failed clients. A request exceeding either two-second deadline is
cancelled and never reaches activation after timeout.

For the real handler with recording delegates, cover:

```text
healthy canonical custom key + TryActivate true  -> success response
missing/corrupt key                              -> skin.selection.missing
TryActivate false                                -> skin.activation.failed
handler exception                                -> control.handler.failed
builtin or malformed selection                   -> control.request.invalid
```

Assert the activation delegate is invoked exactly once on the supplied WPF
dispatcher boundary and response success is not written before it returns.

- [ ] **Step 3: Write failing launch-parser, forward, and fallback tests**

Extend `AppLaunchModeTests` with this exact matrix:

```text
--activate-skin custom:<canonical-guid> -> valid non-preview/non-background activation
--activate-skin missing                 -> invalid
--activate-skin builtin:HudDial         -> invalid
--activate-skin custom:<uppercase-guid> -> invalid
--activate-skin <65 characters>         -> invalid before parsing
two --activate-skin switches            -> invalid
--preview plus --activate-skin          -> invalid
--background plus --activate-skin       -> invalid
```

An activation launch never registers startup. If the normal mutex is already
owned, assert startup sends the typed request to the existing pipe and exits
without creating a window. If the mutex is free, assert startup retains the
lease, creates the normal HUD and server, then invokes the same Task 8
prepare/save/activate path after composition. A failed startup activation keeps
HudDial/current formal selection safe and presents one bounded error.

Update launcher tests to capture `ProcessStartInfo` and assert an exact absolute
`FileName`, `UseShellExecute = true`, empty `Arguments`, and these separate
items only:

```csharp
Assert.Equal(
    ["--activate-skin",
     "custom:11111111-1111-1111-1111-111111111111"],
    captured.ArgumentList);
```

Invalid keys, missing executables, false starts, and Win32/IO failures start no
fallback executable and return a stable error.

For `HudActivationRequester`, assert live success returns `ActivatedLive` and
never starts a process; only `control.unavailable` calls
`TryLaunchActivation` and returns `StartedHud`; timeout, malformed response, or
live activation rejection returns `Rejected`/`Failed` and never starts a second
HUD. This is the single API later used by the designer.

- [ ] **Step 4: Run LocalControl and launch tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~LocalControl|FullyQualifiedName~AppLaunchModeTests|FullyQualifiedName~InstalledAppLauncherTests"
```

Expected: compilation fails because typed protocol, server/client, activation
handler, launch request, and activation launcher do not exist.

- [ ] **Step 5: Implement the bounded per-user protocol**

Use a fixed production pipe name `CodexQuotaHud.LocalControl.v1`; tests inject a
GUID-suffixed name. Both ends use `PipeOptions.CurrentUserOnly`. Encode one
message as `Int32 little-endian length + UTF-8 JSON`; allocate only after the
length is within `1..4096`, read exactly that count, reject additional request
bytes, and use `JsonUnmappedMemberHandling.Disallow`. Never use line-delimited
unbounded readers, BinaryFormatter, type-name handling, or package-controlled
reflection.

The server owns one cancellable accept loop, creates a fresh pipe instance for
each request, contains client/handler exceptions, and completes disposal within
the two-second bound. `LocalControlClient.SendAsync` distinguishes unavailable,
timeout, protocol rejection, and activation rejection with stable error codes;
it never retries a rejected activation.

`HudActivationRequester` first sends the typed command. It falls back to the
exact installed executable only when no per-user server exists, and passes the
selection through `InstalledAppLauncher.TryLaunchActivation` so argument length,
canonical custom-key validation, and `ArgumentList` separation cannot diverge.

- [ ] **Step 6: Integrate activation without replacing the shutdown event**

Parse arguments before mutex acquisition. Preserve the existing normal and
hidden Preview branches. For a valid activation request:

1. try the normal mutex;
2. if occupied, send `ActivateSkin` to the existing pipe and exit;
3. if acquired, build the normal HUD/catalog/controller, start LocalControl,
   and run `QuotaOrbWindow.TryActivateSkinKey` on the dispatcher;
4. keep the HUD running whether activation succeeds or fails.

Start `LocalControlServer` only for the normal HUD, never for hidden Preview.
Dispose it before the single-instance lease. Keep
`InstalledAppShutdownListener` and `InstalledAppShutdownCoordinator` byte-for-
byte compatible at their public/event boundary; add a coexistence test proving
the shutdown event still requests exit while the typed pipe is listening.
Neither activation forwarding nor timeout handling force-terminates a HUD or
designer process.

- [ ] **Step 7: Run focused activation and shutdown compatibility tests GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~LocalControl|FullyQualifiedName~AppLaunchModeTests|FullyQualifiedName~InstalledAppLauncherTests|FullyQualifiedName~InstalledAppShutdown|FullyQualifiedName~SingleInstanceGuard"
```

Expected: activation succeeds through the current-user pipe, occupied-instance
launches forward once, free-instance launches activate after composition, all
timeouts are bounded, and legacy shutdown-event tests remain unchanged and
green.

- [ ] **Step 8: Run App regression/build and commit**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

Expected: all App tests pass with zero build warnings and errors.

```powershell
git add src/CodexQuotaHud.App/Infrastructure/LocalControl src/CodexQuotaHud.App/Infrastructure/AppLaunchRequest.cs src/CodexQuotaHud.App/Infrastructure/InstalledAppLauncher.cs src/CodexQuotaHud.App/App.xaml.cs tests/CodexQuotaHud.App.Tests
git commit -m "feat: add typed local skin activation"
```

---

### Task 11: Standalone Skin Designer project, draft domain, defaults, and identity

**Files:**
- Create: `src/CodexQuotaHud.SkinDesigner/CodexQuotaHud.SkinDesigner.csproj`
- Create: `src/CodexQuotaHud.SkinDesigner/AssemblyInfo.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/App.xaml`
- Create: `src/CodexQuotaHud.SkinDesigner/App.xaml.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Create: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Infrastructure/DesignerSingleInstanceGuard.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftDocument.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftFactory.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftJsonCodec.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftValidator.cs`
- Modify: `src/CodexQuotaHud.Skins/Validation/SkinContractValidator.cs`
- Modify: `tests/CodexQuotaHud.Skins.Tests/Validation/SkinContractValidatorTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/CodexQuotaHud.SkinDesigner.Tests.csproj`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/SkinDraftFactoryTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/DraftJsonCodecTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Infrastructure/DesignerSingleInstanceGuardTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/AppCompositionTests.cs`
- Modify: `CodexQuotaHud.sln`

**Interfaces:**
- Produces: a separate `net9.0-windows` WPF `WinExe` named `CodexQuotaHud.SkinDesigner.exe`; it references `CodexQuotaHud.Skins` and `CodexQuotaHud.App`, while neither existing project references the designer.
- Produces: `DesignerSingleInstanceGuard.MutexName == @"Local\CodexQuotaHud.SkinDesigner.Singleton"`, `DesignerSingleInstanceGuard? TryAcquire()`, and `void Dispose()` with the same owner-thread disposal rule as the normal guard.
- Produces: `DraftAssetReference(SkinAssetSlot Slot, string RelativePath, string OriginalFileName)` and the immutable schema-v1 `SkinDraftDocument` declared in Step 3.
- Produces: `SkinDraftDocument SkinDraftFactory.CreateNew(Guid draftId, Guid skinId, DateTimeOffset nowUtc, SemanticVersion minimumHudVersion)` and the exact Free Decoration Ring defaults in Step 2.
- Produces: `SkinValidationResult<SkinTheme> SkinContractValidator.ValidateTheme(SkinTheme theme)` so draft and package paths share the exact bounded theme rules.
- Produces: `SkinValidationResult<SkinDraftDocument> SkinDraftValidator.Validate(SkinDraftDocument draft)`, `SkinValidationResult<SkinDraftDocument> DraftJsonCodec.Parse(ReadOnlySpan<byte> utf8)`, and `byte[] DraftJsonCodec.Write(SkinDraftDocument draft)` with strict known properties and canonical UTF-8 output.
- Consumes: Task 1 contracts/paths, Task 2 `SkinJsonCodec` and validation primitives, Task 6 template ID/bounds, and Task 10 public client/launcher types only. It does not create quota, app-server, startup-registration, or normal-HUD singleton services.

- [ ] **Step 1: Write failing project-boundary and singleton-identity tests**

Add both projects to the solution folders `src` and `tests`. Assert the project
graph from evaluated MSBuild references:

```text
SkinDesigner -> Skins
SkinDesigner -> App
App          -/-> SkinDesigner
Skins        -/-> SkinDesigner or App
```

Acquire the normal `SingleInstanceGuard` and
`DesignerSingleInstanceGuard` simultaneously and assert both leases are
non-null. A second designer acquisition is null, disposal permits reacquisition,
and an abandoned designer mutex is acquired. Assert the exact two mutex names
are unequal.

Composition tests instantiate designer startup with recording factories and
assert it creates no `RestartableQuotaClient`, `CodexProcessMonitor`,
`StartupRegistration`, `InstalledAppShutdownListener`, or `LocalControlServer`.

- [ ] **Step 2: Write failing deterministic draft-default tests**

Call `CreateNew` with fixed IDs, `2026-08-02T00:00:00Z`, and minimum HUD
`1.1.1`. Assert identity defaults: draft/skin IDs are the supplied distinct
values, revision `0`, project/display name `未命名皮肤`, empty author and
description, package version `1.0.0`, no origin ID/assets, and created/updated
timestamps equal the supplied UTC instant.

Assert the exact default `SkinTheme`:

```text
schema/template             = 1 / free-decoration-ring
all transforms              = x 0, y 0, scale 1, rotation 0,
                              opacity 1, crop focus 0.5/0.5
primary/secondary ring      = #FF53DCF8 / #FF9A68FF
base background/opacity     = #FF0A1622 / 0.9
diameter/thickness/gap      = 96 / 8 / 6 DIP
start angle                 = 270
glow color/intensity        = #FF24CFF2 / 0.5
number/label size           = 28 / 12 DIP
weight/placement            = SemiBold / NumberAboveLabel
rotation/breathing/glow/float animation = 0.25 / 0.5 / 0.75 / 1.0
```

Pass the draft's theme through Task 2 validation with installed HUD `1.1.1`
and assert it is valid without clamping or default repair.

- [ ] **Step 3: Write failing strict draft-schema round-trip tests**

Define:

```csharp
public sealed record SkinDraftDocument(
    int DraftSchemaVersion,
    Guid DraftId,
    Guid SkinId,
    long Revision,
    string ProjectName,
    string DisplayName,
    string Author,
    SemanticVersion PackageVersion,
    string Description,
    SemanticVersion MinimumHudVersion,
    Guid? OriginSkinId,
    SkinTheme Theme,
    IReadOnlyDictionary<SkinAssetSlot, DraftAssetReference> Assets,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
```

Round-trip a document with all three assets and assert canonical bytes are
stable across parse/write. The draft JSON embeds Task 2's canonical theme JSON;
asset paths are only `assets/background.png|jpg`, `assets/center.png|jpg`, and
`assets/decoration.png`. Draft-owned JPEG inputs are canonicalized to `.jpg`;
strict draft JSON rejects `.jpeg` rather than silently rewriting it.

Reject draft schema `0/2`, unknown/duplicate properties, missing fields,
non-canonical GUID/version/time text, negative revision, non-UTC timestamps,
over-limit/control-character names, duplicate slots, absolute/traversal paths,
unsupported extensions, and a theme that fails Task 2 validation. Return exact
locations such as `$.projectName`, `$.assets[0].relativePath`, and `$.theme`.
`ProjectName` and `DisplayName` must be non-empty; editable `Author` and
`Description` may be empty in a draft but remain control-free and within the
package scalar limits. Final package validation still requires every mandatory
manifest field before Apply/Export.

- [ ] **Step 4: Run new project/domain tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
```

Expected: MSBuild fails because the designer projects and all draft/identity
types do not exist.

- [ ] **Step 5: Create the separate composition root and identity**

Use this project boundary:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\CodexQuotaHud.Skins\CodexQuotaHud.Skins.csproj" />
    <ProjectReference Include="..\CodexQuotaHud.App\CodexQuotaHud.App.csproj" />
  </ItemGroup>
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net9.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
</Project>
```

`App.OnStartup` acquires only `DesignerSingleInstanceGuard`; on failure it exits
without touching the normal mutex. On success it creates one new draft and a
minimal `MainWindow` shell for later tasks. It registers no startup, quota,
shutdown-event, or LocalControl listener and never calls the normal App startup
composition. Dispose the designer lease on the acquiring dispatcher thread.

- [ ] **Step 6: Implement immutable domain, strict codec, and exact defaults**

Keep draft-only types inside the designer project. `DraftJsonCodec` parses the
outer document with explicit known-property sets, embeds/parses `SkinTheme`
through Task 2 `SkinJsonCodec`, and writes fields in one fixed order. It never
serializes an absolute source path, window position, quota value, account data,
or executable expression.

Expose Task 2's existing private theme checks through `ValidateTheme` without
changing any rule or package-validation result. `SkinDraftValidator` applies
that shared result plus draft identity, revision, metadata, timestamp, and
relative-asset rules; it deliberately permits incomplete author/description
while editing.

`SkinDraftFactory` uses only supplied IDs/time/minimum HUD version and the
literal defaults from Step 2. It performs no filesystem or process work. Add
`InternalsVisibleTo("CodexQuotaHud.SkinDesigner.Tests")` only for internal
composition seams; keep shared contracts public where later designer tasks
consume them.

- [ ] **Step 7: Run designer/domain and architecture tests GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~SingleInstanceGuard|FullyQualifiedName~AppLaunchModeTests"
```

Expected: draft defaults/codec and both singleton identities pass; normal HUD
launch tests remain green and the project graph contains no App-to-Designer
reference.

- [ ] **Step 8: Build the full solution and commit**

```powershell
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

Expected: both executables and all test projects build with zero warnings and
errors.

```powershell
git add CodexQuotaHud.sln src/CodexQuotaHud.SkinDesigner src/CodexQuotaHud.Skins/Validation/SkinContractValidator.cs tests/CodexQuotaHud.SkinDesigner.Tests tests/CodexQuotaHud.Skins.Tests/Validation/SkinContractValidatorTests.cs
git commit -m "feat: scaffold standalone skin designer"
```

---

### Task 12: Atomic draft recovery, named saves, debounce, and bounded history

**Files:**
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftProjectPaths.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftCatalogModels.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/IDraftFileOperations.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftStore.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftRecoveryService.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftHistory.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftSession.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/DraftStoreTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/DraftRecoveryServiceTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/DraftHistoryTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/CorruptDraftTests.cs`

**Interfaces:**
- Produces: `DraftProjectPaths(string draftsRoot, Guid draftId)` with exact string properties `ProjectRoot`, `NamedDraftPath = <root>\draft.json`, `RecoveryPath = <root>\recovery.json`, and `AssetsRoot = <root>\assets`.
- Produces: `DraftLoadFailure(Guid? DraftId, string LeafName, string ErrorCode, string Message)`, `DraftPersistenceFailure(Guid DraftId, string LeafName, string ErrorCode, string Message)`, `DraftOpenResult(SkinDraftDocument? Document, bool WasRecovered, IReadOnlyList<DraftLoadFailure> Failures)`, and `DraftCatalogSnapshot(IReadOnlyList<SkinDraftDocument> Healthy, IReadOnlyList<DraftLoadFailure> Corrupt)`.
- Produces: `void IDraftFileOperations.CreateDirectory(string path)`, `bool DirectoryExists(string path)`, `bool FileExists(string path)`, `FileAttributes GetAttributes(string path)`, `IEnumerable<string> EnumerateDirectories(string path)`, `byte[] ReadAllBytes(string path)`, `Task WriteAndFlushAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)`, `void ReplaceFile(string sourcePath, string destinationPath)`, and `void DeleteFile(string path)`.
- Produces: `DraftStore(SkinStoragePaths paths, IDraftFileOperations? files = null, Func<Guid>? operationId = null)`, `Task SaveNamedAsync(SkinDraftDocument draft, CancellationToken cancellationToken = default)`, `Task SaveRecoveryAsync(SkinDraftDocument draft, CancellationToken cancellationToken = default)`, `DraftOpenResult LoadForOpen(Guid draftId)`, and `DraftCatalogSnapshot LoadAll()`.
- Produces: `DraftRecoveryService(DraftStore store, Func<TimeSpan, CancellationToken, Task>? delay = null)`, `void NotifyMeaningfulChange(SkinDraftDocument draft)`, `Task FlushAsync(CancellationToken cancellationToken = default)`, `ValueTask DisposeAsync()`, and `event EventHandler<DraftPersistenceFailure>? SaveFailed` with exact one-second debounce.
- Produces: `DraftHistory(SkinDraftDocument initial, int capacity = 100)`, `SkinDraftDocument Current`, `int Count`, `bool CanUndo`, `bool CanRedo`, `bool Push(SkinDraftDocument state)`, `bool Undo(out SkinDraftDocument state)`, and `bool Redo(out SkinDraftDocument state)`; capacity counts the current state and never exceeds 100.
- Produces: `SkinDraftSession(SkinDraftDocument initial, Func<DateTimeOffset> utcNow)`, `SkinDraftDocument Current`, `bool HasUnsavedChanges`, `bool Apply(Func<SkinDraftDocument, SkinDraftDocument> edit)`, `bool TryUndo()`, `bool TryRedo()`, `void MarkNamedSaved()`, and `event EventHandler<SkinDraftDocument>? MeaningfulChange`.
- Consumes: Task 1 `SkinStoragePaths.DraftsRoot`, Task 11 immutable document/codec, and no normal HUD settings or installed-skin directory.

- [ ] **Step 1: Write failing exact-path, named-save, and catalog tests**

Use a unique temporary drafts root and fixed IDs. Assert all paths are strict
descendants named with lowercase GUID-D text and reject an empty/root/reparse
escape before any write. A named save creates exactly:

```text
<draft-id>/draft.json
<draft-id>/assets/
```

with no GUID or project name used as an uncontrolled path segment. `LoadAll`
sorts healthy projects by `UpdatedAtUtc` descending then DraftId, reports corrupt
entries separately, and does not enumerate outside the direct GUID children.
Validate `ProjectName` as 1..80 Unicode scalar values with no control characters;
the name remains JSON data, so Windows filename punctuation never changes the
directory name.

- [ ] **Step 2: Write failing atomic save and rollback tests**

Inject `IDraftFileOperations` and fail each transition: create project root,
write/flush temporary bytes, re-read/parse temporary bytes, replace target, and
temporary cleanup. Assert the previous `draft.json` or `recovery.json` bytes
remain exact for every pre-replace failure. A successful write uses a sibling
`.<target>.tmp-<operation-guid>`, flushes file contents, validates through
`DraftJsonCodec`, then performs one same-directory replace; readers never see
partial JSON.

Cancellation before replace preserves the old target. Cancellation after a
successful replace reports success rather than rolling back valid bytes. Normal
success/failure cleans only its exact temporary file; a cleanup failure reports
`draft.cleanup-failed` without deleting the valid target or another operation.

- [ ] **Step 3: Write failing one-second debounce and latest-state tests**

Use a controllable delay delegate and recording store. Assert:

```text
one change + 999 ms       -> zero writes
one change + 1000 ms      -> one recovery write
three changes inside 1 s  -> one write containing only highest Revision
change during active save -> serialized second write with latest Revision
FlushAsync                -> immediate latest write, pending timer cancelled
DisposeAsync              -> awaits/flushes latest state, no post-dispose write
save exception            -> error event once, service remains usable
```

Only `SkinDraftSession.MeaningfulChange` calls `NotifyMeaningfulChange`; applying
an equal record emits nothing. Undo and redo are meaningful changes and receive
the same debounce. No recovery write touches `draft.json` or formal HUD settings.

- [ ] **Step 4: Write failing 100-state undo/redo tests**

Start at revision `0`, push revisions `1..100`, and assert the in-memory count
is exactly 100: revision `0` is evicted, 99 undos reach revision `1`, and no
further undo succeeds. Cover redo, redo truncation after a new edit, duplicate
state suppression, capacity argument validation, and object/reference
independence for asset dictionaries.

`SkinDraftSession.Apply` accepts an edited value but owns revision/time updates:
it increments revision once, records one history state, sets
`HasUnsavedChanges`, and emits one event. Undo/redo do not create new history
nodes; the session rebases restored content onto a new monotonically increasing
revision/UTC time before emitting recovery. `MarkNamedSaved` updates the saved
content baseline but does not clear history; `HasUnsavedChanges` compares
content while ignoring revision/timestamp bookkeeping.

- [ ] **Step 5: Write failing recovery precedence and corrupt-preservation tests**

Cover this exact open matrix:

```text
valid named only                         -> named, WasRecovered false
valid recovery only                      -> recovery, WasRecovered true
both valid, recovery revision > named    -> recovery, WasRecovered true
both valid, recovery revision <= named   -> named, WasRecovered false
named corrupt, recovery valid            -> recovery plus draft.json failure; preserve both
named valid, recovery corrupt            -> named plus recovery failure; preserve both
both corrupt                              -> no document plus two failures; preserve both
missing project                           -> draft.not-found; create nothing
```

Capture file bytes, timestamps, and directory contents before/after each corrupt
load and assert exact equality. A failed parse never deletes, renames, repairs,
or replaces the damaged file. `DraftLoadFailure` exposes only draft ID, leaf
name, stable code, and safe message—never an absolute path or raw JSON. The UI
in the next task may offer a new draft only after receiving this result.

- [ ] **Step 6: Run draft reliability tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DraftStoreTests|FullyQualifiedName~DraftRecoveryServiceTests|FullyQualifiedName~DraftHistoryTests|FullyQualifiedName~CorruptDraftTests"
```

Expected: compilation fails because store, recovery service, history, session,
and load-result types do not exist.

- [ ] **Step 7: Implement safe paths and atomic named/recovery storage**

Normalize `DraftsRoot`, require lowercase GUID-D direct children, walk existing
ancestors for reparse points, and reuse Task 5's owned-directory safety pattern
without granting access to installed skins. Store named and recovery documents
as separate canonical JSON files. Validate temporary bytes before replacement;
all move/delete targets must already be resolved inside the one project root.

`LoadForOpen` selects by revision, not filesystem timestamp. A valid document
may be returned together with failures for its corrupt sibling so the UI can
warn while preserving evidence; when both files are corrupt return both
failures. Never write during load. `LoadAll` continues past corrupt projects and
does not create defaults for them.

- [ ] **Step 8: Implement debounce, session, and bounded history**

Use one cancellation source for the pending debounce and one semaphore for
physical recovery writes. Each notification replaces the pending snapshot only
when its revision is newer. After the injected one-second delay, re-check the
latest revision under synchronization before saving. `FlushAsync` and
`DisposeAsync` cancel the timer and await the serialized latest write; disposal
is idempotent.

Keep `DraftHistory` independent of filesystem/UI code. Clone the asset
dictionary into each accepted state so later editor mutations cannot alter an
older snapshot. `SkinDraftSession` owns monotonically increasing revision/time
changes and emits the immutable current snapshot after edit/undo/redo for
recovery scheduling; undo/redo move the history cursor but rebase restored
content onto a new recovery revision.

- [ ] **Step 9: Run focused and full designer tests GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~Draft"
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
```

Expected: atomic rollback, exact one-second debounce, latest-state coalescing,
100-state history, named catalog, recovery precedence, and corrupt preservation
all pass without filesystem leakage.

- [ ] **Step 10: Run solution regression/build and commit**

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

Expected: no failed tests, zero warnings/errors, and no whitespace errors.

```powershell
git add src/CodexQuotaHud.SkinDesigner/Drafts tests/CodexQuotaHud.SkinDesigner.Tests/Drafts
git commit -m "feat: recover designer drafts atomically"
```

---

### Task 13: Reusable synthetic controller and production-HUD designer preview

**Files:**
- Create: `src/CodexQuotaHud.App/Preview/SyntheticPreviewComposition.cs`
- Create: `src/CodexQuotaHud.App/Preview/SyntheticPreviewState.cs`
- Create: `src/CodexQuotaHud.App/Preview/SyntheticSkinCandidate.cs`
- Create: `src/CodexQuotaHud.App/Preview/TransientCustomSkinFactory.cs`
- Modify: `src/CodexQuotaHud.App/Preview/IPreviewHud.cs`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewDisplayChoice.cs`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewQuotaRefreshController.cs`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewSession.cs`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewComposition.cs`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewControlWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.App/Preview/InMemorySettingsStore.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Preview/DraftPreviewDocumentBuilder.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Preview/DesignerPreviewController.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Preview/SyntheticPreviewCompositionTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewQuotaRefreshControllerTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewSessionTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewCompositionTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewControlWindowTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/InMemorySettingsStoreTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Preview/DraftPreviewDocumentBuilderTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Preview/DesignerPreviewControllerTests.cs`

**Interfaces:**
- Makes public for Designer reuse: `PreviewDisplayChoice { Dual, FiveHourOnly, WeeklyOnly, NoQuota }`, `IPreviewHud`, `PreviewQuotaRefreshController`, and `PreviewSession`; hidden `--preview` continues to consume the same types.
- Changes: `IPreviewHud` exposes `bool TryActivateSkinKey(string selectionKey)`, `void SetDetailsOpen(bool isOpen)`, `void PreviewEdge(EdgeDockSide side)`, and `void ForceExpanded()`; `QuotaOrbWindow` implements all four through its production coordinators.
- Produces: `SyntheticPreviewState(PreviewDisplayChoice DisplayChoice, double FiveHourPercent, double WeeklyPercent, bool AnimationsEnabled, bool IsRefreshing, bool DetailsOpen, EdgeDockSide EdgeSide)` with `Default = Dual/68/34/true/false/false/None`.
- Changes: `void PreviewSession.Apply(SyntheticPreviewState state)`, `void SetDisplayChoice(PreviewDisplayChoice choice)`, `void SetFiveHourPercent(double value)`, `void SetWeeklyPercent(double value)`, `bool SetBuiltInSkin(SkinId skin)`, `void SetAnimationsEnabled(bool value)`, `void SetRefreshing(bool value)`, `void SetDetailsOpen(bool isOpen)`, `void PreviewEdge(EdgeDockSide side)`, and `void ForceExpanded()`; all methods update the existing production `QuotaOrbViewModel`/HUD surface. `SetBuiltInSkin` maps through Task 7's string key and Task 8's durable in-memory activation path.
- Produces: `SyntheticPreviewComposition(Dispatcher dispatcher, Action requestExit, SkinTemplateRegistry? templates = null)`, public `PreviewSession Session`, `QuotaOrbWindow HudWindow`, `AppSettings CurrentInMemorySettings`, `SkinValidationResult<SkinPackageDocument> SetCustomPackage(SkinPackageDocument package)`, `void SetPreviewWorkArea(Rect workArea)`, `void ShowHud()`, and `void Dispose()`.
- Produces internally: `SyntheticSkinCandidate(IQuotaSkin Skin, SkinPresentation Presentation)`, `SkinValidationResult<SyntheticSkinCandidate> TransientCustomSkinFactory.Create(SkinPackageDocument package)`, and `void QuotaOrbWindow.ActivateSyntheticSkin(SyntheticSkinCandidate candidate)`; activation swaps the production renderer/presentation without persisting a selection.
- Produces: `SkinValidationResult<SkinPackageDocument> DraftPreviewDocumentBuilder.Build(SkinDraftDocument draft, IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets)` and `SkinValidationResult<SkinPackageDocument> DesignerPreviewController.Update(SkinDraftDocument draft, IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets)`.
- Preserves: hidden Preview's `PreviewControlWindow`, tray, installed-HUD handoff, and in-memory-only behavior. Designer composition creates neither those surfaces nor a formal `SettingsStore`.
- Consumes: Task 6 renderer, Task 8 custom adapter/presentation, Task 11 draft schema, and Task 12 immutable session snapshots.

- [ ] **Step 1: Write failing complete synthetic-state matrix tests**

Drive one `PreviewSession` against the real `PreviewQuotaRefreshController`,
production view-model, and a recording `IPreviewHud`. Cover all display shapes:

```text
Dual          -> 5-hour primary + weekly secondary
FiveHourOnly  -> single 5-hour primary
WeeklyOnly    -> single weekly primary
NoQuota       -> Hidden, no primary or secondary
```

For each shape apply the exact presets `100, 68, 21, 20, 11, 10, 0`. Assert
raw percentages, labels, `QuotaDisplayMode`, and alert classifications. Add
independent dual cases `21/10`, `20/11`, `11/20`, and `10/21`; primary and
secondary warning/critical colors must never leak into one another.

Apply details open/closed, Left/Right/Top/Bottom edge collapse followed by
`ForceExpanded`, animations enabled/disabled, and refreshing true/false. Assert
each command reaches the same production interfaces used by normal HUD, and an
invalid enum/NaN/infinity is rejected rather than published.

Loop all five stable `SkinId` values through `SetBuiltInSkin` and assert the
hidden Preview activates their string keys only in its `InMemorySettingsStore`;
the existing control-window skin selector remains functional after Task 8's
compatibility projection is removed.

- [ ] **Step 2: Write failing real custom-renderer and isolation tests**

On STA construct `SyntheticPreviewComposition`, pass a complete Task 6 package,
and assert `HudWindow` hosts a real `CustomQuotaSkin`/`CustomSkinRenderer`, not a
designer-only mock. Render:

```text
Dual 68/34 idle
FiveHourOnly 21 refreshing
WeeklyOnly 20 animations disabled
NoQuota hidden
Dual 11/10 and 10/21 mixed alerts
details open/closed
all four collapsed edge pills
```

For a preview work area `Rect(400, 200, 520, 420)`, every collapsed position and
expanded position stays inside that rectangle and does not use a real monitor
edge. Package updates with the same skin ID replace the transient renderer once
and keep the current synthetic quota state.

Use non-zero package animation intensities and assert global preview
`AnimationsEnabled = false` stops every production animation; re-enabling it
restores only package-configured motion and never forces a zero-intensity effect
to animate.

Create a sentinel formal `settings.json`, record bytes/timestamp, then exercise
every preview control and dispose. Assert exact sentinel equality and that the
composition used `InMemorySettingsStore`, created no `SettingsStore`, quota
client, process monitor, tray, LocalControl server, startup registration, or
app-server connection.

- [ ] **Step 3: Write failing draft-to-preview bridge tests**

Build from Task 11's default draft with zero assets, then one, two, and all three
of background JPEG, center PNG, and alpha-capable decoration PNG. Assert manifest identity/metadata
comes from the draft; theme record identity is exact; manifest asset paths match
`DraftAssetReference.RelativePath`; and SHA-256 values are recomputed from the
provided `SkinAsset.Content`, never trusted from draft JSON.

The transient manifest always sets `OriginSkinId = null` because provenance is
installed-local metadata, not shareable package/preview content.

Reject a missing declared asset, extra slot, slot/path mismatch, invalid decoded
dimensions, non-alpha decoration, or theme outside Task 1 bounds with a specific
`SkinValidationError`. `DesignerPreviewController.Update` must leave the last
valid production renderer visible when a new draft snapshot cannot build.

- [ ] **Step 4: Run synthetic Preview tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~SyntheticPreview|FullyQualifiedName~PreviewSessionTests|FullyQualifiedName~PreviewQuotaRefreshControllerTests"
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter FullyQualifiedName~Preview
```

Expected: compilation fails because reusable composition/state, transient custom
skin activation, and the designer draft bridge do not exist.

- [ ] **Step 5: Extract reusable composition without changing hidden Preview**

Move only the in-memory refresh/view-model/HUD/session construction from
`PreviewComposition` into `SyntheticPreviewComposition`. The existing hidden
composition owns one synthetic composition and separately adds its current tray,
control window, state store, and installed-app handoff. Disposal order is
control/tray, then synthetic HUD/view-model; both remain idempotent.

Make the minimum controller types public for the one-way Designer-to-App project
reference. Do not expose normal App composition, formal settings, quota client,
or startup services. `InMemorySettingsStore` persists Task 7 string keys only in
its instance and never resolves a filesystem path.

- [ ] **Step 6: Add transient production-skin and bounded preview work area**

`TransientCustomSkinFactory` validates the package's theme/template/assets and
creates the same Task 8 adapter/presentation as installed custom skins. Add a
preview-only `QuotaOrbWindow` entry point that attaches this candidate to its
existing skin host, animation controller, popup presentation, edge presentation,
and render loop without calling `TrySelectSkinKey`.

`SetPreviewWorkArea` supplies the rectangle used by synthetic edge geometry;
normal HUD keeps `GetNearestWorkArea()`. Details and edge state remain real
production behavior, while the Designer can constrain the owned HUD window to
its preview stage. A failed transient update leaves the previous candidate and
presentation exact.

- [ ] **Step 7: Implement the immutable Designer preview bridge**

`DraftPreviewDocumentBuilder` combines the immutable draft with already decoded,
owned `SkinAsset` values. It computes lowercase SHA-256, creates manifest asset
references in stable slot order, and validates every declared/provided asset
relationship before returning. It performs no filesystem read and permits
incomplete author/description for visual editing while still enforcing all
theme/image safety rules.

`DesignerPreviewController` stores no mutable copy of the draft. On each Task 12
meaningful-change snapshot it builds a package and calls
`SyntheticPreviewComposition.SetCustomPackage`; the combined validation result
is returned to the
Designer UI and never clear the last valid preview.

- [ ] **Step 8: Run Preview, alert, edge, and formal-settings isolation GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~Preview|FullyQualifiedName~QuotaAlert|FullyQualifiedName~EdgeAutoHide|FullyQualifiedName~DetailsPopup"
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter FullyQualifiedName~Preview
```

Expected: all exact states and alert boundaries pass, hidden Preview regressions
remain green, the Designer uses the production HUD renderer, and formal settings
bytes never change.

- [ ] **Step 9: Build the full solution and commit**

```powershell
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

Expected: zero warnings and errors.

```powershell
git add src/CodexQuotaHud.App/Preview src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs src/CodexQuotaHud.SkinDesigner/Preview tests/CodexQuotaHud.App.Tests/Preview tests/CodexQuotaHud.SkinDesigner.Tests/Preview
git commit -m "feat: reuse production HUD preview in designer"
```

---

### Task 14: Split-view Designer editor, safe image workflow, and close decisions

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/App.xaml.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/DesignerViewModel.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/EditorSectionViewModels.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/ImageSlotViewModel.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/SyntheticPreviewViewModel.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/DesignerLayoutPolicy.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/AsyncRelayCommand.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Images/IImagePicker.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Images/DesignerImageService.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Documents/DesignerDocumentService.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Documents/DraftCloseCoordinator.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Documents/IUnsavedChangesDialog.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftStore.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftRecoveryService.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerViewModelTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/SyntheticPreviewViewModelTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Images/DesignerImageServiceTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Documents/DesignerDocumentServiceTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Documents/DraftCloseCoordinatorTests.cs`

**Interfaces:**
- Produces: `DesignerViewModel` with exact properties `BasicInformationEditorViewModel BasicInformation`, `ImageEditorViewModel Images`, `QuotaRingEditorViewModel QuotaRings`, `ColorEffectsEditorViewModel ColorsAndEffects`, `TextEditorViewModel Text`, and `AnimationEditorViewModel Animation`; each mutation calls Task 12 `SkinDraftSession.Apply` once.
- Produces: `ImageSlotViewModel(SkinAssetSlot slot)` with `bool HasAsset`, `string? OriginalFileName`, `SkinImageTransform Transform`, `AsyncRelayCommand ReplaceCommand`, and `AsyncRelayCommand RemoveCommand` for Background, Center, and Decoration.
- Produces: `ImageMutationResult(bool Succeeded, SkinAsset? Asset, DraftAssetReference? Reference, IReadOnlyList<SkinValidationError> Errors)`, `Task<ImageMutationResult> DesignerImageService.ImportAsync(Guid draftId, SkinAssetSlot slot, string sourcePath, CancellationToken cancellationToken = default)`, and `Task<ImageMutationResult> RemoveAsync(Guid draftId, SkinAssetSlot slot, CancellationToken cancellationToken = default)`.
- Produces: `string? IImagePicker.ChooseImage(SkinAssetSlot slot)`; the production picker filters background/center to PNG/JPEG and decoration to PNG, and returns no path on cancel.
- Produces: `DesignerDocumentResult(SkinDraftDocument? Draft, IReadOnlyDictionary<SkinAssetSlot, SkinAsset> Assets, IReadOnlyList<SkinValidationError> Errors)`, `DesignerDocumentResult DesignerDocumentService.CreateNew(Guid draftId, Guid skinId, DateTimeOffset nowUtc, SemanticVersion minimumHudVersion)`, `DesignerDocumentResult OpenDraft(Guid draftId)`, `DesignerDocumentResult EditInstalled(string selectionKey)`, and `Task<DesignerDocumentResult> ImportForEditingAsync(string packagePath, SemanticVersion installedHudVersion, CancellationToken cancellationToken = default)`; none selects or installs a skin.
- Produces: `DesignerWindowLayout DesignerLayoutPolicy.Calculate(Rect workAreaDip, DpiScale dpi)` returning `DesignerWindowLayout(Rect WindowBounds, double EditorWidth, double PreviewWidth, bool Compact)`.
- Produces: `SyntheticPreviewViewModel` with `IReadOnlyList<double> PercentPresets = [100, 68, 21, 20, 11, 10, 0]`, mutable `PreviewDisplayChoice DisplayChoice`, `double FiveHourPercent`, `double WeeklyPercent`, `bool DetailsOpen`, `bool AnimationsEnabled`, `bool IsRefreshing`, and `AsyncRelayCommand PreviewLeftEdgeCommand`, `PreviewRightEdgeCommand`, `PreviewTopEdgeCommand`, `PreviewBottomEdgeCommand`, and `ExpandCommand` that delegate only to Task 13.
- Produces: `AsyncRelayCommand(Func<CancellationToken, Task> execute, Func<bool>? canExecute = null)` implementing `ICommand`, with `bool IsRunning`, `void Cancel()`, re-entry rejection, and dispatcher-safe `CanExecuteChanged`.
- Produces: `UnsavedCloseChoice { Save, Discard, Cancel }`, `UnsavedCloseChoice IUnsavedChangesDialog.Show(SkinDraftDocument draft)`, and `Task<bool> DraftCloseCoordinator.RequestCloseAsync(CancellationToken cancellationToken = default)`.
- Adds: `Task<bool> DraftStore.DiscardWorkingCopyAsync(Guid draftId, long maximumRevision, CancellationToken cancellationToken = default)` and `Task DraftRecoveryService.DiscardAsync(Guid draftId, long maximumRevision, CancellationToken cancellationToken = default)`; only a valid recovery at or below the expected revision is removed, while corrupt evidence is preserved.
- Consumes: Tasks 1–3 validation/decoding, Task 5 installed reader, Task 11 draft domain, Task 12 store/session/history, and Task 13 production preview controller.

- [ ] **Step 1: Write failing six-section and bounded-edit tests**

Construct `DesignerViewModel` from the Task 11 default draft. Assert the editor
exposes exactly these ordered section headers:

```text
基本信息
图片
额度环
颜色与效果
文字
动画
```

Edit name/author/version/description; ring diameter/thickness/gap/start angle;
primary/secondary/background/glow colors and opacities; number/label sizes,
weight, placement; and four animation intensities. Each accepted value changes
one matching `SkinDraftDocument` field, increments revision once, creates one
undo state, schedules one meaningful-change event, and updates preview once.

At every Task 1 minimum/maximum assert acceptance. One step outside, NaN,
infinity, invalid color/version, over-limit Unicode scalars, or control text is
rejected with a field-specific message and changes no draft/history/preview.
Interactive numeric controls use those same min/max values and never silently
clamp pasted invalid text.

- [ ] **Step 2: Write failing three-slot image lifecycle tests**

Using real small PNG/JPEG fixtures and a unique draft assets root, cover:

```text
Background  -> PNG/JPEG, optional alpha
Center      -> PNG/JPEG, optional alpha
Decoration  -> PNG with an alpha-capable decoded pixel format
```

Select/cancel, first import, replace, and remove each slot. Assert import decodes
through Task 3 before copying, writes an owned canonical
`assets/<slot>.<png|jpg>` path atomically, and never depends on the source after
success. Replacement failure retains old bytes/reference/preview. Removal
deletes only that draft-owned file after the session update is accepted and
preserves the other two slots.

For every slot exercise crop focus X/Y `0..1`, offset `-50..50`, scale
`0.25..3`, rotation `-180..180`, and opacity `0..1`; each transform updates only
the selected slot. Reject spoofed extensions, oversized bytes/dimensions/pixels,
traversal/reparse paths, decoration PNG without alpha capability, unsupported content, and source path
disappearance with exact Task 3 errors and no owned-file mutation.

- [ ] **Step 3: Write failing document-open/import conversion tests**

Assert Create New uses Task 11 defaults and new draft/skin IDs. Open Draft loads
Task 12 named/recovery precedence. Edit Installed accepts only a healthy custom
Task 5 record, copies its package assets into a new draft-owned project, keeps
the custom SkinId/package metadata, and never alters installed bytes. Import for
Editing validates with Task 3, creates a draft-owned copy with a new DraftId,
keeps the package SkinId, and does not call Task 5 Install or formal selection.

Built-ins, corrupt drafts/installed records, invalid packages, cancelled file
pickers, and copy failures return safe errors and retain the current document.
No operation reads or writes normal settings, quota data, account state, or
browser data.

- [ ] **Step 4: Write failing split-layout, DPI, and preview-strip tests**

On STA measure the real window at `100%`, `125%`, `150%`, and `200%` DPI for
work areas `1920x1080`, `1280x720`, and `960x540` DIP. Assert:

```text
left editor scrolls independently
right preview stage remains visible and at least 280 DIP wide
bottom synthetic strip remains visible
Save draft / Apply to HUD / Export package remain visible
window bounds never exceed the supplied work area
long 80-scalar Chinese names wrap or scroll without covering actions
```

The bottom strip exposes exactly Dual/5h/Week/None, presets
`100/68/21/20/11/10/0` for each ring, independent primary/secondary controls,
details open/closed, four edge buttons plus expand, animations, and refreshing.
Every command delegates to Task 13 and writes no formal setting.

Assert text/control contrast, visible focus indicators, labels, tab order,
keyboard operation, and accessible names satisfy the existing accessibility
contract at every layout/DPI case. When Windows animations are disabled, the
existing preview/editor animation paths honor reduced motion without changing
layout, hit targets, command availability, or final rendered state.

- [ ] **Step 5: Write failing Save/Discard/Cancel close tests**

Cover the exact close matrix:

```text
no unsaved edits                 -> close without dialog
Save + named save succeeds       -> MarkNamedSaved, close
Save + validation/write fails    -> remain open, recovery retained
Discard + valid recovery         -> cancel debounce, remove working recovery, close
Discard + corrupt recovery       -> preserve corrupt bytes, remain open with warning
Cancel                           -> remain open, no write/delete
dialog close/Escape              -> Cancel
```

Assert `Window.Closing` is cancelled until the async coordinator returns true.
No choice shuts down or changes the normal HUD. Save/Discard cannot delete an
installed skin, another draft, named draft, or recovery with a newer revision
than the prompt snapshot.

- [ ] **Step 6: Run editor/image/layout/close tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DesignerViewModel|FullyQualifiedName~DesignerImageService|FullyQualifiedName~DesignerDocumentService|FullyQualifiedName~MainWindowLayout|FullyQualifiedName~SyntheticPreviewViewModel|FullyQualifiedName~DraftCloseCoordinator"
```

Expected: compilation fails because the editor view-models, safe image/document
services, layout policy, and close coordinator do not exist.

- [ ] **Step 7: Implement safe document and image services**

Use OS dialogs only through `IImagePicker`/document dialog boundaries. Decode
before mutation, stage a sibling asset file, flush and re-decode it, then update
the immutable session and atomically promote the file. If session validation or
promotion fails, restore the previous exact reference/bytes. Never serialize a
source absolute path; store only leaf display name plus draft-relative owned
path.

Document conversions create isolated draft directories and copy/decode every
asset before exposing the new session. A conversion failure deletes only its
new operation/draft directory and leaves the current session, installed package,
and imported source exact.

- [ ] **Step 8: Implement split view, six editors, and production preview anchor**

Build `MainWindow` as rows `header / content / synthetic strip / primary action
bar`; content is columns `independently scrolling editor / preview stage`. Keep
primary actions outside the editor `ScrollViewer`. Bind all six sections to
immutable-session commands and expose error/disabled/loading states for file and
output operations.

The preview stage owns Task 13's real `QuotaOrbWindow`, continuously updates its
preview work-area rectangle in screen DIPs on move/resize/DPI changes, and
re-centers it after expand. It is owned by the Designer window, hidden/minimized
with it, never appears in taskbar, and closes only with the Designer—not the
normal HUD. Apply `DesignerLayoutPolicy` before show and on work-area/DPI change.

Preserve keyboard focus, labels, tab order, visible focus rings, text/control
contrast, and accessible names for sliders, image buttons, preview controls,
and primary actions. Use the existing Windows animation preference and Task 13
animation gate so necessary state transitions remain clear while nonessential
motion is disabled under reduced motion.

- [ ] **Step 9: Implement bounded close coordination**

Intercept `Closing` once, disable repeat close, and await the recording dialog.
Save validates draft fields then calls Task 12 named atomic save before
`MarkNamedSaved`. Discard calls `DraftRecoveryService.DiscardAsync`; it deletes
only a parsed recovery matching DraftId with revision no newer than the prompt.
If parsing or deletion fails, keep the window open and preserve evidence.
Cancel restores normal editor state without flushing or disposing the recovery
service.

- [ ] **Step 10: Run focused designer UI tests GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DesignerViewModel|FullyQualifiedName~DesignerImageService|FullyQualifiedName~DesignerDocumentService|FullyQualifiedName~MainWindowLayout|FullyQualifiedName~SyntheticPreviewViewModel|FullyQualifiedName~DraftCloseCoordinator"
```

Expected: six-section edits, three asset slots/transforms, document conversions,
all DPI/layout cases, accessibility/reduced-motion checks, preview controls, and
close decisions pass.

- [ ] **Step 11: Run Designer and shared security regressions and commit**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinImage|FullyQualifiedName~SkinPackageAttack|FullyQualifiedName~SkinContractValidator"
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

Expected: all tests pass with zero warnings/errors.

```powershell
git add src/CodexQuotaHud.SkinDesigner tests/CodexQuotaHud.SkinDesigner.Tests
git commit -m "feat: build split-view skin designer"
```

---

### Task 15: Apply/export composition, atomic install, typed activation, and separate lifetime

**Files:**
- Create: `src/CodexQuotaHud.SkinDesigner/Output/DraftPackageBuilder.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Output/DesignerOutputModels.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Output/ISkinOutputDialogs.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Output/SkinApplyService.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Output/SkinExportService.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Output/DesignerOutputCoordinator.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/DesignerViewModel.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/App.xaml.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/DraftPackageBuilderTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/SkinApplyServiceTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/SkinExportServiceTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/DesignerOutputCoordinatorTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/SeparateLifetimeTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Infrastructure/LocalControl/LocalControlActivationHandlerTests.cs`

**Interfaces:**
- Produces: `SkinValidationResult<SkinPackageBuildRequest> DraftPackageBuilder.Build(SkinDraftDocument draft, IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets)` using Task 4 canonical writer input.
- Produces: `DesignerOutputDisposition { AppliedLive, InstalledAndHudStarted, InstalledNotActivated, Exported, Cancelled, Failed }` and `DesignerOutputResult(DesignerOutputDisposition Disposition, InstalledSkinRecord? Installed, string? ExportPath, IReadOnlyList<SkinValidationError> Errors, string? Message)`.
- Produces: `string? ISkinOutputDialogs.ChooseExportPath(string suggestedFileName)`, `bool ConfirmExportReplace(string destinationPath)`, `SkinCollisionDecision ChooseApplyCollision(SkinInstallPreview preview)`, and `void ShowResult(DesignerOutputResult result)` as the only output UI boundaries.
- Produces: `Task<DesignerOutputResult> SkinApplyService.ApplyAsync(SkinDraftDocument draft, IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets, CancellationToken cancellationToken = default)`.
- Produces: `Task<DesignerOutputResult> SkinExportService.ExportAsync(SkinDraftDocument draft, IReadOnlyDictionary<SkinAssetSlot, SkinAsset> assets, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)`.
- Produces: `AsyncRelayCommand DesignerOutputCoordinator.ApplyCommand`, `AsyncRelayCommand ExportCommand`, `bool IsBusy`, `string? ErrorMessage`, and `DesignerOutputResult? LastResult`; each command disables re-entry and leaves draft/history/recovery lifetime owned by the Designer.
- Consumes: Task 3 validation, Task 4 deterministic writer, Task 5 shared atomic installer/collision results, Task 10 `HudActivationRequester`, Task 12 draft state, and Task 14 owned decoded assets/dialog boundary.

- [ ] **Step 1: Write failing package-build and deterministic export tests**

Build Task 4 requests from otherwise complete drafts with zero, one, two, and
all three owned assets; all four are valid because image slots are optional.
Assert exact manifest/theme values, stable slot ordering, hashes derived from
bytes, `OriginSkinId == null`, and no draft/source absolute paths. Installed-local
provenance stays in the draft/catalog only and is never exported. Missing
mandatory metadata, mismatched/extra assets, invalid theme, or an incompatible
minimum HUD version returns the shared field/asset error before writing.

Export twice to separate streams/files and assert byte-for-byte identical
`.cqskin` output and Task 3 revalidation. Cancelled path selection writes
nothing. Existing destination with overwrite false is preserved; confirmed
overwrite uses Task 4's sibling temporary/atomic replace and a simulated final
move failure preserves the old exact bytes. Export never installs, selects,
sends LocalControl, starts HUD, or changes Designer recovery/history.

- [ ] **Step 2: Write failing apply pipeline and collision tests**

Use real Task 4 writer, Task 3 reader, and Task 5 installer under a unique Local
App Data root. Assert this strict observable order:

```text
build -> write operation-scoped staged package -> validate staged package
-> inspect collision -> obtain allowed decision -> atomic install
-> reload installed record -> request typed activation -> report result
```

Cover clean install, same-ID equal/newer Replace/KeepCopy/Cancel, downgrade,
invalid draft/asset, staging write failure, validation failure, install rollback,
and cancellation at every boundary. Cancel/invalid/failure before promotion
creates no installed record. Replace rollback preserves old bytes. Keep Copy
activates the returned new `custom:<new-id>`, not the source ID. Every path
cleans only its exact apply staging directory.

- [ ] **Step 3: Write failing live/offline activation and formal-selection tests**

Seed formal settings with `builtin:EnergyRing` and install a valid custom skin.
Cover:

```text
running HUD + typed activation success -> AppliedLive; custom key persisted
HUD absent + bounded launch succeeds    -> InstalledAndHudStarted
live activation rejected/timeout        -> InstalledNotActivated; EnergyRing remains
HUD launch missing/fails                -> InstalledNotActivated; EnergyRing remains
activation response malformed           -> InstalledNotActivated; no process fallback
```

Assert `HudActivationRequester` is called only after Task 5 returns a healthy
installed record. On activation failure the installed package remains visible
in the HUD catalog, the Designer reports `可在 HUD 皮肤菜单中手动应用`, and no
second install, force termination, direct window call, or settings write occurs.
For offline launch capture the exact two `ArgumentList` values from Task 10.

Add an App-side test proving `LocalControlActivationHandler` re-loads the
installed catalog/key and calls Task 8 prepare/save/activate; a corrupt/missing
record returns rejection before formal selection changes.

- [ ] **Step 4: Write failing separate-lifetime and command-state tests**

Acquire normal and Designer mutexes, start recording normal-HUD and Designer
composition roots simultaneously, then:

```text
close Designer -> HUD/control server/settings remain alive
close HUD      -> Designer/draft recovery/undo remain alive
Apply          -> neither mutex released or reacquired
Export         -> no HUD process/control interaction
second Designer-> rejected without affecting HUD
```

`ApplyCommand`/`ExportCommand` reject re-entry, honor cancellation, restore
enabled state after every outcome, and marshal only result presentation to the
Designer dispatcher. Neither command connects to app-server or registers
startup.

- [ ] **Step 5: Run output/lifetime tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DraftPackageBuilder|FullyQualifiedName~SkinApplyService|FullyQualifiedName~SkinExportService|FullyQualifiedName~DesignerOutputCoordinator|FullyQualifiedName~SeparateLifetime"
```

Expected: compilation fails because package builder, output services/models,
coordinator, and lifetime composition tests do not exist.

- [ ] **Step 6: Implement canonical draft packaging and export**

`DraftPackageBuilder` performs final package-level validation, including
mandatory non-empty author/description, before creating Task 4's request. It
uses owned decoded assets only, returns field/asset errors through
`SkinValidationResult`, and never reopens the user's original files.

`SkinExportService` validates destination ownership semantics, delegates all ZIP
bytes/hashes/order to `SkinPackageWriter`, and maps Task 4 validation,
destination-exists, cancellation, and atomic-replace errors into
`DesignerOutputResult`. Sanitize the suggested filename from display name, cap
it at 80 scalars, and fall back to `<skin-id>.cqskin`; the chosen full path is
never stored in the draft.

- [ ] **Step 7: Implement atomic Apply followed by typed activation**

Create `%LOCALAPPDATA%\CodexQuotaHud\imports\<operation-guid>\apply.cqskin`,
write/flush it through Task 4, re-read it through Task 3, then pass the validated
preview/allowed decision to Task 5. Do not send activation before promotion and
catalog reload succeed. Map clean install to Task 5's internal non-collision
promote path; display Replace/Keep Copy/Cancel only for the allowed equal/newer
same-ID case, and reject downgrade without promotion.

After success call only `HudActivationRequester.ActivateAsync` with
`SkinInstallResult.Installed.SelectionKey`. Map `ActivatedLive` to AppliedLive,
`StartedHud` to InstalledAndHudStarted, and every rejection/failure to
InstalledNotActivated while retaining the installed package. Always remove the
exact apply staging operation; never delete an installed record to compensate
for activation failure.

- [ ] **Step 8: Wire Designer commands without coupling application lifetimes**

Construct writer/reader/installer/requester once in the Designer composition
root using Task 1 paths. Inject them into output services and the UI coordinator;
do not instantiate normal `App`, normal mutex, quota services, LocalControl
server, shutdown listener, or startup registration. Result dialogs are owned by
the Designer window only.

Designer shutdown flushes/disposes Task 12 recovery and its preview/window, then
releases only the Designer mutex. It never signals the HUD shutdown event.
Normal HUD shutdown closes only its control server/window/lease and has no
reference to Designer objects.

- [ ] **Step 9: Run focused output, installer, and activation tests GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~Output|FullyQualifiedName~SeparateLifetime"
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinPackageWriter|FullyQualifiedName~SkinPackageInstaller|FullyQualifiedName~SkinInstallRollback|FullyQualifiedName~SkinPackageAttack"
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~LocalControlActivationHandler|FullyQualifiedName~AppLaunchModeTests"
```

Expected: deterministic export, atomic install/collision/rollback, live and
offline activation, failure preservation, and independent application lifetimes
all pass.

- [ ] **Step 10: Run full solution regression/build and commit**

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

Expected: no failed tests, zero warnings/errors, and no whitespace errors.

```powershell
git add src/CodexQuotaHud.SkinDesigner tests/CodexQuotaHud.SkinDesigner.Tests tests/CodexQuotaHud.App.Tests/Infrastructure/LocalControl/LocalControlActivationHandlerTests.cs
git commit -m "feat: apply and export designer skins"
```

---

### Task 16: Publish both applications and keep the ZIP fallback lightweight

**Files:**
- Modify: `scripts/publish.ps1`
- Modify: `scripts/package-release.ps1`
- Modify: `tests/CodexQuotaHud.App.Tests/Packaging/PackagingScriptTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Packaging/InstallerBuildTests.cs`

**Interfaces:**
- Changes: `scripts/publish.ps1` replaces the ambiguous single
  `-ProjectPath` override with `-AppProjectPath` and
  `-DesignerProjectPath`; production still permits only the exact
  `artifacts\CodexQuotaHud-win-x64` output, while internal test mode still
  requires a unique system-temporary output.
- Produces: one validated publish tree with exact executable locations
  `CodexQuotaHud.App.exe` and
  `designer\CodexQuotaHud.SkinDesigner.exe`; both are Release, `win-x64`,
  self-contained, single-file GUI applications with the same semantic/file/
  assembly version arguments and no PDB sidecar.
- Changes: the internal fake-publisher capture becomes an ordered JSON-lines
  record so tests can prove two independent `dotnet publish` invocations and
  identify which project/output pair failed.
- Preserves: PowerShell name construction
  `"CodexQuotaHud-v$Version-win-x64.zip"` as the normal-HUD fallback.
  Because ZIP installation has no component-selection UI, it intentionally
  contains the normal executable, runtime/import support embedded in that
  executable, install/uninstall scripts, README, and LICENSE, but not the
  optional designer executable or design-time files.
- Consumes: Task 11's designer project and Task 1's shared runtime. The App
  project may reference `CodexQuotaHud.Skins`; it must never reference
  `CodexQuotaHud.SkinDesigner`.

- [ ] **Step 1: Write failing two-project publish-layout tests**

Extend the fake publisher so its behavior is selected from the project filename
and it writes either `CodexQuotaHud.App.exe` or
`CodexQuotaHud.SkinDesigner.exe` into the supplied `-o` directory. Capture one
JSON object per call with `Project`, `Configuration`, `Runtime`,
`SelfContained`, `PublishSingleFile`, `DebugType`, `Version`, `FileVersion`,
`AssemblyVersion`, and `Output`.

Add tests that invoke `publish.ps1 -InternalTestMode` and assert exactly two
records in this order:

```text
src/CodexQuotaHud.App/CodexQuotaHud.App.csproj
  -> $verificationRoot/CodexQuotaHud.App.exe
src/CodexQuotaHud.SkinDesigner/CodexQuotaHud.SkinDesigner.csproj
  -> $verificationRoot/designer/CodexQuotaHud.SkinDesigner.exe
```

Both records must contain `Release`, `win-x64`, self-contained `true`,
`PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`,
`DebugType=None`, `DebugSymbols=false`, and identical version arguments. Assert
the tree contains the two expected executables, no PDB, no source file, and no
third executable.

Cover missing App project, missing Designer project, missing output executable,
failure of the first publisher, failure of the second publisher, existing
output, output reparse point, a non-temporary internal output, and a non-exact
production output. A failed publish leaves no new partial tree; when a prior
valid output existed, it remains byte-for-byte unchanged.

- [ ] **Step 2: Write failing release-package boundary tests**

Run `package-release.ps1` through its internal temporary path and inspect the
ZIP central directory. Assert the archive entries are exactly:

```text
artifacts/CodexQuotaHud-win-x64/CodexQuotaHud.App.exe
scripts/install.ps1
scripts/uninstall.ps1
README.md
LICENSE
```

Assert the designer executable is present in the internal published tree when
the installer builder is called, but absent from the ZIP. Assert the generated
Setup and ZIP remain the only files listed in `SHA256SUMS.txt`, each lowercase
SHA-256 matches its artifact, and the temporary publish/stage/checksum files are
removed on both success and every injected failure.

Also load the three project files as XML and prove the dependency direction:

```text
CodexQuotaHud.App -> CodexQuotaHud.Skins
CodexQuotaHud.SkinDesigner -> CodexQuotaHud.Skins + CodexQuotaHud.App
CodexQuotaHud.App -/-> CodexQuotaHud.SkinDesigner
```

The ZIP normal executable must therefore retain Task 7–10 import, selection,
removal, custom rendering, and activation-listener behavior without any
designer payload.

- [ ] **Step 3: Run packaging tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~PackagingScriptTests|FullyQualifiedName~InstallerBuildTests"
```

Expected: the tests fail because publishing still invokes one project, the
designer executable is not validated, and internal capture is single-call.

- [ ] **Step 4: Make publishing atomic across both executables**

Resolve both project paths relative to the repository only when an override is
absent. Validate both are regular project files before creating any output.
Publish into an operation-scoped sibling stage, with the App at the stage root
and Designer under `designer`. Validate the two exact executable paths and scan
the staged tree for PDB/source/extra executable files before promotion.

Promotion is one rollback-safe operation: reject reparse points in output,
stage, or backup; move an existing exact output to an operation-scoped sibling
backup; move the complete stage to the exact output; then remove the backup. If
publish or promotion fails, remove only the checked operation stage, restore an
existing backup, and prove the old output remains intact. Internal hooks and
custom executable paths remain illegal outside `-InternalTestMode`.

Do not copy the shared runtime as a loose DLL. Both single-file applications
embed their own referenced assemblies; this is what keeps normal-HUD startup
independent when the Designer component is absent or later removed.

- [ ] **Step 5: Package the normal ZIP from the composed publish tree**

Have `package-release.ps1` call the new two-project publisher once, require both
published executables before Setup compilation, then copy only
`CodexQuotaHud.App.exe` into the ZIP stage. Keep the existing install/uninstall
scripts and exact release-root, reparse, cleanup, atomic checksum, and internal
hook restrictions unchanged.

Before ZIP creation, enumerate its stage and reject any entry outside the five
paths from Step 2. Before Setup creation, validate the designer executable at
the exact `designer` child path. A missing normal executable fails during ZIP
staging; a missing designer executable fails before invoking the Inno compiler;
neither failure leaves a stale archive, Setup, checksum, stage, or internal
publish directory.

- [ ] **Step 6: Run focused GREEN and a real temporary dual publish**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~PackagingScriptTests|FullyQualifiedName~InstallerBuildTests"
$verificationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("CQH-Designer-Publish-" + [Guid]::NewGuid().ToString("N"))
try {
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Version 0.0.0 -OutputPath $verificationRoot -InternalTestMode
    Get-ChildItem -LiteralPath $verificationRoot -Recurse -File | Select-Object FullName,Length
} finally {
    if (Test-Path -LiteralPath $verificationRoot) { Remove-Item -LiteralPath $verificationRoot -Recurse -Force }
}
```

Expected: focused tests pass; the temporary real publish lists only the two
expected executable payloads at their exact paths and is removed afterward.
`0.0.0` is an internal verification version and must never be described or
uploaded as a release.

- [ ] **Step 7: Build and commit**

```powershell
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
git add scripts/publish.ps1 scripts/package-release.ps1 tests/CodexQuotaHud.App.Tests/Packaging/PackagingScriptTests.cs tests/CodexQuotaHud.App.Tests/Packaging/InstallerBuildTests.cs
git commit -m "build: publish the optional skin designer"
```

Expected: zero warnings/errors and no whitespace errors.

---

### Task 17: Unchecked localized Inno component with safe add/remove lifecycle

**Files:**
- Modify: `installer/CodexQuotaHud.iss`
- Modify: `scripts/build-installer.ps1`
- Modify: `scripts/installer-lifecycle.ps1`
- Modify: `scripts/installer-lifecycle-production.ps1`
- Modify: `scripts/test-installer.ps1`
- Modify: `tests/CodexQuotaHud.App.Tests/Packaging/InstallerBuildTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Packaging/InstallerLifecycleTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Packaging/InstallerComponentLifecycleTests.cs`

**Interfaces:**
- Adds: Inno setup type `normal` as the fresh default, type `custom` with
  `iscustom`, and component `designer`; `designer` belongs only to `custom`, so
  it is unchecked on a fresh install while `UsePreviousComponents=yes` retains
  an existing selection during upgrade.
- Adds: localized custom messages `DesignerComponentName`,
  `DesignerComponentDescription`, `NormalInstallType`, and
  `CustomInstallType` in both `english` and `chinesesimp`. The component-page
  explanatory label uses the description for the current installer language.
- Adds: `designer\CodexQuotaHud.SkinDesigner.exe` and embedded design resources
  to `{app}\designer` only for `Components: designer`; adds one localized
  Start-menu shortcut named exactly `Codex Quota HUD 皮肤设计器` only for that
  component. The stable product name avoids orphaning a differently localized
  shortcut when Setup is rerun in another language.
- Adds: lifecycle actions `PrepareDesignerComponentRemoval`,
  `CommitDesignerComponentRemoval`, and
  `RollbackDesignerComponentRemoval`, each accepting
  `-DesignerBackupPath`; the backup is an operation-scoped, GUID-named sibling
  of the exact installation directory.
- Preserves: normal App files, five built-ins, custom runtime/import, normal
  desktop task, normal startup task, normal Start-menu entry, hidden raw
  `--preview`, settings, installed skins, imports, drafts, and recovery state.
- Consumes: Task 16's exact dual publish layout and the existing exact-path,
  reparse-safe, rollback-capable lifecycle helper.

- [ ] **Step 1: Write failing Inno component-definition tests**

Parse `CodexQuotaHud.iss` and assert all of the following literals and
relationships rather than merely checking the word `designer`:

```text
[Setup] DefaultSetupType=normal
[Types] normal first; custom has Flags: iscustom
[Components] Name: "designer"; Types: custom
[Files] {#PublishedDir}\designer\CodexQuotaHud.SkinDesigner.exe
        -> {app}\designer; Components: designer
[Icons] localized Designer Start-menu name -> exact Designer executable;
        Components: designer
```

Assert fresh `normal` does not select the component, `custom` can select it,
and prior component selection is retained on upgrade. Assert Chinese and
English name/description/type messages are non-empty and distinct, while the
Designer shortcut name remains the one exact product name in both languages.

Assert no Designer entry exists in `[Tasks]` or `[Registry]`; no Designer icon
uses `{autodesktop}`; no Designer command contains `--background` or
`--preview`; and the public normal desktop/Start-menu entries still launch only
`{app}\CodexQuotaHud.App.exe` with no arguments. The normal startup value alone
uses `--background`.

- [ ] **Step 2: Write failing component-removal lifecycle tests**

In a unique temporary Local App Data tree, create exact program files under
`Programs\CodexQuotaHud\designer`, an exact Designer Start-menu link, unrelated
siblings, and sentinels under:

```text
CodexQuotaHud\settings.json
CodexQuotaHud\skins\11111111-1111-1111-1111-111111111111\...
CodexQuotaHud\designer\drafts\22222222-2222-2222-2222-222222222222\...
CodexQuotaHud\designer\recovery\...
CodexQuotaHud\imports\...
```

`PrepareDesignerComponentRemoval` must move only the program-folder Designer
payload and its exact Start-menu link into the checked operation backup. It
must not touch the normal executable, uninstaller, normal shortcuts/startup,
unrelated files, or any settings-root sentinel. `Rollback...` restores exact
bytes/timestamps and is retry-safe; `Commit...` removes the checked backup and
is retry-safe.

Cover absent component, missing shortcut, read-only/hidden Designer files,
copy/move/delete failures, forged marker, wrong backup prefix, backup outside
the Programs parent, target/ancestor/nested reparse points, and a same-name
unrelated process. An exact running Designer receives a normal window-close
request and a bounded wait; if it remains alive or its executable path/stable
identity cannot be proved, removal fails without force termination and without
moving any file.

Extend `PrepareInstall` and `PrepareUninstall` tests to prove an installed exact
Designer is also closed before overwrite/removal. Existing exact normal HUD
shutdown behavior is unchanged. No lifecycle action ever resolves or deletes
`%LOCALAPPDATA%\CodexQuotaHud` except the already explicit `PurgeSettings`
action.

- [ ] **Step 3: Write failing isolated installer matrix tests**

Extend `test-installer.ps1` with named, isolated scenarios, each using its own
temporary install/shell/settings roots and internal registry name:

| Scenario | Setup command selection | Exact postconditions |
|---|---|---|
| `fresh-default` | `/TYPE=normal` | normal EXE/runtime, normal startup/Start/desktop exist; Designer folder/link absent |
| `fresh-designer` | `/TYPE=custom /COMPONENTS=designer` | both EXEs exist; Designer Start link exists; no Designer desktop/startup/raw Preview link |
| `add-designer` | default install, then selected rerun | Designer files/link added; settings/skin/draft/import hashes unchanged |
| `remove-designer` | selected install, then `/TYPE=normal /COMPONENTS=""` | Designer program files/link removed; normal app/tasks and all user-data hashes unchanged |
| `upgrade-selected` | selected older internal install, then rerun with previous components | Designer remains selected; both payloads upgrade; user data unchanged |
| `uninstall-preserve` | selected install, normal uninstall | program/managed links/managed Run value removed; exact settings root including skins/drafts remains |
| `uninstall-purge` | selected install, explicit purge uninstall | program/managed links/Run value and exact settings root removed; unrelated sibling remains |

Run each mutation twice where idempotence matters. Record setup/uninstall exit
codes and inspect shortcut target/arguments through the existing locale-neutral
shortcut reader. Assert all operation backup/stage/helper paths and internal
registry values are absent after cleanup. Production paths, shortcuts, registry,
running apps, and released artifacts must remain untouched.

- [ ] **Step 4: Run installer tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~InstallerBuildTests|FullyQualifiedName~InstallerLifecycleTests|FullyQualifiedName~InstallerComponentLifecycleTests"
```

Expected: component declarations, second executable validation, removal actions,
and the seven-scenario matrix do not exist.

- [ ] **Step 5: Add the unchecked localized component**

Set `DefaultSetupType=normal` and `UsePreviousComponents=yes`; add `normal` and
`custom` types and the single `designer` component. Files without `Components`
remain the always-installed normal HUD/runtime. Tag only the Designer publish
subtree and the exact `Codex Quota HUD 皮肤设计器` Start-menu icon with
`Components: designer`.

In `InitializeWizard`, add a wrapping `TNewStaticText` to
`WizardForm.SelectComponentsPage.Surface` below the component list. Its caption
comes from `DesignerComponentDescription`, updates with the selected language,
uses the component page width, and does not overlap buttons at 100–200% DPI.
Do not add a desktop/startup task for Designer and do not expose `--preview`.

`build-installer.ps1` must require both exact published executables before
invoking Inno and keep production/internal output boundaries intact. Its
compiler define remains one `PublishedDir`; the `.iss` file resolves the
Designer only through its exact child path.

- [ ] **Step 6: Implement rollback-safe component removal**

Mirror the internal and production lifecycle helpers. Validate the exact
installation directory first, then derive only `{app}\designer`; reject every
reparse point from the checked boundary through target/backup content. The
backup marker records exact source, backup, Designer shortcut source, and
whether that shortcut existed. Never follow, widen, or glob user-controlled
paths.

In `PrepareToInstall`, generate a new Designer backup path only when
`WizardIsComponentSelected('designer')` is false. Run normal `PrepareInstall`
first; then run `PrepareDesignerComponentRemoval` before Inno writes files. If
either fails, surface the localized actionable error and abort. At `ssDone`,
commit legacy migration and Designer removal before setting
`InstallCompleted`. In `DeinitializeSetup`, roll back Designer removal before
legacy compensation when installation did not complete.

Do not rely on Inno's built-in component warning to remove omitted files: it
explicitly does not. The lifecycle action owns exact removal and rollback.
Selected installs skip removal and let `[Files]`/`[Icons]` atomically add or
upgrade the component.

- [ ] **Step 7: Extend isolated Setup smoke without touching the real machine**

Teach the smoke harness to pass exact `/TYPE` and `/COMPONENTS` switches, seed
and hash settings/skin/draft/recovery/import sentinels, inspect both executable
versions, and distinguish the normal and Designer links. All roots must remain
under the generated internal test root; every scenario has preflight checks
that reject the production install, shell, settings, and registry locations.

Normal uninstall preservation and explicit purge continue through the current
uninstall option/lifecycle flow. Removing only the Designer component never
invokes purge and never removes the normal program directory.

- [ ] **Step 8: Run focused lifecycle and isolated installer GREEN**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~InstallerBuildTests|FullyQualifiedName~InstallerLifecycleTests|FullyQualifiedName~InstallerComponentLifecycleTests"
```

Expected: definition, lifecycle attacks, exact add/remove, upgrade,
preservation, uninstall, purge, idempotence, and cleanup tests pass. This is an
isolated automated smoke only; do not launch a production Setup or change the
maintainer machine.

- [ ] **Step 9: Run packaging regression and commit**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter FullyQualifiedName~Packaging
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
git add installer/CodexQuotaHud.iss scripts/build-installer.ps1 scripts/installer-lifecycle.ps1 scripts/installer-lifecycle-production.ps1 scripts/test-installer.ps1 tests/CodexQuotaHud.App.Tests/Packaging
git commit -m "feat: add optional designer installer component"
```

Expected: all packaging tests pass, build has zero warnings/errors, and there
are no whitespace errors.

---

### Task 18: Documentation, full security regression, and honest Windows acceptance gate

**Files:**
- Modify: `README.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `CURRENT_TASK.md`
- Modify: `CHANGELOG_AI.md`
- Create: `docs/verification/2026-08-02-optional-skin-designer-acceptance.md`

**Interfaces:**
- Documents: always-installed runtime/import versus optional Designer,
  `.cqskin` data-only limits, exact storage locations, apply/export workflow,
  Setup add/remove semantics, normal ZIP fallback boundary, and source-build
  commands.
- Records: automated commands, exact pass/fail counts, build warnings/errors,
  commit under test, candidate artifact hashes when built, environment, and
  every unperformed item as `NOT RUN` or `BLOCKED`; no historical v1.1.1
  evidence is attributed to this feature.
- Defines: release gate states `PASS`, `PARTIAL`, `FAIL`, and `NOT RUN`; only
  direct evidence can move a row to `PASS`.
- Preserves: existing v1.1.1 public release instructions/hashes as historical
  facts until a separately authorized versioning/release task changes them.

- [ ] **Step 1: Add the acceptance record and verify the evidence gate starts RED**

Create the acceptance document with these exact sections:

```text
Scope and release boundary
Build identity and environment
Automated regression evidence
Package-security attack evidence
Installer isolated-smoke evidence
Manual Designer and HUD matrix
Manual Setup matrix
Preservation and recovery evidence
Open failures / NOT RUN items
Release decision
```

Start every evidence row as `NOT RUN`. Each row has `Status`, `Date/time
(Asia/Tokyo)`, `Command or action`, `Expected`, `Observed`, and `Evidence`
columns. The release decision begins `NOT RUN — no release is authorized` and
cannot become PASS merely because automated tests pass.

Expected RED: the document's automated, attack, installer, and manual gates are
all `NOT RUN`, so the overall feature acceptance cannot be PASS.

The record identifies separately: the source commit under test, Windows build,
.NET SDK, Inno version, monitor arrangement, DPI, candidate version, candidate
Setup/ZIP SHA-256, and whether the executable was signed. Unknown values remain
`NOT RUN`; never copy v1.1.1 sizes/hashes into new candidate fields.

- [ ] **Step 2: Run the complete automated solution baseline GREEN**

From a clean-enough worktree containing only the intended task commits, run in
this order and enter exact counts/output into the acceptance record:

```powershell
dotnet --version
dotnet restore .\CodexQuotaHud.sln
dotnet test .\tests\CodexQuotaHud.Core.Tests\CodexQuotaHud.Core.Tests.csproj -c Release --no-restore
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

Any failed test, warning, build error, skipped required platform test, or
whitespace error is recorded as `FAIL`; fix it in the owning earlier task and
repeat the focused test plus this entire sequence. Do not edit counts by hand
from historical baselines.

- [ ] **Step 3: Run the explicit attack and rollback suite**

Run and record exact counts for:

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Attack|FullyQualifiedName~Archive|FullyQualifiedName~Validation|FullyQualifiedName~Image|FullyQualifiedName~Rollback|FullyQualifiedName~Storage"
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LocalControl|FullyQualifiedName~CustomSkin|FullyQualifiedName~Packaging|FullyQualifiedName~BuiltInSkin"
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~Recovery|FullyQualifiedName~Output|FullyQualifiedName~Image|FullyQualifiedName~SeparateLifetime"
```

The evidence table explicitly maps passing tests to absolute/traversal paths,
duplicate normalized entries, symlink/reparse content, encrypted/unsupported
ZIP entries, entry/count/compressed/extracted/image/pixel limits, PNG/JPEG
signature/decode/alpha checks, unknown/duplicate JSON properties, unsupported
schema/template/minimum HUD, non-finite/out-of-range values, XAML/DLL/EXE/script/
remote-URL rejection, hash mismatch, collision Replace/Keep/Cancel, atomic
install/export rollback, corrupt-installed fallback, typed IPC framing/unknown
command/oversize payload, failed activation preservation, draft corruption,
and component-removal path attacks.

- [ ] **Step 4: Build an internal-only package candidate without installing it**

Use a unique temporary root and the explicit non-release version `0.0.0`:

```powershell
$verificationRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("CQH-Designer-Package-" + [Guid]::NewGuid().ToString("N"))
try {
    powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 0.0.0 -OutputPath $verificationRoot -InternalTestMode
    Get-ChildItem -LiteralPath $verificationRoot -File | Get-FileHash -Algorithm SHA256
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $verificationRoot 'CodexQuotaHud-v0.0.0-win-x64.zip'))
    try { $zip.Entries | Select-Object FullName,Length,CompressedLength } finally { $zip.Dispose() }
} finally {
    if (Test-Path -LiteralPath $verificationRoot) { Remove-Item -LiteralPath $verificationRoot -Recurse -Force }
}
```

Record the temporary candidate hashes and ZIP entry list before cleanup. Assert
the ZIP has the exact Task 16 normal-only fallback contents and the internal
Setup was compiled from a publish tree containing both executables. This
command does not run Setup, install either application, tag, upload, publish a
release, or mutate the existing real installation.

- [ ] **Step 5: Update user and project documentation with unreleased status**

In `README.md`, preserve the v1.1.1 installation block and add an explicit
`Unreleased source status / 未发布源码状态` section. Document that current source
adds safe `.cqskin` import to the normal HUD, Setup's optional unchecked
Designer component, the component-page Chinese/English text, Start-menu-only
Designer shortcut, add/remove preservation, normal-only ZIP fallback, exact
source commands for both projects, data-only restrictions, and the fact that no
new public binary is yet claimed.

Update `PROJECT_CONTEXT.md` with the shared Skins library, separate Designer
process, exact settings/skins/drafts/import paths, dependency direction,
distribution boundary, and verified-versus-pending status. Update
`CURRENT_TASK.md` so the next continuation point is the manual matrix below,
not the old alert-only continuation. Prepend an unreleased 2026-08-02 entry to
`CHANGELOG_AI.md` with exact automated evidence and a plain list of what was not
run. Do not change release tags, public asset hashes, or say the feature ships
in v1.1.1.

- [ ] **Step 6: Prepare the exact real-Windows manual matrix**

The acceptance document contains one row for every combination below, with no
merged row allowed to hide a missing case:

1. Designer layout and controls at 100%, 125%, 150%, and 200% DPI; include a
   960x540-DIP/small-work-area case and an 80-scalar Chinese name.
2. Opaque JPEG background, transparent-edge PNG decoration, high-resolution
   accepted image, and crop-focus extremes for each applicable slot.
3. Zero, one, two, and all three optional image slots in preview, apply, export,
   import, and restart rendering.
4. Simultaneous normal HUD and Designer operation with one instance of each;
   starting either again activates only its own existing process.
5. Apply to a running HUD; close Designer; restart HUD; sign out/in; and Windows
   restart, with the same selected custom skin and no draft/settings loss.
6. Export on a machine/install with Designer, then validate/import/select on an
   installation without Designer.
7. Left, right, top, and bottom edge collapse/expand, plus primary and secondary
   monitor placement, details popup, tray selection, and independent dual-quota
   alert boundaries 21/20/11/10/0.
8. All five built-ins before and after custom-skin use, including click/double-
   click, refresh, animation toggle, edge presentation, tray, startup, and
   existing installed/hidden-Preview handoffs.
9. Real Setup fresh default without Designer, fresh selected with Designer,
   rerun add, rerun remove, selected-component upgrade, normal uninstall
   preserve, and explicit purge. Inspect exact files, shortcuts, Run value, and
   settings/skin/draft/import hashes after each transition.

For screenshots, name files with date, DPI/scenario, and monitor. For stateful
checks, record pre/post SHA-256 and exact executable/shortcut arguments. A visual
observation without a screenshot or written observed value is `PARTIAL`, not
`PASS`.

- [ ] **Step 7: Respect the authorization boundary**

Execution of this implementation plan stops before Step 6's GUI launch, real
Setup run, sign-out, restart, or mutation of the maintainer installation unless
the user separately authorizes those actions. Without that authorization, leave
every manual row `NOT RUN`, set the overall feature acceptance to `PARTIAL`, and
state that release criteria 1–7 remain unproven on a real desktop.

If authorization is later granted, preflight the exact candidate path, version,
hash, install target, settings backup/hash set, and rollback route before any
Setup launch. Real acceptance still does not authorize tag creation, upload,
release publication, replacement of v1.1.1 assets, or push.

- [ ] **Step 8: Verify documentation/evidence GREEN and commit only truthful records**

```powershell
git diff --check
git status --short
git add README.md PROJECT_CONTEXT.md CURRENT_TASK.md CHANGELOG_AI.md docs/verification/2026-08-02-optional-skin-designer-acceptance.md
git commit -m "docs: record optional skin designer verification"
```

Expected: the commit includes only documentation/evidence. Every claimed PASS
has direct evidence, every unperformed manual action is explicit, the feature
is labeled unreleased, and no install, tag, upload, release, asset replacement,
or push has occurred.

---

## Final implementation handoff gate

Before handing this plan to an executor, re-read Tasks 1–18 in order and verify:

- every produced type or script action is introduced before its first consumer;
- `assets` is required but may contain zero, one, two, or all three distinct
  optional slots, with duplicate slots rejected;
- the normal HUD references shared runtime only and never Designer;
- the Designer has its own mutex, draft/recovery roots, preview bridge, apply,
  export, and close semantics;
- package parsing, storage, IPC, lifecycle, and component removal all retain
  exact-path, bounded-input, no-code, atomic/rollback rules;
- fresh Setup leaves Designer unchecked, selected Setup creates only its
  Start-menu entry, and rerun removal preserves settings/skins/drafts/imports;
- the ZIP fallback remains normal-only and still supports custom import/runtime;
- every task has exact files, interfaces, RED/GREEN checks, commands, and a
  narrow commit point; build/full-regression checks appear at dependency
  milestones and in the final evidence task;
- no placeholder filename, version token, method name, fixture, command, or
  expected result remains; and
- documentation distinguishes automated evidence, manual evidence, current
  v1.1.1 history, unreleased source state, and unauthorized release actions.

Then run:

```powershell
git diff --check
git status --short
```

The only uncommitted file while authoring this plan should be this plan itself.
