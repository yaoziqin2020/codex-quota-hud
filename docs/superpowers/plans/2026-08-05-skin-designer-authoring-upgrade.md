# Skin Designer Authoring Upgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 发布 Codex Quota HUD v1.3.0，为皮肤作者补上文字整体偏移与行距控制，修复导出默认目录，并加入构图参考线、单项动画预演、Dual 圈层说明和明确的“应用到 HUD”结果反馈。

**Architecture:** 两个新的文字布局值作为 `SkinTheme` 的向后兼容可选字段进入现有 schema v1；旧 v1.2.x 包缺字段时按 `0` 读取，新 Designer 导出的包自动声明最低 HUD v1.3.0。生产渲染器与预览参考线共用同一套纯几何计算，避免显示和辅助线漂移。构图参考线、动画单项预演和 Dual 说明都是 Designer 预览工具，不写入草稿；输出反馈只呈现已有安装/激活结果，不伪造 HUD 已切换。

**Tech Stack:** .NET 8、C#、WPF/XAML、xUnit、现有 `free-decoration-ring` 自定义皮肤模板、Inno Setup 6、PowerShell 发布/安装器脚本。

## Global Constraints

- 真实源码目录固定为 `C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731`；不得使用旧对话目录、backup、sandbox 或其它 worktree。
- 当前分支是 `feat/inno-setup-installer-20260731`，计划编写时 HEAD 为 `e66641d`，相对远端 ahead 7；实施前重新确认，冲突时先报告。
- `tmp/` 是用户已有未跟踪目录，禁止读取业务内容、修改、删除、暂存或提交。
- 目标产品版本为 `1.3.0`。历史标签和 Release `v1.2.0`、`v1.2.1`、`v1.2.2`、`v1.2.3` 保持不可变，不删除或替换历史资产。
- schema 仍为 `1`。v1.3.0 必须读取旧 v1.2.x `theme.json`；v1.3.0 新导出的包必须自动声明 `minimumHudVersion >= 1.3.0`。
- 新字段默认值必须完全复现当前视觉：`TextOffsetY = 0`、`TextLineGap = 0`。
- 文字范围固定：整体上下偏移 `-32..32 DIP`；行距增量 `-16..32 DIP`；非有限值和越界值必须被验证器拒绝。
- 预览工具不得写入 `SkinDraftDocument`、不得产生撤销历史、不得让草稿变脏，也不得进入 `.cqskin`。
- Dual 的真实语义固定：主/外圈 = 5 小时，副/内圈 = 每周；单圈 5h/Week 只显示所选额度。不得为某一皮肤倒置生产数据含义。
- 参考线只是几何辅助，不声称能够识别 PNG 内实际角色轮廓或自动保证无碰撞。
- “应用到 HUD”结果必须根据 `DesignerOutputDisposition` 如实显示；`InstalledNotActivated` 不能写成“已切换成功”。
- Setup 仍是普通用户安装包，Designer 仍为默认不勾选的可选组件；ZIP 仍是普通 HUD 后备包，不借本次升级改变产品边界。
- 完成顺序固定：代码与自动测试 → Release build → 1.3.0 Setup/ZIP → 隔离安装器矩阵 → 本机真实升级与 Agent 冒烟 → 用户实操确认 → 才允许推 main、打标签和上传 Release。
- 每个测试必须依赖状态/事件/结构，不依赖精确墙钟时间或“看起来差不多”。

---

## Contract and UI Decisions

### Persisted text controls

`SkinTheme` 在现有必需参数 `Animation` 后增加两个带默认值的尾随参数：

```csharp
public sealed record SkinTheme(
    int SchemaVersion,
    string TemplateId,
    SkinImageTransform Background,
    SkinImageTransform Center,
    SkinImageTransform Decoration,
    string PrimaryRingColor,
    string SecondaryRingColor,
    string BaseBackgroundColor,
    double BaseBackgroundOpacity,
    double RingDiameter,
    double RingThickness,
    double RingGap,
    double StartAngle,
    string GlowColor,
    double GlowIntensity,
    double NumberTextSize,
    double LabelTextSize,
    SkinTextWeight TextWeight,
    SkinTextPlacement TextPlacement,
    SkinAnimationSettings Animation,
    double TextOffsetY = 0d,
    double TextLineGap = 0d);
```

JSON 使用 `textOffsetY` 和 `textLineGap`。解析时两者可缺省；写出时两者始终写入 canonical JSON。

`TextLineGap` 是“在现有版式基础上的增量”，不是绝对基线距离。布局计算只实现一次：

