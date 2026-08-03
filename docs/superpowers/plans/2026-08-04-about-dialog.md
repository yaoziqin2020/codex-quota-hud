# About Dialog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one reusable Codex Quota HUD “关于” window to both HUD and tray menus, then rebuild and install-test the corrected v1.2.0 release.

**Architecture:** Keep immutable product/version metadata separate from WPF presentation. One lifecycle coordinator owns at most one `AboutWindow`; normal mode and each synthetic preview composition inject the same `Show` action into their HUD and tray menus. The final v1.2.0 package is rebuilt only after automated and manual behavior checks pass.

**Tech Stack:** .NET 9, C# 13, WPF, Windows Forms tray menu, xUnit, PowerShell release scripts, Inno Setup.

## Global Constraints

- Product name is exactly `Codex Quota HUD`.
- Public author name is exactly `老姚`.
- Repository is exactly `https://github.com/yaoziqin2020/codex-quota-hud`.
- License label is exactly `MIT License`.
- Display version comes from the running App assembly and is normalized to three numeric components; it is not hardcoded to `1.2.0` in window code.
- Both the HUD context menu and tray context menu place `关于` immediately before `退出`.
- At most one About window may exist per running composition.
- Do not add update checks, network version requests, feedback, privacy pages, Designer-only menus, or new UI dependencies.
- The public Setup remains ordinary-user software: normal desktop shortcut and startup are selected by default; Setup never creates a Developer Preview shortcut.
- The optional Skin Designer Setup component remains visible and unchecked by default.
- The pre-About v1.2.0 artifacts are obsolete and must be rebuilt before installation or release use.

---

### Task 1: About metadata and single-window lifecycle

**Files:**
- Create: `src/CodexQuotaHud.App/UI/About/AboutInformation.cs`
- Create: `src/CodexQuotaHud.App/UI/About/IAboutWindow.cs`
- Create: `src/CodexQuotaHud.App/UI/About/AboutWindowCoordinator.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/AboutInformationTests.cs`
- Create: `tests/CodexQuotaHud.App.Tests/UI/AboutWindowCoordinatorTests.cs`

**Interfaces:**
- Produces: `AboutInformation.Current`, `AboutInformation.FormatVersion(Version?)`, `IAboutWindow`, and `AboutWindowCoordinator.Show()` / `Dispose()`.
- Consumes: version metadata from `typeof(AboutInformation).Assembly.GetName().Version`.

- [ ] **Step 1: Write failing metadata tests**

Add literal expectations that prove version suffixes are not exposed and invalid metadata is safe:

```csharp
[Theory]
[InlineData(1, 2, 0, "1.2.0")]
[InlineData(7, 4, 19, "7.4.19")]
public void FormatVersion_UsesThreeNumericComponents(
    int major, int minor, int build, string expected) =>
    Assert.Equal(expected,
        AboutInformation.FormatVersion(new Version(major, minor, build, 99)));

[Fact]
public void Current_UsesPublicProjectIdentity()
{
    var current = AboutInformation.Current;
    Assert.Equal("Codex Quota HUD", current.ProductName);
    Assert.Equal("老姚", current.Author);
    Assert.Equal("yaoziqin2020/codex-quota-hud", current.RepositoryLabel);
    Assert.Equal("https://github.com/yaoziqin2020/codex-quota-hud",
        current.RepositoryUrl);
    Assert.Equal("MIT License", current.LicenseName);
}
```

- [ ] **Step 2: Run metadata tests and verify RED**

Run:

```powershell
dotnet test tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj --no-restore --filter "FullyQualifiedName~AboutInformationTests"
```

Expected: compilation fails because `AboutInformation` does not exist.

- [ ] **Step 3: Implement immutable metadata**

Create a record with exact constants and a formatter whose only fallback is `未知`:

```csharp
internal sealed record AboutInformation(
    string ProductName,
    string VersionText,
    string Author,
    string RepositoryLabel,
    string RepositoryUrl,
    string LicenseName)
{
    internal static AboutInformation Current { get; } = new(
        "Codex Quota HUD",
        FormatVersion(typeof(AboutInformation).Assembly.GetName().Version),
        "老姚",
        "yaoziqin2020/codex-quota-hud",
        "https://github.com/yaoziqin2020/codex-quota-hud",
        "MIT License");

    internal static string FormatVersion(Version? version) =>
        version is null || version.Build < 0
            ? "未知"
            : $"{version.Major}.{version.Minor}.{version.Build}";
}
```

- [ ] **Step 4: Write failing lifecycle tests**

