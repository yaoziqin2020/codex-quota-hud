# Skin Designer Animation Controls Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Implementation status (2026-08-04):** Implemented and serially verified in
local candidate `6348c6a`. The checklist below remains the original execution
record; unchecked boxes are not retroactively converted into evidence. The
installed-layout follow-up replaced the initial split control strip with fixed-
width value boxes and adjacent `预览状态` / `停靠预览` task groups. Exact final
test, package, installer, and local-install evidence is recorded in
`docs/verification/2026-08-02-optional-skin-designer-acceptance.md`.

**Goal:** Finish the reported custom-skin import and preview regressions, add understandable animation presets, make breathing and glow visibly match their names, repair the synthetic-preview controls, and give the optional Skin Designer a dedicated application icon.

**Architecture:** Keep the `.cqskin` schema unchanged. Put preset resolution and motion-range calculation in small pure helpers, expose preset/decorative-image state through the existing Designer view model, keep WPF event handlers thin, and render glow through a dedicated visual layer. Preserve the existing App/Designer/Skins project boundaries and package the Designer icon inside the Designer executable.

**Tech Stack:** .NET 9, C# 13, WPF, xUnit, Inno Setup, PowerShell packaging scripts, built-in ImageGen for the icon source, Windows multi-frame `.ico` resources.

## Global Constraints

- Work only in `C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731`; do not use an old conversation worktree.
- Keep release version `1.2.0`; do not tag, publish a GitHub Release, replace `v1.1.1` assets, or create a pull request.
- Do not change the `.cqskin` schema or add timelines, keyframes, or new animation channels.
- Preserve existing imported drafts exactly; only newly created drafts receive the new no-decoration `Gentle` defaults.
- `静止`, `柔和`, and `明显` are the only top-level presets; manual fine-tuning reports `自定义`.
- Without a decoration image, rotation and floating controls are disabled and presets write those two values as `0`.
- Quota number and label remain stationary during breathing and glow.
- Run WPF test projects serially because parallel builds can lock generated `obj` files.
- The ordinary HUD executable, ordinary-user shortcuts, and ordinary HUD icon remain unchanged.
- Setup keeps the Designer optional and unchecked by default.

---

### Task 1: Close the already-debugged import, switch, and initial-animation regressions

**Files:**
- Create: `src/CodexQuotaHud.Skins/Storage/SkinPackageExchangeDirectory.cs`
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/SkinManagement/ISkinManagementDialogs.cs`
- Modify: `src/CodexQuotaHud.App/UI/SkinManagement/SkinManagementController.cs`
- Modify: `src/CodexQuotaHud.App/UI/TrayController.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Output/ISkinOutputDialogs.cs`
- Test: `tests/CodexQuotaHud.App.Tests/Infrastructure/LocalControl/LocalControlActivationHandlerTests.cs`
- Test: `tests/CodexQuotaHud.App.Tests/Preview/SyntheticPreviewCompositionTests.cs`
- Test: `tests/CodexQuotaHud.App.Tests/UI/TraySkinMenuTests.cs`
- Test: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/WindowsSkinOutputDialogsTests.cs`

**Interfaces:**
- Produces: `SkinPackageExchangeDirectory.DefaultPath`, `EnsureExists()`, and `SuggestedExportPath(string displayName, SemanticVersion version)`.
- Produces: `SkinManagementController.SynchronizeCatalog(InstalledSkinCatalogSnapshot snapshot)` and `QuotaOrbWindow.SynchronizeSkinCatalog(InstalledSkinCatalogSnapshot snapshot)`.
- Preserves: custom skin names select the skin directly; deletion remains a separate submenu action.

- [ ] **Step 1: Review the existing focused tests and working-tree diff**

Confirm the diff contains the prior RED/GREEN fixes only: shared Documents exchange directory, immediate catalog synchronization after import/removal/external activation, direct custom-skin selection, separate deletion, and `ApplyAnimationState()` after the synthetic HUD first becomes visible.