```csharp
var (numberBaseY, labelBaseY) = placement switch
{
    SkinTextPlacement.LabelAboveNumber => (18d, -22d),
    SkinTextPlacement.NumberAboveLabel => (-18d, 25d),
    _ => (0d, 26d)
};
var labelDirection = Math.Sign(labelBaseY - numberBaseY);
var halfGap = textLineGap / 2d;
return new FreeDecorationRingTextLayout(
    NumberY: numberBaseY + textOffsetY - (labelDirection * halfGap),
    LabelY: labelBaseY + textOffsetY + (labelDirection * halfGap));
```

因此 `0/0` 与 v1.2.3 像素位置一致；正行距把两行等量向外分开，负行距等量收紧，整体中点只受 `TextOffsetY` 影响。

### Preview-only tools

新增预览选择 `全部 / 转圈 / 呼吸 / 光晕 / 浮动 / 刷新加速`：

- `全部`：使用原草稿的四种动画值。
- `转圈/呼吸/光晕/浮动`：生成仅供预览的派生包，保留所选强度，其余三项临时置 `0`。
- `刷新加速`：保留四种动画值，并临时进入刷新状态；离开该模式时恢复进入前的“刷新中”状态。
- 所有模式保留草稿的 `RefreshSpeedMultiplier` 和 `RefreshHoldSeconds`，不改草稿和导出数据。

构图参考线显示：外圈直径、内圈直径、中心图呼吸峰值 64-DIP 容器边界、数字中心线、标签中心线。参考线默认关闭、仅 Designer 合成预览可用。

### Output feedback

成功应用后的主题弹窗至少显示：

```text
已应用到 HUD

皮肤：<DisplayName>
版本：<PackageVersion>
皮肤 ID：<SkinId>
状态：<已确认实时切换 / HUD 已启动并切换 / 已安装但未确认切换>
```

导出弹窗显示最终文件名和完整目录；失败弹窗保留错误信息。按钮仍使用 Designer 深色主题，文件选择器继续使用 Windows 原生外观。

---

### Task 1: Extend the skin contract without breaking old packages

**Files:**
- Modify: `src/CodexQuotaHud.Skins/Contracts/SkinContracts.cs`
- Modify: `src/CodexQuotaHud.Skins/Contracts/SkinPackageLimits.cs`
- Modify: `src/CodexQuotaHud.Skins/Serialization/SkinJsonCodec.cs`
- Modify: `src/CodexQuotaHud.Skins/Validation/SkinContractValidator.cs`
- Modify: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingTemplate.cs`
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/App.xaml.cs`
- Modify: `tests/CodexQuotaHud.Skins.Tests/Serialization/SkinJsonCodecTests.cs`
- Modify: `tests/CodexQuotaHud.Skins.Tests/Validation/SkinContractValidatorTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Documents/DesignerDocumentServiceTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/DraftPackageBuilderTests.cs`

**Interfaces:**
- Add `SkinTheme.TextOffsetY` and `SkinTheme.TextLineGap`, both default `0d`.
- Add limits `MinimumTextOffsetYDip = -32`, `MaximumTextOffsetYDip = 32`, `MinimumTextLineGapDip = -16`, `MaximumTextLineGapDip = 32`.
- Raise `FreeDecorationRingTemplate.MinimumHudVersion`, App runtime baseline, and Designer runtime baseline from `1.2.3` to `1.3.0`.

- [ ] **Step 1: Add failing compatibility and validation tests**

In `SkinJsonCodecTests`, assert all of the following:

```csharp
[Fact]
public void ParseTheme_LegacyJsonDefaultsNewTextLayoutFieldsToZero()
{
    var result = SkinJsonCodec.ParseTheme(LegacyV123ThemeJson());
    Assert.True(result.IsValid);
    Assert.Equal(0d, result.Value!.TextOffsetY);
    Assert.Equal(0d, result.Value.TextLineGap);
}

[Fact]
public void WriteAndParseTheme_RoundTripsTextLayoutFields()
{
    var theme = ValidTheme() with { TextOffsetY = -7, TextLineGap = 4 };
    var parsed = SkinJsonCodec.ParseTheme(SkinJsonCodec.WriteTheme(theme));
    Assert.Equal(-7, parsed.Value!.TextOffsetY);
    Assert.Equal(4, parsed.Value.TextLineGap);
}
```

Also assert canonical JSON contains both property names exactly once and that an unrelated unknown theme property remains rejected.

In `SkinContractValidatorTests`, add finite/min/max/below-min/above-max cases for both fields and assert error locations `$.textOffsetY` and `$.textLineGap`.

