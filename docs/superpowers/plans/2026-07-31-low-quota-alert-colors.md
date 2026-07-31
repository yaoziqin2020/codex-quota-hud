# Low-Quota Alert Colors Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add consistent amber and red low-quota colors to the floating HUD, collapsed edge bar, tray icon, and detail rows while preserving every skin's normal palette and independent dual-quota state.

**Architecture:** One shared `QuotaAlertPolicy` classifies normalized percentages into `Normal`, `Warning`, or `Critical`, and one shared palette exposes the exact WPF and `System.Drawing` colors. `QuotaSkinState` derives independent primary and secondary levels, skins apply them only to quota-bearing elements, and edge/tray/detail presentation consumes the same policy without duplicating thresholds.

**Tech Stack:** C# 13, .NET 9, WPF, Windows Forms tray rendering, xUnit

## Global Constraints

- `>20%` is `Normal`; `>10%` and `<=20%` is `Warning`; `<=10%` is `Critical`.
- Warning color is exactly `#FFFFB547`; critical color is exactly `#FFFF5A67`.
- WPF and `System.Drawing` rendering use the same RGB values.
- `Normal` preserves the selected skin's existing colors; there is no global normal color.
- Primary and secondary quota levels are classified independently.
- Missing quota remains missing; it must not be fabricated as a critical `0%`.
- Valid `0%` is critical.
- Floating HUD, collapsed edge bar, tray icon, and detail rows all participate.
- Only quota-bearing elements change color; frames, tracks, labels, backgrounds, decorations, and ambient animation keep their current skin presentation.
- No flashing, pulsing, sound, toast, modal dialog, animation-speed change, or automatic refresh is added.
- Thresholds are fixed; no settings, registry values, JSON fields, or UI controls are added.
- Existing dual/single/hidden selection, app-server, refresh, persistence, single-instance, preview-handoff, and window-positioning behavior is unchanged.
- No package dependency is added.
- Follow TDD and commit each completed task separately.

---

## File Structure

- Create `src/CodexQuotaHud.App/UI/QuotaAlertPresentation.cs`
  - Owns `QuotaAlertLevel`, threshold classification, exact semantic colors,
    frozen WPF brushes, and normal-preserving WPF/tray color resolution.
- Create `tests/CodexQuotaHud.App.Tests/UI/QuotaAlertPresentationTests.cs`
  - Verifies boundaries, normalization, exact colors, and preservation of
    normal surface colors.
- Modify `src/CodexQuotaHud.App/UI/Skins/IQuotaSkin.cs`
  - Exposes derived primary and optional secondary alert levels without
    changing existing constructors.
- Modify `tests/CodexQuotaHud.App.Tests/UI/SkinControllerTests.cs`
  - Verifies independent dual levels and single-mode secondary removal.
- Modify all five skin XAML/code-behind pairs under
  `src/CodexQuotaHud.App/UI/Skins/`
  - Captures existing normal brushes and overrides only quota-bearing
    elements during render.
- Create `tests/CodexQuotaHud.App.Tests/UI/QuotaAlertSkinTests.cs`
  - Parameterizes five-skin normal/warning/critical and dual-independence
    checks on an STA thread.
- Modify `tests/CodexQuotaHud.App.Tests/UI/LiquidTankSkinTests.cs`
  - Verifies liquid body and both wave fills follow primary alert state.
- Modify `src/CodexQuotaHud.App/UI/QuotaOrbViewModel.cs`
  - Retains numeric detail percentages and exposes each row's alert level.
- Modify `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml`
  - Applies detail-row alert colors with style data triggers.
- Modify `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
  - Applies primary alert presentation to edge fill, outline, and glow while
    retaining the skin's track and texture.
- Modify `src/CodexQuotaHud.App/UI/TrayIconRenderer.cs`
  - Overrides only the progress-ring accent for warning and critical state.
- Modify corresponding view-model, window, popup, and tray tests.
- Modify `README.md`, `CURRENT_TASK.md`, `PROJECT_CONTEXT.md`, and
  `CHANGELOG_AI.md`
  - Documents behavior, final counts, manual status, and corrects the stale
    pre-fix quality baseline.

---

### Task 1: Shared Alert Policy, Palette, and Skin State

**Files:**
- Create: `src/CodexQuotaHud.App/UI/QuotaAlertPresentation.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/QuotaAlertPresentationTests.cs`
- Modify: `src/CodexQuotaHud.App/UI/Skins/IQuotaSkin.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/SkinControllerTests.cs`

**Interfaces:**
- Produces:

```csharp
public enum QuotaAlertLevel
{
    Normal,
    Warning,
    Critical
}

