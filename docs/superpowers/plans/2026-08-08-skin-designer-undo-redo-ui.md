# Skin Designer Undo/Redo UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add discoverable undo/redo buttons and `Ctrl+Z`/`Ctrl+Y` shortcuts to the Skin Designer, with safe parameter-only history that cannot restore deleted image bytes.

**Architecture:** `SkinDraftSession` remains the sole owner of bounded draft history and exposes availability plus an image-mutation history-boundary operation. `DesignerViewModel` owns two shared `AsyncRelayCommand` instances, refreshes their availability through the existing meaningful-change event, and routes successful image mutations to the boundary. `MainWindow.xaml` binds both the compact editor-column toolbar and window-level key gestures to those commands.

**Tech Stack:** .NET 8, C# 12, WPF, xUnit, PowerShell packaging scripts, Inno Setup 6.7.3.

## Global Constraints

- Work only in `C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731`; never use an old conversation worktree.
- Preserve the user-owned untracked `tmp/` directory: do not read, modify, delete, stage, or commit it.
- Version stays `1.3.0`; this is a candidate rebuild, not a new version number.
- Draft schema stays `1`; `storageRelativePath` is optional, legacy three-property drafts remain readable, and `.cqskin` schema/minimum HUD version do not change. A draft saved with the new optional property requires Designer v1.3.0 or newer to reopen.
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

### Task 5: Refresh every visible editor control after history navigation

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/UI/DesignerViewModel.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerViewModelTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`

**Interfaces:**
- Consumes: existing `SkinDraftSession.MeaningfulChange`, `DesignerViewModel.Current`, `SyncImageTransformControls`, and text selection handlers.
- Produces: `DesignerViewModel : INotifyPropertyChanged`, event `PropertyChanged`, and one `PropertyChanged(nameof(Current))` publication per accepted meaningful change.
- Produces: `MainWindow.SyncManualEditorControls()` which refreshes image-transform sliders plus text-weight/text-placement selectors without adding a revision.

- [ ] **Step 1: Add failing view-model and real-window regressions**

In `DesignerViewModelTests.cs`, subscribe to `PropertyChanged`, perform one accepted text-offset edit and one rejected/no-op edit, and assert only the accepted edit publishes `nameof(DesignerViewModel.Current)` exactly once.

In `MainWindowLayoutTests.cs`, add one STA real-window test with literal expectations:

```csharp
var offset = Assert.IsType<Slider>(window.FindName("TextOffsetYSlider"));
var offsetText = Assert.IsType<TextBlock>(window.FindName("TextOffsetYValueText"));
offset.Value = 12;
Assert.Equal(12, window.Editor.Current.Theme.TextOffsetY);
Assert.Equal("+12 DIP", offsetText.Text);
var editedRevision = window.Editor.Current.Revision;

await window.Editor.UndoCommand.ExecuteAsync();
window.UpdateLayout();
Assert.Equal(0, window.Editor.Current.Theme.TextOffsetY);
Assert.Equal(0, offset.Value);
Assert.Equal("0 DIP", offsetText.Text);

await window.Editor.RedoCommand.ExecuteAsync();
window.UpdateLayout();
Assert.Equal(12, offset.Value);
Assert.Equal("+12 DIP", offsetText.Text);
Assert.Equal(editedRevision + 2, window.Editor.Current.Revision);
```

The same test must set a non-default image transform, `SkinTextWeight.Bold`, and `SkinTextPlacement.LabelAboveNumber`, navigate Undo/Redo, and assert the corresponding sliders/combobox indexes exactly match the restored model. Record the revision immediately before manual-control synchronization and prove synchronization itself does not increment it.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DesignerViewModelTests|FullyQualifiedName~MainWindowLayoutTests"
```

Expected: `PropertyChanged` API is missing at compile time and the real-window control assertions fail under the current stale binding/manual-control behavior.

- [ ] **Step 3: Publish `Current` changes from the view model**

Implement `INotifyPropertyChanged` on `DesignerViewModel`:

```csharp
public event PropertyChangedEventHandler? PropertyChanged;
```

At the beginning of `OnMeaningfulChange`, raise exactly:

```csharp
PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
```

Retain the existing image/animation/preview/command notifications. Rejected/no-op edits emit no `MeaningfulChange`, so no property event is manufactured.