In `DesignerDocumentServiceTests`, assert opening a 1.2.3 draft/package under the new template normalizes `MinimumHudVersion` to 1.3.0 without changing its skin ID, package version, theme, or assets. In `DraftPackageBuilderTests`, assert the resulting manifest declares 1.3.0.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinJsonCodecTests|FullyQualifiedName~SkinContractValidatorTests"
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DesignerDocumentServiceTests|FullyQualifiedName~DraftPackageBuilderTests"
```

Expected: compilation or assertions fail because the two fields, limits, optional parser entries, and 1.3.0 minimum do not exist.

- [ ] **Step 3: Implement optional theme parsing and canonical writing**

Add both names to `ThemeProperties`, plus:

```csharp
private static readonly IReadOnlySet<string> OptionalThemeProperties =
    new HashSet<string>(StringComparer.Ordinal)
    {
        "textOffsetY",
        "textLineGap"
    };
```

Pass that set to `ValidateObject` for the theme root. Parse both values with the existing helper:

```csharp
var textOffsetY = ReadOptionalDouble(
    root,
    "textOffsetY",
    "$.textOffsetY",
    0d,
    errors);
var textLineGap = ReadOptionalDouble(
    root,
    "textLineGap",
    "$.textLineGap",
    0d,
    errors);
```

Pass them as named arguments into `SkinTheme`, and write both values immediately after `textPlacement` and before the `animation` object.

- [ ] **Step 4: Implement validation and version floor**

Validate both values through the existing `ValidateNumber` path. Change only the three actual 1.2.3 runtime/template baselines to 1.3.0; do not globally replace release-history strings.

- [ ] **Step 5: Run focused and full Skins tests**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DesignerDocumentServiceTests|FullyQualifiedName~DraftPackageBuilderTests"
```

Expected: all selected tests pass with zero skipped; a v1.2.3 theme lacking both properties reads as `0/0`, while a v1.3.0 write retains non-zero values.

- [ ] **Step 6: Commit the contract slice**

```powershell
git add src/CodexQuotaHud.Skins/Contracts src/CodexQuotaHud.Skins/Serialization src/CodexQuotaHud.Skins/Validation src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingTemplate.cs src/CodexQuotaHud.App/App.xaml.cs src/CodexQuotaHud.SkinDesigner/App.xaml.cs tests/CodexQuotaHud.Skins.Tests tests/CodexQuotaHud.SkinDesigner.Tests/Documents/DesignerDocumentServiceTests.cs tests/CodexQuotaHud.SkinDesigner.Tests/Output/DraftPackageBuilderTests.cs
git commit -m "feat: add compatible skin text layout fields"
```

---

### Task 2: Share exact geometry and expose the two Designer controls

**Files:**
- Create: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingGeometry.cs`
- Modify: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingRenderer.xaml.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftFactory.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/EditorSectionViewModels.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Templates/FreeDecorationRingGeometryTests.cs`
- Modify: `tests/CodexQuotaHud.Skins.Tests/Templates/FreeDecorationRingRendererTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/SkinDraftFactoryTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/TextEditorViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`

**Interfaces:**

```csharp
public readonly record struct FreeDecorationRingTextLayout(
    double NumberY,
    double LabelY);

public readonly record struct FreeDecorationRingGuideGeometry(
    double PrimaryDiameter,
    double SecondaryDiameter,
    double CenterPeakSize,
    double CenterPeakOffsetX,
    double CenterPeakOffsetY,
    FreeDecorationRingTextLayout Text);

public static class FreeDecorationRingGeometry
{
    public static FreeDecorationRingTextLayout CalculateTextLayout(
        SkinTextPlacement placement,
        double textOffsetY,
        double textLineGap);

    public static FreeDecorationRingGuideGeometry CalculateGuideGeometry(
        SkinTheme theme);
}
```

`CenterPeakSize` must use the production breathing peak `64 * Center.Scale * (1 + 0.12 * BreathingIntensity)`; center offsets remain the theme's translated X/Y values.

- [ ] **Step 1: Write failing pure geometry tests**

Cover every placement at `0/0`, positive and negative gaps, shared offset, secondary diameter clamping, and breathing peak. Exact examples:

```csharp
[Theory]
[InlineData(SkinTextPlacement.Centered, 0, 26)]
[InlineData(SkinTextPlacement.NumberAboveLabel, -18, 25)]
[InlineData(SkinTextPlacement.LabelAboveNumber, 18, -22)]
public void CalculateTextLayout_ZeroValuesPreserveV123Margins(
    SkinTextPlacement placement,
    double expectedNumber,
    double expectedLabel)
```

For `NumberAboveLabel`, offset `-4`, gap `6` must produce number `-25` and label `24`. For `LabelAboveNumber`, the same inputs must produce number `17` and label `-29`.

- [ ] **Step 2: Write failing renderer and Designer tests**

Assert the renderer applies the calculator result to both TextBlock margins. Assert a new draft uses `0/0`; the Text editor setters mutate only their respective fields, mark the session dirty, support undo/redo, and preserve all other fields. Assert XAML contains accessible labels `文字整体上下` and `数字/时间间距`, correct ranges, current-value display, and non-duplicated `Tag` values.