- [ ] **Step 2: Run the four focused regression groups serially**

```powershell
dotnet test tests/CodexQuotaHud.App.Tests/CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~LocalControlActivationHandlerTests|FullyQualifiedName~SyntheticPreviewCompositionTests|FullyQualifiedName~TraySkinMenuTests"
dotnet test tests/CodexQuotaHud.SkinDesigner.Tests/CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~WindowsSkinOutputDialogsTests"
```

Expected: all selected tests pass. If a test fails, preserve its exact output and return to root-cause analysis before editing.

- [ ] **Step 3: Validate patch hygiene**

```powershell
git diff --check
git status --short
```

Expected: no whitespace errors; `tmp/` remains untracked and excluded from the commit.

- [ ] **Step 4: Commit the verified regression fixes**

```powershell
git add src/CodexQuotaHud.Skins/Storage/SkinPackageExchangeDirectory.cs src/CodexQuotaHud.App/App.xaml.cs src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs src/CodexQuotaHud.App/UI/SkinManagement/ISkinManagementDialogs.cs src/CodexQuotaHud.App/UI/SkinManagement/SkinManagementController.cs src/CodexQuotaHud.App/UI/TrayController.cs src/CodexQuotaHud.SkinDesigner/Output/ISkinOutputDialogs.cs tests/CodexQuotaHud.App.Tests/Infrastructure/LocalControl/LocalControlActivationHandlerTests.cs tests/CodexQuotaHud.App.Tests/Preview/SyntheticPreviewCompositionTests.cs tests/CodexQuotaHud.App.Tests/UI/TraySkinMenuTests.cs tests/CodexQuotaHud.SkinDesigner.Tests/Output/WindowsSkinOutputDialogsTests.cs
git commit -m "fix: synchronize custom skin workflows"
```

### Task 2: Add pure animation presets and the new-draft default

**Files:**
- Create: `src/CodexQuotaHud.SkinDesigner/UI/AnimationPresets.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftFactory.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/AnimationPresetTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/SkinDraftFactoryTests.cs`

**Interfaces:**
- Produces: `enum AnimationPresetKind { Still, Gentle, Noticeable }`.
- Produces: `AnimationPresets.Resolve(AnimationPresetKind preset, bool hasDecoration) : SkinAnimationSettings`.
- Produces: `AnimationPresets.DisplayName(SkinAnimationSettings settings, bool hasDecoration) : string`.

- [ ] **Step 1: Write failing preset mapping tests**

```csharp
[Theory]
[InlineData(AnimationPresetKind.Still, false, 0, 0, 0, 0, "静止")]
[InlineData(AnimationPresetKind.Gentle, false, 0, .55, .65, 0, "柔和")]
[InlineData(AnimationPresetKind.Noticeable, false, 0, .9, .9, 0, "明显")]
[InlineData(AnimationPresetKind.Gentle, true, .45, .45, .55, .15, "柔和")]
[InlineData(AnimationPresetKind.Noticeable, true, .8, .9, .9, .25, "明显")]
public void Resolve_UsesExactApprovedValues(
    AnimationPresetKind kind,
    bool hasDecoration,
    double rotation,
    double breathing,
    double glow,
    double floating,
    string name)
{
    var settings = AnimationPresets.Resolve(kind, hasDecoration);
    Assert.Equal(new SkinAnimationSettings(rotation, breathing, glow, floating), settings);
    Assert.Equal(name, AnimationPresets.DisplayName(settings, hasDecoration));
}

[Fact]
public void DisplayName_ReturnsCustomForManualValues() =>
    Assert.Equal("自定义", AnimationPresets.DisplayName(
        new SkinAnimationSettings(.123, .456, .789, .111), true));
```

- [ ] **Step 2: Change the draft-default assertion to the no-decoration Gentle profile**

```csharp
Animation: new SkinAnimationSettings(
    RotationIntensity: 0,
    BreathingIntensity: .55,
    GlowIntensity: .65,
    FloatingIntensity: 0)
```

