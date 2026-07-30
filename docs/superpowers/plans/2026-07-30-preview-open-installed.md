# Preview Open Installed App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a preview control that exits cleanly, releases the single-instance lock, and then opens the installed normal HUD.

**Architecture:** A focused `InstalledAppLauncher` resolves and starts the current-user installed executable. The preview control raises a one-time handoff request; `App` records it, completes normal cleanup, and only then invokes the launcher from `OnExit`.

**Tech Stack:** C# 13, .NET 9.0.316, WPF, xUnit, no new packages.

## Global Constraints

- Installed target: `%LOCALAPPDATA%\Programs\CodexQuotaHud\CodexQuotaHud.App.exe`.
- Start the installed executable only after `SingleInstanceGuard` cleanup.
- Pass no `--preview` argument to the installed executable.
- Missing installed executable disables the control and displays `未找到已安装正式版`.
- Repeated clicks and repeated exit callbacks start at most one process.
- Ordinary preview exit and all normal-mode exits start no process.
- Launch failure must not interrupt cleanup or restart preview.
- Do not modify installed `v1.0.0`, startup registration, tag, or release assets.

---

### Task 1: Installed application launcher

**Files:**
- Create: `src/CodexQuotaHud.App/Infrastructure/InstalledAppLauncher.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppLauncherTests.cs`

**Interfaces:**
- Produces: `internal sealed class InstalledAppLauncher`
- Produces: `string ExecutablePath`
- Produces: `bool IsAvailable`
- Produces: `bool TryLaunch(out string? error)`

- [ ] **Step 1: Write failing launcher tests**

Use a temporary Local App Data root and a recording process-start delegate:

```csharp
[Fact]
public void ResolvesCurrentUserInstalledExecutable()
{
    var launcher = new InstalledAppLauncher(
        localAppData: @"C:\Users\Test\AppData\Local",
        fileExists: _ => true,
        startProcess: _ => true);

    Assert.Equal(
        @"C:\Users\Test\AppData\Local\Programs\CodexQuotaHud\CodexQuotaHud.App.exe",
        launcher.ExecutablePath);
    Assert.True(launcher.IsAvailable);
}

[Fact]
public void TryLaunch_StartsExactAbsolutePathWithoutArguments()
{
    ProcessStartInfo? captured = null;
    var launcher = new InstalledAppLauncher(
        localAppData: @"C:\Users\Test\AppData\Local",
        fileExists: _ => true,
        startProcess: info => { captured = info; return true; });

    Assert.True(launcher.TryLaunch(out var error));
    Assert.Null(error);
    Assert.Equal(launcher.ExecutablePath, captured!.FileName);
    Assert.Empty(captured.ArgumentList);
    Assert.True(captured.UseShellExecute);
}
```

Add cases for missing file and start exception returning `false` with an
error. The breaks caught are wrong install path, accidental preview arguments,
and exceptions escaping after cleanup.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~InstalledAppLauncherTests
```

Expected: compilation fails because `InstalledAppLauncher` does not exist.

- [ ] **Step 3: Implement the launcher**

Resolve the path with:

```csharp
Path.Combine(
    localAppData,
    "Programs",
    "CodexQuotaHud",
    "CodexQuotaHud.App.exe")
```

The default constructor uses
`Environment.SpecialFolder.LocalApplicationData`, `File.Exists`, and
`Process.Start`. `TryLaunch` rechecks availability, starts a
`ProcessStartInfo(ExecutablePath) { UseShellExecute = true }`, and catches
`Win32Exception`, `InvalidOperationException`, and `IOException`.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all launcher tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.App/Infrastructure/InstalledAppLauncher.cs tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppLauncherTests.cs
git commit -m "feat: add installed HUD launcher"
```

---

### Task 2: Preview button and one-time request