- [ ] **Step 3: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~FreeDecorationRingGeometryTests|FullyQualifiedName~FreeDecorationRingRendererTests"
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~SkinDraftFactoryTests|FullyQualifiedName~TextEditorViewModelTests|FullyQualifiedName~MainWindowLayoutTests"
```

Expected: failure because the calculator, setters, sliders, tags, and restore mapping are absent.

- [ ] **Step 4: Implement the shared geometry and renderer**

Move the current secondary-diameter and text-margin formulas into `FreeDecorationRingGeometry`. `FreeDecorationRingRenderer.ApplyTheme` consumes the returned values; do not leave a second copy of the old switch.

- [ ] **Step 5: Add the two compact controls**

In the existing “文字” Expander, place the sliders after placement:

```xml
<Slider x:Name="TextOffsetYSlider"
        Minimum="-32" Maximum="32" TickFrequency="1"
        IsSnapToTickEnabled="True" Tag="TextOffsetY"
        ValueChanged="EditorSlider_OnValueChanged"
        AutomationProperties.Name="文字整体上下偏移" />
<Slider x:Name="TextLineGapSlider"
        Minimum="-16" Maximum="32" TickFrequency="1"
        IsSnapToTickEnabled="True" Tag="TextLineGap"
        ValueChanged="EditorSlider_OnValueChanged"
        AutomationProperties.Name="数字和时间行距" />
```

Show the signed current values in DIP next to each label. Add both cases to `EditorSlider_OnValueChanged` and `RestoreEditorSlider`; keep the existing rollback/error presentation path. Renumber later tab indexes sequentially once, without changing control order.

- [ ] **Step 6: Run focused and full Designer/Skins suites**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
```

Expected: all tests pass with zero skipped and the old `0/0` rendering tests remain unchanged.

- [ ] **Step 7: Commit the authoring controls**

```powershell
git add src/CodexQuotaHud.Skins/Templates/FreeDecorationRing src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftFactory.cs src/CodexQuotaHud.SkinDesigner/UI/EditorSectionViewModels.cs src/CodexQuotaHud.SkinDesigner/MainWindow.xaml src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs tests/CodexQuotaHud.Skins.Tests/Templates tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/SkinDraftFactoryTests.cs tests/CodexQuotaHud.SkinDesigner.Tests/UI
git commit -m "feat: tune skin text composition in designer"
```

---

### Task 3: Fix the export picker start directory

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/Output/ISkinOutputDialogs.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/WindowsSkinOutputDialogsTests.cs`

**Interfaces:**

```csharp
internal sealed record SkinExportDialogOptions(
    string InitialDirectory,
    string FileName);

internal static SkinExportDialogOptions BuildExportDialogOptions(
    string suggestedPath);
```

- [ ] **Step 1: Write failing option-builder tests**

Assert a suggestion under `Documents\Codex Quota HUD Skins` produces that exact `InitialDirectory`, only the leaf `.cqskin` name in `FileName`, and never places a full path into `SaveFileDialog.FileName`. Keep the existing dispatcher/owner test.

- [ ] **Step 2: Run the focused test and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter FullyQualifiedName~WindowsSkinOutputDialogsTests
```

Expected: failure because the options seam and `InitialDirectory` do not exist.

- [ ] **Step 3: Configure the native Save dialog correctly**

The public method still calls `SkinPackageExchangeDirectory.SuggestedExportPath`, which creates the exchange directory and rejects directory injection. The native dialog uses:

```csharp
var options = BuildExportDialogOptions(suggestedPath);
var dialog = new SaveFileDialog
{
    Title = "导出皮肤包",
    Filter = "Codex Quota HUD 皮肤 (*.cqskin)|*.cqskin",
    AddExtension = true,
    DefaultExt = ".cqskin",
    InitialDirectory = options.InitialDirectory,
    FileName = options.FileName,
    OverwritePrompt = false
};
```

Do not replace the native file picker with a themed custom picker.

- [ ] **Step 4: Run focused and full Designer output tests**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~WindowsSkinOutputDialogsTests|FullyQualifiedName~SkinExportServiceTests|FullyQualifiedName~DesignerOutputCoordinatorTests"
```

Expected: all selected tests pass.

- [ ] **Step 5: Commit the picker fix**

```powershell
git add src/CodexQuotaHud.SkinDesigner/Output/ISkinOutputDialogs.cs tests/CodexQuotaHud.SkinDesigner.Tests/Output/WindowsSkinOutputDialogsTests.cs
git commit -m "fix: start skin export in exchange directory"
```

---

### Task 4: Add composition guides to the real synthetic HUD preview

**Files:**
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.App/Preview/SyntheticPreviewComposition.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Preview/DesignerPreviewController.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Preview/DesignerPreviewToolsViewModel.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/SyntheticPreviewCompositionTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/QuotaOrbWindowStartupTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Preview/DesignerPreviewControllerTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Preview/DesignerPreviewToolsViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`

