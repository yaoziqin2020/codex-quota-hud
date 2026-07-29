# Codex Quota HUD Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a lightweight Windows HUD that appears only while Codex Desktop is running and displays the remaining 5-hour and weekly Codex quotas using five switchable animated skins.

**Architecture:** A .NET 9 WPF shell hosts a transparent orb window and tray menu. A small core library maps official `codex app-server` JSONL responses into a UI-neutral display state; infrastructure services monitor Codex, own the app-server child process, refresh once per minute, and persist only non-sensitive settings. The UI consumes one immutable display model, so skins never infer quota semantics.

**Tech Stack:** .NET SDK 9.0.316, `net9.0-windows`, WPF, Windows Forms `NotifyIcon`, `System.Text.Json`, xUnit, self-contained `win-x64` publishing.

## Global Constraints

- Windows 10/11 x64 only.
- Use the official stable `codex app-server` stdio JSONL transport; do not scrape Codex UI, browser cookies, or private HTTP endpoints.
- Send one `initialize` request, then an `initialized` notification, before `account/rateLimits/read`.
- Identify windows by `windowDurationMins`: `300` is 5 hours and `10080` is weekly; never depend on `primary`/`secondary` ordering.
- Remaining percent is `Math.Clamp(100 - usedPercent, 0, 100)`.
- Refresh every 60 seconds while Codex runs; retain stale success data for at most 5 minutes.
- If both windows are missing, hide the orb. If one window is missing, render the available window in single-quota mode.
- The app must have no console window and only one running instance.
- Five built-in skins are required: `EnergyRing`, `LiquidGlass`, `HudDial`, `Aurora`, and `LiquidTank`; `HudDial` is the default.
- Hidden windows stop composition animations.
- Persist only orb position, animation preference, selected skin, and last successful refresh timestamp.
- Publish a self-contained `win-x64` build so the installed copy does not require a separate .NET runtime.

---

### Task 1: Solution Skeleton and Display-State Domain

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `CodexQuotaHud.sln`
- Create: `src/CodexQuotaHud.Core/CodexQuotaHud.Core.csproj`
- Create: `src/CodexQuotaHud.Core/Models/QuotaModels.cs`
- Create: `tests/CodexQuotaHud.Core.Tests/CodexQuotaHud.Core.Tests.csproj`
- Create: `tests/CodexQuotaHud.Core.Tests/Models/QuotaDisplayStateTests.cs`

**Interfaces:**
- Produces: `QuotaWindowKind`, `QuotaWindow`, `QuotaSnapshot`, `QuotaDisplayMode`, `QuotaDisplayState`, and `SkinId`.
- `QuotaDisplayState.FromSnapshot(QuotaSnapshot?)` is the only function that decides hidden, single, or dual quota mode.

- [ ] **Step 1: Create the solution and projects**

Run:

```powershell
dotnet new globaljson --sdk-version 9.0.316 --force
dotnet new sln -n CodexQuotaHud
dotnet new classlib -n CodexQuotaHud.Core -o src/CodexQuotaHud.Core -f net9.0
dotnet new xunit -n CodexQuotaHud.Core.Tests -o tests/CodexQuotaHud.Core.Tests -f net9.0
dotnet sln add src/CodexQuotaHud.Core/CodexQuotaHud.Core.csproj
dotnet sln add tests/CodexQuotaHud.Core.Tests/CodexQuotaHud.Core.Tests.csproj
dotnet add tests/CodexQuotaHud.Core.Tests/CodexQuotaHud.Core.Tests.csproj reference src/CodexQuotaHud.Core/CodexQuotaHud.Core.csproj
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Write failing display-state tests**

Create tests covering all four states:

```csharp
[Theory]
[InlineData(false, false, QuotaDisplayMode.Hidden)]
[InlineData(true, false, QuotaDisplayMode.Single)]
[InlineData(false, true, QuotaDisplayMode.Single)]
[InlineData(true, true, QuotaDisplayMode.Dual)]
public void FromSnapshot_SelectsExpectedMode(
    bool hasFiveHour,
    bool hasWeekly,
    QuotaDisplayMode expected)
{
    var snapshot = new QuotaSnapshot(
        hasFiveHour ? Window(QuotaWindowKind.FiveHour, 62) : null,
        hasWeekly ? Window(QuotaWindowKind.Weekly, 84) : null,
        DateTimeOffset.Parse("2026-07-29T00:00:00Z"));

    Assert.Equal(expected, QuotaDisplayState.FromSnapshot(snapshot).Mode);
}

