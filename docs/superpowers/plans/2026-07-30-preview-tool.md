# Quota Preview Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an isolated `--preview` mode that drives the production HUD with deterministic dual, single, and hidden quota states from a separate developer control window.

**Architecture:** `App` selects the existing production composition root or a preview composition root. Preview mode uses a real `QuotaOrbViewModel`, `QuotaOrbWindow`, skin controller, popup, tray, and edge geometry, but substitutes an in-memory settings store and a deterministic `PreviewQuotaRefreshController`; a separate `PreviewControlWindow` publishes state and invokes narrow HUD inspection commands.

**Tech Stack:** C# 13, .NET 9.0.316, WPF, xUnit, existing Core/App projects, no new packages.

## Global Constraints

- `--preview` is the only preview entry and normal launch behavior must remain unchanged.
- Preview mode must not construct or contact `codex app-server`, register startup, or write the production settings file.
- The production `QuotaOrbWindow`, details popup, skins, tray, animation state, and edge geometry remain the rendering path.
- Preview defaults are five-hour 68%, weekly 34%, dual mode, animations enabled, and idle refresh state.
- Preview supports dual, five-hour-only, weekly-only, and no-quota states; values are clamped to `0..100`.
- The control window title is `Codex Quota HUD — 开发预览`.
- No preview preset persistence and no user-facing simulation menu.
- Real app-server dual-window acceptance remains a later manual check.

---

### Task 1: Preview launch contract

**Files:**
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/AppLaunchModeTests.cs`

**Interfaces:**
- Produces: `internal static bool IsPreviewLaunch(IReadOnlyList<string> arguments)`
- Produces: `internal static bool ShouldRegisterStartup(IReadOnlyList<string> arguments)`
- Preserves: `internal static bool IsInteractiveLaunch(IReadOnlyList<string> arguments)`

- [ ] **Step 1: Write failing launch-policy tests**

Add literal cases proving case-insensitive preview detection and startup isolation:

```csharp
[Theory]
[InlineData(true, "--preview")]
[InlineData(true, "--PREVIEW")]
[InlineData(false)]
[InlineData(false, "--background")]
public void PreviewLaunch_RequiresPreviewArgument(
    bool expected,
    params string[] arguments) =>
    Assert.Equal(expected, App.IsPreviewLaunch(arguments));

[Theory]
[InlineData(true)]
[InlineData(true, "--other")]
[InlineData(false, "--background")]
[InlineData(false, "--preview")]
[InlineData(false, "--preview", "--other")]
public void StartupRegistration_OnlyRunsForNormalInteractiveLaunch(
    bool expected,
    params string[] arguments) =>
    Assert.Equal(expected, App.ShouldRegisterStartup(arguments));
```

The break caught is preview accidentally entering normal registration or
normal launches being reclassified.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter FullyQualifiedName~AppLaunchModeTests
```

Expected: compilation fails because `IsPreviewLaunch` and
`ShouldRegisterStartup` do not exist.

- [ ] **Step 3: Implement minimal launch policy**

Add:

```csharp
internal static bool IsPreviewLaunch(IReadOnlyList<string> arguments) =>
    arguments.Any(argument => string.Equals(
        argument, "--preview", StringComparison.OrdinalIgnoreCase));

internal static bool ShouldRegisterStartup(IReadOnlyList<string> arguments) =>
    IsInteractiveLaunch(arguments) && !IsPreviewLaunch(arguments);
```

Replace the startup-registration condition with
`ShouldRegisterStartup(e.Args)`.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all `AppLaunchModeTests` pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.App/App.xaml.cs tests/CodexQuotaHud.App.Tests/AppLaunchModeTests.cs
git commit -m "test: define isolated preview launch policy"
```

---

### Task 2: Deterministic preview state source

**Files:**
- Create: `src/CodexQuotaHud.App/Preview/PreviewDisplayChoice.cs`
- Create: `src/CodexQuotaHud.App/Preview/PreviewQuotaRefreshController.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Preview/PreviewQuotaRefreshControllerTests.cs`

**Interfaces:**
- Produces: `internal enum PreviewDisplayChoice { Dual, FiveHourOnly, WeeklyOnly, NoQuota }`
- Produces: `internal sealed class PreviewQuotaRefreshController : IQuotaRefreshController`
- Produces: `Publish(PreviewDisplayChoice choice, double fiveHourPercent, double weeklyPercent, bool isRefreshing)`
- Produces: `QuotaRefreshState CurrentState`

- [ ] **Step 1: Write failing table-driven state tests**

Create tests that subscribe to `StateChanged`, call `Publish`, and assert
hand-derived literals:

```csharp
[Theory]
[InlineData(PreviewDisplayChoice.Dual, QuotaDisplayMode.Dual, "5 小时", true)]
[InlineData(PreviewDisplayChoice.FiveHourOnly, QuotaDisplayMode.Single, "5 小时", false)]
[InlineData(PreviewDisplayChoice.WeeklyOnly, QuotaDisplayMode.Single, "每周", false)]
[InlineData(PreviewDisplayChoice.NoQuota, QuotaDisplayMode.Hidden, null, false)]
public void Publish_ProducesRequestedProductionDisplayShape(
    PreviewDisplayChoice choice,
    QuotaDisplayMode expectedMode,
    string? expectedPrimaryLabel,
    bool expectedSecondary)