public static class QuotaAlertPolicy
{
    public static QuotaAlertLevel Classify(double remainingPercent);
}

public static class QuotaAlertPalette
{
    public static System.Windows.Media.Color WarningMediaColor { get; }
    public static System.Windows.Media.Color CriticalMediaColor { get; }
    public static System.Windows.Media.Brush WarningBrush { get; }
    public static System.Windows.Media.Brush CriticalBrush { get; }
    public static System.Drawing.Color WarningDrawingColor { get; }
    public static System.Drawing.Color CriticalDrawingColor { get; }

    public static System.Windows.Media.Brush ResolveBrush(
        QuotaAlertLevel level,
        System.Windows.Media.Brush normal);

    public static System.Windows.Media.Color ResolveMediaColor(
        QuotaAlertLevel level,
        System.Windows.Media.Color normal);

    public static System.Drawing.Color ResolveDrawingColor(
        QuotaAlertLevel level,
        System.Drawing.Color normal);
}
```

- Extends `QuotaSkinState` with:

```csharp
public QuotaAlertLevel PrimaryAlert =>
    QuotaAlertPolicy.Classify(PrimaryPercent);

public QuotaAlertLevel? SecondaryAlert =>
    SecondaryPercent is { } percent
        ? QuotaAlertPolicy.Classify(percent)
        : null;
```

- [ ] **Step 1: Write failing policy and palette tests**

Create exact boundary coverage:

```csharp
[Theory]
[InlineData(20.1, QuotaAlertLevel.Normal)]
[InlineData(20, QuotaAlertLevel.Warning)]
[InlineData(10.1, QuotaAlertLevel.Warning)]
[InlineData(10, QuotaAlertLevel.Critical)]
[InlineData(0, QuotaAlertLevel.Critical)]
[InlineData(-5, QuotaAlertLevel.Critical)]
[InlineData(101, QuotaAlertLevel.Normal)]
[InlineData(double.NaN, QuotaAlertLevel.Critical)]
[InlineData(double.PositiveInfinity, QuotaAlertLevel.Critical)]
public void Classify_UsesFixedNormalizedBoundaries(
    double percent,
    QuotaAlertLevel expected)
{
    Assert.Equal(expected, QuotaAlertPolicy.Classify(percent));
}
```

Add palette tests asserting:

- warning media ARGB is `FF FF B5 47`;
- critical media ARGB is `FF FF 5A 67`;
- drawing colors have the same RGB values;
- warning and critical WPF brushes are frozen;
- `ResolveBrush(Normal, normal)` returns the exact same normal brush instance;
- `ResolveMediaColor` and `ResolveDrawingColor` return their normal input for
  `Normal`.

- [ ] **Step 2: Run the new tests and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~QuotaAlertPresentationTests
```

Expected: compilation fails because `QuotaAlertLevel`, `QuotaAlertPolicy`, and
`QuotaAlertPalette` do not exist.

- [ ] **Step 3: Implement the minimal shared policy and palette**

Use this normalization and classification:

```csharp
var normalized = double.IsFinite(remainingPercent)
    ? Math.Clamp(remainingPercent, 0, 100)
    : 0;
return normalized <= 10
    ? QuotaAlertLevel.Critical
    : normalized <= 20
        ? QuotaAlertLevel.Warning
        : QuotaAlertLevel.Normal;
```

Construct warning and critical colors once. Construct frozen
`SolidColorBrush` instances once. Every resolver must call
`ArgumentNullException.ThrowIfNull(normal)` for WPF brushes and use a switch
that returns the supplied normal value for `Normal`.

- [ ] **Step 4: Add failing independent-state tests**

Extend `SkinControllerTests`:

```csharp
[Fact]
public void QuotaSkinState_DerivesIndependentAlertLevels()
{
    var dual = new QuotaSkinState(
        9,
        75,
        "5 小时",
        QuotaDisplayMode.Dual,
        IsRefreshing: false,
        AnimationsEnabled: true);

    Assert.Equal(QuotaAlertLevel.Critical, dual.PrimaryAlert);
    Assert.Equal(QuotaAlertLevel.Normal, dual.SecondaryAlert);

    var single = dual with
    {
        PrimaryPercent = 20,
        Mode = QuotaDisplayMode.Single
    };
    Assert.Equal(QuotaAlertLevel.Warning, single.PrimaryAlert);
    Assert.Null(single.SecondaryAlert);
}
```

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QuotaAlertPresentationTests|FullyQualifiedName~SkinControllerTests"
```

Expected: the new skin-state test fails because alert properties do not exist.

- [ ] **Step 5: Add derived alert properties and verify GREEN**

Add the two derived properties to `QuotaSkinState`; do not add constructor
parameters or persisted fields.

Run the same command. Expected: all selected policy, palette, and
`SkinControllerTests` pass.

- [ ] **Step 6: Commit the shared semantic model**

```powershell
git add src/CodexQuotaHud.App/UI/QuotaAlertPresentation.cs src/CodexQuotaHud.App/UI/Skins/IQuotaSkin.cs tests/CodexQuotaHud.App.Tests/UI/QuotaAlertPresentationTests.cs tests/CodexQuotaHud.App.Tests/UI/SkinControllerTests.cs
git commit -m "feat: add shared low-quota alert levels"
```

---

### Task 2: Five-Skin Alert Presentation

**Files:**
- Modify: `src/CodexQuotaHud.App/UI/Skins/HudDialSkin.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/Skins/EnergyRingSkin.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/Skins/LiquidGlassSkin.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/Skins/AuroraSkin.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/Skins/LiquidTankSkin.xaml.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/QuotaAlertSkinTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/LiquidTankSkinTests.cs`

**Interfaces:**
- Consumes:
  - `QuotaSkinState.PrimaryAlert`;
  - `QuotaSkinState.SecondaryAlert`;
  - `QuotaAlertPalette.ResolveBrush(level, normalBrush)`.
- Produces no new public interface; each built-in skin restores captured normal
  brushes whenever a later render returns to `Normal`.

- [ ] **Step 1: Write failing parameterized five-skin tests**

Run tests on an STA thread. Parameterize the four arc-based skins
`HudDial`, `EnergyRing`, `LiquidGlass`, and `Aurora`; select through
`SkinController`, render normal (`75/80`), mixed (`20/9`), and normal again.

Resolve these common named elements from `skin.View`:

- `PrimaryArc` as `ProgressArc`;
- `SecondaryArc` as `ProgressArc`;
- `PercentText` as `TextBlock`.

Assert:

1. capture the three normal brushes after the first render;
2. at `20/9`, primary arc and percentage equal `WarningBrush`;
3. at `20/9`, secondary arc equals `CriticalBrush`;
4. the primary and secondary brushes differ in the mixed state;
5. after rendering `75/80`, all three exact normal brush references or
   equivalent brush values are restored;
6. in single mode, `SecondaryArc.Visibility` remains collapsed.

Use helper methods that compare `Brush.ToString()` for XAML-created gradient
or solid normal brushes and compare alert brushes directly for warning and
critical. `LiquidTank` is the fifth skin and is covered by its dedicated
failing test in Step 4 because it intentionally has no primary `ProgressArc`.

- [ ] **Step 2: Run the five-skin tests and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~QuotaAlertSkinTests
```

Expected: alert assertions fail because all skin renderers retain their XAML
colors.

- [ ] **Step 3: Implement alert colors in the four arc-based skins**

For `HudDialSkin`, `EnergyRingSkin`, `LiquidGlassSkin`, and `AuroraSkin`:

1. after `InitializeComponent`, capture:
   - `PrimaryArc.Stroke`;
   - `SecondaryArc.Stroke`;
   - `PercentText.Foreground`;
2. during every `RenderCore`, assign:

```csharp
PrimaryArc.Stroke = QuotaAlertPalette.ResolveBrush(
    state.PrimaryAlert,
    _normalPrimaryStroke);
PercentText.Foreground = QuotaAlertPalette.ResolveBrush(
    state.PrimaryAlert,
    _normalPercentForeground);
SecondaryArc.Stroke = QuotaAlertPalette.ResolveBrush(
    state.SecondaryAlert ?? QuotaAlertLevel.Normal,
    _normalSecondaryStroke);
```

For `LiquidGlass`, also capture and resolve `FluidBlob.Fill` with the primary
level. Do not recolor the glass shell, highlights, label, bubbles, background,
track strokes, or animation decorations.

- [ ] **Step 4: Add failing LiquidTank-specific tests**

In `LiquidTankSkinTests`, render normal, warning, critical, then normal again.
Resolve:

- `LiquidBody`;
- `BackWaveSurface`;
- `FrontWaveSurface`;
- `PercentText`;
- `SecondaryArc`.

Assert that all three liquid fills and the percentage follow primary state,
the secondary arc follows secondary state independently, the white crest
stroke and vessel frame do not change, and all normal gradient fills are
restored.

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QuotaAlertSkinTests|FullyQualifiedName~LiquidTankSkinTests"
```

Expected: new LiquidTank alert assertions fail.

- [ ] **Step 5: Implement LiquidTank alert presentation**

Capture the normal fills for `LiquidBody`, `BackWaveSurface`, and
`FrontWaveSurface`, plus the normal percentage foreground and secondary
stroke, after XAML initialization.

On each render:

- resolve all three liquid fills and the percentage from
  `state.PrimaryAlert`;
- resolve only the outer `SecondaryArc.Stroke` from
  `state.SecondaryAlert ?? Normal`;
- do not change liquid geometry, opacity, wave motion, bubbles, vessel frame,
  label, track, or crest stroke.

- [ ] **Step 6: Run all skin tests and verify GREEN**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QuotaAlertSkinTests|FullyQualifiedName~LiquidTankSkinTests|FullyQualifiedName~SkinControllerTests"
```

Expected: all selected tests pass, including normal-color restoration and
existing animation/waterline tests.

- [ ] **Step 7: Commit five-skin presentation**

```powershell
git add src/CodexQuotaHud.App/UI/Skins tests/CodexQuotaHud.App.Tests/UI/QuotaAlertSkinTests.cs tests/CodexQuotaHud.App.Tests/UI/LiquidTankSkinTests.cs
git commit -m "feat: color low quota across HUD skins"
```

---

### Task 3: Edge Bar, Tray Icon, and Detail Rows

**Files:**
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbViewModel.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/TrayIconRenderer.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/QuotaOrbViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/QuotaOrbWindowStartupTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/TrayIconRendererTests.cs`

**Interfaces:**
- Changes detail presentation to:

```csharp
public sealed record QuotaDetailRow(
    string Label,
    double RemainingPercent,
    string? ResetsAt)
{
    public string Remaining => $"{RemainingPercent:0}%";
    public QuotaAlertLevel AlertLevel =>
        QuotaAlertPolicy.Classify(RemainingPercent);
}
```

- Edge bar consumes the view model's primary percentage and selected skin.
- Tray state keeps the existing signature and resolves the alert internally.

- [ ] **Step 1: Write failing numeric detail-row tests**

Update existing `QuotaOrbViewModelTests` row assertions to verify both formatted
text and numeric state. Add a dual state with primary `20` and secondary `10`:

```csharp
Assert.Equal(20, rows[0].RemainingPercent);
Assert.Equal("20%", rows[0].Remaining);
Assert.Equal(QuotaAlertLevel.Warning, rows[0].AlertLevel);
Assert.Equal(10, rows[1].RemainingPercent);
Assert.Equal(QuotaAlertLevel.Critical, rows[1].AlertLevel);
```

Retain the existing assertions that missing windows produce no row.

- [ ] **Step 2: Run view-model tests and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~QuotaOrbViewModelTests
```

Expected: compilation fails because `RemainingPercent` and `AlertLevel` do not
exist.

- [ ] **Step 3: Implement numeric detail rows and XAML triggers**

Replace the formatted-string constructor field with numeric
`RemainingPercent`; keep the calculated `Remaining` string so existing binding
and copy remain unchanged.