- [ ] **Step 3: Run the tests and verify RED**

```powershell
dotnet test tests/CodexQuotaHud.SkinDesigner.Tests/CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AnimationPresetTests|FullyQualifiedName~SkinDraftFactoryTests"
```

Expected: compilation fails because `AnimationPresetKind` and `AnimationPresets` do not exist, and the old draft default does not match.

- [ ] **Step 4: Implement the exact preset helper**

```csharp
public enum AnimationPresetKind
{
    Still,
    Gentle,
    Noticeable
}

public static class AnimationPresets
{
    public static SkinAnimationSettings Resolve(
        AnimationPresetKind preset,
        bool hasDecoration) => preset switch
        {
            AnimationPresetKind.Still => new(0, 0, 0, 0),
            AnimationPresetKind.Gentle when hasDecoration =>
                new(.45, .45, .55, .15),
            AnimationPresetKind.Gentle => new(0, .55, .65, 0),
            AnimationPresetKind.Noticeable when hasDecoration =>
                new(.8, .9, .9, .25),
            AnimationPresetKind.Noticeable => new(0, .9, .9, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };

    public static string DisplayName(
        SkinAnimationSettings settings,
        bool hasDecoration)
    {
        ArgumentNullException.ThrowIfNull(settings);
        foreach (var preset in Enum.GetValues<AnimationPresetKind>())
        {
            if (EqualsWithinTolerance(settings, Resolve(preset, hasDecoration)))
            {
                return preset switch
                {
                    AnimationPresetKind.Still => "静止",
                    AnimationPresetKind.Gentle => "柔和",
                    AnimationPresetKind.Noticeable => "明显",
                    _ => throw new ArgumentOutOfRangeException()
                };
            }
        }

        return "自定义";
    }
}
```

Use a private `1e-6` per-field tolerance helper so slider round-tripping does not incorrectly report `自定义`.

- [ ] **Step 5: Change `SkinDraftFactory.CreateNew` to the no-decoration Gentle values**

Replace only the four animation defaults with `(0, .55, .65, 0)`. Do not change imported or opened drafts.

- [ ] **Step 6: Run the focused tests and verify GREEN**

Run the command from Step 3. Expected: all selected tests pass.

- [ ] **Step 7: Commit the pure preset layer**

```powershell
git add src/CodexQuotaHud.SkinDesigner/UI/AnimationPresets.cs src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftFactory.cs tests/CodexQuotaHud.SkinDesigner.Tests/UI/AnimationPresetTests.cs tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/SkinDraftFactoryTests.cs
git commit -m "feat: add simple skin animation presets"
```

### Task 3: Expose preset and decoration availability through the Designer view model

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/EditorSectionViewModels.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/DesignerViewModel.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerViewModelTests.cs`

**Interfaces:**
- Consumes: `AnimationPresets.Resolve` and `AnimationPresets.DisplayName` from Task 2.
- Produces: `AnimationEditorViewModel.ApplyPreset(AnimationPresetKind preset) : EditorMutationResult`.
- Produces: bindable `CanEditDecorationAnimation`, `DecorationAnimationHint`, and `CurrentPresetName` properties.
- Produces: `AnimationEditorViewModel.NotifyStateChanged()` for owner-driven refresh after theme or asset changes.

- [ ] **Step 1: Write failing view-model tests**

Add tests that prove:

```csharp
var sut = CreateViewModel(out var session, out var previewed);
var before = session.Current.Revision;
var result = sut.Animation.ApplyPreset(AnimationPresetKind.Noticeable);
Assert.True(result.Succeeded, Format(result.Errors));
Assert.Equal(before + 1, session.Current.Revision);
Assert.Equal(new SkinAnimationSettings(0, .9, .9, 0),
    session.Current.Theme.Animation);