**Interfaces:**
- Add `QuotaOrbWindow.SetDesignerGuides(FreeDecorationRingGuideGeometry? geometry)`; `null` collapses and clears the overlay.
- Add `SyntheticPreviewComposition.SetDesignerGuides(SkinTheme? theme, bool visible)`.
- Add `DesignerPreviewController.SetGuidesVisible(bool value)`; it reapplies the last valid theme only.
- Expose `PreviewTools.CompositionGuidesVisible` to XAML; default `false`.

- [ ] **Step 1: Write failing overlay and controller tests**

Assert the guide overlay is collapsed by default in ordinary and synthetic HUD construction. When enabled with a valid theme, assert:

- overlay is visible and `IsHitTestVisible = false`;
- outer/inner ellipse dimensions equal shared geometry;
- center peak rectangle includes saved offsets and peak breathing scale;
- number/label lines use the shared text layout;
- disabling clears visibility;
- an invalid subsequent draft leaves the last good skin and last good guide geometry unchanged.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~SyntheticPreviewCompositionTests|FullyQualifiedName~QuotaOrbWindowStartupTests"
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DesignerPreviewControllerTests|FullyQualifiedName~DesignerPreviewToolsViewModelTests|FullyQualifiedName~MainWindowLayoutTests"
```

Expected: failure because overlay names, APIs, and view model are missing.

- [ ] **Step 3: Add the preview-only overlay**

Insert `DesignerGuideOverlay` immediately after `SkinHost`, above skin content and below the transparent drag surface. Use dashed high-contrast strokes with restrained opacity, `Panel.ZIndex="900"`, `Visibility="Collapsed"`, and `IsHitTestVisible="False"`. Do not add guides to any skin renderer or persisted setting.

- [ ] **Step 4: Wire the last-good theme through the preview controller**

`DesignerPreviewController` stores the last successfully built package. `Update` renders it, then reapplies guide visibility from its original theme. `SetGuidesVisible` updates only the overlay; it must not rebuild the package or mutate the draft.

- [ ] **Step 5: Add the compact Designer control**

In a new `预览工具` bordered group inside the existing `SyntheticStateRow`, add a checkbox `构图参考线`. Put the group inside the existing `WrapPanel` so 1280-wide and narrow windows wrap instead of overlap. Add an accessible name and update tab order.

- [ ] **Step 6: Run focused and full App/Designer suites**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
```

Expected: all tests pass; an ordinary HUD never shows guide elements.

- [ ] **Step 7: Commit composition guides**

```powershell
git add src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs src/CodexQuotaHud.App/Preview/SyntheticPreviewComposition.cs src/CodexQuotaHud.SkinDesigner/Preview src/CodexQuotaHud.SkinDesigner/MainWindow.xaml src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs tests/CodexQuotaHud.App.Tests tests/CodexQuotaHud.SkinDesigner.Tests
git commit -m "feat: add skin composition guides to preview"
```

---

### Task 5: Add non-persistent per-channel animation audition

**Files:**
- Create: `src/CodexQuotaHud.SkinDesigner/Preview/DesignerAnimationAudition.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Preview/DesignerPreviewController.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Preview/DesignerPreviewToolsViewModel.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/SyntheticPreviewViewModel.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Preview/DesignerPreviewControllerTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Preview/DesignerPreviewToolsViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/SyntheticPreviewViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`

**Interfaces:**

```csharp
public enum DesignerAnimationAudition
{
    All,
    Rotation,
    Breathing,
    Glow,
    Floating,
    Refresh
}
```

`DesignerPreviewController.SetAnimationAudition` rerenders a derived in-memory package from the last valid original package. `DesignerPreviewToolsViewModel` coordinates `Refresh` with `SyntheticPreviewViewModel.IsRefreshing` and restores the previous value on exit/dispose.

`PreviewTools.CanEditRefreshCheckbox` is `false` only while `Refresh` audition is active. Bind the existing `刷新中` checkbox's `IsEnabled` to that property so the forced audition state cannot contradict a simultaneous manual toggle.

- [ ] **Step 1: Write failing isolation tests**

For a theme with four non-zero channels, assert each isolated mode has exactly one non-zero channel, while `All` and `Refresh` preserve all four. Assert speed/hold are unchanged. Assert the original package reference/data and `SkinDraftSession` history are unchanged after every switch.

