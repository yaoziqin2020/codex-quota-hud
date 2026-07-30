# Preview Replaces Installed App Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `--preview` close a running installed Codex Quota HUD before opening preview, using graceful shutdown first and an exact-installed-path force-close fallback for older builds.

**Architecture:** A named current-user event lets listener-enabled normal mode dispatch the existing orderly `RequestExit` path. Preview startup delegates occupied-mutex recovery to a coordinator that signals the event, waits for the mutex, and only then force-closes a process whose normalized executable path exactly matches the current-user installation path. The coordinator returns the acquired single-instance lease to `App`, so mutex ownership stays on the WPF startup thread.

**Tech Stack:** C# 13, .NET 9, WPF, `EventWaitHandle`, `System.Diagnostics.Process`, xUnit

## Global Constraints

- Normal and preview modes continue to share `Local\CodexQuotaHud.Singleton`.
- Graceful shutdown gets a bounded two-second window before fallback.
- Force-close may target only `%LOCALAPPDATA%\Programs\CodexQuotaHud\CodexQuotaHud.App.exe`, compared by normalized full path with `OrdinalIgnoreCase`.
- A development build, another same-name executable, or a process whose path cannot be inspected must never be terminated.
- Preview continues only after it owns the single-instance mutex.
- A failed replacement shows a clear error and leaves preview closed.
- A second normal launch retains the current silent single-instance exit behavior.
- The reverse `退出预览并打开正式版` handoff remains unchanged.
- Do not add a package dependency.
- Use test-driven development and keep every commit limited to the task being completed.

---

## File Structure

- Create `src/CodexQuotaHud.App/Infrastructure/InstalledAppShutdownListener.cs`
  - Owns the named auto-reset event, background wait thread, UI-dispatched exit callback, and bounded disposal.
- Create `tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppShutdownListenerTests.cs`
  - Verifies signalling, one callback per signal, and clean disposal.
- Create `src/CodexQuotaHud.App/Infrastructure/InstalledAppShutdownCoordinator.cs`
  - Owns preview mutex acquisition, graceful wait, exact-path selection, one force-close attempt, and final mutex acquisition.
- Create `src/CodexQuotaHud.App/Infrastructure/InstalledAppShutdownPlatform.cs`
  - Wraps event signalling, process enumeration, executable-path inspection, termination, exit waiting, monotonic time, and short waits.
- Create `tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppShutdownCoordinatorTests.cs`
  - Uses fakes to cover every orchestration and safety branch without killing a real process.
- Modify `src/CodexQuotaHud.App/App.xaml.cs`
  - Chooses preview mode before final mutex handling, stores an `IDisposable` lease, starts the normal listener, and reports preview replacement failure.
- Modify `tests/CodexQuotaHud.App.Tests/AppLaunchModeTests.cs`
  - Covers the mode-specific acquisition boundary and preserves reverse-handoff ordering.
- Modify `README.md`, `CURRENT_TASK.md`, `PROJECT_CONTEXT.md`, and `CHANGELOG_AI.md`
  - Records the new symmetric handoff, exact-path force-close rule, verification totals, and remaining manual acceptance.

---

### Task 1: Graceful Installed-App Shutdown Signal

**Files:**
- Create: `src/CodexQuotaHud.App/Infrastructure/InstalledAppShutdownListener.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppShutdownListenerTests.cs`

**Interfaces:**
- Consumes: an `Action requestExit` supplied by `App`; an optional event name for isolated tests.
- Produces: `InstalledAppShutdownListener(Action requestExit)`, internal `InstalledAppShutdownListener(string eventName, Action requestExit)`, static `bool TrySignal()`, internal static `bool TrySignal(string eventName)`, and idempotent `Dispose()`.

- [ ] **Step 1: Write the failing listener tests**

Create tests using unique `Local\CodexQuotaHud.Tests.Shutdown.{Guid}` names:

```csharp
[Fact]
public void Signal_InvokesExitCallback()
{
    var name = UniqueEventName();
    using var called = new ManualResetEventSlim();
    using var listener = new InstalledAppShutdownListener(name, called.Set);

    Assert.True(InstalledAppShutdownListener.TrySignal(name));
    Assert.True(called.Wait(TimeSpan.FromSeconds(2)));
}

[Fact]
public void MissingListener_ReturnsFalse()
{
    Assert.False(InstalledAppShutdownListener.TrySignal(UniqueEventName()));
}

[Fact]
public void Dispose_StopsFutureCallbacksAndIsIdempotent()
{
    var name = UniqueEventName();
    var calls = 0;
    var listener = new InstalledAppShutdownListener(
        name,
        () => Interlocked.Increment(ref calls));

    listener.Dispose();
    listener.Dispose();

    Assert.False(InstalledAppShutdownListener.TrySignal(name));
    Assert.Equal(0, Volatile.Read(ref calls));
}
```

Also test two sequential signals by resetting a `ManualResetEventSlim` between
signals and waiting until the callback count reaches two.

- [ ] **Step 2: Run the focused tests and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~InstalledAppShutdownListenerTests
```

Expected: compilation fails because `InstalledAppShutdownListener` does not
exist.

- [ ] **Step 3: Implement the named-event listener**

Implement these constants and lifecycle rules:

```csharp
internal sealed class InstalledAppShutdownListener : IDisposable
{
    internal const string EventName =
        @"Local\CodexQuotaHud.ShutdownRequested";

    private readonly EventWaitHandle _shutdownEvent;
    private readonly ManualResetEvent _stopEvent = new(false);
    private readonly Thread _thread;
    private readonly Action _requestExit;
    private int _disposed;

    public InstalledAppShutdownListener(Action requestExit)
        : this(EventName, requestExit)
    {
    }
}
```

The internal constructor must:

- validate `eventName` and `requestExit`;
- create an `EventWaitHandle(false, EventResetMode.AutoReset, eventName)`;
- start an `IsBackground = true` thread named
  `CodexQuotaHud.ShutdownListener`;
- have that thread call
  `WaitHandle.WaitAny([_shutdownEvent, _stopEvent])`;
- invoke `_requestExit()` only when index `0` wins;
- exit when index `1` wins;
- contain exceptions from the callback so the wait thread cannot crash the
  process.

`TrySignal(string eventName)` must use
`EventWaitHandle.TryOpenExisting(eventName, out var handle)`, set and dispose
the opened handle, and return `false` for a missing or inaccessible event.

`Dispose()` must be idempotent, set `_stopEvent`, join the thread for at most
two seconds, then dispose both handles. Do not use `Thread.Abort`.

- [ ] **Step 4: Run the focused tests and verify GREEN**

Run the same filtered command. Expected: all
`InstalledAppShutdownListenerTests` pass with no hanging test process.

- [ ] **Step 5: Commit the listener**

```powershell
git add src/CodexQuotaHud.App/Infrastructure/InstalledAppShutdownListener.cs tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppShutdownListenerTests.cs
git commit -m "feat: add graceful installed HUD shutdown signal"
```

---

### Task 2: Safe Preview Replacement Coordinator

**Files:**
- Create: `src/CodexQuotaHud.App/Infrastructure/InstalledAppShutdownCoordinator.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/InstalledAppShutdownPlatform.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppShutdownCoordinatorTests.cs`

**Interfaces:**
- Consumes: `Func<IDisposable?> tryAcquire`, installed executable path, and `IInstalledAppShutdownPlatform`.
- Produces: `bool TryAcquireForPreview(out IDisposable? lease, out string? error)`.
- Produces internal platform contracts:

```csharp
internal interface IInstalledAppShutdownPlatform
{
    long Timestamp { get; }
    long TimestampFrequency { get; }
    bool TrySignalShutdown();
    IReadOnlyList<IInstalledAppProcess> CaptureProcesses();
    void Wait(TimeSpan duration);
}