**Files:**
- Modify: `src/CodexQuotaHud.App/Preview/PreviewControlWindow.xaml`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewControlWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewComposition.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewControlWindowTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewCompositionTests.cs`

**Interfaces:**
- Consumes: `InstalledAppLauncher.IsAvailable`
- Produces: `event EventHandler? OpenInstalledRequested`
- Produces: `internal bool CanOpenInstalled`
- Produces: `internal string? InstalledAppMessage`

- [ ] **Step 1: Write failing UI and composition tests**

Construct the real control window on STA with `installedAppAvailable: false`
and assert:

```csharp
Assert.False(window.CanOpenInstalled);
Assert.Equal("未找到已安装正式版", window.InstalledAppMessage);
```

Construct with availability true, call the real button action twice, and
assert one `OpenInstalledRequested` notification and one exit request.
Assert ordinary `Close()` emits no installed-app request.

The breaks caught are an enabled dead button, duplicate launches, and ordinary
close accidentally switching modes.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PreviewControlWindowTests|FullyQualifiedName~PreviewCompositionTests"
```

Expected: compilation fails because the new preview interface is absent.

- [ ] **Step 3: Implement the button and event forwarding**

Add a bottom button with content `退出预览并打开正式版`, bind its enabled state
from the constructor, and show the missing message only when unavailable.
The click handler uses `Interlocked.Exchange` to ensure one request, raises
`OpenInstalledRequested`, and then raises the existing exit request.

`PreviewComposition` constructs `InstalledAppLauncher`, passes availability to
the control window, and forwards the control event without starting a process.

- [ ] **Step 4: Run focused tests and verify GREEN**

Run the Step 2 command. Expected: all selected tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CodexQuotaHud.App/Preview tests/CodexQuotaHud.App.Tests/Preview
git commit -m "feat: request installed HUD from preview"
```

---

### Task 3: Cleanup-ordered application handoff

**Files:**
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/AppLaunchModeTests.cs`
- Modify: `README.md`
- Modify: `CURRENT_TASK.md`
- Modify: `CHANGELOG_AI.md`

**Interfaces:**
- Consumes: `PreviewComposition.OpenInstalledRequested`
- Produces: `internal static void CompleteExit(bool openInstalled, Action cleanup, Func<bool> launch, Action<string> traceError)`

- [ ] **Step 1: Write failing lifecycle-order tests**

Test the extracted exit coordinator with a literal event log:

```csharp
[Fact]
public void ExitHandoff_LaunchesOnlyAfterCleanup()
{
    var events = new List<string>();

    App.CompleteExit(
        openInstalled: true,
        cleanup: () => events.Add("cleanup"),
        launch: () => { events.Add("launch"); return true; },
        traceError: _ => events.Add("error"));

    Assert.Equal(["cleanup", "launch"], events);
}
```

Add cases proving `openInstalled: false` never launches and launch failure is
traced after cleanup without throwing. The breaks caught are reacquiring the
mutex too early and normal exit launching a replacement process.

- [ ] **Step 2: Run focused tests and verify RED**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AppLaunchModeTests
```

Expected: compilation fails because `CompleteExit` does not exist.

- [ ] **Step 3: Implement cleanup-ordered handoff**

Subscribe to `PreviewComposition.OpenInstalledRequested`, atomically set a
pending flag, and call `RequestExit`. In `OnExit`, execute existing emergency
cleanup as the `cleanup` action, then call `InstalledAppLauncher.TryLaunch`
only when the consumed pending flag is true. Trace failure text after cleanup.

Keep the normal and preview startup composition unchanged.

- [ ] **Step 4: Run focused and full verification**

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AppLaunchModeTests
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

Expected: all tests pass and build completes with zero errors.

- [ ] **Step 5: Update handoff documentation**

Document the new button, missing-install behavior, cleanup-before-launch
contract, exact new test counts, and whether manual installed-app handoff was
verified.

- [ ] **Step 6: Commit**

```powershell
git add src/CodexQuotaHud.App/App.xaml.cs tests/CodexQuotaHud.App.Tests/AppLaunchModeTests.cs README.md CURRENT_TASK.md CHANGELOG_AI.md
git commit -m "feat: open installed HUD after preview exit"
```

- [ ] **Step 7: Final diff and status check**

```powershell
git diff --check
git status --short --branch
```

Expected: clean feature branch containing only the approved handoff work.
