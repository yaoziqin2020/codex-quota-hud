# Skin Designer Undo/Redo UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add discoverable undo/redo buttons and `Ctrl+Z`/`Ctrl+Y` shortcuts to the Skin Designer, with safe parameter-only history that cannot restore deleted image bytes.

**Architecture:** `SkinDraftSession` remains the sole owner of bounded draft history and exposes availability plus an image-mutation history-boundary operation. `DesignerViewModel` owns two shared `AsyncRelayCommand` instances, refreshes their availability through the existing meaningful-change event, and routes successful image mutations to the boundary. `MainWindow.xaml` binds both the compact editor-column toolbar and window-level key gestures to those commands.

**Tech Stack:** .NET 8, C# 12, WPF, xUnit, PowerShell packaging scripts, Inno Setup 6.7.3.

## Global Constraints

- Work only in `C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731`; never use an old conversation worktree.
- Preserve the user-owned untracked `tmp/` directory: do not read, modify, delete, stage, or commit it.
- Version stays `1.3.0`; this is a candidate rebuild, not a new version number.
- Undo/redo covers parameter and metadata snapshots only: text layout, colors, quota-ring settings, animation settings, and descriptive fields.
- A successful image replacement or removal starts a new history segment; undo/redo cannot cross that image operation.
- A cancelled, failed, rejected, or no-op image operation must not clear existing undo/redo history.
- Starting a new image history segment must not move the named-save baseline; the image-mutated draft remains dirty until named save.
- History remains bounded to 100 snapshots and a new edit after undo truncates the redo branch.
- Toolbar controls are named `UndoDraftButton` and `RedoDraftButton`; labels are `撤销` and `重做`; automation names are `撤销草稿编辑 (Ctrl+Z)` and `重做草稿编辑 (Ctrl+Y)`.
- `Ctrl+Z` and `Ctrl+Y` bind to the same command objects as the buttons; no code-behind shortcut path or global keyboard hook is allowed.
- Put the compact `编辑历史` toolbar at the top of the editor column, not in the existing four-button document toolbar.
- Keep the current Designer dark style, 600-DIP minimum-width usability, contiguous unique keyboard tab indexes, and all existing Chinese labels.
- Use strict TDD: add behavior tests first, run them and record the expected RED caused by missing production behavior, then write the minimum production code and rerun GREEN.
- Do not weaken existing tests, add source-text-only assertions, or assert only on mocks; each new test must name and exercise the production break it catches.
- Do not change installer component semantics: Setup remains ordinary-user App with optional Designer; ZIP remains the command-line fallback.
- Do not push, merge `main`, tag, create a GitHub Release, or publish remote assets before explicit user acceptance of the newly installed candidate.

---

### Task 1: Safe draft-history boundaries

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftHistory.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftSession.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/DraftHistoryTests.cs`

**Interfaces:**
- Consumes: existing `DraftHistory.Push`, `Undo`, `Redo`, `DraftSnapshot.Clone`, and `SkinDraftSession.ApplyCore` revision/timestamp logic.
- Produces: `DraftHistory.Reset(SkinDraftDocument state)`, public `SkinDraftSession.CanUndo`, public `SkinDraftSession.CanRedo`, and internal `SkinDraftSession.ApplyAsHistoryBoundary(Func<SkinDraftDocument, SkinDraftDocument> edit)`.
- Guarantees: `ApplyAsHistoryBoundary` publishes exactly one meaningful change, retains the named-save baseline, and leaves a one-state history whose current state is the accepted image mutation.

- [ ] **Step 1: Add failing session availability and boundary tests**

Add these behavior tests to `DraftHistoryTests.cs` using the existing `Draft(...)`, `CreatedAt`, and monotonic-time fixture style:

```csharp
[Fact]
public void SessionAvailability_TracksEditUndoRedoAndBranchedEdit()
{
    var next = CreatedAt;
    var session = new SkinDraftSession(
        Draft(0),
        () => next = next.AddSeconds(1));
    Assert.False(session.CanUndo);
    Assert.False(session.CanRedo);

    Assert.True(session.Apply(current => current with { DisplayName = "One" }));
    Assert.True(session.CanUndo);
    Assert.False(session.CanRedo);

    Assert.True(session.TryUndo());
    Assert.False(session.CanUndo);
    Assert.True(session.CanRedo);

    Assert.True(session.Apply(current => current with { DisplayName = "Branch" }));
    Assert.True(session.CanUndo);
    Assert.False(session.CanRedo);
}