Use a real coordinator with a reusable namespace-level `internal sealed FakeAboutWindow` implementing the complete `IAboutWindow` contract. Assert that two `Show()` calls create one instance and activate it, a `Closed` event permits a new instance, and `Dispose()` closes the current instance. Keep the fake in `AboutWindowCoordinatorTests.cs` so preview integration tests in the same test assembly can reuse it.

```csharp
[Fact]
public void Show_ReusesOpenWindowAndReopensAfterClose()
{
    var created = new List<FakeAboutWindow>();
    using var sut = new AboutWindowCoordinator(() =>
    {
        var window = new FakeAboutWindow();
        created.Add(window);
        return window;
    });

    sut.Show();
    sut.Show();
    Assert.Single(created);
    Assert.Equal(1, created[0].ShowCalls);
    Assert.Equal(1, created[0].ActivateCalls);

    created[0].RaiseClosed();
    sut.Show();
    Assert.Equal(2, created.Count);
}
```

- [ ] **Step 5: Run lifecycle tests and verify RED**

Run the same project with filter `FullyQualifiedName~AboutWindowCoordinatorTests`.

Expected: compilation fails because `IAboutWindow` and `AboutWindowCoordinator` do not exist.

- [ ] **Step 6: Implement the lifecycle coordinator**

`IAboutWindow` exposes `Closed`, `Show()`, `Activate()`, and `Close()`. `AboutWindowCoordinator` stores the active instance, subscribes before showing, activates an existing instance, clears the reference only for the matching `Closed` sender, and closes/unsubscribes on disposal.

- [ ] **Step 7: Run both new test classes and verify GREEN**

Run with filter:

```text
FullyQualifiedName~AboutInformationTests|FullyQualifiedName~AboutWindowCoordinatorTests
```

Expected: all tests pass with zero warnings or errors.

- [ ] **Step 8: Commit Task 1**

```powershell
git add src/CodexQuotaHud.App/UI/About tests/CodexQuotaHud.App.Tests/UI/AboutInformationTests.cs tests/CodexQuotaHud.App.Tests/UI/AboutWindowCoordinatorTests.cs
git commit -m "feat: add about dialog model and lifecycle"
```

### Task 2: WPF About window and shared menu actions