Add `xmlns:ui="clr-namespace:CodexQuotaHud.App.UI"`, name the `ItemsControl`
`DetailsItems`, and name the remaining text `RemainingText`. Replace its direct
foreground with a style:

```xml
<TextBlock.Style>
    <Style TargetType="TextBlock">
        <Setter Property="Foreground"
                Value="{DynamicResource PopupAccentBrush}" />
        <Style.Triggers>
            <DataTrigger Binding="{Binding AlertLevel}"
                         Value="{x:Static ui:QuotaAlertLevel.Warning}">
                <Setter Property="Foreground"
                        Value="{x:Static ui:QuotaAlertPalette.WarningBrush}" />
            </DataTrigger>
            <DataTrigger Binding="{Binding AlertLevel}"
                         Value="{x:Static ui:QuotaAlertLevel.Critical}">
                <Setter Property="Foreground"
                        Value="{x:Static ui:QuotaAlertPalette.CriticalBrush}" />
            </DataTrigger>
        </Style.Triggers>
    </Style>
</TextBlock.Style>
```

Do not color labels, reset times, stale text, or metadata.

- [ ] **Step 4: Write failing edge-bar alert tests**

Extend `QuotaOrbWindowStartupTests` using a mutable fake refresh controller:

1. select a skin and capture its normal edge theme;
2. publish primary `20`, apply a collapsed side, and assert:
   - fill background is `WarningBrush`;
   - outline border is `WarningBrush`;
   - glow color is `WarningMediaColor`;
   - track and texture remain the selected skin's normal values;
3. publish primary `10` and assert critical equivalents;
4. publish primary `75` and assert the full normal skin edge theme is restored.

Also test the real detail style. Publish rows through the mutable refresh
controller, call `DetailsItems.ApplyTemplate()` and `UpdateLayout()`, obtain
the generated `ContentPresenter` from
`DetailsItems.ItemContainerGenerator.ContainerFromIndex(index)`, and resolve
`RemainingText` through the item template's namescope. Assert one warning and
one critical row use the shared static brushes, then publish normal rows and
assert the skin popup accent is restored.

- [ ] **Step 5: Implement edge alert resolution**

Move edge theme assignment into `ApplyEdgeProgressState` so every skin or
percentage update reapplies a complete state:

```csharp
var theme = EdgeProgressThemeProvider.Get(_viewModel.SelectedSkin);
var level = QuotaAlertPolicy.Classify(_viewModel.PrimaryPercent);
EdgeProgressTrack.Background = theme.Track;
EdgeProgressOutline.BorderBrush =
    QuotaAlertPalette.ResolveBrush(level, theme.Border);
EdgeProgressFill.Background =
    QuotaAlertPalette.ResolveBrush(level, theme.Fill);
EdgeProgressTexture.Background = theme.Texture;
EdgeHandleGlow.Color =
    QuotaAlertPalette.ResolveMediaColor(level, theme.GlowColor);
```

Retain theme texture/glow opacity and material texture. Remove duplicate edge
theme assignments from `ApplyPopupTheme`, and ensure `ApplySelectedSkin` calls
`ApplyEdgeProgressState` after selecting/rendering the new skin. In the window
constructor, call `ApplyEdgeProgressState(EdgeDockSide.None)` immediately after
`ApplyPopupTheme()` so the initial edge presentation is fully initialized before
the first percentage or skin change.

- [ ] **Step 6: Write failing tray-state tests**

Add:

```csharp
[Theory]
[InlineData(20, 0xFF, 0xB5, 0x47)]
[InlineData(10, 0xFF, 0x5A, 0x67)]
public void CreateState_LowQuotaOverridesOnlyRingAccent(
    double percent,
    byte red,
    byte green,
    byte blue)
{
    var state = TrayIconRenderer.CreateState(
        QuotaDisplayMode.Single,
        percent,
        SkinId.Aurora);

    Assert.Equal(Color.FromArgb(red, green, blue), state.Accent);
    Assert.Equal($"{percent:0}", state.Text);
}
```

Keep and strengthen the existing distinct-skin-accent test at `50%`, proving
normal skin colors remain distinct. Add hidden-mode coverage proving the
no-data dash does not become a fabricated critical percentage.

- [ ] **Step 7: Implement tray alert accent and verify all surfaces**