[Fact]
public void WeeklyOnly_UsesWeeklyAsPrimaryValue()
{
    var weekly = Window(QuotaWindowKind.Weekly, 84);
    var state = QuotaDisplayState.FromSnapshot(
        new QuotaSnapshot(null, weekly, DateTimeOffset.UtcNow));

    Assert.Equal(QuotaWindowKind.Weekly, state.Primary!.Kind);
    Assert.Null(state.Secondary);
}
```

- [ ] **Step 3: Run the tests and verify failure**

Run:

```powershell
dotnet test tests/CodexQuotaHud.Core.Tests/CodexQuotaHud.Core.Tests.csproj
```

Expected: compilation fails because the domain types do not exist.

- [ ] **Step 4: Implement the immutable domain types**

Create `QuotaModels.cs` with:

```csharp
public enum QuotaWindowKind { FiveHour, Weekly }
public enum QuotaDisplayMode { Hidden, Single, Dual }
public enum SkinId { EnergyRing, LiquidGlass, HudDial, Aurora, LiquidTank }

public sealed record QuotaWindow(
    QuotaWindowKind Kind,
    double RemainingPercent,
    DateTimeOffset? ResetsAt);

public sealed record QuotaSnapshot(
    QuotaWindow? FiveHour,
    QuotaWindow? Weekly,
    DateTimeOffset FetchedAt);

public sealed record QuotaDisplayState(
    QuotaDisplayMode Mode,
    QuotaWindow? Primary,
    QuotaWindow? Secondary,
    DateTimeOffset? FetchedAt,
    bool IsStale)
{
    public static QuotaDisplayState Hidden() =>
        new(QuotaDisplayMode.Hidden, null, null, null, false);

    public static QuotaDisplayState FromSnapshot(
        QuotaSnapshot? snapshot,
        bool isStale = false)
    {
        if (snapshot is null || (snapshot.FiveHour is null && snapshot.Weekly is null))
            return Hidden();

        if (snapshot.FiveHour is not null && snapshot.Weekly is not null)
            return new(QuotaDisplayMode.Dual, snapshot.FiveHour, snapshot.Weekly,
                snapshot.FetchedAt, isStale);

        var only = snapshot.FiveHour ?? snapshot.Weekly;
        return new(QuotaDisplayMode.Single, only, null, snapshot.FetchedAt, isStale);
    }
}
```

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test
git add global.json Directory.Build.props CodexQuotaHud.sln src tests
git commit -m "feat: add quota display domain"
```

Expected: all tests pass.

---

### Task 2: Official Rate-Limit Response Mapping

**Files:**
- Create: `src/CodexQuotaHud.Core/RateLimits/RateLimitMapper.cs`
- Create: `tests/CodexQuotaHud.Core.Tests/RateLimits/RateLimitMapperTests.cs`
- Create: `tests/CodexQuotaHud.Core.Tests/Fixtures/rate-limits-dual.json`
- Create: `tests/CodexQuotaHud.Core.Tests/Fixtures/rate-limits-weekly-only.json`

**Interfaces:**
- Consumes: `QuotaSnapshot` and `QuotaWindow` from Task 1.
- Produces: `RateLimitMapper.Map(JsonElement result, DateTimeOffset fetchedAt)`.

- [ ] **Step 1: Write fixtures from the stable app-server contract**

The dual fixture must contain unordered windows to prove ordering is ignored:

```json
{
  "rateLimits": {
    "primary": {
      "usedPercent": 16,
      "windowDurationMins": 10080,
      "resetsAt": 1785888000
    },
    "secondary": {
      "usedPercent": 38,
      "windowDurationMins": 300,
      "resetsAt": 1785297600
    }
  }
}
```

The weekly-only fixture uses `secondary: null`.

- [ ] **Step 2: Write failing mapper tests**

Cover:

```csharp
[Fact] public void Map_RecognizesWindowsByDuration_NotPosition();
[Fact] public void Map_ConvertsUsedToRemaining();
[Theory]
[InlineData(-20, 100)]
[InlineData(120, 0)]
public void Map_ClampsRemainingPercent(double used, double expected);
[Fact] public void Map_MissingFiveHour_ReturnsWeeklyOnly();
[Fact] public void Map_UnknownDuration_IsIgnored();
[Fact] public void Map_MissingRateLimits_ReturnsEmptySnapshot();
```