[Fact]
public void ImageBoundary_KeepsAcceptedDraftDirtyAndClearsBothDirections()
{
    var next = CreatedAt;
    var session = new SkinDraftSession(
        Draft(0),
        () => next = next.AddSeconds(1));
    Assert.True(session.Apply(current => current with { DisplayName = "Before image" }));
    Assert.True(session.TryUndo());
    Assert.True(session.CanRedo);
    var events = 0;
    session.MeaningfulChange += (_, _) => events++;

    Assert.True(session.ApplyAsHistoryBoundary(current => current with
    {
        Assets = ReadOnly(new Dictionary<SkinAssetSlot, DraftAssetReference>
        {
            [SkinAssetSlot.Center] = new(
                SkinAssetSlot.Center,
                "assets/center.png",
                "center.png")
        })
    }));

    Assert.False(session.CanUndo);
    Assert.False(session.CanRedo);
    Assert.False(session.TryUndo());
    Assert.True(session.HasUnsavedChanges);
    Assert.True(session.Current.Assets.ContainsKey(SkinAssetSlot.Center));
    Assert.Equal(1, events);
}
```

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DraftHistoryTests"
```

Expected: compilation fails only because `CanUndo`, `CanRedo`, and `ApplyAsHistoryBoundary` do not yet exist. Correct test typos if the failure is unrelated; do not add production code until this is the observed RED.

- [ ] **Step 3: Implement the minimal reset and session boundary**

Add to `DraftHistory`:

```csharp
public void Reset(SkinDraftDocument state)
{
    ArgumentNullException.ThrowIfNull(state);
    _states.Clear();
    _states.Add(DraftSnapshot.Clone(state));
    _index = 0;
}
```

Expose availability and add the boundary to `SkinDraftSession`:

```csharp
public bool CanUndo => _history.CanUndo;

public bool CanRedo => _history.CanRedo;

internal bool ApplyAsHistoryBoundary(
    Func<SkinDraftDocument, SkinDraftDocument> edit) =>
    ApplyCore(
        edit,
        requireStructuralChange: true,
        startsNewHistorySegment: true);
```

Extend `ApplyCore` with `bool startsNewHistorySegment = false`. After producing the accepted revision/timestamp, use exactly one of these paths before assigning `_current` and raising `MeaningfulChange`:

```csharp
if (startsNewHistorySegment)
{
    _history.Reset(accepted);
}
else if (!_history.Push(accepted))
{
    return false;
}
```

Do not alter `_namedSavedBaseline` inside this method.

- [ ] **Step 4: Run focused GREEN and the entire draft-history file**

Run the Step 2 command. Expected: all `DraftHistoryTests` pass, with zero failed and zero skipped.

- [ ] **Step 5: Self-review mutation coverage**

Confirm the new tests fail if any of these regressions are introduced: boundary calls `Push` instead of `Reset`; boundary updates `_namedSavedBaseline`; boundary omits `MeaningfulChange`; `CanRedo` is accidentally delegated to `CanUndo`.

- [ ] **Step 6: Commit Task 1**

```powershell
git add -- src/CodexQuotaHud.SkinDesigner/Drafts/DraftHistory.cs src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftSession.cs tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/DraftHistoryTests.cs
git commit -m "feat: add safe designer history boundaries"
```

---

