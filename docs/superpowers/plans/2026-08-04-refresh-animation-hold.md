# Refresh Animation Hold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Preserve built-in refresh profiles, let custom-skin authors choose a 0-4x refresh multiplier, and keep the effective refresh state visible for an additional author-configurable duration.

**Architecture:** Add backward-compatible `refreshSpeedMultiplier` and `refreshHoldSeconds` fields to the shared skin animation contract. Apply the custom multiplier in the existing custom renderer while leaving built-in profiles unchanged. Centralize the post-refresh hold in `OrbAnimationController`, using cancellable delayed transitions so all skins share identical hold semantics.

**Tech Stack:** .NET 8, WPF, System.Text.Json, xUnit, fake asynchronous delay

## Global Constraints

- Target release: v1.2.3.
- Preserve every built-in skin's current idle/refresh speed profile.
- Custom-skin refresh multiplier is 0-4x, defaults to the current 2x behavior,
  and never affects built-in skins.
- Default hold is 1.5 seconds; allowed range is 0–3 seconds inclusive.
- Existing skin JSON without the field behaves as 1.5 seconds without migration or re-export.
- A repeated refresh restarts the hold; it never stacks speed or timers.
- Disable animation, hide HUD, switch skin, detach, and dispose cancel pending holds immediately.
- Do not delay network requests, quota state updates, or refresh-result display.

---

### Task 1: Backward-compatible skin contract

**Files:**
- Modify: `src/CodexQuotaHud.Skins/Contracts/SkinContracts.cs`
- Modify: `src/CodexQuotaHud.Skins/Contracts/SkinPackageLimits.cs`
- Modify: `src/CodexQuotaHud.Skins/Serialization/SkinJsonCodec.cs`
- Modify: `src/CodexQuotaHud.Skins/Validation/SkinContractValidator.cs`
- Modify: `tests/CodexQuotaHud.Skins.Tests/Serialization/SkinJsonCodecTests.cs`
- Modify: `tests/CodexQuotaHud.Skins.Tests/Validation/SkinContractValidatorTests.cs`

**Interfaces:**
- Extend `SkinAnimationSettings` with `double RefreshSpeedMultiplier = 2d` and
  `double RefreshHoldSeconds = 1.5d`.
- Add speed limits `0d..4d` and hold limits `0d..3d`.
- Use canonical JSON properties `refreshSpeedMultiplier` and
  `refreshHoldSeconds`.

- [ ] **Step 1: Write failing compatibility tests**

Cover old four-property JSON defaulting to `2x` and `1.5s`; speed boundaries
`0`, `2`, and `4`; hold boundaries `0`, `1.5`, and `3`; canonical round-trip
emission; rejection below/above either range; and rejection of NaN/infinity at
the object-validation boundary.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --filter "FullyQualifiedName~SkinJsonCodecTests|FullyQualifiedName~SkinContractValidatorTests"
```

Expected: FAIL because the field and validation do not exist.

- [ ] **Step 3: Implement optional read and canonical write**

Read both fields with `TryGetProperty`; when absent, use `2d` and `1.5d`. Add
both properties to the strict animation allow-list and always emit them from
canonical output. Validate speed as finite within `0..4` and hold as finite
within `0..3`.

- [ ] **Step 4: Run focused tests and verify pass**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.Skins tests/CodexQuotaHud.Skins.Tests
git commit -m "feat: add refresh animation hold setting"
```

### Task 2: Version and document compatibility

**Files:**
- Modify: `src/CodexQuotaHud.Skins/Templates/FreeDecorationRing/FreeDecorationRingTemplate.cs`
- Modify: App/Designer version sources identified by `rg "1\.2\.2|VersionPrefix|Version" src packaging`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/SkinDraftFactoryTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Documents/DesignerDocumentServiceTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/DraftPackageBuilderTests.cs`

**Interfaces:**
- New Designer output using the two refresh fields requires HUD v1.2.3.
- Old imported packages and drafts normalize missing values to 2x and 1.5s.
- A declared minimum version higher than v1.2.3 remains unchanged.

- [ ] **Step 1: Write failing version and normalization tests**

Assert new drafts use 2x/1.5s and v1.2.3, old drafts/packages open with those
defaults, saving/exporting writes both fields and v1.2.3, and higher compatible
minimum versions are preserved.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~SkinDraftFactoryTests|FullyQualifiedName~DesignerDocumentServiceTests|FullyQualifiedName~DraftPackageBuilderTests"
```

- [ ] **Step 3: Update source baselines and template metadata**

Set the App and Designer source compatibility baselines and `FreeDecorationRingTemplate.MinimumHudVersion` to v1.2.3. Rely on the Task 1 optional constructor/default read for old content; do not add a disk migration pass.