Assert.Single(previewed);
Assert.Equal("明显", sut.Animation.CurrentPresetName);
Assert.False(sut.Animation.CanEditDecorationAnimation);
Assert.Contains("透明装饰图", sut.Animation.DecorationAnimationHint);
```

Also construct the internal view model with a Decoration asset and assert that `Noticeable` writes `(.8, .9, .9, .25)`. Subscribe to `PropertyChanged`, remove/add the decoration through the existing image mutation committer, and assert the three bindable properties are raised. Removing the decoration must leave the four existing animation numbers unchanged while `CanEditDecorationAnimation` becomes false and `CurrentPresetName` becomes `自定义`; only a later preset click may write effective no-decoration values.

- [ ] **Step 2: Run the focused tests and verify RED**

```powershell
dotnet test tests/CodexQuotaHud.SkinDesigner.Tests/CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DesignerViewModelTests"
```

Expected: compilation fails because the new animation view-model API does not exist.

- [ ] **Step 3: Implement notification and one-revision preset application**

Make `AnimationEditorViewModel` implement `INotifyPropertyChanged` and add:

```csharp
public bool CanEditDecorationAnimation =>
    Owner.Assets.ContainsKey(SkinAssetSlot.Decoration);

public string DecorationAnimationHint => CanEditDecorationAnimation
    ? "装饰旋转和浮动会作用于当前透明装饰图。"
    : "装饰旋转和浮动需要先添加透明装饰图。";

public string CurrentPresetName => AnimationPresets.DisplayName(
    Owner.Current.Theme.Animation,
    CanEditDecorationAnimation);

public EditorMutationResult ApplyPreset(AnimationPresetKind preset)
{
    var settings = AnimationPresets.Resolve(preset, CanEditDecorationAnimation);
    return Owner.Apply(draft => draft with
    {
        Theme = draft.Theme with { Animation = settings }
    });
}
```

`NotifyStateChanged()` raises all three property names. In `DesignerViewModel.OnMeaningfulChange`, call `Animation.NotifyStateChanged()` immediately after `Images.NotifyStateChanged()` so manual sliders, undo/redo, and image add/remove refresh the UI from the same meaningful-change event.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the command from Step 2. Expected: all selected tests pass and each preset produces exactly one revision and preview update.

- [ ] **Step 5: Commit the view-model behavior**

```powershell
git add src/CodexQuotaHud.SkinDesigner/UI/EditorSectionViewModels.cs src/CodexQuotaHud.SkinDesigner/UI/DesignerViewModel.cs tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerViewModelTests.cs
git commit -m "feat: expose animation preset state"
```

### Task 4: Replace subtle/misnamed animation behavior with semantic motion layers

**Files:**
- Create: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingMotionProfile.cs`
- Modify: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingRenderer.xaml`
- Modify: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingRenderer.xaml.cs`
- Create: `tests/CodexQuotaHud.Skins.Tests/Templates/FreeDecorationRingMotionProfileTests.cs`
- Modify: `tests/CodexQuotaHud.Skins.Tests/Templates/FreeDecorationRingLayerTests.cs`

**Interfaces:**
- Produces: `AnimationRange(double From, double To, double HalfCycleSeconds)`.
- Produces: `FreeDecorationRingMotionProfile.Breathing(double baseScale, double intensity)` and `.Glow(double intensity)`.
- Produces: renderer element `AnimatedGlow` aligned behind the primary progress ring.

- [ ] **Step 1: Write failing pure motion-profile tests**

```csharp
[Theory]
[InlineData(1, 1, .96, 1.12, 1.4)]
[InlineData(1, .55, .978, 1.066, 1.85)]
public void Breathing_UsesApprovedVisibleRange(
    double baseScale, double intensity,
    double from, double to, double halfCycle)
{
    var range = FreeDecorationRingMotionProfile.Breathing(baseScale, intensity);
    Assert.Equal(from, range.From, 6);
    Assert.Equal(to, range.To, 6);
    Assert.Equal(halfCycle, range.HalfCycleSeconds, 6);
}

[Fact]
public void Glow_NoticeableUsesApprovedOpacityRange()
{
    var range = FreeDecorationRingMotionProfile.Glow(.9);
    Assert.Equal(.08, range.From, 6);
    Assert.Equal(.825, range.To, 6);
    Assert.Equal(1.5, range.HalfCycleSeconds, 6);
}
```