### Task 2: Undo/redo commands and image-boundary integration

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/DesignerViewModel.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Images/DesignerImageCommitterIntegrationTests.cs`

**Interfaces:**
- Consumes: Task 1 `SkinDraftSession.CanUndo`, `CanRedo`, `TryUndo`, `TryRedo`, and `ApplyAsHistoryBoundary`; existing `AsyncRelayCommand`; existing `MeaningfulChange`; `IDesignerImageMutationCommitter` transactional rollback.
- Produces: public `DesignerViewModel.UndoCommand` and `DesignerViewModel.RedoCommand`, both `AsyncRelayCommand`.
- Produces: successful `TryCommit`/`TryRemove` use the Task 1 boundary; failure/cancellation paths never reach it and retain previous history.

- [ ] **Step 1: Add failing command behavior tests**

Add tests to `DesignerViewModelTests.cs` that exercise the real session and view model, not a command mock:

```csharp
[Fact]
public async Task HistoryCommands_RestoreExactDraftAndPreviewAndBranchRedo()
{
    using var sut = CreateViewModel(out var session, out var previewed);
    Assert.False(sut.UndoCommand.CanExecute(null));
    Assert.False(sut.RedoCommand.CanExecute(null));

    Assert.True(sut.Text.SetTextOffsetY(12).Succeeded);
    Assert.True(sut.UndoCommand.CanExecute(null));
    await sut.UndoCommand.ExecuteAsync();
    Assert.Equal(0, session.Current.Theme.TextOffsetY);
    Assert.Equal(0, previewed[^1].Theme.TextOffsetY);
    Assert.False(sut.UndoCommand.CanExecute(null));
    Assert.True(sut.RedoCommand.CanExecute(null));

    await sut.RedoCommand.ExecuteAsync();
    Assert.Equal(12, session.Current.Theme.TextOffsetY);
    Assert.Equal(12, previewed[^1].Theme.TextOffsetY);

    await sut.UndoCommand.ExecuteAsync();
    Assert.True(sut.Text.SetTextLineGap(6).Succeeded);
    Assert.False(sut.RedoCommand.CanExecute(null));
}
```

Use the existing `TextEditorViewModel.SetTextOffsetY(double)` and
`TextEditorViewModel.SetTextLineGap(double)` methods exactly as shown, preserving
the literal values `12` and `6` and the observable assertions.

Add a second test proving command disposal makes both commands non-executable and does not throw during a later session event.

- [ ] **Step 2: Add failing image-boundary integration tests**

Extend `DesignerImageCommitterIntegrationTests.cs` with real `SkinDraftSession` + `DesignerViewModel` cases:

1. Create an ordinary parameter edit and undo so redo exists; successfully import/commit a valid image; assert `session.CanUndo == false`, `session.CanRedo == false`, asset/reference are current, and `HasUnsavedChanges == true`.
2. Create ordinary history, then make the image commit delegate reject or throw through the existing integration seam; assert the image service rolls back bytes/assets and the session's previous `CanUndo`/`CanRedo` values remain unchanged.
3. Cancel before commit with a cancelled token or picker cancellation at the existing service boundary; assert history remains unchanged.

- [ ] **Step 3: Run focused command and image tests and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DesignerViewModelTests|FullyQualifiedName~DesignerImageCommitterIntegrationTests"
```

Expected: command tests fail to compile because `UndoCommand`/`RedoCommand` do not exist, and the successful-image test fails because production still calls `ApplyMeaningful` rather than the history boundary.

- [ ] **Step 4: Implement shared commands and notifications**

In `DesignerViewModel` constructor, create the commands before subscribing to the session event:

```csharp
UndoCommand = new AsyncRelayCommand(
    _ =>
    {
        _session.TryUndo();
        return Task.CompletedTask;
    },
    () => _session.CanUndo);
RedoCommand = new AsyncRelayCommand(
    _ =>
    {
        _session.TryRedo();
        return Task.CompletedTask;
    },
    () => _session.CanRedo);
```

Expose them as public read-only properties. At the end of `OnMeaningfulChange`, call:

```csharp
UndoCommand.NotifyCanExecuteChanged();
RedoCommand.NotifyCanExecuteChanged();
```

In `Dispose`, unsubscribe first and dispose both history commands along with the existing owned resources. Preserve idempotent disposal.

- [ ] **Step 5: Route successful image mutations to the boundary**

Rename the private delegate to `_imageMutationCommit` for clarity and set its production default to `_session.ApplyAsHistoryBoundary`. Preserve the internal constructor injection so rejection/throw rollback tests remain possible. Both explicit-interface methods `TryCommit` and `TryRemove` must call the same delegate. A no-op removal that finds neither asset nor reference continues to return `true` without clearing history.