Assert entering `Refresh` saves the previous refresh checkbox value, forces `true`, and disables the manual checkbox; leaving restores the saved value and reenables the checkbox, including when the saved value was already `true`. Dispose must also restore and reenable.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DesignerPreviewControllerTests|FullyQualifiedName~DesignerPreviewToolsViewModelTests|FullyQualifiedName~SyntheticPreviewViewModelTests"
```

Expected: failure because the enum and audition state do not exist.

- [ ] **Step 3: Implement derived preview packages**

Use `with` expressions only on the last valid in-memory package:

```csharp
private static SkinAnimationSettings Audition(
    SkinAnimationSettings saved,
    DesignerAnimationAudition mode) => mode switch
{
    DesignerAnimationAudition.Rotation => saved with
    {
        BreathingIntensity = 0,
        GlowIntensity = 0,
        FloatingIntensity = 0
    },
    DesignerAnimationAudition.Breathing => saved with
    {
        RotationIntensity = 0,
        GlowIntensity = 0,
        FloatingIntensity = 0
    },
    DesignerAnimationAudition.Glow => saved with
    {
        RotationIntensity = 0,
        BreathingIntensity = 0,
        FloatingIntensity = 0
    },
    DesignerAnimationAudition.Floating => saved with
    {
        RotationIntensity = 0,
        BreathingIntensity = 0,
        GlowIntensity = 0
    },
    _ => saved
};
```

Do not write the derived package back into `Editor`, assets, draft store, undo history, Apply, or Export.

- [ ] **Step 4: Add one compact audition selector**

Next to `构图参考线`, add a ComboBox labeled `单项动画预演` with Chinese display labels `全部 / 转圈 / 呼吸 / 光晕 / 浮动 / 刷新加速`. Keep the existing `动画` and `刷新中` checkboxes; the selector is a diagnostic lens, not a replacement for saved animation parameters.

- [ ] **Step 5: Run the full Designer suite**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
```

Expected: all tests pass with no draft/history mutation from preview-only modes.

- [ ] **Step 6: Commit animation audition**

```powershell
git add src/CodexQuotaHud.SkinDesigner/Preview src/CodexQuotaHud.SkinDesigner/UI/SyntheticPreviewViewModel.cs src/CodexQuotaHud.SkinDesigner/MainWindow.xaml tests/CodexQuotaHud.SkinDesigner.Tests
git commit -m "feat: audition skin animation channels in preview"
```

---

### Task 6: Explain Dual ring roles in the synthetic preview

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/SyntheticPreviewViewModel.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/SyntheticPreviewViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewQuotaRefreshControllerTests.cs`

**Interfaces:**
- Add read-only `SyntheticPreviewViewModel.DisplayRoleHint`.
- Raise `PropertyChanged(nameof(DisplayRoleHint))` whenever `DisplayChoice` changes.

- [ ] **Step 1: Write failing semantic tests**

Assert exact states:

```text
Dual：外圈 = 5 小时，内圈 = 每周
5h：单圈显示 5 小时额度
Week：单圈显示每周额度
None：隐藏额度显示
```

Reassert at the App layer that `QuotaDisplayState.Primary.Kind` is `FiveHour`, `Secondary.Kind` is `Weekly`, and the free-decoration primary diameter is the outer ring.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter FullyQualifiedName~PreviewQuotaRefreshControllerTests
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~SyntheticPreviewViewModelTests|FullyQualifiedName~MainWindowLayoutTests"
```

- [ ] **Step 3: Add a full-width muted hint without crowding controls**

Give `SyntheticQuotaRow` two rows. Keep the three existing control columns in row 0; add one muted `TextBlock` in row 1 with `Grid.ColumnSpan="3"`, top margin 6, wrapping enabled, bound to `Synthetic.DisplayRoleHint`. Do not add labels inside the 132-DIP orb.

- [ ] **Step 4: Run the full Designer suite and layout checks**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
```

Expected: full-width and narrow-window layout tests pass; the hint changes immediately with the display choice.

- [ ] **Step 5: Commit the Dual explanation**

```powershell
git add src/CodexQuotaHud.SkinDesigner/UI/SyntheticPreviewViewModel.cs src/CodexQuotaHud.SkinDesigner/MainWindow.xaml tests/CodexQuotaHud.SkinDesigner.Tests/UI tests/CodexQuotaHud.App.Tests/Preview/PreviewQuotaRefreshControllerTests.cs
git commit -m "feat: explain dual quota ring roles in designer"
```

---

### Task 7: Make Apply-to-HUD and export results unambiguous

**Files:**
- Create: `src/CodexQuotaHud.SkinDesigner/Output/DesignerOutputPresentation.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Output/ISkinOutputDialogs.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/WindowsSkinOutputDialogsTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/SkinApplyServiceTests.cs`

**Interfaces:**

```csharp
internal sealed record DesignerOutputPresentation(
    string Title,
    string Message,
    DesignerDialogIcon Icon);

internal static DesignerOutputPresentation Create(
    DesignerOutputResult result);