- [ ] **Step 2: Change the renderer dependency-property tests to require the dedicated glow layer**

In `GetAnimationProperties`, map `AnimationChannel.Glow` to `renderer.AnimatedGlow` and `UIElement.OpacityProperty`. Add assertions that `PrimaryProgress.Opacity` is not animated and that stopping animations restores `AnimatedGlow.Opacity` to `0`.

- [ ] **Step 3: Run the focused Skins tests and verify RED**

```powershell
dotnet test tests/CodexQuotaHud.Skins.Tests/CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~FreeDecorationRingMotionProfileTests|FullyQualifiedName~FreeDecorationRingLayerTests"
```

Expected: compilation fails because the profile and `AnimatedGlow` do not exist.

- [ ] **Step 4: Implement the pure motion profile**

```csharp
internal readonly record struct AnimationRange(
    double From,
    double To,
    double HalfCycleSeconds);

internal static class FreeDecorationRingMotionProfile
{
    public static AnimationRange Breathing(double baseScale, double intensity)
    {
        var value = Math.Clamp(intensity, 0, 1);
        return new(
            baseScale * (1 - (.04 * value)),
            baseScale * (1 + (.12 * value)),
            2.4 - value);
    }

    public static AnimationRange Glow(double intensity)
    {
        var value = Math.Clamp(intensity, 0, 1);
        return new(.08, .15 + (.75 * value), 2.4 - value);
    }
}
```

- [ ] **Step 5: Add and theme the dedicated glow ellipse**

Place this between the decoration/center imagery and the primary track/progress content, with a Z-index below the primary ring:

```xml
<Ellipse x:Name="AnimatedGlow" Panel.ZIndex="49"
         Fill="Transparent" Opacity="0" IsHitTestVisible="False" />
```

In `ApplyTheme`, set its stroke to `GlowColor`, configure a zero-depth `DropShadowEffect` with `GlowColor`, and use static `theme.GlowIntensity` for the effect opacity. Give it the same diameter as the primary ring and a stroke thickness based on the primary ring thickness.

- [ ] **Step 6: Rewire breathing and glow tracks**

Use `FreeDecorationRingMotionProfile.Breathing(_centerTransforms.BaseScale, settings.BreathingIntensity)` for both center scale axes. Use `FreeDecorationRingMotionProfile.Glow(settings.GlowIntensity)` on `AnimatedGlow.Opacity`. Update `CreatePulse` to consume `AnimationRange`. Do not animate `PrimaryProgress.Opacity`.

In `ResetTransforms`, remove the animation clock from `AnimatedGlow.Opacity` and set the base value to `0`; leave the static `BaseFill.Effect` unchanged.

- [ ] **Step 7: Run focused Skins tests and verify GREEN**

Run the command from Step 3. Expected: all selected tests pass; breathing owns center scale only, glow owns the dedicated layer only, and reset restores exact base state.

- [ ] **Step 8: Commit the renderer behavior**

```powershell
git add src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingMotionProfile.cs src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingRenderer.xaml src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingRenderer.xaml.cs tests/CodexQuotaHud.Skins.Tests/Templates/FreeDecorationRingMotionProfileTests.cs tests/CodexQuotaHud.Skins.Tests/Templates/FreeDecorationRingLayerTests.cs
git commit -m "fix: make custom skin motion visible"
```

### Task 5: Redesign the Designer animation and synthetic-preview controls

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`

**Interfaces:**
- Consumes: Task 3 bindable properties and `ApplyPreset`.
- Produces: named controls `AnimationStillPresetButton`, `AnimationGentlePresetButton`, `AnimationNoticeablePresetButton`, `AnimationPresetStatusText`, `AdvancedAnimationSection`, `SyntheticQuotaRow`, and `SyntheticStateRow`.
- Preserves: all existing slider tags, synthetic preview bindings, commands, and selection-change handlers.

- [ ] **Step 1: Write failing WPF layout and interaction tests**

Add assertions that:

```csharp
Assert.False(Assert.IsType<Expander>(
    window.FindName("AdvancedAnimationSection")).IsExpanded);