- [ ] **Step 6: Run focused GREEN**

Run the Step 3 command. Expected: all selected tests pass, zero failed/skipped.

- [ ] **Step 7: Run the full Designer unit suite**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore
```

Expected: all Designer tests pass with no regression in transactional image rollback, preview updates, recovery events, or disposal.

- [ ] **Step 8: Commit Task 2**

```powershell
git add -- src/CodexQuotaHud.SkinDesigner/UI/DesignerViewModel.cs tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerViewModelTests.cs tests/CodexQuotaHud.SkinDesigner.Tests/Images/DesignerImageCommitterIntegrationTests.cs
git commit -m "feat: expose designer undo redo commands"
```

---

### Task 3: Discoverable WPF controls and keyboard gestures

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`

**Interfaces:**
- Consumes: Task 2 `Editor.UndoCommand` and `Editor.RedoCommand`.
- Produces: named buttons `UndoDraftButton`, `RedoDraftButton`; window `KeyBinding`s for `Ctrl+Z`, `Ctrl+Y`; contiguous tab sequence `1..68`.

- [ ] **Step 1: Add failing real-window binding and layout tests**

Add a `RunSta` test to `MainWindowLayoutTests.cs` that constructs the real window and asserts:

```csharp
var undo = Assert.IsType<Button>(window.FindName("UndoDraftButton"));
var redo = Assert.IsType<Button>(window.FindName("RedoDraftButton"));
Assert.Equal("撤销", undo.Content);
Assert.Equal("重做", redo.Content);
Assert.Same(window.Editor.UndoCommand, undo.Command);
Assert.Same(window.Editor.RedoCommand, redo.Command);
Assert.Equal("撤销草稿编辑 (Ctrl+Z)", AutomationProperties.GetName(undo));
Assert.Equal("重做草稿编辑 (Ctrl+Y)", AutomationProperties.GetName(redo));
```

From `window.InputBindings.OfType<KeyBinding>()`, locate exact `Control+Z` and `Control+Y`, assert each command is reference-equal to the matching button command, and assert no duplicate binding exists for either gesture.

Set the real window to `Width = 600`, `Height = 720`, call `UpdateLayout`, find `EditHistoryToolbar`, and use the existing `AssertFullyRenderedWithin` helper to prove both buttons fit the editor scroll viewport.

Update the existing accessibility test to expect `Enumerable.Range(1, 68)`. Assert the two new controls occupy indexes 5 and 6, then update the existing positional expectations by +2 only where they occur after the new controls.

- [ ] **Step 2: Run focused layout tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~MainWindowLayoutTests"
```

Expected: the new real-window test fails because the named buttons/key bindings do not exist, and the accessibility test reports 66 rather than 68 controls.

- [ ] **Step 3: Add the window key bindings**

Insert directly below the `<Window ...>` opening element and before `<Window.Resources>`:

```xml
<Window.InputBindings>
    <KeyBinding Key="Z" Modifiers="Control"
                Command="{Binding Editor.UndoCommand}" />
    <KeyBinding Key="Y" Modifiers="Control"
                Command="{Binding Editor.RedoCommand}" />
</Window.InputBindings>
```

- [ ] **Step 4: Add the compact editor-column toolbar**

Inside the editor `StackPanel`, before `BasicInformationSection`, add a compact `Border` named `EditHistoryToolbar` using the existing surface/border brushes. Use one row with `编辑历史` on the left and the two buttons on the right:

```xml
<Border x:Name="EditHistoryToolbar"
        Margin="0,0,0,10" Padding="10,8"
        Background="{DynamicResource DesignerSurfaceBrush}"
        BorderBrush="{DynamicResource DesignerBorderBrush}"
        BorderThickness="1" CornerRadius="6">
    <Grid>
        <TextBlock VerticalAlignment="Center" FontWeight="SemiBold"
                   Text="编辑历史" />
        <StackPanel HorizontalAlignment="Right" Orientation="Horizontal">
            <Button x:Name="UndoDraftButton" Content="撤销"
                    MinWidth="64" MinHeight="30" Padding="10,4"
                    Command="{Binding Editor.UndoCommand}"
                    AutomationProperties.Name="撤销草稿编辑 (Ctrl+Z)"
                    KeyboardNavigation.TabIndex="5" />
            <Button x:Name="RedoDraftButton" Content="重做"
                    MinWidth="64" MinHeight="30" Padding="10,4"
                    Margin="6,0,0,0"
                    Command="{Binding Editor.RedoCommand}"
                    AutomationProperties.Name="重做草稿编辑 (Ctrl+Y)"
                    KeyboardNavigation.TabIndex="6" />
        </StackPanel>
    </Grid>