- [ ] **Step 3: Verify failure**

Run:

```powershell
dotnet test --filter FullyQualifiedName~RateLimitMapperTests
```

Expected: compilation fails because `RateLimitMapper` is missing.

- [ ] **Step 4: Implement duration-based mapping**

Use constants and a bounded helper:

```csharp
public static class RateLimitMapper
{
    public const int FiveHourMinutes = 300;
    public const int WeeklyMinutes = 10_080;

    public static QuotaSnapshot Map(JsonElement result, DateTimeOffset fetchedAt)
    {
        QuotaWindow? fiveHour = null;
        QuotaWindow? weekly = null;

        if (result.TryGetProperty("rateLimits", out var limits))
        {
            foreach (var name in new[] { "primary", "secondary" })
            {
                if (!limits.TryGetProperty(name, out var item) ||
                    item.ValueKind != JsonValueKind.Object)
                    continue;

                var mapped = MapWindow(item);
                if (mapped?.Kind == QuotaWindowKind.FiveHour) fiveHour = mapped;
                if (mapped?.Kind == QuotaWindowKind.Weekly) weekly = mapped;
            }
        }

        return new QuotaSnapshot(fiveHour, weekly, fetchedAt);
    }
}
```

`MapWindow` must reject missing/non-numeric `usedPercent` and `windowDurationMins`, convert Unix seconds with `DateTimeOffset.FromUnixTimeSeconds`, and ignore unknown durations.

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test
git add src/CodexQuotaHud.Core/RateLimits tests/CodexQuotaHud.Core.Tests
git commit -m "feat: map official Codex rate limits"
```

---

### Task 3: JSONL RPC Transport and App-Server Protocol

**Files:**
- Create: `src/CodexQuotaHud.Core/RateLimits/IQuotaClient.cs`
- Create: `src/CodexQuotaHud.App/CodexQuotaHud.App.csproj`
- Create: `src/CodexQuotaHud.App/Infrastructure/IAppServerProcess.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/JsonlRpcClient.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/CodexAppServerClient.cs`
- Create: `tests/CodexQuotaHud.App.Tests/CodexQuotaHud.App.Tests.csproj`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/JsonlRpcClientTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/CodexAppServerClientTests.cs`

**Interfaces:**
- Produces: `IQuotaClient.ReadAsync(CancellationToken)`.
- `IAppServerProcess` exposes `TextWriter StandardInput`, `TextReader StandardOutput`, `TextReader StandardError`, `bool HasExited`, and `KillAsync()`.
- `JsonlRpcClient.RequestAsync(string method, object? parameters, CancellationToken)` returns the response `result` as a cloned `JsonElement`.

- [ ] **Step 1: Create the WPF and app-test projects**

Run:

```powershell
dotnet new wpf -n CodexQuotaHud.App -o src/CodexQuotaHud.App -f net9.0
dotnet new xunit -n CodexQuotaHud.App.Tests -o tests/CodexQuotaHud.App.Tests -f net9.0
dotnet sln add src/CodexQuotaHud.App/CodexQuotaHud.App.csproj
dotnet sln add tests/CodexQuotaHud.App.Tests/CodexQuotaHud.App.Tests.csproj
dotnet add src/CodexQuotaHud.App/CodexQuotaHud.App.csproj reference src/CodexQuotaHud.Core/CodexQuotaHud.Core.csproj
dotnet add tests/CodexQuotaHud.App.Tests/CodexQuotaHud.App.Tests.csproj reference src/CodexQuotaHud.App/CodexQuotaHud.App.csproj
```

Set the WPF project:

```xml
<PropertyGroup>
  <OutputType>WinExe</OutputType>
  <TargetFramework>net9.0-windows</TargetFramework>
  <UseWPF>true</UseWPF>
  <UseWindowsForms>true</UseWindowsForms>
</PropertyGroup>
```

- [ ] **Step 2: Write failing JSONL correlation tests**

Use a fake duplex process and assert:

```csharp
[Fact]
public async Task RequestAsync_MatchesResponseById_WhenNotificationArrivesFirst()
{
    var io = new FakeAppServerProcess(
        """{"method":"account/rateLimits/updated","params":{}}""" + "\n" +
        """{"id":1,"result":{"ok":true}}""" + "\n");

    var client = new JsonlRpcClient(io.StandardInput, io.StandardOutput);
    var result = await client.RequestAsync("sample/read", null, default);

    Assert.True(result.GetProperty("ok").GetBoolean());
}
```

