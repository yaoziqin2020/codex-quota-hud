# Preview Window State Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the preview control window's initial scrollbar at standard DPI and persist its last valid size and position independently from normal HUD settings.

**Architecture:** A preview-only JSON store owns validated window geometry at `%LOCALAPPDATA%\CodexQuotaHud\preview-window.json`. The real control window applies clamped geometry at startup and saves debounced user move/resize changes while retaining its existing `ScrollViewer` as a small-screen fallback.

**Tech Stack:** C# 13, .NET 9.0.316, WPF, System.Text.Json, xUnit, no new packages.

## Global Constraints

- Default preview control size is `380 × 650`; minimum remains `340 × 520`.
- Persist only `Left`, `Top`, `Width`, and `Height`.
- Do not modify normal `%LOCALAPPDATA%\CodexQuotaHud\settings.json`.
- Store preview geometry at `%LOCALAPPDATA%\CodexQuotaHud\preview-window.json`.
- Malformed, missing, non-finite, or undersized data falls back safely.
- Clamp restored geometry to a current monitor work area.
- Keep automatic vertical scrolling for small screens and high DPI.
- Save atomically and contain filesystem failures.

---

### Task 1: Preview-only geometry store

**Files:**
- Create: `src/CodexQuotaHud.App/Preview/PreviewWindowState.cs`
- Create: `src/CodexQuotaHud.App/Preview/PreviewWindowStateStore.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Preview/PreviewWindowStateStoreTests.cs`

**Interfaces:**
- Produces: `internal sealed record PreviewWindowState(double Left, double Top, double Width, double Height)`
- Produces: `internal static PreviewWindowState Default`
- Produces: `internal sealed class PreviewWindowStateStore`
- Produces: `string StatePath`, `PreviewWindowState Load()`, `void Save(PreviewWindowState state)`

- [ ] **Step 1: Write failing store tests**

Use a unique temporary root and assert literal behavior:

```csharp
[Fact]
public void DefaultPathAndState_ArePreviewSpecific()
{
    var store = new PreviewWindowStateStore(@"C:\Users\Test\AppData\Local");

    Assert.Equal(
        @"C:\Users\Test\AppData\Local\CodexQuotaHud\preview-window.json",
        store.StatePath);
    Assert.Equal(380, PreviewWindowState.Default.Width);
    Assert.Equal(650, PreviewWindowState.Default.Height);
}
```

Add real-file save/reload coverage for `Left=120`, `Top=80`, `Width=440`,
`Height=720`. Add malformed JSON, missing fields, `NaN` represented through a
controlled deserialize input, width below `340`, and height below `520`
cases; each must return the literal default state. Assert no
`settings.json` file is created.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PreviewWindowStateStoreTests
```

Expected: compilation fails because the preview window-state types are absent.

- [ ] **Step 3: Implement validated atomic storage**

Use `System.Text.Json`. Validate all four values with `double.IsFinite`,
`Width >= 340`, and `Height >= 520`. `Load` catches `IOException`,
`UnauthorizedAccessException`, and `JsonException` and returns `Default`.
`Save` validates first, creates only the preview directory, writes a
same-directory unique temporary file, and atomically moves it over the exact
state path. Clean the temporary file on caught filesystem failures.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all store tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.App/Preview/PreviewWindowState.cs src/CodexQuotaHud.App/Preview/PreviewWindowStateStore.cs tests/CodexQuotaHud.App.Tests/Preview/PreviewWindowStateStoreTests.cs
git commit -m "feat: persist preview window geometry separately"
```

---

### Task 2: Apply, clamp, and save control-window geometry

**Files:**
- Modify: `src/CodexQuotaHud.App/Preview/PreviewControlWindow.xaml`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewControlWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewComposition.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewControlWindowTests.cs`
- Modify: `README.md`
- Modify: `CURRENT_TASK.md`
- Modify: `CHANGELOG_AI.md`
- Modify: `PROJECT_CONTEXT.md`

**Interfaces:**
- Consumes: `PreviewWindowStateStore.Load()` and `.Save(...)`
- Produces: `internal static PreviewWindowState ClampState(PreviewWindowState state, WorkArea workArea)`
- Produces: `internal void SaveWindowStateNow()`

- [ ] **Step 1: Write failing geometry/UI tests**

Add literal clamp cases:

```csharp
[Fact]
public void ClampState_RecoversOffscreenWindow()
{
    var state = new PreviewWindowState(4000, 3000, 440, 720);
    var area = new WorkArea(0, 0, 1920, 1040);

    var result = PreviewControlWindow.ClampState(state, area);

    Assert.Equal(1480, result.Left);
    Assert.Equal(320, result.Top);
    Assert.Equal(440, result.Width);
    Assert.Equal(720, result.Height);
}
```

Construct the real WPF window on STA with a temporary store containing
`Left=120`, `Top=80`, `Width=440`, `Height=720`; assert those values are
applied. Change them to `160`, `90`, `460`, `740`, call
`SaveWindowStateNow`, reload the real store, and assert exact values. Assert
the default window reports `380 × 650`.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PreviewControlWindowTests
```

Expected: compilation fails because the window does not accept or save the
new state store and `ClampState` is absent.

- [ ] **Step 3: Implement startup restore and debounced save**

Change XAML defaults to `Width="380" Height="650"` while keeping the existing
minimums and `ScrollViewer`.

Pass `PreviewWindowStateStore` from `PreviewComposition`. Before the window is
shown, load state, find the nearest `Screen.WorkingArea`, convert it to WPF
coordinates with the existing monitor/DPI approach, clamp state, and apply
manual startup location, size, and position.

After `Loaded`, subscribe to `LocationChanged` and `SizeChanged`. Restart a
single `DispatcherTimer` with a 300 ms interval; its tick calls
`SaveWindowStateNow`. Closing stops the timer and saves once. Catch store save
exceptions in the store, so UI handlers remain non-throwing.

- [ ] **Step 4: Run focused and complete verification**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PreviewControlWindowTests
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

Expected: focused tests and full suite pass; build has zero errors.

- [ ] **Step 5: Update handoff documents**

Record the independent preview state path, default complete-content size,
scroll fallback, exact new test counts, and manual checks not performed.

- [ ] **Step 6: Commit**

```powershell
git add src/CodexQuotaHud.App/Preview tests/CodexQuotaHud.App.Tests/Preview README.md CURRENT_TASK.md CHANGELOG_AI.md PROJECT_CONTEXT.md
git commit -m "fix: remember preview control window size"
```

- [ ] **Step 7: Final checks**

```powershell
git diff --check
git status --short --branch
```

Expected: clean feature branch with only approved preview-window state work.