</Border>
```

Increment every existing `KeyboardNavigation.TabIndex` from `5..66` to `7..68` without changing control order, names, labels, bindings, or layout values.

- [ ] **Step 5: Run focused GREEN and the full Designer suite**

Run the Step 2 command, then:

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore
```

Expected: focused and full Designer suites pass, zero failed/skipped; the real 600-DIP window renders the toolbar completely and tab indexes are exactly `1..68`.

- [ ] **Step 6: Commit Task 3**

```powershell
git add -- src/CodexQuotaHud.SkinDesigner/MainWindow.xaml tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs
git commit -m "feat: add designer undo redo controls"
```

---

### Task 4: Full verification, v1.3.0 candidate rebuild, installed smoke, and handoff evidence

**Files:**
- Modify: `README.md`
- Modify: `CURRENT_TASK.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `CHANGELOG_AI.md`
- Modify: `docs/releases/v1.3.0.md`
- Modify: `docs/verification/2026-08-05-skin-designer-authoring-upgrade-acceptance.md`
- Generate but do not commit: `artifacts/release/CodexQuotaHud-Setup-v1.3.0.exe`
- Generate but do not commit: `artifacts/release/CodexQuotaHud-v1.3.0-win-x64.zip`
- Generate but do not commit: `artifacts/release/SHA256SUMS.txt`

**Interfaces:**
- Consumes: Tasks 1–3 production behavior, existing serial test gates, `scripts/package-release.ps1`, `scripts/test-installer.ps1`, existing v1.3.0 installed-state and UI acceptance method.
- Produces: a locally installed v1.3.0 candidate containing undo/redo, updated exact hashes/sizes and honest installed-smoke evidence; no remote publication.

- [ ] **Step 1: Run the fresh serial Release gates**

Run each command separately and record exact counts, zero skipped, exact command, and Asia/Tokyo timestamp:

```powershell
dotnet test .\tests\CodexQuotaHud.Core.Tests\CodexQuotaHud.Core.Tests.csproj -c Release --no-restore
dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

Required outcome: every test passes with zero skipped, build reports zero warnings/errors, and `git diff --check` emits no output.

- [ ] **Step 2: Rebuild all v1.3.0 candidate assets from the current source**

```powershell
.\scripts\package-release.ps1 -Version 1.3.0
```

Require non-empty Setup, ZIP, and exactly matching `SHA256SUMS.txt`. Record byte sizes, SHA-256, file/product/informational versions, ZIP manifest entries, and Authenticode status. Replace prior hashes in all six owned documents; do not describe older hashes as current.

- [ ] **Step 3: Rerun the exact isolated nine-scenario installer matrix**

```powershell
.\scripts\test-installer.ps1 -Version 1.3.0 -InstallerPath .\artifacts\release\CodexQuotaHud-Setup-v1.3.0.exe
```

Require all nine scenarios to pass: `fresh-default`, `fresh-designer`, `add-designer`, `remove-designer`, `upgrade-selected`, `uninstall-preserve`, `uninstall-purge`, `cleanup-legacy-failure`, and `cleanup-designer-failure`. Validate and remove only explicit matrix roots through the script's existing safety boundaries; never touch the production install or user data during this step.

- [ ] **Step 4: Upgrade the real local candidate while preserving state**