```

For labels, derive them from `Display.Primary.Kind` in the assertion rather
than copying a production helper. Add separate assertions that `125` becomes
`100`, `-8` becomes `0`, and `isRefreshing: true` reaches the published state.
Assert `RefreshNowAsync` republishes `CurrentState` without external work.

The breaks caught are wrong window selection, missing clamping, and preview
refresh invoking a real dependency.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter FullyQualifiedName~PreviewQuotaRefreshControllerTests
```

Expected: compilation fails because the preview types do not exist.

- [ ] **Step 3: Implement the state source**

Use a fixed base timestamp such as `2030-01-01T00:00:00Z`, five-hour reset
`+5h`, and weekly reset `+7d`. Build `QuotaSnapshot` according to the chosen
presence shape, convert it with `QuotaDisplayState.FromSnapshot`, and publish:

```csharp
var state = new QuotaRefreshState(
    IsCodexRunning: true,
    IsRefreshing: isRefreshing,
    Display: QuotaDisplayState.FromSnapshot(snapshot),
    LastError: null);
CurrentState = state;
StateChanged?.Invoke(state);
```

Initialize the controller by publishing dual `68/34` idle state. Do not add a
client, process, timer, file, or network dependency.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all preview controller tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.App/Preview tests/CodexQuotaHud.App.Tests/Preview/PreviewQuotaRefreshControllerTests.cs
git commit -m "feat: add deterministic preview quota source"
```

---

### Task 3: Memory-only preview settings

**Files:**
- Create: `src/CodexQuotaHud.App/Preview/InMemorySettingsStore.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Preview/InMemorySettingsStoreTests.cs`

**Interfaces:**
- Produces: `internal sealed class InMemorySettingsStore : ISettingsStore`
- Produces: `AppSettings Load()`
- Produces: `void Save(AppSettings settings)`
- Produces: `AppSettings Current`

- [ ] **Step 1: Write failing behavior tests**

```csharp
[Fact]
public void Save_UpdatesOnlyTheStoreInstance()
{
    var store = new InMemorySettingsStore(
        new AppSettings(SelectedSkin: SkinId.HudDial));

    store.Save(store.Load() with
    {
        SelectedSkin = SkinId.Aurora,
        Left = 420,
        Top = 240
    });

    Assert.Equal(SkinId.Aurora, store.Current.SelectedSkin);
    Assert.Equal(420, store.Load().Left);
    Assert.Equal(240, store.Load().Top);
}
```

Also create two stores and prove saving one does not change the other. The
break caught is shared/static state leaking preview preferences.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter FullyQualifiedName~InMemorySettingsStoreTests
```

Expected: compilation fails because `InMemorySettingsStore` is missing.

- [ ] **Step 3: Implement the in-memory store**

Store one immutable `AppSettings` record behind an instance lock. `Load`
returns the current record and `Save` replaces it after a null check. Do not
delegate to `SettingsStore` or touch a filesystem path.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all memory-store tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.App/Preview/InMemorySettingsStore.cs tests/CodexQuotaHud.App.Tests/Preview/InMemorySettingsStoreTests.cs
git commit -m "feat: isolate preview settings in memory"
```

---

### Task 4: Preview session and production HUD inspection bridge

**Files:**
- Create: `src/CodexQuotaHud.App/Preview/IPreviewHud.cs`
- Create: `src/CodexQuotaHud.App/Preview/PreviewSession.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Preview/PreviewSessionTests.cs`

**Interfaces:**
- Produces: `internal interface IPreviewHud`
- Produces: `void SetDetailsOpen(bool isOpen)`
- Produces: `void PreviewEdge(EdgeDockSide side)`
- Produces: `void ForceExpanded()`
- Produces: `internal sealed class PreviewSession`
- Consumes: `PreviewQuotaRefreshController`, `QuotaOrbViewModel`, `IPreviewHud`

- [ ] **Step 1: Write failing session tests**

Use a recording `IPreviewHud` and real preview controller/view model. Assert
observable production view-model results:

```csharp
session.SetDisplayChoice(PreviewDisplayChoice.WeeklyOnly);
Assert.Equal(QuotaDisplayMode.Single, viewModel.DisplayMode);
Assert.Equal("每周", viewModel.PrimaryLabel);
Assert.Null(viewModel.SecondaryPercent);