**Files:**
- Create: `src/CodexQuotaHud.App/UI/About/AboutWindow.xaml`
- Create: `src/CodexQuotaHud.App/UI/About/AboutWindow.xaml.cs`
- Create: `src/CodexQuotaHud.App/UI/About/AboutLinkLauncher.cs`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml`
- Modify: `src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs`
- Modify: `src/CodexQuotaHud.App/UI/TrayController.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/UI/TraySkinMenuTests.cs`

**Interfaces:**
- Consumes: `AboutInformation.Current`, `IAboutWindow`, and one injected `Action showAbout`.
- Produces: `AboutWindow`, `AboutLinkLauncher.TryOpen(string, out string?)`, and matching About actions in both menus.

- [ ] **Step 1: Write the failing menu integration test**

Extend `TraySkinMenuTests` with a real `QuotaOrbWindow` and `TrayController` sharing one counting action. Assert the last two labels are `关于`, `退出`; raise the WPF menu click and call `PerformClick()` on the tray item; assert two calls total.

```csharp
Assert.Equal(["关于", "退出"], WpfTailLabels(window));
Assert.Equal(["关于", "退出"], TrayTailLabels(tray));
aboutWpf.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
aboutTray.PerformClick();
Assert.Equal(2, aboutCalls);
```

- [ ] **Step 2: Run the menu test and verify RED**

Run:

```powershell
dotnet test tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj --no-restore --filter "FullyQualifiedName~AboutEntries_PrecedeExitAndInvokeSharedAction"
```

Expected: compilation or assertion failure because the new constructor action and menu entries do not exist.

- [ ] **Step 3: Add the shared menu action**

Add a non-null `_showAbout` action to `QuotaOrbWindow`, preserve existing overloads with a no-op default, and add an internal overload accepting the action. Add this XAML immediately before `退出`:

```xml
<MenuItem x:Name="AboutMenuItem" Header="关于" Click="OnAboutClick" />
```

The click handler calls only `_showAbout()`. Add the same optional action to `TrayController`; create `关于` immediately before `退出` and wire its click to the same action.

- [ ] **Step 4: Run the menu test and verify GREEN**

Expected: the exact integration test passes.

- [ ] **Step 5: Build the compact About window**

Create a non-resizable, non-taskbar WPF window titled `关于 Codex Quota HUD`, using `Assets/AppIcon.ico`, a compact vertical layout, a clickable repository `Hyperlink`, and one `关闭` button. In the constructor, bind exact `AboutInformation` values and show `版本 {VersionText}`.

`AboutWindow` implements `IAboutWindow`. Its repository handler calls:

```csharp
if (!AboutLinkLauncher.TryOpen(information.RepositoryUrl, out var error))
{
    MessageBox.Show(this, error, "Codex Quota HUD",
        MessageBoxButton.OK, MessageBoxImage.Warning);
}
```

`AboutLinkLauncher` uses `ProcessStartInfo` with `UseShellExecute = true`, returns `false` plus `无法打开项目主页。` on launch failure, and never terminates the HUD.

- [ ] **Step 6: Make the default coordinator create the real window**

Add a parameterless production constructor:

```csharp
internal AboutWindowCoordinator()
    : this(() => new AboutWindow(AboutInformation.Current))
{
}
```

- [ ] **Step 7: Run About, tray, and WPF UI tests**

Run filters `About`, `TraySkinMenuTests`, and `QuotaOrbWindowStartupTests` in the App test project.

Expected: all selected tests pass with zero warnings or errors.

- [ ] **Step 8: Commit Task 2**

```powershell
git add src/CodexQuotaHud.App/UI/About src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml src/CodexQuotaHud.App/UI/QuotaOrbWindow.xaml.cs src/CodexQuotaHud.App/UI/TrayController.cs tests/CodexQuotaHud.App.Tests/UI
git commit -m "feat: add about window to hud menus"
```

### Task 3: Normal and preview composition integration

**Files:**
- Modify: `src/CodexQuotaHud.App/App.xaml.cs`
- Modify: `src/CodexQuotaHud.App/Preview/SyntheticPreviewComposition.cs`
- Modify: `src/CodexQuotaHud.App/Preview/PreviewComposition.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/SyntheticPreviewCompositionTests.cs`
- Modify: `tests/CodexQuotaHud.App.Tests/Preview/PreviewCompositionTests.cs`

**Interfaces:**
- Consumes: one `AboutWindowCoordinator` per normal App or synthetic composition.
- Produces: the same About instance from HUD and tray in normal and `--preview` modes, with deterministic cleanup.

- [ ] **Step 1: Write failing composition ownership tests**

Add an internal constructor seam that accepts an `AboutWindowCoordinator` only after this test has failed. The test injects a coordinator backed by `FakeAboutWindow`, clicks the real HUD and tray menu items, and proves both reach one lifecycle owner without opening the real UI:

```csharp
[Fact]
public void HudAndTrayAboutEntriesShareOneWindowCoordinator()
{
    RunSta(() =>
    {
        var fake = new FakeAboutWindow();
        using var about = new AboutWindowCoordinator(() => fake);
        using var composition = new PreviewComposition(
            Dispatcher.CurrentDispatcher,
            () => { },
            new InstalledAppLauncher(
                @"C:\Missing",
                _ => false,
                _ => throw new InvalidOperationException()),
            about);

        composition.HudWindow.AboutMenuItem.RaiseEvent(
            new RoutedEventArgs(MenuItem.ClickEvent));
        FindTrayItem(composition.Tray, "关于").PerformClick();

        Assert.Equal(1, fake.ShowCalls);
        Assert.Equal(1, fake.ActivateCalls);
    });
}
```

Add this helper in `PreviewCompositionTests` to inspect the real tray menu without adding a production-only accessor:

```csharp
private static Forms.ToolStripMenuItem FindTrayItem(
    TrayController tray,
    string label)
{
    var notifyIcon = Assert.IsType<Forms.NotifyIcon>(typeof(TrayController)
        .GetField("_notifyIcon", BindingFlags.Instance | BindingFlags.NonPublic)!
        .GetValue(tray));
    return Assert.IsType<Forms.ToolStripMenuItem>(notifyIcon.ContextMenuStrip!
        .Items.Cast<Forms.ToolStripItem>()
        .Single(item => item.Text == label));
}
```

Add a disposal test using a fresh fake and coordinator; call `composition.Show()`, then `composition.Dispose()`, and assert the fake received exactly one `Close()` call.

- [ ] **Step 2: Run the new composition tests and verify RED**

Expected: tests fail because compositions do not yet own or pass an About coordinator.

- [ ] **Step 3: Integrate normal mode**

Add `_about` to `App`. Construct it before `_window`; pass `_about.Show` to both `QuotaOrbWindow` and `TrayController`; dispose it during `CompleteExit` before closing the HUD window.

- [ ] **Step 4: Integrate synthetic and preview modes**

Let `SyntheticPreviewComposition` own one coordinator, pass its `Show` action into its HUD, expose the same action internally for `PreviewComposition.Tray`, and dispose it exactly once. Its internal constructor accepts an optional prebuilt coordinator for tests; ownership still transfers to the composition. Extend the existing internal `PreviewComposition` constructor with that same optional coordinator and pass it through. The Skin Designer automatically receives the default behavior because it already owns a `SyntheticPreviewComposition`.

- [ ] **Step 5: Run composition tests and verify GREEN**

Run App test filters `PreviewComposition`, `SyntheticPreviewComposition`, and `About`.

Expected: all selected tests pass.

- [ ] **Step 6: Commit Task 3**

```powershell
git add src/CodexQuotaHud.App/App.xaml.cs src/CodexQuotaHud.App/Preview tests/CodexQuotaHud.App.Tests/Preview
git commit -m "feat: share about dialog across app modes"
```

### Task 4: Documentation, full verification, and v1.2.0 rebuild

**Files:**
- Modify: `README.md`
- Modify: `CURRENT_TASK.md`
- Modify: `CHANGELOG_AI.md`
- Modify: `docs/verification/skin-designer-manual-test-matrix.md`
- Regenerate (ignored release output): `artifacts/release/CodexQuotaHud-Setup-v1.2.0.exe`
- Regenerate (ignored release output): `artifacts/release/CodexQuotaHud-v1.2.0-win-x64.zip`
- Regenerate (ignored release output): `artifacts/release/SHA256SUMS.txt`

**Interfaces:**
- Consumes: completed About behavior and the fixed `None -> Dual` preview transition.
- Produces: truthful source status, passing verification evidence, and one installable v1.2.0 candidate.

- [ ] **Step 1: Update release-facing documentation**

Record the About menu/window, runtime version display, the `None -> visible mode` regression and fix, automated counts, and manual status. Do not claim public release, signing, installation success, GitHub tag, or upload before those actions occur.

- [ ] **Step 2: Run formatting and full automated verification**

Run:

```powershell
git diff --check
dotnet test CodexQuotaHud.sln --no-restore --logger "console;verbosity=minimal"
dotnet build CodexQuotaHud.sln --no-restore -c Release
```

Expected: zero failed tests and zero build warnings/errors. If the previously documented intermittent storage test reappears, preserve its complete evidence and stop the release decision until root-caused.

- [ ] **Step 3: Commit source and verification docs**

```powershell
git add README.md CURRENT_TASK.md CHANGELOG_AI.md docs/verification/skin-designer-manual-test-matrix.md
git commit -m "docs: record about dialog verification"
```

- [ ] **Step 4: Rebuild the real v1.2.0 artifacts**

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\package-release.ps1 -Version 1.2.0
```