- [ ] **Step 4: Synchronize the unbound window controls safely**

Add `_updatingTextControls` and:

```csharp
private void SyncManualEditorControls()
{
    SyncImageTransformControls();
    SyncTextControls();
}

private void SyncTextControls()
{
    _updatingTextControls = true;
    try
    {
        TextWeightBox.SelectedIndex = Editor.Current.Theme.TextWeight switch
        {
            SkinTextWeight.Regular => 0,
            SkinTextWeight.SemiBold => 1,
            SkinTextWeight.Bold => 2,
            _ => -1
        };
        TextPlacementBox.SelectedIndex = Editor.Current.Theme.TextPlacement switch
        {
            SkinTextPlacement.Centered => 0,
            SkinTextPlacement.NumberAboveLabel => 1,
            SkinTextPlacement.LabelAboveNumber => 2,
            _ => -1
        };
    }
    finally
    {
        _updatingTextControls = false;
    }
}
```

Use `SyncManualEditorControls()` after `InitializeComponent()` and before `_editorControlsReady = true`. In `MainWindow.OnMeaningfulChange`, notify recovery and dispatch the same sync method onto the window Dispatcher when required. Both text selection handlers must return while `_updatingTextControls` is true.

- [ ] **Step 5: Run focused GREEN and full Designer GREEN**

Run the Step 2 command, then the full Designer project. Required: zero failed/skipped; visible model, bound controls, manual controls, preview, and history remain synchronized.

- [ ] **Step 6: Commit Task 5**

```powershell
git add -- src/CodexQuotaHud.SkinDesigner/UI/DesignerViewModel.cs src/CodexQuotaHud.SkinDesigner/MainWindow.xaml.cs tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerViewModelTests.cs tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs
git commit -m "fix: refresh designer controls after history changes"
```

---

### Task 6: Backward-compatible immutable draft-asset contract and storage lease

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftDocument.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftJsonCodec.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/SkinDraftValidator.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/Drafts/DraftAssetStorage.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Drafts/IDraftFileOperations.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/DraftJsonCodecTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Drafts/DraftAssetFileOperationsTests.cs`

**Interfaces:**
- Produces: optional fourth constructor parameter `string? StorageRelativePath = null` on `DraftAssetReference`; all three-argument callers remain source compatible.
- Produces: immutable locators `assets/sha256-<64 lowercase hex>.png|jpg`; `RelativePath` retains the canonical package path.
- Produces: `DraftAssetStorage.CreateContentRelativePath`, `ResolveOwnedLeaf`, `IsValidContentRelativePath`, and `MatchesContent`.
- Produces: `IDesignerDraftAssetsLease.MoveOperationToImmutable(string operationLeafName, string contentAddressedLeafName)` using no-replace rename semantics.

- [ ] **Step 1: Add failing JSON/validation tests**

Add literal legacy and addressed fixtures. Required canonical addressed order is:

```json
{"slot":"center","relativePath":"assets/center.png","storageRelativePath":"assets/sha256-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png","originalFileName":"center.png"}
```

Prove legacy three-property schema-1 JSON round-trips byte-canonically with `StorageRelativePath == null`; addressed JSON write/parse/write is canonical; unknown/duplicate properties still fail; and invalid values fail with code `draft.asset.storage-path.invalid` at `$.assets[n].storageRelativePath`. Invalid matrix: uppercase hash, 63/65 hex chars, wrong prefix, slash traversal, backslash, absolute path, `.jpeg`, unsupported extension, and extension different from `RelativePath`.

- [ ] **Step 2: Add failing storage-lease tests**

Test that an operation leaf can promote once to `sha256-<hash>.png`; a second no-replace promotion to the same leaf fails without changing existing bytes; fixed canonical leaves still work; traversal/reparse/invalid content leaves fail before mutation.

- [ ] **Step 3: Run focused RED**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DraftJsonCodecTests|FullyQualifiedName~DraftAssetFileOperationsTests"
```

Expected: missing fourth property/helper/lease API and addressed-path validation failures.

- [ ] **Step 4: Implement the optional contract and strict codec**

Change the record to:

```csharp
public sealed record DraftAssetReference(
    SkinAssetSlot Slot,
    string RelativePath,
    string OriginalFileName,
    string? StorageRelativePath = null);
```

