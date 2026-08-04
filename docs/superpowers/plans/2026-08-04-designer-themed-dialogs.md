# Designer Themed Dialogs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Keep every Skin Designer control visually consistent during modal operations and replace Designer-owned native message boxes with accessible dark-themed WPF dialogs.

**Architecture:** Move the Designer palette and control styles into one application-level resource dictionary shared by the main window and modal dialogs. Add one typed dialog service and reusable modal window; existing document/output adapters map domain choices to stable dialog action IDs. Native Windows file pickers remain unchanged.

**Tech Stack:** .NET 8, WPF XAML, xUnit, STA WPF tests

## Global Constraints

- Target release: v1.2.3.
- File-open and file-save pickers remain native Windows dialogs.
- No Designer-owned path may call `System.Windows.MessageBox.Show` afterward.
- Disabled owner controls remain dark while any modal dialog is open.
- Preserve owner selection, Dispatcher affinity, Enter/Escape behavior, button semantics, and accessible names.
- Do not modify HUD dialogs or unrelated application styling.

---

### Task 1: Shared Designer theme and complete button template

**Files:**
- Create: `src/CodexQuotaHud.SkinDesigner/UI/DesignerTheme.xaml`
- Modify: `src/CodexQuotaHud.SkinDesigner/App.xaml`
- Modify: `src/CodexQuotaHud.SkinDesigner/MainWindow.xaml`
- Test: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs`
- Test: `tests/CodexQuotaHud.SkinDesigner.Tests/ProjectBoundaryTests.cs`

**Interfaces:**
- Produces application resources `DesignerBackgroundBrush`, `DesignerSurfaceBrush`, `DesignerRaisedBrush`, `DesignerTextBrush`, `DesignerMutedTextBrush`, `DesignerBorderBrush`, `DesignerAccentBrush`, `DesignerAccentTextBrush`, and `DesignerFocusVisualStyle`.
- Produces one implicit `Button` style with a Designer-owned `ControlTemplate` consumed by the main window and Task 2 dialogs.

- [ ] **Step 1: Write failing template and disabled-owner tests**

Add STA assertions that a main-window button resolves a custom template. Set `window.IsEnabled = false`, update layout, and assert the template root still uses the dark raised brush with reduced opacity. Add a source-boundary test requiring the palette and implicit button template in `UI/DesignerTheme.xaml`, not `MainWindow.xaml`.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~MainWindowLayoutTests|FullyQualifiedName~ProjectBoundaryTests"
```

Expected: FAIL because buttons use the platform template and resources are window-local.

- [ ] **Step 3: Implement the shared dictionary and visual states**

Merge the dictionary in `App.xaml`:

```xml
<ResourceDictionary.MergedDictionaries>
    <ResourceDictionary Source="UI/DesignerTheme.xaml" />
</ResourceDictionary.MergedDictionaries>
```

The button template binds foreground/background/border/padding and defines normal, hover, pressed, focused/default, and disabled triggers. The disabled trigger keeps dark brushes and applies `Opacity="0.55"`; it must not delegate to a Windows theme template.

- [ ] **Step 4: Run focused tests and verify pass**

Run the Step 2 command. Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.SkinDesigner/App.xaml src/CodexQuotaHud.SkinDesigner/MainWindow.xaml src/CodexQuotaHud.SkinDesigner/UI/DesignerTheme.xaml tests/CodexQuotaHud.SkinDesigner.Tests/UI/MainWindowLayoutTests.cs tests/CodexQuotaHud.SkinDesigner.Tests/ProjectBoundaryTests.cs
git commit -m "fix: preserve designer button theme during dialogs"
```

### Task 2: Typed themed dialog primitive

**Files:**
- Create: `src/CodexQuotaHud.SkinDesigner/UI/Dialogs/DesignerDialogModels.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/Dialogs/IDesignerDialogService.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/Dialogs/DesignerDialogWindow.xaml`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/Dialogs/DesignerDialogWindow.xaml.cs`
- Create: `src/CodexQuotaHud.SkinDesigner/UI/Dialogs/DesignerDialogService.cs`
- Create: `tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerDialogWindowTests.cs`