Also test error responses, cancellation, malformed lines, and EOF.

- [ ] **Step 3: Verify failure**

Run:

```powershell
dotnet test tests/CodexQuotaHud.App.Tests/CodexQuotaHud.App.Tests.csproj
```

- [ ] **Step 4: Implement the JSONL client**

Use one background reader loop, an `Interlocked.Increment` request id, and a `ConcurrentDictionary<long, TaskCompletionSource<JsonElement>>`. Serialize one compact JSON object per line and call `FlushAsync`. Ignore notifications that do not contain `id`; fail pending requests on EOF.

- [ ] **Step 5: Write and implement the handshake test**

The fake process must assert this exact order:

```json
{"method":"initialize","id":1,"params":{"clientInfo":{"name":"codex_quota_hud","title":"Codex Quota HUD","version":"1.0.0"}}}
{"method":"initialized"}
{"method":"account/rateLimits/read","id":2}
```

`CodexAppServerClient.ReadAsync` must initialize only once per child process, then map the `result` through `RateLimitMapper`.

- [ ] **Step 6: Run tests and commit**

Run:

```powershell
dotnet test
git add src/CodexQuotaHud.App src/CodexQuotaHud.Core/RateLimits tests/CodexQuotaHud.App.Tests
git commit -m "feat: read quotas through Codex app-server JSONL"
```

---

### Task 4: Codex Process Discovery and Child-Process Ownership

**Files:**
- Create: `src/CodexQuotaHud.App/Infrastructure/ICodexProcessMonitor.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/CodexProcessMonitor.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/CodexExecutableLocator.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/AppServerProcess.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/CodexProcessMonitorTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/CodexExecutableLocatorTests.cs`

**Interfaces:**
- Produces: `ICodexProcessMonitor.IsRunning` and `RunningChanged`.
- Produces: `CodexExecutableLocator.Find()` returning an absolute path or `null`.
- `AppServerProcess.Start(path)` launches `path app-server --listen stdio://`.

- [ ] **Step 1: Write failing discovery tests**

Inject process snapshots instead of calling `Process.GetProcesses()` in tests. Cover:

```csharp
[Fact] public void DetectsCodexDesktopCaseInsensitively();
[Fact] public void IgnoresCurrentHudProcess();
[Fact] public void EmitsOnlyWhenRunningStateChanges();
[Fact] public void LocatorPrefersExplicitEnvironmentOverride();
[Fact] public void LocatorFallsBackToRunningCodexModuleThenPath();
```

- [ ] **Step 2: Implement monitoring**

Poll every 2 seconds using `PeriodicTimer`. Treat a process named `Codex` or `codex` as the desktop app only when it is not the HUD process and either has a non-zero main window handle or its executable path contains `OpenAI.Codex_`. Catch access-denied exceptions per process instead of failing the whole poll.

- [ ] **Step 3: Implement executable lookup and secure process start**

Lookup order:

1. `CODEX_QUOTA_HUD_CODEX_PATH`.
2. Executable path from the detected Codex Desktop process.
3. `where.exe codex`.
4. `%LOCALAPPDATA%\Microsoft\WindowsApps\codex.exe`.

Launch with:

```csharp
new ProcessStartInfo
{
    FileName = absoluteCodexPath,
    ArgumentList = { "app-server", "--listen", "stdio://" },
    UseShellExecute = false,
    RedirectStandardInput = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
    WindowStyle = ProcessWindowStyle.Hidden
};
```

Assign the child to a Windows Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, so it cannot outlive the HUD after a crash or normal exit.

- [ ] **Step 4: Run tests and commit**

Run:

```powershell
dotnet test
git add src/CodexQuotaHud.App/Infrastructure tests/CodexQuotaHud.App.Tests/Infrastructure
git commit -m "feat: monitor Codex and own app-server process"
```

---

### Task 5: Refresh State Machine and Stale-Data Rules

**Files:**
- Create: `src/CodexQuotaHud.Core/Refresh/IClock.cs`
- Create: `src/CodexQuotaHud.Core/Refresh/QuotaRefreshService.cs`
- Create: `src/CodexQuotaHud.Core/Refresh/QuotaRefreshState.cs`
- Create: `tests/CodexQuotaHud.Core.Tests/Refresh/QuotaRefreshServiceTests.cs`