Assert.Equal("柔和", Assert.IsType<TextBlock>(
    window.FindName("AnimationPresetStatusText")).Text);
Assert.False(rotationSlider.IsEnabled);
Assert.False(floatingSlider.IsEnabled);
Assert.Contains("透明装饰图", decorationHint.Text);
Assert.NotNull(window.FindName("SyntheticQuotaRow"));
Assert.NotNull(window.FindName("SyntheticStateRow"));
```

At `window.Width = 600`, measure both named rows and assert their right/bottom bounds do not exceed `SyntheticPreviewStrip`. Open the five-hour preset ComboBox, obtain the generated `ComboBoxItem` and its visual `TextBlock`, and assert the resolved foreground color is `#FF0B1220` on the light background.

Raise `Button.ClickEvent` on `AnimationNoticeablePresetButton` and assert the current draft changes to the no-decoration `Noticeable` values in one revision.

- [ ] **Step 2: Update the accessibility expectation before implementation**

The three new preset buttons occupy tab indexes `38`, `39`, and `40`; the four advanced sliders move to `41` through `44`; all existing synthetic and action controls shift by `+3`. Change the continuous range assertion from `1..57` to `1..60` and preserve all automation-name assertions.

- [ ] **Step 3: Run the focused WPF tests and verify RED**

```powershell
dotnet test tests/CodexQuotaHud.SkinDesigner.Tests/CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MainWindowLayoutTests"
```

Expected: tests fail because the named controls/layout rows do not exist, ComboBox text resolves to the wrong brush, and tab order has not moved.

- [ ] **Step 4: Fix ComboBox foreground inheritance at the source**

Remove the foreground setter from the implicit `TextBlock` style. The Window already provides `DesignerTextBrush`; normal text inherits that light foreground, while TextBlocks generated inside the light ComboBox inherit the ComboBox's dark foreground. Keep `FieldLabelStyle` explicit.

- [ ] **Step 5: Add presets and collapse advanced fine-tuning**

At the top of `AnimationSection`, add the three named buttons with `Tag="Still"`, `Tag="Gentle"`, and `Tag="Noticeable"`, all wired to `AnimationPreset_OnClick`. Bind the status text to `Editor.Animation.CurrentPresetName`. Move the existing four sliders into `AdvancedAnimationSection` with labels `装饰旋转`, `中心呼吸`, `额度环光晕`, and `装饰浮动`.

Bind rotation/floating `IsEnabled` to `Editor.Animation.CanEditDecorationAnimation` and add a hint bound to `DecorationAnimationHint`.

- [ ] **Step 6: Add the thin preset click handler**

```csharp
private void AnimationPreset_OnClick(object sender, RoutedEventArgs e)
{
    if (sender is not Button { Tag: string tag } button ||
        !Enum.TryParse<AnimationPresetKind>(tag, out var preset))
    {
        return;
    }

    PresentMutationResult(button, Editor.Animation.ApplyPreset(preset));
}
```

- [ ] **Step 7: Replace the one-row synthetic strip with two named rows**

`SyntheticQuotaRow` uses columns `Auto, *, *` for display mode, five-hour controls, and weekly controls. `SyntheticStateRow` uses columns `*, Auto` for the three toggles and the existing five placement/expand buttons. Keep the outer `SyntheticPreviewStrip`, commands, and binding names unchanged.

- [ ] **Step 8: Run focused WPF tests and verify GREEN**

Run the command from Step 3. Expected: all selected layout, interaction, readability, and accessibility tests pass.

- [ ] **Step 9: Commit the Designer UI changes**

```powershell
git add src/CodexQuotaHud.SkinDesigner/MainWindow.xaml src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs
git commit -m "feat: simplify skin designer animation controls"
```