internal interface IInstalledAppProcess : IDisposable
{
    string? ExecutablePath { get; }
    void Kill();
    bool WaitForExit(TimeSpan timeout);
}
```

- [ ] **Step 1: Write failing coordinator tests with deterministic fakes**

Create a `FakeLease : IDisposable`, `FakePlatform`, and `FakeProcess`.
`FakePlatform.Wait` advances `Timestamp` rather than sleeping.

Cover these cases with explicit assertions:

```csharp
[Fact]
public void FreeMutex_ReturnsLeaseWithoutSignalOrProcessCapture()
{
    var lease = new FakeLease();
    var platform = new FakePlatform();
    var coordinator = CreateCoordinator(platform, () => lease);

    Assert.True(coordinator.TryAcquireForPreview(
        out var acquired,
        out var error));
    Assert.Same(lease, acquired);
    Assert.Null(error);
    Assert.Equal(0, platform.SignalCalls);
    Assert.Equal(0, platform.CaptureCalls);
}
```

Add tests that verify:

- occupied, signal succeeds, third retry returns a lease: no process is killed;
- signal is absent, exact installed path with different casing is killed once,
  waited for once, and the post-exit retry lease is returned;
- a same-name development executable at another path is not killed;
- a process with `ExecutablePath == null` is not killed;
- a path getter throwing `Win32Exception` is contained and not killed;
- exact process `Kill()` throwing returns `false`, a null lease, and a
  non-empty Chinese error;
- `WaitForExit` returning `false` returns failure without another kill;
- mutex remaining occupied after process exit returns failure;
- multiple exact-path processes are rejected rather than mass-killed, because
  a singleton replacement target must be unambiguous.

- [ ] **Step 2: Run the coordinator tests and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~InstalledAppShutdownCoordinatorTests
```

Expected: compilation fails because the coordinator and platform contracts do
not exist.

- [ ] **Step 3: Implement coordinator orchestration**

Use these fixed timing values:

```csharp
private static readonly TimeSpan GracefulTimeout =
    TimeSpan.FromSeconds(2);
private static readonly TimeSpan ForceExitTimeout =
    TimeSpan.FromSeconds(2);
private static readonly TimeSpan RetryInterval =
    TimeSpan.FromMilliseconds(50);
```

The constructor normalizes the installed path once with `Path.GetFullPath`.
`TryAcquireForPreview` must:

```csharp
lease = _tryAcquire();
if (lease is not null)
{
    error = null;
    return true;
}

_ = _platform.TrySignalShutdown();
lease = TryAcquireUntil(GracefulTimeout);
if (lease is not null)
{
    error = null;
    return true;
}
```

Then capture processes once and inspect each path inside its own
`try/catch`. Select only paths for which:

```csharp
string.Equals(
    Path.GetFullPath(candidate.ExecutablePath),
    _installedExecutablePath,
    StringComparison.OrdinalIgnoreCase)
```

Dispose every captured process, including non-matches and exceptions. If
exactly one match exists, call `Kill()` once and
`WaitForExit(ForceExitTimeout)` once. Retry acquisition for up to two seconds
after confirmed process exit.

Return these stable user-facing errors:

- `未找到正在运行的已安装正式版，预览无法取得单实例锁。`
- `检测到多个正式版进程，为避免误关，预览未启动。`
- `无法关闭正在运行的正式版：{detail}`
- `正式版已关闭，但单实例锁仍未释放。`

Never call `Kill()` for a null, inaccessible, non-normalizable, or non-exact
path.

- [ ] **Step 4: Implement the Windows platform boundary**

`InstalledAppShutdownPlatform` must:

- return `Stopwatch.GetTimestamp()` and `Stopwatch.Frequency`;
- delegate signalling to `InstalledAppShutdownListener.TrySignal()`;
- capture `Process.GetProcesses()` and wrap each process;
- use `Thread.Sleep(duration)` only in production;
- expose executable path through `process.MainModule?.FileName`;
- use `process.Kill(entireProcessTree: true)`;
- convert timeout safely to bounded milliseconds for
  `process.WaitForExit(milliseconds)`;
- dispose the wrapped `Process`.

Process enumeration failure returns an empty list to the coordinator with a
trace warning; individual inspection and kill failures remain distinguishable
through the coordinator error.