**Interfaces:**
- Consumes: `IQuotaClient` and `IClock`; the WPF app forwards `ICodexProcessMonitor.RunningChanged` through `SetCodexRunningAsync(bool, CancellationToken)`.
- Produces: `StateChanged(QuotaRefreshState)` where state includes `IsCodexRunning`, `IsRefreshing`, `Display`, `LastError`.
- Produces: `RefreshNowAsync(bool onlyIfStale, CancellationToken)`.

- [ ] **Step 1: Write state-machine tests with a fake clock**

Cover:

```csharp
[Fact] public async Task CodexStart_TriggersImmediateRefresh();
[Fact] public async Task CodexStop_HidesAndStopsPolling();
[Fact] public async Task PeriodicRefresh_RunsEverySixtySeconds();
[Fact] public async Task HoverRefresh_SkipsFreshData();
[Fact] public async Task FailedRefresh_KeepsSuccessForFiveMinutesAsStale();
[Fact] public async Task FailedRefresh_HidesDataAfterFiveMinutes();
[Fact] public async Task ConcurrentRefreshes_CollapseIntoOneRequest();
```

- [ ] **Step 2: Verify failure**

Run:

```powershell
dotnet test --filter FullyQualifiedName~QuotaRefreshServiceTests
```

- [ ] **Step 3: Implement one serialized refresh path**

Use a `SemaphoreSlim(1, 1)` around all refreshes. During a request emit `IsRefreshing = true` while preserving the old display value. On success store the snapshot and clear the error. On failure:

```csharp
var age = clock.UtcNow - lastSuccess.FetchedAt;
display = age <= TimeSpan.FromMinutes(5)
    ? QuotaDisplayState.FromSnapshot(lastSuccess, isStale: true)
    : QuotaDisplayState.Hidden();
```

Do not show a dialog; expose a short status string for the tray tooltip.

- [ ] **Step 4: Run tests and commit**

Run:

```powershell
dotnet test
git add src/CodexQuotaHud.Core/Refresh tests/CodexQuotaHud.Core.Tests/Refresh
git commit -m "feat: orchestrate quota refresh and stale fallback"
```

---

### Task 6: Settings, Single Instance, and Startup Registration

**Files:**
- Create: `src/CodexQuotaHud.Core/Settings/AppSettings.cs`
- Create: `src/CodexQuotaHud.Core/Settings/SettingsStore.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/SingleInstanceGuard.cs`
- Create: `src/CodexQuotaHud.App/Infrastructure/StartupRegistration.cs`
- Create: `tests/CodexQuotaHud.Core.Tests/Settings/SettingsStoreTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/SingleInstanceGuardTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/Infrastructure/StartupRegistrationTests.cs`

**Interfaces:**
- `AppSettings` fields: `Left`, `Top`, `AnimationsEnabled`, `SelectedSkin`, `LastSuccessfulRefresh`.
- `SettingsStore` reads/writes `%LOCALAPPDATA%\CodexQuotaHud\settings.json` using atomic replace.
- `StartupRegistration` owns only `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexQuotaHud`.

- [ ] **Step 1: Write failing settings tests**

Cover missing file defaults, corrupt JSON fallback, invalid skin fallback to `HudDial`, atomic save, and no serialized account/rate-limit payload fields.

- [ ] **Step 2: Implement settings**

Use:

```csharp
public sealed record AppSettings(
    double? Left = null,
    double? Top = null,
    bool AnimationsEnabled = true,
    SkinId SelectedSkin = SkinId.HudDial,
    DateTimeOffset? LastSuccessfulRefresh = null);
```

Write to `settings.json.tmp`, flush, then `File.Move(temp, target, overwrite: true)`.

- [ ] **Step 3: Implement single-instance and startup tests**

Use named mutex `Local\CodexQuotaHud.Singleton`. `StartupRegistration.Enable()` writes a quoted executable path and `--background`; `Disable()` removes only the `CodexQuotaHud` value.

- [ ] **Step 4: Run tests and commit**

Run:

```powershell
dotnet test
git add src tests
git commit -m "feat: persist HUD settings and startup state"
```

---

### Task 7: WPF Shell, Tray Menu, and Hover Details