Keep draft schema version `1`. Writer omits the property when null; otherwise writes it between `relativePath` and `originalFileName`. Replace the asset strict-object check with one that requires the three legacy properties and permits the optional property exactly once. Reader treats absence as null and present non-string/null as invalid.

- [ ] **Step 5: Implement content-address helpers and validation**

`CreateContentRelativePath` hashes exact encoded bytes with SHA-256 and returns lowercase hex under `assets/`; it derives `.png` or `.jpg` from the already validated canonical package path. `ResolveOwnedLeaf` returns `Path.GetFileName(StorageRelativePath ?? RelativePath)`. Validation must enforce the exact locator grammar and extension match. `MatchesContent` recomputes exact SHA-256 and compares ordinal lowercase hex.

- [ ] **Step 6: Add no-replace immutable promotion**

Extend the lease and native rename plumbing with `replaceIfExists`. Existing canonical/JSON promotions keep their current replacement semantics. `MoveOperationToImmutable` validates both leaves and calls rename with replacement disabled; it never deletes an existing blob.

- [ ] **Step 7: Run focused GREEN and commit**

Run the Step 3 command, then full Designer tests. Commit only Task 6 files:

```powershell
git commit -m "feat: add immutable draft asset references"
```

---

### Task 7: Append-only image mutations and exact document resolution

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/Images/DesignerImageService.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Documents/DesignerDocumentService.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Images/DesignerImageServiceTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Images/DesignerImageCommitterIntegrationTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Documents/DesignerDocumentServiceTests.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/Documents/DraftAssetPersistenceIntegrationTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/DraftPackageBuilderTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Preview/DraftPreviewDocumentBuilderTests.cs`

**Interfaces:**
- Consumes: Task 6 optional storage locator, content helper, and no-replace promotion.
- Guarantees: import writes/reuses immutable bytes then commits the reference; remove changes only session reference/assets; neither deletes a named/recovery-referenced file.
- Guarantees: addressed document load verifies locator hash; legacy null locator resolves the canonical fixed leaf.

- [ ] **Step 1: Replace unsafe mutable-slot expectations with failing append-only tests**

Required behaviors:

- replacing a slot retains exact old bytes and creates/reuses the exact addressed blob;
- an existing addressed blob is reused only when its bytes match its hash;
- mismatched existing blob fails closed without session mutation;
- rejected/cancelled import preserves prior reference/history and never deletes prior blobs;
- remove commits reference removal, starts the existing history boundary, and retains physical bytes;
- direct removal-after-undo asserts both history directions clear, addressing the Task 2 deferred minor.

- [ ] **Step 2: Add failing document conversion/resolution tests**

Test legacy null locators, different named/recovery addressed locators, package/edit-installed conversion to addressed leaves, missing blob, and hash mismatch code `document.asset-hash-mismatch` at the asset location. Assert returned `SkinAsset.RelativePath` is still canonical, never the storage locator.

- [ ] **Step 3: Add failing cross-component persistence and package-path tests**

Using real unique storage roots, test exact byte hashes for named replacement +
Discard, named removal + Discard, replacement + Save, replacement recovery
reopen, named-save failure, and a crash before recovery flush. Each case must
reopen through the real `DesignerDocumentService`, not just inspect file
existence. Add builder/preview cases whose canonical `RelativePath` differs
from `StorageRelativePath`; assert `.cqskin` declarations/archive entries and
runtime preview use only the canonical package path and never expose the
Designer-only locator.

- [ ] **Step 4: Run focused RED**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~DesignerImageServiceTests|FullyQualifiedName~DesignerImageCommitterIntegrationTests|FullyQualifiedName~DesignerDocumentServiceTests|FullyQualifiedName~DraftAssetPersistenceIntegrationTests|FullyQualifiedName~DraftPackageBuilderTests|FullyQualifiedName~DraftPreviewDocumentBuilderTests"
```

Expected: current import deletes/replaces canonical files, remove deletes bytes, and document loading ignores addressed locators.

- [ ] **Step 5: Rewrite import as append-only promotion**