**Interfaces:**
- Produces `DesignerDialogIcon { Information, Warning, Error, Question }`.
- Produces `DesignerDialogAction(string Id, string Label, bool IsDefault = false, bool IsCancel = false)`.
- Produces `DesignerDialogRequest(string Title, string Message, DesignerDialogIcon Icon, IReadOnlyList<DesignerDialogAction> Actions)`.
- Produces `IDesignerDialogService.Show(Window? owner, DesignerDialogRequest request) : string`.

- [ ] **Step 1: Write failing contract and STA window tests**

Cover one/two/three actions, unique IDs, default/cancel designation, Enter, Escape, title-bar close, owner centering, shared theme resources, accessible names, long-message wrapping, and invalid zero-action requests.

- [ ] **Step 2: Run the new tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DesignerDialogWindowTests"
```

Expected: FAIL because the dialog types do not exist.

- [ ] **Step 3: Implement the modal dialog**

Build the action row from the request, set `IsDefault`/`IsCancel`, return the stable action ID, and close. If the title-bar closes the window, return the explicit cancel action; when none exists, use the last action as the safe result. Use only shared Designer resources and never call `MessageBox`.

- [ ] **Step 4: Run dialog and layout tests**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DesignerDialogWindowTests|FullyQualifiedName~MainWindowLayoutTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.SkinDesigner/UI/Dialogs tests/CodexQuotaHud.SkinDesigner.Tests/UI/DesignerDialogWindowTests.cs
git commit -m "feat: add themed designer dialog service"
```

### Task 3: Replace all Designer-owned message boxes

**Files:**
- Modify: `src/CodexQuotaHud.SkinDesigner/App.xaml.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Documents/IUnsavedChangesDialog.cs`
- Modify: `src/CodexQuotaHud.SkinDesigner/Output/ISkinOutputDialogs.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Documents/DraftCloseCoordinatorTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/Output/WindowsSkinOutputDialogsTests.cs`
- Modify: `tests/CodexQuotaHud.SkinDesigner.Tests/ProjectBoundaryTests.cs`

**Interfaces:**
- Consumes Task 2 `IDesignerDialogService`.
- Preserves `IUnsavedChangesDialog` and `ISkinOutputDialogs` public signatures.
- Preserves `DesignerWindowOwner.Current` as the active owner boundary.

- [ ] **Step 1: Write failing adapter mapping tests**

With a fake service, verify exact mappings: `save/discard/cancel`, `replace/keep-copy/cancel`, export `replace/cancel`, and result `ok`. Assert title, message, icon, default, cancel, and owner. Add a source test rejecting `MessageBox.Show` under the Designer while explicitly allowing `OpenFileDialog` and `SaveFileDialog`.

- [ ] **Step 2: Run focused tests and verify failure**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --filter "FullyQualifiedName~DraftCloseCoordinatorTests|FullyQualifiedName~WindowsSkinOutputDialogsTests|FullyQualifiedName~ProjectBoundaryTests"
```

- [ ] **Step 3: Inject one service and replace native message paths**

Create one `DesignerDialogService` in `App.OnStartup`, pass it into document/output adapters, and map returned IDs to existing domain enums/booleans. Keep native open/save picker code and cancellation behavior unchanged.

- [ ] **Step 4: Run the full Designer suite**

```powershell
dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release
```

Expected: all tests pass with zero skipped.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.SkinDesigner tests/CodexQuotaHud.SkinDesigner.Tests
git commit -m "fix: unify designer dialogs with dark theme"
```

### Task 4: Visual acceptance and evidence

**Files:**
- Modify: `docs/verification/2026-08-02-optional-skin-designer-acceptance.md`
- Modify: `CHANGELOG_AI.md`

**Interfaces:**
- Consumes Tasks 1–3.
- Produces exact visual evidence; unobserved rows remain `NOT RUN` or `PARTIAL`.

- [ ] **Step 1: Build and launch the source Designer**

```powershell
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
dotnet run --project .\src\CodexQuotaHud.SkinDesigner -c Release --no-build
```

- [ ] **Step 2: Exercise all modal states**

Open a native draft picker and confirm background buttons remain dark and disabled. Trigger unsaved, export replacement, collision, success, warning, and failure themed dialogs; verify centering, order, focus, Enter, Escape, and wrapping.

- [ ] **Step 3: Record and commit evidence**

```powershell
git add docs/verification/2026-08-02-optional-skin-designer-acceptance.md CHANGELOG_AI.md
git commit -m "docs: record themed designer dialog verification"
```