**Files:**
- Modify: `src/CodexQuotaHud.App/App.xaml`
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Create: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml`
- Create: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Create: `src/CodexQuotaHud.App/UI/QuotaOrbViewModel.cs`
- Create: `src/CodexQuotaHud.App/UI/TrayController.cs`
- Create: `src/CodexQuotaHud.App/UI/Controls/ProgressArc.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/QuotaOrbViewModelTests.cs`

**Interfaces:**
- Consumes: `QuotaRefreshState`, `SettingsStore`, and `SkinController`.
- Produces bindable `PrimaryPercent`, `SecondaryPercent`, `PrimaryLabel`, `Details`, `IsRefreshing`, `IsStale`, and `IsVisible`.

- [ ] **Step 1: Write failing view-model tests**

Cover:

```csharp
[Fact] public void HiddenState_HidesWindow();
[Fact] public void WeeklyOnly_ShowsWeeklyLabelAndNoSecondaryRing();
[Fact] public void Dual_ShowsFiveHourInCenterAndWeeklyOutside();
[Fact] public void StaleState_AddsDataMayBeStaleMessage();
[Fact] public void SkinSelection_UpdatesImmediatelyAndPersists();
```

- [ ] **Step 2: Implement the view model**

Use `INotifyPropertyChanged`; dispatch service events onto the WPF dispatcher. Format percentages as rounded whole numbers and reset time in local time. Keep formatting out of individual skins.

- [ ] **Step 3: Build the transparent shell**

Set:

```xml
WindowStyle="None"
AllowsTransparency="True"
Background="Transparent"
ShowInTaskbar="False"
Topmost="True"
ResizeMode="NoResize"
Width="132"
Height="132"
```

The window contains:

- a skin host;
- a hover `Popup` with only available quota rows;
- an invisible drag surface;
- a shared context menu.

On drag completion clamp the position to the nearest monitor work area and save it.

- [ ] **Step 4: Implement the tray controller**

Menu items:

- `立即刷新`
- `皮肤 >` five mutually exclusive choices
- `动画` checked toggle
- disabled status line such as `上次更新：刚刚` or `暂时读不到额度`
- `退出`

The orb context menu reuses the same commands and selected state.

- [ ] **Step 5: Wire application lifecycle**

`App.OnStartup` must:

1. acquire the single-instance mutex or exit;
2. load settings;
3. create the process monitor, quota client, refresh service, view model, window, and tray;
4. keep the WPF shutdown mode explicit;
5. register startup after first successful interactive launch;
6. hide the orb until a non-hidden display state arrives.

- [ ] **Step 6: Run tests and commit**

Run:

```powershell
dotnet test
dotnet build -c Release
git add src tests
git commit -m "feat: add quota orb shell and tray controls"
```

---

### Task 8: Five Skin Controls and Animation State

**Files:**
- Create: `src/CodexQuotaHud.App/UI/Skins/IQuotaSkin.cs`
- Create: `src/CodexQuotaHud.App/UI/Skins/SkinController.cs`
- Create: `src/CodexQuotaHud.App/UI/Skins/EnergyRingSkin.xaml`
- Create: `src/CodexQuotaHud.App/UI/Skins/LiquidGlassSkin.xaml`
- Create: `src/CodexQuotaHud.App/UI/Skins/HudDialSkin.xaml`
- Create: `src/CodexQuotaHud.App/UI/Skins/AuroraSkin.xaml`
- Create: `src/CodexQuotaHud.App/UI/Skins/LiquidTankSkin.xaml`
- Create: `src/CodexQuotaHud.App/UI/Animation/OrbAnimationController.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/SkinControllerTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/OrbAnimationControllerTests.cs`

**Interfaces:**
- Each skin consumes only `QuotaSkinState(PrimaryPercent, SecondaryPercent?, PrimaryLabel, Mode, IsRefreshing, AnimationsEnabled)`.
- `SkinController.Select(SkinId)` swaps the control immediately.
- `OrbAnimationController.SetState(Hidden|Idle|Refreshing)` controls WPF storyboards.

- [ ] **Step 1: Write failing skin registration tests**

Assert all five enum values resolve, `HudDial` is the default, invalid persisted values resolve to `HudDial`, and single mode passes `SecondaryPercent = null`.

- [ ] **Step 2: Implement one shared skin contract**

```csharp
public sealed record QuotaSkinState(
    double PrimaryPercent,
    double? SecondaryPercent,
    string PrimaryLabel,
    QuotaDisplayMode Mode,
    bool IsRefreshing,
    bool AnimationsEnabled);