After existing decode/pixel validation, compute the package path and `StorageRelativePath`. If the blob exists, read and require exact hash/content match. Otherwise write/flush/read/decode an operation leaf and promote with `MoveOperationToImmutable`; on a same-content race, re-read the winner and require exact match. Commit a four-property reference. If session rejects or cancellation arrives after promotion, delete only remaining operation-temporary files; retain the unreferenced immutable blob.

- [ ] **Step 6: Rewrite removal as reference-only mutation**

`RemoveAsync` retains draft-id/slot/cancellation validation and calls `CommitRemove(slot)` without quarantining/deleting any canonical or addressed asset. Delete old mutable-slot tomb/rollback helpers only after all callers/tests move to append-only behavior.

- [ ] **Step 7: Convert/import and load exact storage bytes**

Package/edit-installed project creation writes each decoded package asset as an immutable blob and stores its locator. Loading uses `DraftAssetStorage.ResolveOwnedLeaf(reference)`; null locators use the legacy leaf. For addressed blobs, verify exact hash before decoding. Keep package `RelativePath` on returned `SkinAsset` and all validation limits.

- [ ] **Step 8: Run focused/full GREEN and commit**

Run Step 4, then full Designer suite. Commit all Task 7 files:

```powershell
git commit -m "fix: preserve named draft image bytes"
```

---

### Task 8: Fixed-candidate verification, package replacement, installed acceptance, and handoff

**Files:**
- Modify only the same six Task 4 handoff documents.
- Generate but do not commit the same three v1.3.0 release assets.

**Interfaces:**
- Consumes: Tasks 5–7 and the exact Task 4 evidence method.
- Produces: a new source-identified v1.3.0 local candidate; Task 4 hashes/source identity become historical failed-candidate evidence.

- [ ] **Step 1: Run fresh serial source gates**

Run Core, Skins, App, Designer separately in Release, zero skipped; build solution with zero warnings/errors; `git diff --check` clean.

- [ ] **Step 2: Rebuild v1.3.0 assets and rerun installer matrix 9/9**

Use the exact Task 4 packaging/matrix commands. Record new full hashes/sizes/versions; never retain Task 4 failed-candidate hashes as current.

- [ ] **Step 3: Upgrade real installation with bounded backup/restore**

Preserve exact user settings, all drafts/assets/recovery, installed skins, exchange packages, startup, and maintainer preview shortcut. Verify installed App/Designer match new publish payload and source commit.

- [ ] **Step 4: Re-run every installed row, including both prior blockers**

Required direct outcomes:

- Undo/Redo buttons and `Ctrl+Z`/`Ctrl+Y` restore model, preview, all visible bound/manual controls, and correct history availability;
- new edit clears redo; Save/reopen persists;
- replace/remove then Discard reopens exact old JSON and image bytes with no `document.asset-missing`;
- replace then Save reopens exact new bytes;
- picker cancellation preserves history;
- six auditions remain distinct;
- Apply dialog identity and actual HUD switch pass;
- untouched v1.2.3 import completes and displays effective offset/gap `0/0`;
- tray menu actions run on the exact installed formal HUD;
- close Designer while guides are On and one non-All audition is active, then prove the formal HUD remains free of both states.

Any failed/partial/not-run required row keeps the candidate not release-ready. Restore user state exactly after testing.

- [ ] **Step 5: Update six handoff docs and commit evidence only**

Record fresh source/tests/hashes/matrix/install/UI/restoration evidence. Keep unsigned status explicit. No push/main/tag/release until user accepts the installed fixed candidate.

---

## Plan self-review record

- Spec coverage: Tasks 1–3 cover history and UI discovery. Task 4 records the failed installed candidate. Tasks 5–7 cover visible-control synchronization and immutable named/recovery image ownership. Task 8 repeats all release/install/UI gates on the fixed source.
- Completeness scan: every production edit has a named file, exact interface, RED command, GREEN command, and observable acceptance result.
- Type consistency: Task 1 produces history APIs; Task 2 produces commands; Task 3 binds them; Task 5 refreshes consumers; Task 6 produces `StorageRelativePath` and immutable lease APIs; Task 7 consumes those exact names; Task 8 tests the installed result.
- Safety check: no step reads or mutates `tmp/`; no remote action is authorized; image bytes are not placed in history; immutable named/recovery references prevent Discard from deleting or silently replacing saved bytes; no eager garbage collector is introduced.