```

- [ ] **Step 1: Write failing presentation tests for every disposition**

Cover `AppliedLive`, `InstalledAndHudStarted`, `InstalledNotActivated`, `Exported`, `Cancelled`, and `Failed`. For the first three, assert exact display name, package version, skin ID, and truthful activation wording. Cleanup errors must force warning icon even after a committed output. Export must show `Path.GetFileName` plus full parent directory.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~WindowsSkinOutputDialogsTests|FullyQualifiedName~SkinApplyServiceTests"
```

Expected: failure because current dialog only repeats the short service message.

- [ ] **Step 3: Implement a pure result formatter**

Keep `SkinApplyService` responsible for operation/disposition and `DesignerOutputPresentation` responsible for Chinese user-facing wording. `WindowsSkinOutputDialogs.ShowResult` converts the result once and passes the presentation to the existing themed dialog service. Do not show a success icon for `InstalledNotActivated`, failed, or cleanup-error outcomes.

- [ ] **Step 4: Run all output and themed-dialog tests**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~Output|FullyQualifiedName~DesignerDialogWindowTests"
```

Expected: all selected tests pass and all non-file dialogs retain the Designer theme.

- [ ] **Step 5: Commit output feedback**

```powershell
git add src/CodexQuotaHud.SkinDesigner/Output tests/CodexQuotaHud.SkinDesigner.Tests/Output tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerDialogWindowTests.cs
git commit -m "feat: show exact skin output results"
```

---

### Task 8: Integrated source verification and manual Designer acceptance

**Files:**
- Create: `docs/verification/2026-08-05-skin-designer-authoring-upgrade-acceptance.md`
- Modify: only source/tests found defective by this task; do not broaden scope.

- [ ] **Step 1: Run serial Release project suites**

Run each test assembly separately to preserve current WPF serialization evidence:

```powershell
dotnet test .\tests\CodexQuotaHud.Core.Tests\CodexQuotaHud.Core.Tests.csproj -c Release --no-restore
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

Expected: every test passes with zero skipped; build has zero warnings and zero errors; diff check has no output. Record fresh counts instead of copying 1.2.3 counts.

- [ ] **Step 2: Launch the source Designer and exercise the complete authoring flow**

```powershell
dotnet run --project .\src\CodexQuotaHud.SkinDesigner\CodexQuotaHud.SkinDesigner.csproj -c Release --no-build
```

Using a copy of an existing custom skin draft, verify:

1. text offset moves both lines together;
2. line gap expands/contracts without changing their midpoint;
3. undo/redo and save/reopen preserve both values;
4. `0/0` matches the v1.2.3 appearance;
5. guides align with both rings, peak center container, and text lines while remaining click-through;
6. every audition mode is visibly distinct and switching back to `全部` restores the saved animation mix;
7. leaving `刷新加速` restores the former refresh checkbox;
8. Dual/5h/Week/None hints match displayed rings;
9. export picker opens in `Documents\Codex Quota HUD Skins`;
10. Apply result names the exact skin/version/ID and reports whether the running HUD acknowledged activation.

- [ ] **Step 3: Verify package compatibility in both directions**

- Import at least one untouched v1.2.3 `.cqskin` into the v1.3.0 source Designer and confirm layout defaults `0/0`.
- Export a non-zero layout package, inspect `manifest.json` and `theme.json`, and confirm minimum HUD `1.3.0`, exactly one of each new property, and canonical package hashes.
- Keep an installed v1.2.3 binary available only as an isolated test target if needed; confirm the 1.3.0 manifest is rejected as requiring a newer HUD before it can be misrepresented as compatible. Do not overwrite the maintainer installation for this negative check.

- [ ] **Step 4: Record evidence, defects, and not-run items**

The acceptance document must use `PASS / FAIL / PARTIAL / NOT RUN`, exact command, timestamp Asia/Tokyo, expected, observed, and evidence. Screenshots go under `docs/verification/assets/` only when they materially prove layout; temporary captures remain ignored.

- [ ] **Step 5: Commit verified source evidence**

```powershell
git add docs/verification/2026-08-05-skin-designer-authoring-upgrade-acceptance.md
git commit -m "test: record designer authoring upgrade acceptance"
```

Do not commit if any required source gate is red; fix narrowly, rerun the affected test and then all four project suites.

---

### Task 9: Build the v1.3.0 candidate, install locally, and stop for user acceptance