In `CreateState`, preserve existing hidden handling. For visible data:

```csharp
var level = QuotaAlertPolicy.Classify(percent.Value);
var accent = QuotaAlertPalette.ResolveDrawingColor(
    level,
    AccentFor(skin));
```

Do not change tray background, numeric text color, arc geometry, size, icon
lifetime, tooltip, or primary-quota selection.

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QuotaOrbViewModelTests|FullyQualifiedName~QuotaOrbWindowStartupTests|FullyQualifiedName~TrayIconRendererTests"
```

Expected: all selected view-model, real-window, edge-theme, detail-style, and
tray tests pass.

- [ ] **Step 8: Commit non-skin surfaces**

```powershell
git add src/CodexQuotaHud.App/UI/QuotaOrbViewModel.cs src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs src/CodexQuotaHud.App/UI/TrayIconRenderer.cs tests/CodexQuotaHud.App.Tests/UI/QuotaOrbViewModelTests.cs tests/CodexQuotaHud.App.Tests/UI/QuotaOrbWindowStartupTests.cs tests/CodexQuotaHud.App.Tests/UI/TrayIconRendererTests.cs
git commit -m "feat: show low-quota alerts across HUD surfaces"
```

---

### Task 4: Full Verification and Documentation

**Files:**
- Modify: `README.md`
- Modify: `CURRENT_TASK.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `CHANGELOG_AI.md`

**Interfaces:**
- Consumes final implementation behavior and actual test totals.
- Produces an accurate handoff without claiming GUI, deployment, installation,
  push, or release work that was not performed.

- [ ] **Step 1: Run all focused alert tests together**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~QuotaAlert|FullyQualifiedName~SkinControllerTests|FullyQualifiedName~LiquidTankSkinTests|FullyQualifiedName~QuotaOrbViewModelTests|FullyQualifiedName~QuotaOrbWindowStartupTests|FullyQualifiedName~TrayIconRendererTests"
```

Expected: all alert-policy, five-skin, liquid, view-model, edge, detail, and
tray tests pass.

- [ ] **Step 2: Run complete automated verification**

Run:

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore --logger "console;verbosity=minimal"
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

Expected: every test passes, Release build has zero warnings and zero errors,
and diff check prints nothing.

- [ ] **Step 3: Update the four handoff documents**

Document:

- exact `>20`, `>10..20`, and `<=10` behavior;
- exact warning and critical colors;
- independent dual-quota colors;
- participating floating HUD, edge, tray, and detail surfaces;
- no flashing, popup, sound, settings, or refresh change;
- preview sliders as the manual boundary and mixed-state tool;
- final Core, App/UI, and total test counts;
- whether GUI/manual acceptance was actually performed;
- installed `v1.0.0`, deployment, release, and push state remaining unchanged
  unless separately authorized.

Correct `PROJECT_CONTEXT.md` from its stale `231/286` baseline to the final
verified counts and update its “run the full suite” wording.

- [ ] **Step 4: Verify documentation and branch state**

Run:

```powershell
rg -n \"20%|10%|Warning|Critical|amber|red|测试|tests\" README.md CURRENT_TASK.md PROJECT_CONTEXT.md CHANGELOG_AI.md
git diff --check
git status --short
```

Expected: counts and thresholds agree in all four documents; status lists only
those intended documentation files.

- [ ] **Step 5: Commit documentation**

```powershell
git add README.md CURRENT_TASK.md PROJECT_CONTEXT.md CHANGELOG_AI.md
git commit -m "docs: record low-quota alert colors"
```

- [ ] **Step 6: Perform manual preview acceptance only when GUI launch is authorized**

Using the reviewed canonical Release artifact and existing preview shortcut:

1. Select each of the five skins.
2. Check primary values `21`, `20`, `11`, `10`, and `0`.
3. In dual mode check primary/secondary `75/20`, `9/75`, `20/10`, and
   `10/10`.
4. Check full orb, detail rows, tray icon, and each collapsed edge side.
5. Confirm normal colors return after moving a slider back above `20`.
6. Confirm no flashing, popup, animation-speed, refresh, or settings change.
7. Record tested commit and artifact path/hash.

If GUI launch is not authorized, explicitly report this acceptance as not
performed.