public interface IQuotaSkin
{
    SkinId Id { get; }
    FrameworkElement View { get; }
    void Render(QuotaSkinState state);
}
```

Use the shared `ProgressArc` control for numeric arc geometry; do not duplicate quota mapping in XAML code-behind.

- [ ] **Step 3: Implement the five visuals**

- `EnergyRing`: cyan primary arc, purple secondary arc, soft glow.
- `LiquidGlass`: glass sphere body, subtle internal highlight, optional outer secondary arc.
- `HudDial`: concentric dashed tick rings; primary and secondary rotate in opposite directions.
- `Aurora`: restrained gradient border with the lowest idle animation intensity.
- `LiquidTank`: clipped liquid level equals primary percent; secondary is an outer arc.

For every skin, `QuotaDisplayMode.Single` removes the unused second layer instead of drawing an empty track.

- [ ] **Step 4: Implement animation transitions**

Use WPF `Storyboard` and easing:

- `Hidden`: stop storyboards and release animation clocks.
- `Idle`: slow rotation, 18–30 seconds per revolution depending on skin.
- `Refreshing`: ease to 2–4 seconds per revolution.
- completion/failure: ease back to idle over 600 milliseconds.
- animations disabled: stop rotation and liquid slosh while still updating progress values.

The liquid tank uses a clipped `RectangleGeometry` or translated liquid group; do not run a frame timer in application code.

- [ ] **Step 5: Run tests and commit**

Run:

```powershell
dotnet test
dotnet build -c Release
git add src tests
git commit -m "feat: add five animated quota HUD skins"
```

---

### Task 9: Installation, Packaging, and End-to-End Verification

**Files:**
- Create: `scripts/publish.ps1`
- Create: `scripts/install.ps1`
- Create: `scripts/uninstall.ps1`
- Create: `README.md`
- Create: `docs/verification/2026-07-29-windows-checklist.md`

**Interfaces:**
- `publish.ps1` produces `artifacts/CodexQuotaHud-win-x64/`.
- `install.ps1` copies the published directory to `%LOCALAPPDATA%\Programs\CodexQuotaHud`, registers startup, and starts one hidden instance.
- `uninstall.ps1` stops only `CodexQuotaHud`, removes its Run value, and removes its installed directory after validating the exact path.

- [ ] **Step 1: Create deterministic publish script**

Run:

```powershell
dotnet publish src/CodexQuotaHud.App/CodexQuotaHud.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o artifacts/CodexQuotaHud-win-x64
```

The script must fail on non-zero exit and verify `CodexQuotaHud.App.exe` exists.

- [ ] **Step 2: Add safe install and uninstall scripts**

Installation target is exactly:

```text
%LOCALAPPDATA%\Programs\CodexQuotaHud
```

Before removing anything, `uninstall.ps1` must resolve the path and verify it equals that target. It must not recursively delete a computed parent or user profile path.

- [ ] **Step 3: Write usage and privacy documentation**

README sections:

- what the orb shows;
- why the 5-hour ring may be absent;
- five skins and switching;
- install, update, and uninstall;
- no credential/cookie storage;
- troubleshooting `codex app-server` and tray status.

Link the official app-server README used for protocol behavior:

```text
https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md
```

- [ ] **Step 4: Run automated verification**

Run:

```powershell
dotnet test -c Release
dotnet build -c Release
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
Get-Item artifacts/CodexQuotaHud-win-x64/CodexQuotaHud.App.exe
```

Expected: zero warnings/errors, all tests pass, and the executable exists.

- [ ] **Step 5: Run Windows manual verification**

Record pass/fail for:

1. launch produces no console window;
2. second launch does not create another process;
3. with Codex closed the tray remains but orb is hidden;
4. opening Codex causes an immediate read and orb appearance;
5. closing Codex hides the orb and stops the child app-server;
6. dual response shows 5-hour center plus weekly outside;
7. weekly-only response shows one layer labeled `每周`;
8. switching all five skins is immediate and survives restart;
9. hover refresh accelerates animation only when data is stale;
10. dragging survives restart and remains on-screen after monitor changes;
11. app-server failure becomes tray status without a popup;
12. idle Task Manager usage is recorded after 5 minutes.

- [ ] **Step 6: Commit the release-ready state**

Run:

```powershell
git add scripts README.md docs/verification
git commit -m "build: package Codex quota HUD for Windows"
git status --short
```

Expected: clean working tree.