session.SetFiveHourPercent(91);
session.SetDisplayChoice(PreviewDisplayChoice.Dual);
Assert.Equal(91, viewModel.PrimaryPercent);
Assert.Equal(34, viewModel.SecondaryPercent);

session.SetRefreshing(true);
Assert.True(viewModel.SkinState.IsRefreshing);
```

Assert every `SkinId` can be selected and reaches
`viewModel.SelectedSkin`. Assert details and four valid edge commands reach the
recording HUD, while `EdgeDockSide.None` is rejected.

The breaks caught are controls updating only preview fields instead of the
production view model, and edge commands bypassing validation.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter FullyQualifiedName~PreviewSessionTests
```

Expected: compilation fails because session and HUD bridge types are missing.

- [ ] **Step 3: Implement minimal session and bridge**

`PreviewSession` owns the current choice, percentages, and refreshing flag.
Each setter updates one field and republishes the complete state. Skin and
animation setters use the existing view-model properties.

Make `QuotaOrbWindow` implement `IPreviewHud`. Add narrow internal methods:

```csharp
void IPreviewHud.SetDetailsOpen(bool isOpen) =>
    (isOpen ? (Action)ShowDetailsPopup : CloseDetailsPopup)();

void IPreviewHud.PreviewEdge(EdgeDockSide side)
{
    if (side == EdgeDockSide.None)
        throw new ArgumentOutOfRangeException(nameof(side));
    // Resolve nearest work area, move to its requested external edge,
    // update the existing controller dock side, then call AnimateEdge.
}

void IPreviewHud.ForceExpanded() =>
    AnimateEdge(_edgeAutoHideController.DockSide, collapsed: false);
```

Reuse `GetNearestWorkArea`, `EdgeAutoHideGeometry`, `ApplyEdgeVisualState`, and
`AnimateEdge`; do not duplicate collapsed-position formulas.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all session tests pass.

- [ ] **Step 5: Run existing window/geometry tests**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter "FullyQualifiedName~EdgeAutoHide|FullyQualifiedName~Popup|FullyQualifiedName~QuotaOrbViewModel"
```

Expected: all selected tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/CodexQuotaHud.App/Preview src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs tests/CodexQuotaHud.App.Tests/Preview/PreviewSessionTests.cs
git commit -m "feat: drive production HUD from preview session"
```

---

### Task 5: Developer control window

**Files:**
- Create: `src/CodexQuotaHud.App/Preview/PreviewControlWindow.xaml`
- Create: `src/CodexQuotaHud.App/Preview/PreviewControlWindow.xaml.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Preview/PreviewControlWindowTests.cs`

**Interfaces:**
- Consumes: `PreviewSession`
- Produces: `internal sealed partial class PreviewControlWindow : Window`
- Produces: `event EventHandler? ExitRequested`

- [ ] **Step 1: Write failing STA UI tests**

Following existing WPF test helpers, construct the window on an STA thread and
assert real controls:

```csharp
Assert.Equal("Codex Quota HUD — 开发预览", window.Title);
Assert.False(window.Topmost);
Assert.Equal(PreviewDisplayChoice.Dual, window.SelectedDisplayChoice);
Assert.Equal(68, window.FiveHourPercent);
Assert.Equal(34, window.WeeklyPercent);
```

Invoke each control through its real command/event surface and assert the real
session/view model changes. Verify closing raises `ExitRequested` once. The
break caught is a visually present control that is not wired to production
state.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter FullyQualifiedName~PreviewControlWindowTests
```

Expected: compilation fails because the window does not exist.

- [ ] **Step 3: Implement the compact control window**

Create a normal resizable tool window, approximately `360x560`, with:

- display-mode radio buttons;
- two labeled sliders and numeric readouts (`0..100`);
- skin combo box populated from `Enum.GetValues<SkinId>()`;
- animation and refreshing check boxes;
- details open/close buttons;
- expanded, left, right, top, and bottom buttons;
- a visible note: `模拟数据仅用于视觉预览，不代表真实额度。`

Wire each event to exactly one `PreviewSession` method. Keep code-behind
focused on UI translation; state construction stays in `PreviewSession`.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all control-window tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.App/Preview tests/CodexQuotaHud.App.Tests/Preview/PreviewControlWindowTests.cs
git commit -m "feat: add quota preview control window"
```

