# Refresh Animation Hold Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep the existing refresh-animation speeds unchanged while allowing every skin to keep its accelerated refresh effect visible for an additional author-configurable duration.

**Architecture:** Add one backward-compatible `refreshHoldSeconds` field to the shared skin animation contract. Centralize the post-refresh hold in `OrbAnimationController`, using cancellable delayed transitions so built-in and custom skins share identical timing semantics without changing their renderers' speed profiles. Expose only this duration in the Designer.

**Tech Stack:** .NET 8, WPF, System.Text.Json, xUnit, fake asynchronous delay

## Global Constraints

- Target release: v1.2.3.
- Add no refresh speed or multiplier option.
- Preserve every built-in skin's current idle/refresh speed profile.
- Preserve the custom renderer's current fixed refreshing speed behavior.
- Default hold is 1.5 seconds; allowed range is 0–3 seconds inclusive.
- Existing skin JSON without the field behaves as 1.5 seconds without migration or re-export.
- A repeated refresh restarts the hold; it never stacks speed or timers.
- Disable animation, hide HUD, switch skin, detach, and dispose cancel pending holds immediately.
- Do not delay network requests, quota state updates, or refresh-result display.

---

### Task 1: Backward-compatible skin contract

**Files:**
- Modify: `src/CodexQuotaHud.SkinContracts/SkinAnimationSettings.cs`
- Modify: `src/CodexQuotaHud.SkinContracts/SkinPackageLimits.cs`
- Modify: `src/CodexQuotaHud.SkinContracts/SkinJsonCodec.cs`
- Modify: `src/CodexQuotaHud.SkinContracts/SkinPackageValidator.cs`
- Modify: `tests/CodexQuotaHud.SkinContracts.Tests/SkinJsonCodecTests.cs`
- Modify: `tests/CodexQuotaHud.SkinContracts.Tests/SkinPackageValidatorTests.cs`

**Interfaces:**
- Extend `SkinAnimationSettings` with `double RefreshHoldSeconds = 1.5d`.
- Add limits `MinimumRefreshHoldSeconds = 0d` and `MaximumRefreshHoldSeconds = 3d`.
- Use canonical JSON property `refreshHoldSeconds`.

- [ ] **Step 1: Write failing compatibility tests**

Cover old four-property JSON defaulting to `1.5`, explicit `0`, `1.5`, and `3`, canonical round-trip emission, rejection below/above range, and rejection of NaN/infinity at the object-validation boundary.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinContracts.Tests\CodexQuotaHud.SkinContracts.Tests.csproj -c Release --filter "FullyQualifiedName~SkinJsonCodecTests|FullyQualifiedName~SkinPackageValidatorTests"
```

Expected: FAIL because the field and validation do not exist.

- [ ] **Step 3: Implement optional read and canonical write**

Read with `TryGetProperty`; when absent, construct `SkinAnimationSettings` with `1.5d`. Add the property to the strict animation allow-list and always emit it from canonical output. Validate it as finite and within `0..3`.

- [ ] **Step 4: Run focused tests and verify pass**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.SkinContracts tests/CodexQuotaHud.SkinContracts.Tests
git commit -m "feat: add refresh animation hold setting"
```