**Files:**
- Modify: `README.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `CURRENT_TASK.md`
- Modify: `CHANGELOG_AI.md`
- Create: `docs/releases/v1.3.0.md`
- Modify: `docs/verification/2026-08-05-skin-designer-authoring-upgrade-acceptance.md`
- Generated only: `artifacts/release/CodexQuotaHud-Setup-v1.3.0.exe`
- Generated only: `artifacts/release/CodexQuotaHud-v1.3.0-win-x64.zip`
- Generated only: `artifacts/release/SHA256SUMS.txt`

- [ ] **Step 1: Update release documentation without claiming unrun evidence**

Document the two new persisted controls, three preview improvements, picker fix, output feedback, compatibility boundary, optional Designer installer behavior, unsigned status, and exact remaining manual checks. Keep evidence fields `NOT RUN` until executed.

- [ ] **Step 2: Create the production candidate from the final commit**

```powershell
.\scripts\package-release.ps1 -Version 1.3.0
.\scripts\test-installer.ps1 -Version 1.3.0 -InstallerPath .\artifacts\release\CodexQuotaHud-Setup-v1.3.0.exe
```

Expected: Setup, normal-only ZIP, and checksum file are rebuilt from one publish payload; all nine isolated installer scenarios pass; production installation and user data are untouched by the isolated matrix.

- [ ] **Step 3: Verify candidate identity and boundaries**

Record sizes and SHA-256 hashes; verify manifest lines; inspect ZIP entries; inspect Setup/App/Designer versions; run `Get-AuthenticodeSignature` and report `NotSigned` honestly. Confirm:

- ZIP contains normal HUD and PowerShell fallback only, no Designer;
- Setup defaults Designer unchecked and includes it only when selected;
- Setup selects creation of the normal desktop shortcut by default, allows the
  user to deselect it, and the shortcut launches without `--preview`;
- startup remains `--background` when selected;
- historical installed skins, drafts, recovery, exchange packages, and settings
  are not part of installer cleanup; Setup deliberately removes Developer
  Preview desktop entries, so the maintainer-only `--preview` shortcut is
  snapshotted and restored separately after product installation.

- [ ] **Step 4: Perform the real local upgrade to v1.3.0**

Before upgrade, snapshot installed binary versions/hashes, settings, selected skin, installed custom-skin inventory, draft inventory, startup registry, shortcuts, and uninstall entry. Install the verified Setup with Designer selected and startup preserved. After upgrade, confirm App and Designer are `1.3.0.0`, hashes match publish output, user data is unchanged, formal HUD starts, tray menu works, and Designer opens.

- [ ] **Step 5: Complete Agent-owned installed smoke checks**

On installed binaries, repeat the key text controls, guides, audition selector, Dual hint, export-directory, Apply feedback, and old-package import checks. Verify normal HUD never shows composition guides or audition-isolated animation after the Designer closes.

- [ ] **Step 6: Stop and hand the installed candidate to the user**

Report the exact installed version, artifact hashes, automated counts, installer matrix, preserved data, and any not-run items. Ask the user to perform practical visual acceptance. At this point:

- do not push;
- do not fast-forward `main`;
- do not create tag `v1.3.0`;
- do not create/upload a GitHub Release;
- do not delete v1.2.3 or earlier artifacts.

- [ ] **Step 7: After explicit user acceptance, close the release**

Only after the user says the installed candidate is accepted:

1. update acceptance/handoff docs with the user's result and time;
2. commit the final evidence;
3. verify intended commit range and clean tracked state (`tmp/` remains untouched);
4. fast-forward/merge to `main` using the repository's established no-PR workflow;
5. push `main`;
6. wait for Windows CI and require success;
7. create annotated tag `v1.3.0` on the tested commit;
8. create GitHub Release using `docs/releases/v1.3.0.md`;
9. upload Setup, ZIP, and `SHA256SUMS.txt`;
10. download all three public assets and verify size/hash equality with local files.

If CI or public readback fails, do not call the release complete and do not move the tag to a different commit.

---

## Final Verification Checklist

- [ ] Old v1.2.x skin JSON missing the two fields parses as `0/0`.
- [ ] New package JSON round-trips non-zero text offset/gap and declares minimum HUD 1.3.0.
- [ ] Text layout renderer and guide overlay consume one shared geometry implementation.
- [ ] `0/0` is visually and numerically identical to v1.2.3 margins.
- [ ] Preview guides are default-off, click-through, Designer-only, and never persisted.
- [ ] Animation audition does not mutate draft, history, Apply, or Export data.
- [ ] Refresh audition restores the prior refresh state.
- [ ] Dual outer = 5h and inner = Week is tested and explained in UI.
- [ ] Export starts in `Documents\Codex Quota HUD Skins` with a leaf filename.
- [ ] Apply feedback includes exact skin/version/ID and truthful activation disposition.
- [ ] All four Release test assemblies pass serially with zero skipped.
- [ ] Release build has zero warnings/errors and `git diff --check` passes.
- [ ] v1.3.0 Setup/ZIP/checksums pass structure, hash, version, signature, and nine-scenario installer checks.
- [ ] Real local upgrade preserves settings, skins, drafts, recovery, startup, and maintainer shortcut state.
- [ ] User visually accepts the installed candidate before any remote release action.
- [ ] `tmp/` is untouched and uncommitted.