Expected: Setup, ZIP, and `SHA256SUMS.txt` are recreated under `artifacts\release`; App and Designer report file version `1.2.0.0`; Setup retains its stable production AppId and ordinary-user tasks.

- [ ] **Step 5: Validate artifacts before installation**

Check SHA-256 values against `SHA256SUMS.txt`; confirm Setup is unsigned but structurally valid; inspect ZIP contents; confirm no `--preview` Setup shortcut and no default Designer component selection.

- [ ] **Step 6: Perform the real upgrade installation**

Use the already preserved settings/shortcut backup. Install `CodexQuotaHud-Setup-v1.2.0.exe` over the current installation with the optional Designer selected for this maintainer-machine test while preserving ordinary-user Setup defaults. Verify startup registration and the normal desktop/Start-menu shortcut before restoring the maintainer-only desktop state.

- [ ] **Step 7: Perform manual runtime acceptance**

Verify normal mode and `--preview` mode, both menu surfaces, version `1.2.0`, author `老姚`, the GitHub link, one-window behavior, closing without exiting HUD, and Skin Designer `None -> Dual`, `None -> 5h`, and `None -> Week` recovery.

- [ ] **Step 8: Restore maintainer-only desktop intent and clean obsolete artifacts**

After public Setup behavior is proven, leave this machine with only `Codex Quota HUD 开发预览` on the desktop using `--preview`, while retaining formal startup behavior. Remove only the obsolete generated v0.0.0 and pre-About v1.2.0 release artifacts after the new v1.2.0 candidate is verified; keep the install backup until acceptance is complete.

- [ ] **Step 9: Report release boundary**

Report exact artifact paths, hashes, installed version, automated/manual evidence, retained backup, and anything not verified. Do not tag, push, publish a GitHub Release, or upload assets without a separate explicit user instruction.