Take a bounded pre-install snapshot of App/Designer versions and hashes, startup entry, selected skin, settings, installed custom skins, Designer drafts/recovery, exchange packages, shortcuts, and running processes. Run Setup with Designer selected and startup selected while deliberately omitting the normal desktop-icon task. After exit `0`, prove:

- installed App and Designer hashes equal the current publish payload;
- both report `1.3.0.0` and the current source informational commit;
- startup remains exact `--background`;
- settings and user content are preserved except an allowed refresh timestamp advance;
- the formal Setup does not create the maintainer-only preview shortcut;
- if the existing maintainer preview shortcut was present before installation, restore that exact standard-App `--preview` shortcut separately and label it as local maintainer behavior, not Setup behavior.

- [ ] **Step 5: Directly exercise the installed undo/redo flow**

Launch the exact installed Designer path and revalidate PID, executable path, title, `Responding`, and window handle before each automated input phase. Use an animation-complete existing skin or draft and record direct observations for:

1. change `文字整体偏移` to a distinct value, click `撤销`, verify control and live preview return exactly;
2. click `重做`, verify both return to the edited value;
3. make a new edit after undo, verify `重做` disables;
4. verify `Ctrl+Z` and `Ctrl+Y` produce the same observable results as the buttons;
5. save, close, reopen, and verify the final text offset/line gap persist;
6. replace or remove one image successfully, verify both history controls disable and the draft remains dirty; cancel a later image picker and verify it does not alter current history availability.

If the Designer was closed by the user, record it as user action and relaunch; do not classify it as a crash. If any identity check fails, stop input and record that row `NOT RUN` instead of guessing.

- [ ] **Step 6: Finish the remaining installed-smoke gates ourselves**

Using the exact installed binaries and safe UI control path, directly record separate PASS/PARTIAL/NOT RUN rows for:

- six animation auditions on a skin with nonzero rotation, breathing, glow, floating, and refresh settings;
- Apply-to-HUD result dialog fields (skin name, version, skin ID) and actual formal-HUD switch;
- import of an untouched v1.2.3 package and effective text offset/line gap `0/0`;
- formal HUD tray menu actions;
- closing Designer leaves formal HUD without composition guides or a single-audition override.

Restore the user's selected skin and all preview-only state afterward. Source automated compatibility/isolation tests are supporting evidence, not substitutes for these installed observations.

- [ ] **Step 7: Update handoff documents truthfully**

Update the six owned documents with current source commit, package hashes/sizes, matrix result, installed versions/hashes, exact manual rows, restored-state result, unsigned status, and remaining user-only visual judgment if any. Keep overall state `PARTIAL` if any required installed row is PARTIAL/NOT RUN. Do not claim release-ready while an explicit gate remains open.

- [ ] **Step 8: Commit Task 4 documentation only**

```powershell
git add -- README.md CURRENT_TASK.md PROJECT_CONTEXT.md CHANGELOG_AI.md docs/releases/v1.3.0.md docs/verification/2026-08-05-skin-designer-authoring-upgrade-acceptance.md
git diff --cached --check
git commit -m "docs: record undo redo candidate acceptance"
```

Confirm `git status --short` contains no tracked change and only the pre-existing user-owned `tmp/` remains untracked. Do not stage release binaries or ignored acceptance artifacts.

---

## Plan self-review record

- Spec coverage: Tasks 1–3 cover availability, parameter/metadata undo, redo branching, image history boundaries, dirty baseline, shared commands, disposal, compact toolbar, accessibility, shortcuts, and minimum-width layout. Task 4 covers serial verification, candidate replacement, isolated installer matrix, real upgrade, direct installed checks, state restoration, and honest handoff.
- Completeness scan: every production edit has a named file, exact interface, RED command, GREEN command, and observable acceptance result.
- Type consistency: Task 1 produces `CanUndo`, `CanRedo`, and `ApplyAsHistoryBoundary`; Task 2 consumes those exact members and produces `UndoCommand`/`RedoCommand`; Task 3 binds those exact command properties; Task 4 tests the same user-visible operations.
- Safety check: no step reads or mutates `tmp/`; no remote action is authorized; image bytes are not placed in history; successful image mutations cannot be undone into a missing file reference.