### Task 6: Create and embed the dedicated Skin Designer icon

**Files:**
- Create: `src/CodexQuotaHud.SkinDesigner/Assets/DesignerIcon.png`
- Create: `src/CodexQuotaHud.SkinDesigner/Assets/DesignerIcon.ico`
- Modify: `src/CodexQuotaHud.SkinDesigner/CodexQuotaHud.SkinDesigner.csproj`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/ProjectBoundaryTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`

**Interfaces:**
- Produces: embedded WPF resource `Assets/DesignerIcon.ico` and MSBuild `ApplicationIcon=Assets\DesignerIcon.ico`.
- Preserves: `src/CodexQuotaHud.App/Assets/AppIcon.ico` byte-for-byte.

- [ ] **Step 1: Write failing project/resource tests**

Extend `EvaluatedDesignerOutput_IsWindowsWpfWinExeWithStableName` to request `ApplicationIcon` and assert `Assets\DesignerIcon.ico`. Add a WPF assertion that `MainWindow.Icon` is non-null. Decode the icon with `IconBitmapDecoder` and assert frames include widths `16`, `24`, `32`, `48`, and `256`.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test tests/CodexQuotaHud.SkinDesigner.Tests/CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ProjectBoundaryTests|FullyQualifiedName~MainWindowLayoutTests"
```

Expected: ApplicationIcon is empty, MainWindow has no icon, and the Designer icon files are absent.

- [ ] **Step 3: Generate the icon source through built-in ImageGen**

First extract the largest frame of the existing HUD icon to a temporary PNG and inspect it. Then use ImageGen with that local reference and this exact direction:

```text
Create a Windows application icon for Codex Quota HUD Skin Designer. Preserve the reference icon's dark navy circular base and luminous cyan double HUD ring so it clearly belongs to the same product family. Add one simple diagonal rose-gold stylus/paintbrush mark in the lower-right quadrant to distinguish the designer. No text, no letters, no tiny details, centered geometry, high contrast, transparent outside the icon silhouette, crisp and readable at 16px, polished flat-vector finish with restrained cyan glow.
```

Save the selected transparent square source as `Assets/DesignerIcon.png`. Do not replace the ordinary HUD icon.

- [ ] **Step 4: Build the Windows multi-frame ICO**

Convert the approved PNG into one `.ico` containing `16, 24, 32, 48, 64, 128, 256` pixel frames. Preserve transparency. Inspect the resulting PNG and decoded icon frames before committing.

- [ ] **Step 5: Embed and assign the icon**

Add:

```xml
<ItemGroup>
  <Resource Include="Assets\DesignerIcon.ico" />
</ItemGroup>
<PropertyGroup>
  <ApplicationIcon>Assets\DesignerIcon.ico</ApplicationIcon>
</PropertyGroup>
```

Set `Icon="Assets/DesignerIcon.ico"` on `MainWindow`. The installer shortcut already points to the Designer executable, so it inherits the embedded icon without an installer semantic change.

- [ ] **Step 6: Run focused tests and verify GREEN**

Run the command from Step 2. Expected: project metadata, WPF resource construction, and icon frame tests pass.

- [ ] **Step 7: Commit the dedicated icon**

```powershell
git add src/CodexQuotaHud.SkinDesigner/Assets/DesignerIcon.png src/CodexQuotaHud.SkinDesigner/Assets/DesignerIcon.ico src/CodexQuotaHud.SkinDesigner/CodexQuotaHud.SkinDesigner.csproj src/CodexQuotaHud.SkinDesigner/MainWindow.xaml tests/CodexQuotaHud.SkinDesigner.Tests/ProjectBoundaryTests.cs tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs
git commit -m "feat: brand the skin designer"
```

### Task 7: Run complete verification, build the local 1.2.0 candidate, and close documentation