- [ ] **Step 5: Run focused tests and the infrastructure test group**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InstalledAppShutdownCoordinatorTests|FullyQualifiedName~InstalledAppShutdownListenerTests|FullyQualifiedName~SingleInstanceGuardTests|FullyQualifiedName~InstalledAppLauncherTests"
```

Expected: all selected tests pass. Confirm fake processes report
`DisposeCalls == 1`.

- [ ] **Step 6: Commit the coordinator**

```powershell
git add src/CodexQuotaHud.App/Infrastructure/InstalledAppShutdownCoordinator.cs src/CodexQuotaHud.App/Infrastructure/InstalledAppShutdownPlatform.cs tests/CodexQuotaHud.App.Tests/Infrastructure/InstalledAppShutdownCoordinatorTests.cs
git commit -m "feat: replace installed HUD before preview"
```

---

### Task 3: Integrate Mode-Specific Startup and Error Reporting

**Files:**
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/AppLaunchModeTests.cs`

**Interfaces:**
- Consumes: `InstalledAppShutdownCoordinator.TryAcquireForPreview`,
  `InstalledAppShutdownListener`, `InstalledAppLauncher.ExecutablePath`, and
  existing `RequestExit`.
- Produces: internal static
  `bool TryAcquireForLaunch(bool preview, Func<IDisposable?> acquireNormal, Func<(bool Success, IDisposable? Lease, string? Error)> acquirePreview, Action<string> showError, out IDisposable? lease)`.

- [ ] **Step 1: Write failing launch-boundary tests**

Add tests proving preview and normal mode take different paths:

```csharp
[Fact]
public void PreviewLaunch_UsesReplacementAcquisitionAndReportsFailure()
{
    var normalCalls = 0;
    var messages = new List<string>();

    var result = App.TryAcquireForLaunch(
        preview: true,
        acquireNormal: () => { normalCalls++; return new FakeLease(); },
        acquirePreview: () => (false, null, "无法关闭正式版"),
        showError: messages.Add,
        out var lease);

    Assert.False(result);
    Assert.Null(lease);
    Assert.Equal(0, normalCalls);
    Assert.Equal(["无法关闭正式版"], messages);
}

[Fact]
public void NormalLaunch_DoesNotInvokeReplacementOrShowError()
{
    var replacementCalls = 0;
    var messages = new List<string>();

    var result = App.TryAcquireForLaunch(
        preview: false,
        acquireNormal: () => null,
        acquirePreview: () =>
        {
            replacementCalls++;
            return (true, new FakeLease(), null);
        },
        showError: messages.Add,
        out var lease);

    Assert.False(result);
    Assert.Null(lease);
    Assert.Equal(0, replacementCalls);
    Assert.Empty(messages);
}
```

Also add a successful preview test that asserts the returned fake lease is
preserved, and a successful normal test.

- [ ] **Step 2: Run launch-mode tests and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~AppLaunchModeTests
```

Expected: compilation fails because `TryAcquireForLaunch` does not exist.

- [ ] **Step 3: Implement the testable launch boundary**

Change `_singleInstance` from `SingleInstanceGuard?` to `IDisposable?`, add:

```csharp
private InstalledAppShutdownListener? _shutdownListener;
```

Implement `TryAcquireForLaunch` so normal mode invokes only
`acquireNormal`. Preview invokes only `acquirePreview`, forwards a non-empty
failure string to `showError`, and returns its lease on success.

In `OnStartup`, compute `var preview = IsPreviewLaunch(e.Args)` before mutex
handling. Create the installed path through `InstalledAppLauncher`, and call:

```csharp
var acquired = TryAcquireForLaunch(
    preview,
    () => SingleInstanceGuard.TryAcquire(),
    () =>
    {
        var coordinator = new InstalledAppShutdownCoordinator(
            installedAppLauncher.ExecutablePath,
            () => SingleInstanceGuard.TryAcquire(),
            InstalledAppShutdownPlatform.Instance);
        var success = coordinator.TryAcquireForPreview(
            out var lease,
            out var error);
        return (success, lease, error);
    },
    message => MessageBox.Show(
        message,
        "Codex Quota HUD — 开发预览",
        MessageBoxButton.OK,
        MessageBoxImage.Warning),
    out _singleInstance);