### Task 2: Version and document compatibility

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/Templates/FreeDecorationRingTemplate.cs`
- Modify: `src/CodexQuotaHud.App/Compatibility/HudRuntimeCompatibility.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Compatibility/DesignerRuntimeCompatibility.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Documents/SkinDraftFactoryTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Documents/DesignerDocumentServiceTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/SkinOutputServiceTests.cs`

**Interfaces:**
- New Designer output using `refreshHoldSeconds` requires HUD v1.2.3.
- Old imported packages and drafts normalize the missing value to 1.5.
- A declared minimum version higher than v1.2.3 remains unchanged.

- [ ] **Step 1: Write failing version and normalization tests**

Assert new drafts use 1.5 and v1.2.3, old drafts/packages open with 1.5, saving/exporting writes the field and v1.2.3, and higher compatible minimum versions are preserved.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~SkinDraftFactoryTests|FullyQualifiedName~DesignerDocumentServiceTests|FullyQualifiedName~SkinOutputServiceTests"
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
- Create: `src/CodexQuotaHud.App/Animation/IAnimationDelay.cs`
- Modify: `src/CodexQuotaHud.App/Animation/IOrbAnimationTarget.cs`
- Modify: `src/CodexQuotaHud.App/Animation/OrbAnimationController.cs`
- Modify: `src/CodexQuotaHud.App/Skins/Custom/CustomQuotaSkin.cs`
- Modify: `src/CodexQuotaHud.App/QuotaOrbWindow.xaml.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Animation/FakeAnimationDelay.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Animation/OrbAnimationControllerTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Skins/CustomQuotaSkinTests.cs`

**Interfaces:**
- Produce `IAnimationDelay.Delay(TimeSpan duration, CancellationToken cancellationToken) : Task`.
- Add `TimeSpan RefreshHoldDuration { get; }` to `IOrbAnimationTarget`, with built-in targets returning 1.5 seconds.
- `CustomQuotaSkin` returns `Theme.Animation.RefreshHoldSeconds`.
- Make `OrbAnimationController` disposable and distinguish requested state from effective rendered state.

- [ ] **Step 1: Write failing deterministic state-machine tests**

Using `FakeAnimationDelay`, cover: refresh begins immediately; refresh completion keeps the effective refreshing state for the full duration; zero is immediate; repeated refresh cancels/restarts the timer; old continuations cannot alter a new target; hidden, disabled, detached, switched, and disposed controllers cancel immediately. Assert no real sleeps and no speed-profile properties are changed.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~OrbAnimationControllerTests|FullyQualifiedName~CustomQuotaSkinTests"
```

- [ ] **Step 3: Implement requested/effective state transitions**

On `Refreshing`, cancel the prior delay and apply refreshing immediately. On a request for `Idle` while effectively refreshing, retain refreshing, start the target duration, then apply idle only if the generation, target, enabled state, and requested state are still current. All cancellation paths invalidate the generation before applying their immediate safe state.

- [ ] **Step 4: Wire target duration without changing renderer speed**

Pass the parsed duration through `CustomQuotaSkin`; give built-in targets 1.5 seconds. Leave `AnimatedQuotaSkin` timing values and the custom renderer's current refreshing ratio untouched. Dispose the controller with the HUD window.

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

### Task 4: One Designer duration control and live preview

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/Editing/AnimationEditorViewModel.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Preview/SyntheticPreviewViewModel.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Editing/AnimationEditorViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Preview/SyntheticPreviewViewModelTests.cs`

**Interfaces:**
- Add `AnimationEditorViewModel.SetRefreshHoldSeconds(double value)`.
- Add one `刷新加速延续` slider/numeric control, range 0–3, displaying one decimal and `秒`.
- Do not add a speed or multiplier control.

- [ ] **Step 1: Write failing editing and layout tests**

Assert clamping/validation, draft dirty tracking, `1.5 秒` display, accessible label, one duration control only, and no speed/multiplier label. Verify unchecking `刷新中` enters the shared hold path, while checking it again restarts the preview refresh state.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~AnimationEditorViewModelTests|FullyQualifiedName~MainWindowLayoutTests|FullyQualifiedName~SyntheticPreviewViewModelTests"
```

- [ ] **Step 3: Implement the single control**

Bind it to `RefreshHoldSeconds`, reuse the existing slider/numeric editing pipeline, and update preview state through the production animation controller. Old documents already receive 1.5 from Task 1 and therefore show the control immediately.

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

With a built-in skin, confirm its acceleration remains visibly active for about 1.5 seconds after a fast refresh completes and its prior speed character is unchanged. With a custom skin, test `0`, `1.5`, and `3` seconds; re-refresh during the hold; then disable animation, hide, and switch skins. Confirm each cancellation path is immediate and no timer stacks.

- [ ] **Step 3: Verify old and newly exported skin packages**

Import an old package with no field and confirm 1.5 seconds. Open it in the Designer, change the duration, save/apply/export, inspect canonical JSON for `refreshHoldSeconds`, and confirm the package declares HUD v1.2.3.

- [ ] **Step 4: Build, install, and smoke-test v1.2.3 artifacts**

Use the repository's documented v1.2.2 packaging workflow with version changed to v1.2.3. Verify Setup excludes developer tooling unless selected, ZIP behavior remains documented, installed App/Designer report 1.2.3, startup/formal HUD remains correct, and the Designer dialog plan's acceptance is also complete.

- [ ] **Step 5: Record exact evidence and release**

Update project handoff documents with pass/fail/not-run evidence. Commit only intended files, push `main`, create annotated tag `v1.2.3`, upload verified artifacts to the GitHub release, and confirm the final CI run succeeds. Never stage `tmp/`.