**Files:**
- Modify: `README.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `CURRENT_TASK.md`
- Modify: `CHANGELOG_AI.md`
- Modify: `docs/verification/2026-08-02-optional-skin-designer-acceptance.md`
- Generated, not committed unless project policy already tracks them: `artifacts/CodexQuotaHud-win-x64/**`, `artifacts/release/CodexQuotaHud-Setup-v1.2.0.exe`, `artifacts/release/CodexQuotaHud-v1.2.0-win-x64.zip`, `artifacts/release/SHA256SUMS.txt`

**Interfaces:**
- Consumes: all production behavior from Tasks 1 through 6.
- Produces: verified local `1.2.0` Setup/ZIP candidate and exact acceptance evidence.

- [ ] **Step 1: Run every test project serially**

```powershell
dotnet test tests/CodexQuotaHud.Core.Tests/CodexQuotaHud.Core.Tests.csproj -c Release --no-restore
dotnet test tests/CodexQuotaHud.Skins.Tests/CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore
dotnet test tests/CodexQuotaHud.App.Tests/CodexQuotaHud.App.Tests.csproj -c Release --no-restore
dotnet test tests/CodexQuotaHud.SkinDesigner.Tests/CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore
```

Expected: zero failures and zero skipped tests. Record exact totals; do not reuse historical totals.

- [ ] **Step 2: Run Release build and diff checks**

```powershell
dotnet build CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

Expected: zero warnings, zero errors, and no whitespace errors.

- [ ] **Step 3: Build the full local v1.2.0 package set**

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/package-release.ps1 -Version 1.2.0
```

Expected: Setup, ordinary-user ZIP, and `SHA256SUMS.txt` exist; Setup contains the optional Designer, ZIP retains its existing normal-HUD semantics, and all packaged executable versions report `1.2.0.0`.

- [ ] **Step 4: Install the Setup candidate with the optional Designer selected**

Run the built Setup interactively. Preserve ordinary-user defaults: normal HUD shortcuts remain normal; Designer remains an optional component. This is an external machine-state change and requires the existing installation approval before execution.

- [ ] **Step 5: Perform the manual installed-build matrix**

Verify and record:

1. Percentage ComboBox closed selections and opened items are readable.
2. The synthetic strip is two rows and usable at normal width and the 600-DIP minimum.
3. New draft starts at `柔和`; `高级细调` starts collapsed.
4. Without a decoration image, rotation/floating are disabled with the correct explanation.
5. With the Soft Rose draft, `柔和` breathing and glow are visible and `明显` is unmistakable; text does not move.
6. Disabling animation stops rotation, breathing, glow, and floating and restores the exact static look.
7. Import defaults to `Documents\Codex Quota HUD Skins`, appears in the skin list immediately, selects by clicking its name, and survives restart.
8. Delete remains a separate action.
9. Designer title bar, taskbar, executable, and optional Start-menu shortcut use the dedicated icon; ordinary HUD icon is unchanged.
10. Apply to HUD and exported/re-imported `.cqskin` both preserve the new animation settings and assets.

- [ ] **Step 6: Update the five project handoff documents with exact evidence**

Record changed behavior, exact test totals, build result, artifact sizes/hashes, installed binary version/hash checks, completed manual rows, and any rows not run. State explicitly that no tag, release upload, or public publication was performed.

- [ ] **Step 7: Commit documentation and acceptance evidence**

```powershell
git add README.md PROJECT_CONTEXT.md CURRENT_TASK.md CHANGELOG_AI.md docs/verification/2026-08-02-optional-skin-designer-acceptance.md
git commit -m "docs: close skin designer v1.2.0 candidate"
```

- [ ] **Step 8: Final repository audit**

```powershell
git status --short --branch
git log -8 --oneline --decorate
git diff --check HEAD~1 HEAD
```

Expected: only explicitly excluded local scratch files remain untracked. Confirm no release artifacts, generated temp images, local settings, or draft directories are staged.

- [ ] **Step 9: Push the existing feature branch after user-authorized final acceptance**

```powershell
git push origin feat/inno-setup-installer-20260731
```

Expected: remote branch advances to the verified local commits. Do not create a PR and do not push a tag.