```

If acquisition fails, call `Shutdown()` and return exactly as today. If normal
mode succeeds, start:

```csharp
_shutdownListener = new InstalledAppShutdownListener(
    () => Dispatcher.BeginInvoke(RequestExit));
```

Do this after successful mutex acquisition and before constructing normal
resources. Do not start the listener for preview.

Add listener disposal to both asynchronous and emergency cleanup before
disposing `_singleInstance`. Null the field during asynchronous cleanup.

- [ ] **Step 4: Run focused startup tests and verify GREEN**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~AppLaunchModeTests|FullyQualifiedName~InstalledAppShutdown"
```

Expected: all selected tests pass. Existing `CompleteExit` tests must still
prove cleanup occurs before the installed executable launches.

- [ ] **Step 5: Run the complete automated suite and Release build**

Run:

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
```

Expected: all tests pass; build has zero warnings and zero errors;
`git diff --check` prints nothing.

- [ ] **Step 6: Commit startup integration**

```powershell
git add src/CodexQuotaHud.App/App.xaml.cs tests/CodexQuotaHud.App.Tests/AppLaunchModeTests.cs
git commit -m "feat: close installed HUD when preview starts"
```

---

### Task 4: Documentation, Regression Verification, and Manual Handoff

**Files:**
- Modify: `README.md`
- Modify: `CURRENT_TASK.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `CHANGELOG_AI.md`

**Interfaces:**
- Consumes: final test counts and actual implementation behavior from Tasks
  1–3.
- Produces: accurate handoff documentation and a clean, verified branch ready
  for integration.

- [ ] **Step 1: Update documentation with exact behavior**

Document all of the following:

- launching the desktop development-preview shortcut closes installed mode
  first;
- listener-enabled builds exit gracefully through normal cleanup;
- older installed builds use the exact standard installation-path fallback;
- no same-name process at another path is force-closed;
- failure displays a message and does not open preview;
- reverse handoff remains `退出预览并打开正式版`;
- installed `v1.0.0` is not changed merely by implementing this source change;
- final Core, App, and total test counts;
- the two-direction manual acceptance remains unchecked until actually
  performed.

Do not claim the installed package was upgraded, deployed, or manually
accepted unless those actions were separately performed.

- [ ] **Step 2: Run final verification from a clean command invocation**

Run:

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore --logger "console;verbosity=minimal"
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
git diff --check
git status --short
```

Expected: every test passes, build has zero warnings/errors, diff check is
empty, and status lists only the four intended documentation files.

- [ ] **Step 3: Commit documentation**

```powershell
git add README.md CURRENT_TASK.md PROJECT_CONTEXT.md CHANGELOG_AI.md
git commit -m "docs: record symmetric preview handoff"
```

- [ ] **Step 4: Inspect the final branch**

Run:

```powershell
git status --short --branch
git log --oneline --decorate -8
```

Expected: the feature branch is clean and contains the listener, coordinator,
startup integration, and documentation commits above the plan/spec commits.

- [ ] **Step 5: Perform manual acceptance only when authorized to launch GUI processes**

From the canonical project root:

1. Start installed Codex Quota HUD and verify its tray/HUD is visible.
2. Open the existing desktop `Codex Quota HUD 开发预览` shortcut.
3. Confirm installed mode disappears before preview HUD and control window
   appear.
4. Click `退出预览并打开正式版`.
5. Confirm preview closes and exactly one installed tray/HUD returns.
6. Record whether the first direction used graceful signalling or the legacy
   exact-path fallback.

If GUI launch is authorized, use the currently installed `v1.0.0` to verify
the legacy exact-path fallback and both handoff directions. Until a
listener-enabled build is installed, report only the graceful-notification
path as not performed. If GUI launch is not authorized, report all manual
checks as not performed; do not convert their absence into a pass.