---

### Task 6: Preview composition root and lifecycle

**Files:**
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Create: `src/CodexQuotaHud.App/Preview/PreviewComposition.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/AppLaunchModeTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Preview/PreviewCompositionTests.cs`

**Interfaces:**
- Produces: `internal sealed class PreviewComposition : IDisposable`
- Produces: `QuotaOrbViewModel ViewModel`
- Produces: `QuotaOrbWindow HudWindow`
- Produces: `PreviewControlWindow ControlWindow`
- Produces: `TrayController Tray`

- [ ] **Step 1: Write failing composition tests**

Construct the composition with an exit callback and assert:

- the HUD view model begins in dual `68/34`;
- settings are `InMemorySettingsStore`;
- no process monitor, restartable client, refresh service, or running
  coordinator is part of the preview composition API;
- either window requesting exit invokes the callback once;
- `Dispose` is idempotent.

The break caught is preview startup reusing production external resources or
leaking windows/tray resources.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --filter FullyQualifiedName~PreviewCompositionTests
```

Expected: compilation fails because `PreviewComposition` is missing.

- [ ] **Step 3: Implement preview composition**

Construct in this order:

1. `InMemorySettingsStore(new AppSettings())`
2. `PreviewQuotaRefreshController`
3. `QuotaOrbViewModel`
4. `QuotaOrbWindow`
5. `TrayController`
6. `PreviewSession`
7. `PreviewControlWindow`

Publish the initial controller state after the view model subscribes. Show the
HUD and control window. Dispose control window, tray, HUD, view model, and
controller-owned subscriptions exactly once.

In `App.OnStartup`, branch immediately after acquiring the single-instance
guard:

```csharp
if (IsPreviewLaunch(e.Args))
{
    _previewComposition = new PreviewComposition(RequestExit);
    return;
}
```

Add preview disposal to both orderly and emergency cleanup. Do not construct
normal settings, process monitoring, quota client, refresh service, or startup
registration before this branch.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command and `AppLaunchModeTests`. Expected: all selected tests
pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.App/App.xaml.cs src/CodexQuotaHud.App/Preview/PreviewComposition.cs tests/CodexQuotaHud.App.Tests
git commit -m "feat: compose isolated preview application mode"
```

---

### Task 7: Documentation and full verification

**Files:**
- Modify: `README.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `CURRENT_TASK.md`
- Modify: `CHANGELOG_AI.md`
- Modify: `docs/verification/2026-07-29-windows-checklist.md`

**Interfaces:**
- Documents: developer command, safety boundary, controls, automated totals,
  and remaining real-data acceptance.

- [ ] **Step 1: Add concise developer documentation**

Document:

```powershell
dotnet run --project .\src\CodexQuotaHud.App -- --preview
```

State that preview mode uses synthetic in-memory data, never contacts
`app-server`, never writes normal settings, and does not replace later
real-data validation. Update the three handoff documents with exact
implementation, verification, and remaining manual checks.

- [ ] **Step 2: Run all automated tests**

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
```

Expected: exit code `0`, no failed tests. Record the new exact Core, App/UI,
and total counts from output; do not retain the old `232` count if it changed.

- [ ] **Step 3: Run the Release build**

```powershell
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

Expected: exit code `0`, zero build errors.

- [ ] **Step 4: Launch and manually inspect the preview**

```powershell
dotnet run --project .\src\CodexQuotaHud.App -c Release -- --preview
```

Check all five skins in dual and both single modes, hidden-state recovery,
details, idle/refreshing/disabled animation, and all four edge commands.
Close preview and confirm the normal settings file timestamp/content did not
change. Record any unavailable multi-monitor or DPI checks as unverified.

- [ ] **Step 5: Inspect the final diff**

```powershell
git diff --check
git status --short
git diff --stat
```

Expected: only preview-tool source, tests, and handoff/documentation changes.

- [ ] **Step 6: Commit**

```powershell
git add README.md PROJECT_CONTEXT.md CURRENT_TASK.md CHANGELOG_AI.md docs/verification/2026-07-29-windows-checklist.md
git commit -m "docs: document quota preview workflow"
```

- [ ] **Step 7: Report acceptance evidence**

Report exact test/build counts, manual checks completed, manual checks not
completed, Git status, and whether the installed `v1.0.0` build was left
unchanged. Do not deploy locally, move the release tag, replace release
assets, or push unless the user separately requests it.