- [ ] **Step 4: Run focused tests and verify pass**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.App src/CodexQuotaHud.SkinDesigner tests/CodexQuotaHud.SkinDesigner.Tests
git commit -m "feat: target skin hold metadata at v1.2.3"
```

### Task 3: Cancellable post-refresh hold state machine

**Files:**
- Create: `src/CodexQuotaHud.App/UI/Animation/IAnimationDelay.cs`
- Modify: `src/CodexQuotaHud.App/UI/Animation/OrbAnimationController.cs`
- Modify: `src/CodexQuotaHud.App/UI/Skins/CustomQuotaSkin.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/FakeAnimationDelay.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/OrbAnimationControllerTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/CustomQuotaSkinTests.cs`

**Interfaces:**
- Produce `IAnimationDelay.Delay(TimeSpan duration, CancellationToken cancellationToken) : Task`.
- Add `TimeSpan RefreshHoldDuration { get; }` to `IOrbAnimationTarget`, with built-in targets returning 1.5 seconds.
- `CustomQuotaSkin` returns `Theme.Animation.RefreshHoldSeconds`.
- Make `OrbAnimationController` disposable and distinguish requested state from effective rendered state.

- [ ] **Step 1: Write failing deterministic state-machine tests**

Using `FakeAnimationDelay`, cover: refresh begins immediately; refresh
completion keeps the effective refreshing state for the full duration; zero is
immediate; repeated refresh cancels/restarts the timer; old continuations
cannot alter a new target; hidden, disabled, detached, switched, and disposed
controllers cancel immediately. Assert built-in speed profiles are unchanged
and multiplier values do not stack.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~OrbAnimationControllerTests|FullyQualifiedName~CustomQuotaSkinTests"
```

- [ ] **Step 3: Implement requested/effective state transitions**

On `Refreshing`, cancel the prior delay and apply refreshing immediately. On a request for `Idle` while effectively refreshing, retain refreshing, start the target duration, then apply idle only if the generation, target, enabled state, and requested state are still current. All cancellation paths invalidate the generation before applying their immediate safe state.

- [ ] **Step 4: Wire custom speed and shared duration**

Pass both parsed values through `CustomQuotaSkin`; replace the custom renderer's
fixed `2d` with the package multiplier, whose missing-field default remains
`2d`. Give built-in targets 1.5 seconds and leave every `AnimatedQuotaSkin`
timing value untouched. Dispose the controller with the HUD window.

- [ ] **Step 5: Run focused and App suites**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release
```

Expected: all tests pass with zero skipped.

- [ ] **Step 6: Commit**

```powershell
git add src/CodexQuotaHud.App tests/CodexQuotaHud.App.Tests
git commit -m "feat: hold refresh animation after completion"
```

### Task 4: Designer refresh controls and live preview

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/EditorSectionViewModels.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/SyntheticPreviewViewModel.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/AnimationPresetTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/SyntheticPreviewViewModelTests.cs`

**Interfaces:**
- Add `AnimationEditorViewModel.SetRefreshSpeedMultiplier(double value)` and
  `SetRefreshHoldSeconds(double value)`.
- Add `刷新速度` with range 0–4 displaying one decimal and `×`, plus
  `加速延续` with range 0–3 displaying one decimal and `秒`.

- [ ] **Step 1: Write failing editing and layout tests**

Assert clamping/validation, draft dirty tracking, `2.0×` and `1.5 秒` display,
accessible labels, and exactly one control for each value. Verify 0x, 2x, and
4x visibly affect custom preview speed. Verify unchecking `刷新中` enters the
shared hold path, while checking it again restarts the preview refresh state.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~AnimationEditorViewModelTests|FullyQualifiedName~MainWindowLayoutTests|FullyQualifiedName~SyntheticPreviewViewModelTests"
```

- [ ] **Step 3: Implement the single control**

Bind both fields through the existing slider/numeric editing pipeline and
update preview state through the production animation controller. Old
documents receive 2x/1.5s from Task 1 and therefore show both controls
immediately.

- [ ] **Step 4: Run the full Designer suite**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
```

Expected: all tests pass with zero skipped.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.SkinDesigner tests/CodexQuotaHud.SkinDesigner.Tests
git commit -m "feat: edit refresh hold in skin designer"
```

### Task 5: End-to-end verification and v1.2.3 release evidence

**Files:**
- Modify: `README.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `CURRENT_TASK.md`
- Modify: `CHANGELOG_AI.md`
- Modify: `docs/verification/2026-08-02-optional-skin-designer-acceptance.md`
- Modify: packaging/version files identified by the repository's v1.2.2 release scripts

- [ ] **Step 1: Run source quality gates**

```powershell
dotnet test .\CodexQuotaHud.sln -c Release
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

- [ ] **Step 2: Perform manual timing acceptance**

With a built-in skin, confirm its acceleration remains visibly active for about
1.5 seconds after a fast refresh completes and its prior speed character is
unchanged. With a custom skin, test speed `0x`, `2x`, and `4x`, and hold `0`,
`1.5`, and `3` seconds; re-refresh during the hold; then disable animation,
hide, and switch skins. Confirm each cancellation path is immediate and no
timer or multiplier stacks.

- [ ] **Step 3: Verify old and newly exported skin packages**

Import an old package with neither field and confirm 2x/1.5s. Open it in the
Designer, change both values, save/apply/export, inspect canonical JSON for
both properties, and confirm the package declares HUD v1.2.3.

- [ ] **Step 4: Build, install, and smoke-test v1.2.3 artifacts**

Use the repository's documented v1.2.2 packaging workflow with version changed to v1.2.3. Verify Setup excludes developer tooling unless selected, ZIP behavior remains documented, installed App/Designer report 1.2.3, startup/formal HUD remains correct, and the Designer dialog plan's acceptance is also complete.

- [ ] **Step 5: Record exact local evidence and stop for user acceptance**

Update project handoff documents with pass/fail/not-run evidence and commit only
intended files. Install the verified v1.2.3 candidate locally, complete the
Agent-owned smoke checks, and hand it to the user for practical acceptance.
Never stage `tmp/`. Do not push, move `main`, create tag `v1.2.3`, upload
artifacts, or publish a GitHub Release until the user explicitly confirms the
installed candidate is accepted. After that confirmation, perform those remote
release actions and confirm the final CI run succeeds.
