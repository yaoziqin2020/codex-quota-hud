# Codex Quota HUD Inno Setup Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build and release a bilingual, current-user `CodexQuotaHud-Setup-v1.1.0.exe` with safe in-place upgrade and uninstall while retaining the existing ZIP workflow.

**Architecture:** Inno Setup 6 owns the visible wizard, installed files, shortcuts, startup task, Apps & Features entry, and uninstaller. A narrow, testable PowerShell lifecycle helper owns graceful/exact-path process shutdown, legacy backup/rollback, and optional settings deletion; versioned publish and packaging scripts feed one reviewed payload into both Setup and ZIP.

**Tech Stack:** C# 13, .NET 9, PowerShell 5.1+, Inno Setup 6, xUnit, GitHub Actions Windows runners

## Global Constraints

- Primary installer asset: `CodexQuotaHud-Setup-v1.1.0.exe`.
- Fallback asset: `CodexQuotaHud-v1.1.0-win-x64.zip`.
- Checksum asset: `SHA256SUMS.txt`, with SHA-256 entries for Setup and ZIP.
- Installer technology is Inno Setup 6; no custom WPF bootstrapper, MSI, or MSIX.
- Install only for the current user with `PrivilegesRequired=lowest`; never request elevation.
- Production destination is fixed at `%LOCALAPPDATA%\Programs\CodexQuotaHud`.
- Use one stable `AppId` for `v1.1.0` and every later installer version.
- Support Simplified Chinese and English; initial selection follows Windows language.
- Always create the normal Start Menu shortcut.
- Startup task is selected by default and removable when deselected on upgrade.
- Normal desktop shortcut is selected by default and removable when deselected on upgrade.
- Developer Preview desktop shortcut uses the same EXE plus `--preview`; it is not selected by default.
- Upgrade must preserve `%LOCALAPPDATA%\CodexQuotaHud`, including `settings.json` and `preview-window.json`.
- Legacy `v1.0.0` replacement signals graceful shutdown first and may force-close only the exact standard installed executable.
- Failure or cancellation during legacy migration restores the prior payload and does not launch the new app.
- Default uninstall preserves user settings; explicit purge removes only the exact `%LOCALAPPDATA%\CodexQuotaHud` directory after boundary and reparse-point checks.
- `v1.1.0` is unsigned; do not claim a verified publisher.
- Do not add an automatic updater, GitHub Packages publication, or a second preview binary.
- Do not move or replace the `v1.0.0` tag or assets.
- Follow TDD and commit each completed task separately.

---

## File Structure

- Modify `scripts/publish.ps1`
  - Accepts one semantic version and passes it into the .NET publish.
- Create `scripts/installer-lifecycle.ps1`
  - Owns exact-target validation, shutdown, legacy backup/commit/rollback, and
    optional settings purge.
- Create `installer/CodexQuotaHud.iss`
  - Owns the bilingual per-user wizard, tasks, files, registry, shortcuts,
    lifecycle calls, uninstall checkbox, and stable application identity.
- Create `scripts/build-installer.ps1`
  - Validates inputs, invokes `ISCC.exe`, and verifies the Setup artifact.
- Modify `scripts/package-release.ps1`
  - Publishes once, builds Setup and ZIP from the same payload, and writes
    `SHA256SUMS.txt` only after both artifacts pass validation.
- Create `scripts/test-installer.ps1`
  - Exercises a compile-time isolated installer variant without touching the
    production installation.
- Split/add packaging tests:
  - Modify `tests/CodexQuotaHud.App.Tests/Packaging/PackagingScriptTests.cs`
  - Create `tests/CodexQuotaHud.App.Tests/Packaging/InstallerLifecycleTests.cs`
  - Create `tests/CodexQuotaHud.App.Tests/Packaging/InstallerBuildTests.cs`
- Modify `.github/workflows/ci.yml`
  - Installs/locates Inno Setup, builds the real installer, and runs isolated
    installer smoke checks.
- Create `docs/releases/v1.1.0.md`
- Modify `README.md`, `CURRENT_TASK.md`, `PROJECT_CONTEXT.md`, and
  `CHANGELOG_AI.md`

---

### Task 1: One-Source Versioned Publish

**Files:**
- Modify: `scripts/publish.ps1`
- Modify: `scripts/package-release.ps1`
- Modify: `tests/CodexQuotaHud.App.Tests/Packaging/PackagingScriptTests.cs`

**Interfaces:**
- Produces:

```powershell
.\scripts\publish.ps1 -Version 1.1.0
```

- `publish.ps1` keeps its existing internal-test parameters and adds:

```powershell
[ValidatePattern('^\d+\.\d+\.\d+$')]
[string] $Version = '1.1.0'
```

- Later tasks consume the exact published directory:

```text
artifacts/CodexQuotaHud-win-x64
```

- [ ] **Step 1: Add failing version-propagation tests**

Extend `Publish_UsesSelfContainedSingleFileWinX64Contract` to invoke:

```csharp
var result = await RunPowerShellAsync(
    Script("publish.ps1"),
    "-Version", "1.1.0",
    "-ProjectPath", Project("CodexQuotaHud.App.csproj"),
    "-OutputPath", output,
    "-DotNetExecutable", fakeDotNet,
    "-InternalTestMode",
    "-InternalArgumentCapturePath", capture);
```

Add assertions:

```csharp
Assert.Contains("-p:Version=1.1.0", arguments);
Assert.Contains("-p:FileVersion=1.1.0.0", arguments);
Assert.Contains("-p:AssemblyVersion=1.1.0.0", arguments);
```

Add:

```csharp
[Theory]
[InlineData("1")]
[InlineData("1.1")]
[InlineData("v1.1.0")]
[InlineData("1.1.0-beta")]
public async Task Publish_RejectsNonReleaseVersion(string version)
{
    using var temp = new TemporaryDirectory();
    var result = await RunPowerShellAsync(
        Script("publish.ps1"),
        "-Version", version,
        "-OutputPath", Path.Combine(temp.Path, "published"),
        "-InternalTestMode");

    Assert.NotEqual(0, result.ExitCode);
}
```

- [ ] **Step 2: Run the publish tests and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~PackagingScriptTests.Publish"
```

Expected: the argument assertions fail because `publish.ps1` does not accept
or propagate `-Version`.

- [ ] **Step 3: Implement version validation and propagation**

Add the version parameter, derive:

```powershell
$fourPartVersion = "$Version.0"
```

and add these publish arguments:

```powershell
"-p:Version=$Version"
"-p:FileVersion=$fourPartVersion"
"-p:AssemblyVersion=$fourPartVersion"
```

`package-release.ps1` must pass its validated `-Version` into
`publish.ps1`; it must not call an unversioned publish.

- [ ] **Step 4: Verify GREEN**

Run the focused command from Step 2. Expected: every selected test passes.

Then run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~PackagingScriptTests
```

Expected: all existing packaging safety tests remain green.

- [ ] **Step 5: Commit**

```powershell
git add scripts/publish.ps1 scripts/package-release.ps1 tests/CodexQuotaHud.App.Tests/Packaging/PackagingScriptTests.cs
git commit -m "build: propagate release version into publish"
```

---

### Task 2: Installer Lifecycle Safety Helper

**Files:**
- Create: `scripts/installer-lifecycle.ps1`
- Create: `tests/CodexQuotaHud.App.Tests/Packaging/InstallerLifecycleTests.cs`

**Interfaces:**
- Produces these actions:

```powershell
.\scripts\installer-lifecycle.ps1 `
  -Action PrepareInstall|CommitInstall|RollbackInstall|PrepareUninstall|PurgeSettings `
  -InstallPath <absolute-path> `
  -LocalAppDataRoot <absolute-path> `
  -LegacyBackupPath <absolute-path>
```

- Production callers may omit `LocalAppDataRoot`; it then resolves from
  `Environment.SpecialFolder.LocalApplicationData`.
- The production legacy backup is a uniquely suffixed sibling of the install
  directory:

```text
%LOCALAPPDATA%\Programs\CodexQuotaHud.legacy-backup.<guid>
```

  It must normalize under the exact `%LOCALAPPDATA%\Programs` parent, match the
  `CodexQuotaHud.legacy-backup.` prefix, and pass the same root/reparse-point
  protections as the install target.
- Internal tests additionally consume:

```powershell
-InternalTestMode
-InternalProcessSnapshotPath <json>
-InternalActionLogPath <json>
-InternalSkipShutdownSignal
```

- The JSON action log uses:

```json
[
  {"Action":"SignalShutdown","EventName":"Local\\CodexQuotaHud.ShutdownRequested"},
  {"Action":"StopProcess","ProcessId":101,"ExecutablePath":"..."},
  {"Action":"BackupLegacy","Source":"...","Destination":"..."}
]
```

- [ ] **Step 1: Write failing exact-target shutdown tests**

Create tests proving:

```csharp
[Fact]
public async Task PrepareInstall_SignalsThenStopsOnlyExactInstalledProcess()
```

Use a snapshot containing:

- one `CodexQuotaHud.App.exe` at the exact install path;
- one same-name executable elsewhere;
- one different process at the install path.

Assert:

```csharp
Assert.Equal(
    new[] { "SignalShutdown", "WaitForExit", "StopProcess", "WaitForExit" },
    actions.Select(ActionName));
Assert.Equal(101, SingleAction(actions, "StopProcess").ProcessId);
```

Add rejection tests for:

- install target equal to Local App Data, `Programs`, user profile, or a
  filesystem root;
- target other than exact `Programs\CodexQuotaHud`;
- target or settings directory containing a reparse point;
- a process whose path cannot be inspected.

- [ ] **Step 2: Run and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~InstallerLifecycleTests
```

Expected: compilation/test setup fails because the lifecycle script does not
exist.

- [ ] **Step 3: Implement target validation and shutdown**

Port the existing proven normalization/reparse checks into the new focused
script without weakening them. Define:

```powershell
function Get-ValidatedInstallTarget(
    [string] $InstallPath,
    [string] $LocalAppDataRoot)

function Get-ValidatedSettingsTarget(
    [string] $SettingsPath,
    [string] $LocalAppDataRoot)

function Stop-ExactInstalledInstance(
    [string] $ExecutablePath)
```

Production shutdown order is:

```powershell
try {
    $event = [System.Threading.EventWaitHandle]::OpenExisting(
        'Local\CodexQuotaHud.ShutdownRequested')
    [void]$event.Set()
}
catch [System.Threading.WaitHandleCannotBeOpenedException] {
    # Listener-incompatible legacy version; continue to bounded fallback.
}

# Wait up to two seconds for exact installed process to exit.
# Then Stop-Process only when name and normalized executable path both match.
# Wait up to ten seconds and throw if the exact process remains.
```

No process-name-only kill is allowed.

- [ ] **Step 4: Write failing legacy backup/rollback tests**

Add:

```csharp
[Fact]
public async Task LegacyMigration_BackupCommitAndRollbackAreIdempotent()
```

Verify:

1. `PrepareInstall` copies the exact legacy directory to the supplied backup;
2. `RollbackInstall` removes only the new exact target and restores the backup;
3. a second rollback is a no-op;
4. `CommitInstall` removes only the supplied backup;
5. a sibling marker remains.

Add failure tests for a backup outside the validated `Programs` parent, a
backup without the exact `CodexQuotaHud.legacy-backup.` prefix, and any backup
reparse point. In internal mode the equivalent target is:

```text
<temp>\LocalAppData\Programs\CodexQuotaHud.legacy-backup.<guid>
```

- [ ] **Step 5: Implement legacy actions**

For `PrepareInstall`, back up only when:

```powershell
Test-Path "$InstallPath\CodexQuotaHud.App.exe"
```

and the Inno caller declared the install as legacy. Copy into the already
validated `LegacyBackupPath`, write a marker file:

```text
CodexQuotaHud.LegacyBackup.json
```

containing only normalized source/destination and no user data.

`RollbackInstall` validates the marker, deletes only the exact install target,
and restores the backup. `CommitInstall` deletes only the validated backup.

- [ ] **Step 6: Write failing purge tests**

Add:

```csharp
[Fact]
public async Task PurgeSettings_RemovesOnlyExactSettingsDirectory()
```

Verify removal of:

```text
<LocalAppData>\CodexQuotaHud\settings.json
<LocalAppData>\CodexQuotaHud\preview-window.json
```

while preserving Local App Data sibling directories and rejecting parent,
profile, root, or reparse-point targets.

- [ ] **Step 7: Implement purge and verify GREEN**

`PurgeSettings` may remove only:

```powershell
Join-Path $LocalAppDataRoot 'CodexQuotaHud'
```

after full boundary and reparse checks.

Run the focused command from Step 2. Expected: all lifecycle tests pass.

- [ ] **Step 8: Commit**

```powershell
git add scripts/installer-lifecycle.ps1 tests/CodexQuotaHud.App.Tests/Packaging/InstallerLifecycleTests.cs
git commit -m "feat: add safe installer lifecycle helper"
```

---

### Task 3: Bilingual Inno Setup Wizard and Builder

**Files:**
- Create: `installer/CodexQuotaHud.iss`
- Create: `scripts/build-installer.ps1`
- Create: `tests/CodexQuotaHud.App.Tests/Packaging/InstallerBuildTests.cs`

**Interfaces:**
- Produces:

```powershell
.\scripts\build-installer.ps1 `
  -Version 1.1.0 `
  -PublishedPath .\artifacts\CodexQuotaHud-win-x64 `
  -OutputPath .\artifacts\release
```

- Output:

```text
artifacts/release/CodexQuotaHud-Setup-v1.1.0.exe
```

- `build-installer.ps1` internal test hooks:

```powershell
-InnoCompilerPath <fake-or-real-ISCC>
-InternalTestMode
-InternalArgumentCapturePath <json>
-InternalCompilerExitCode <int>
-InternalSkipFakeSetup
```

- [ ] **Step 1: Write failing builder tests**

Add tests:

```csharp
[Fact]
public async Task BuildInstaller_PassesExactDefinesAndOutputToIscc()

[Fact]
public async Task BuildInstaller_FailsWhenCompilerFails()

[Fact]
public async Task BuildInstaller_FailsWhenSetupOutputIsMissing()

[Theory]
[InlineData("1.1")]
[InlineData("v1.1.0")]
public async Task BuildInstaller_RejectsInvalidVersion(string version)
```

The success test asserts captured arguments include:

```text
/DAppVersion=1.1.0
/DPublishedDir=<absolute published path>
/DRepositoryRoot=<absolute repository path>
/O<absolute release path>
installer\CodexQuotaHud.iss
```

and verifies the exact Setup filename.

- [ ] **Step 2: Run and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~InstallerBuildTests
```

Expected: tests fail because `build-installer.ps1` and the Inno definition do
not exist.

- [ ] **Step 3: Implement the builder**

Validate:

- semantic version;
- exact published executable;
- production output exactly under `artifacts\release`;
- compiler candidates, in order:

```text
C:\Program Files (x86)\Inno Setup 6\ISCC.exe
C:\Program Files\Inno Setup 6\ISCC.exe
```

Production must reject internal hooks. Invoke `ISCC.exe` with the exact defines
above, propagate the exit code, and require:

```powershell
$expected = Join-Path $OutputPath "CodexQuotaHud-Setup-v$Version.exe"
```

- [ ] **Step 4: Create the core Inno definition**

Use a stable literal application ID:

```ini
#define StableAppId "{{7F6E38C7-5928-4A18-9C9B-9B6D9B90D314}"

[Setup]
AppId={#StableAppId}
AppName=Codex Quota HUD
AppVersion={#AppVersion}
AppPublisher=老姚
DefaultDirName={localappdata}\Programs\CodexQuotaHud
DisableDirPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
OutputBaseFilename=CodexQuotaHud-Setup-v{#AppVersion}
SetupIconFile={#RepositoryRoot}\src\CodexQuotaHud.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\CodexQuotaHud.App.exe
CloseApplications=no
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
```

Define tasks:

```ini
[Tasks]
Name: "startup"; Description: "{cm:StartupTask}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:DesktopTask}"; Flags: checkedonce
Name: "previewdesktopicon"; Description: "{cm:PreviewDesktopTask}"; Flags: unchecked
```

Define files and icons:

```ini
[Files]
Source: "{#PublishedDir}\CodexQuotaHud.App.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#RepositoryRoot}\scripts\installer-lifecycle.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#RepositoryRoot}\scripts\installer-lifecycle.ps1"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\Codex Quota HUD"; Filename: "{app}\CodexQuotaHud.App.exe"
Name: "{autodesktop}\Codex Quota HUD"; Filename: "{app}\CodexQuotaHud.App.exe"; Tasks: desktopicon
Name: "{autodesktop}\Codex Quota HUD 开发预览"; Filename: "{app}\CodexQuotaHud.App.exe"; Parameters: "--preview"; Tasks: previewdesktopicon
```

The `[Registry]` entry writes only:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Run\CodexQuotaHud
```

with quoted `{app}\CodexQuotaHud.App.exe --background` when `startup` is
selected.

Before applying tasks on every install/upgrade, `[Code]` deletes only the two
known desktop shortcut paths and the exact Run value. Inno then recreates only
the selected items; deselection therefore takes effect.

- [ ] **Step 5: Add lifecycle and completion wiring**

The Inno `[Code]` section:

- calls `ExtractTemporaryFile('installer-lifecycle.ps1')` before any target
  replacement and invokes that temporary copy hidden with
  `-ExecutionPolicy Bypass -NoProfile -NonInteractive`;
- detects legacy mode only when the stable Inno uninstall key is absent and
  the exact installed executable exists;
- creates the validated sibling backup path
  `{localappdata}\Programs\CodexQuotaHud.legacy-backup.<guid>` only for legacy
  migration;
- calls `PrepareInstall` before file replacement;
- calls `CommitInstall` only after `ssDone`;
- calls `RollbackInstall` from setup deinitialization when legacy preparation
  happened but installation did not finish;
- never launches the app on failure.

Use:

```ini
[Run]
Filename: "{app}\CodexQuotaHud.App.exe"; Description: "{cm:LaunchProgram,Codex Quota HUD}"; Flags: nowait postinstall skipifsilent
```

- [ ] **Step 6: Add uninstall settings checkbox**

Create a `TNewCheckBox` on `UninstallProgressForm.InnerPage` in
`InitializeUninstall`; captions:

```text
同时删除个人设置和预览窗口状态
Also remove personal settings and preview window state
```

Default:

```pascal
PurgeSettingsCheckBox.Checked := False;
```

On uninstall:

1. extract a temporary lifecycle helper before managed files are removed and
   call `PrepareUninstall` through that copy;
2. allow Inno to remove managed files, shortcuts, registry, and uninstall
   entry;
3. call `PurgeSettings` through the already extracted temporary helper only
   when the checkbox is checked.

- [ ] **Step 7: Compile the real installer and verify GREEN**

First publish:

```powershell
.\scripts\publish.ps1 -Version 1.1.0
```

Then build:

```powershell
.\scripts\build-installer.ps1 -Version 1.1.0
```

Expected:

- `ISCC.exe` exits 0;
- the Setup file exists at the exact output name;
- no separate `.bin` payload is produced.

Run the focused tests from Step 2. Expected: all pass.

- [ ] **Step 8: Commit**

```powershell
git add installer/CodexQuotaHud.iss scripts/build-installer.ps1 tests/CodexQuotaHud.App.Tests/Packaging/InstallerBuildTests.cs
git commit -m "feat: add bilingual Windows setup wizard"
```

---

### Task 4: Dual Release Packaging, Checksums, and Isolated Installer Smoke Test

**Files:**
- Modify: `scripts/package-release.ps1`
- Create: `scripts/test-installer.ps1`
- Modify: `tests/CodexQuotaHud.App.Tests/Packaging/InstallerBuildTests.cs`
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Produces:

```text
artifacts/release/CodexQuotaHud-Setup-v1.1.0.exe
artifacts/release/CodexQuotaHud-v1.1.0-win-x64.zip
artifacts/release/SHA256SUMS.txt
```

- Isolated smoke entry point:

```powershell
.\scripts\test-installer.ps1 `
  -Version 1.1.0 `
  -InstallerPath .\artifacts\release\CodexQuotaHud-Setup-v1.1.0.exe
```

- `package-release.ps1` keeps production defaults and exposes these
  internal-test-only dependency injection parameters:

```powershell
-InternalTestMode
-DotNetExecutable <fake-or-real-dotnet>
-InnoCompilerPath <fake-or-real-ISCC>
-InternalArgumentCapturePath <json>
-InternalCompilerExitCode <int>
-InternalSkipFakeSetup
-OutputPath <absolute-test-output>
```

  Production rejects the internal hooks and continues to use the canonical
  `artifacts\release` path. Internal mode requires an explicitly supplied
  temporary `OutputPath` outside the repository's production artifacts.

- [ ] **Step 1: Write failing release-artifact tests**

Add tests using fake publish and compiler tools that verify:

```csharp
Assert.True(File.Exists(setup));
Assert.True(File.Exists(zip));
Assert.True(File.Exists(checksums));
```

Parse `SHA256SUMS.txt` and assert exactly two lowercase, 64-hex hashes using:

```text
<hash>  CodexQuotaHud-Setup-v1.1.0.exe
<hash>  CodexQuotaHud-v1.1.0-win-x64.zip
```

Add failure tests proving:

- compiler failure leaves no checksum manifest;
- missing Setup or ZIP leaves no checksum manifest;
- the ZIP still contains the executable, `install.ps1`, `uninstall.ps1`,
  README, and LICENSE;
- the ZIP does not contain Setup.

Invoke `package-release.ps1 -InternalTestMode` with a fake dotnet executable
that creates the expected published payload and a fake ISCC executable that
captures arguments and creates the exact Setup filename. Do not mock the
packager's file validation, ZIP creation, or hashing.

- [ ] **Step 2: Run and verify RED**

Run:

```powershell
dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~InstallerBuildTests|FullyQualifiedName~PackagingScriptTests"
```

Expected: new dual-artifact/checksum tests fail against the current ZIP-only
packager.

- [ ] **Step 3: Implement atomic release packaging**

`package-release.ps1` must:

1. remove only the exact versioned stage/artifacts and stale checksum;
2. call `publish.ps1 -Version $Version` once;
3. build ZIP from that payload;
4. call `build-installer.ps1` with the same payload and version;
5. verify both filenames;
6. compute SHA-256 with `Get-FileHash`;
7. write `SHA256SUMS.txt` as UTF-8 without BOM only after both succeed.

Any error removes the incomplete checksum manifest and reports the missing
stage.

In internal mode it forwards only the documented fake dependency paths/hooks
to `publish.ps1` and `build-installer.ps1`. In production it never forwards or
defines internal test values.

- [ ] **Step 4: Implement isolated smoke-test build mode**

`build-installer.ps1 -InternalTestMode` passes unique defines:

```text
/DInternalTestId=<guid>
/DInternalTestRoot=<system-temp absolute path>
```

The Inno definition uses them only under `#ifdef InternalTestRoot` to redirect:

- install directory;
- Start Menu/desktop shortcut directories;
- startup registry value name;
- AppId/uninstall key.

Production compilation never defines these values.

`test-installer.ps1`:

1. creates one validated system-temp root;
2. runs the isolated Setup silently;
3. verifies executable, normal Start Menu link, selected normal desktop link,
   missing preview link by default, and test startup value;
4. writes settings and preview-state markers;
5. runs the same installer again with preview selected and startup/normal
   desktop deselected;
6. verifies removed/created task artifacts and preserved settings;
7. runs the isolated uninstaller with default preservation and verifies
   settings remain;
8. reinstalls, uninstalls with `/PURGESETTINGS`, and verifies only the exact
   test settings directory is removed;
9. cleans all test registry/shortcut/temp artifacts in `finally`.

The script refuses to run if any resolved test target equals or contains the
production install or settings path.

- [ ] **Step 5: Update CI**

After .NET test/build, add:

```yaml
- name: Install Inno Setup
  shell: powershell
  run: choco install innosetup --no-progress -y

- name: Build release candidates
  shell: powershell
  run: .\scripts\package-release.ps1 -Version 1.1.0

- name: Smoke-test isolated installer
  shell: powershell
  run: .\scripts\test-installer.ps1 -Version 1.1.0 -InstallerPath .\artifacts\release\CodexQuotaHud-Setup-v1.1.0.exe
```

Do not publish CI artifacts or create releases in this workflow.

- [ ] **Step 6: Verify GREEN**

Run the focused tests from Step 2, then:

```powershell
.\scripts\package-release.ps1 -Version 1.1.0
.\scripts\test-installer.ps1 -Version 1.1.0 -InstallerPath .\artifacts\release\CodexQuotaHud-Setup-v1.1.0.exe
```

Expected: tests pass, all three artifacts exist, and isolated install/upgrade/
uninstall smoke checks complete without touching the production installation.

- [ ] **Step 7: Commit**

```powershell
git add scripts/package-release.ps1 scripts/test-installer.ps1 tests/CodexQuotaHud.App.Tests/Packaging/InstallerBuildTests.cs .github/workflows/ci.yml
git commit -m "build: package and test setup release assets"
```

---

### Task 5: Documentation, Full Verification, and Manual Upgrade Acceptance

**Files:**
- Create: `docs/releases/v1.1.0.md`
- Modify: `README.md`
- Modify: `CURRENT_TASK.md`
- Modify: `PROJECT_CONTEXT.md`
- Modify: `CHANGELOG_AI.md`

**Interfaces:**
- Consumes the exact final artifact paths and actual verification totals.
- Produces a release candidate handoff; it does not create a tag or GitHub
  Release.

- [ ] **Step 1: Run full automated verification**

Run:

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore --logger "console;verbosity=minimal"
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
.\scripts\package-release.ps1 -Version 1.1.0
.\scripts\test-installer.ps1 -Version 1.1.0 -InstallerPath .\artifacts\release\CodexQuotaHud-Setup-v1.1.0.exe
git diff --check
```

Record actual Core, App/UI, total, build-warning, build-error, Setup size/hash,
ZIP size/hash, and smoke-test results. Do not reuse earlier counts.

- [ ] **Step 2: Write release documentation**

`README.md` must make Setup the primary path:

```text
Download CodexQuotaHud-Setup-v1.1.0.exe and double-click it.
```

Document:

- Simplified Chinese/English selection;
- no administrator requirement;
- three task options and defaults;
- direct upgrade from `v1.0.0`;
- settings preservation;
- default-preserve/optional-purge uninstall;
- unsigned SmartScreen/unknown-publisher notice;
- SHA-256 verification;
- ZIP/PowerShell fallback;
- GitHub Packages not used.

`docs/releases/v1.1.0.md` contains bilingual release notes and exact asset
names without claiming they are uploaded.

Update the three handoff documents with actual automated evidence and explicit
manual/release status.

- [ ] **Step 3: Commit the release-candidate docs**

```powershell
git add README.md docs/releases/v1.1.0.md CURRENT_TASK.md PROJECT_CONTEXT.md CHANGELOG_AI.md
git commit -m "docs: prepare v1.1.0 installer release"
```

- [ ] **Step 4: Perform real desktop acceptance only with explicit GUI/install authorization**

Using the reviewed Setup hash:

1. Record the current installed `v1.0.0` executable hash and settings files.
2. Start the installed HUD.
3. Run the real bilingual Setup without elevation.
4. Verify one Apps & Features entry at `1.1.0`.
5. Verify old settings, position, skin, animation, and preview geometry remain.
6. Verify default startup and normal desktop shortcut exist; preview shortcut
   does not.
7. Re-run Setup with preview selected and other tasks deselected.
8. Verify task removal/creation and both preview/installed handoff directions.
9. Uninstall with settings preservation and verify settings remain.
10. Reinstall and uninstall with settings purge; verify only the exact settings
    directory is removed.
11. Reinstall the accepted final build for continued use.

If GUI/install authorization is absent, stop before this step and report the
release as blocked from tagging/upload.

- [ ] **Step 5: Record acceptance**

Update `CURRENT_TASK.md`, `PROJECT_CONTEXT.md`, and `CHANGELOG_AI.md` with:

- tested commit;
- exact Setup/ZIP hashes;
- Windows version;
- upgrade, shortcut, startup, uninstall-preserve, uninstall-purge results;
- final installed state;
- any unverified path.

Commit:

```powershell
git add CURRENT_TASK.md PROJECT_CONTEXT.md CHANGELOG_AI.md
git commit -m "docs: record v1.1.0 installer acceptance"
```

---

### Task 6: Publish `v1.1.0` After Explicit Release Authorization

**Files:**
- No source changes expected after accepted documentation.

**Interfaces:**
- Consumes reviewed commit and exact files:

```text
artifacts/release/CodexQuotaHud-Setup-v1.1.0.exe
artifacts/release/CodexQuotaHud-v1.1.0-win-x64.zip
artifacts/release/SHA256SUMS.txt
```

- Produces Git tag and GitHub Release `v1.1.0`.

- [ ] **Step 1: Rebuild and re-hash from the tagged candidate**

Run:

```powershell
.\scripts\package-release.ps1 -Version 1.1.0
Get-Content .\artifacts\release\SHA256SUMS.txt
git status --short
```

Expected: hashes match the manually accepted artifacts and the worktree is
clean. If hashes differ, do not tag; repeat manual acceptance on the rebuilt
Setup.

- [ ] **Step 2: Confirm remote and tag safety**

Run:

```powershell
git fetch origin
git status --short
git rev-list --left-right --count origin/main...main
git tag --list v1.1.0
gh release view v1.1.0
```

Expected:

- local and remote `main` are synchronized;
- no local or remote `v1.1.0` exists;
- `v1.0.0` remains untouched.

- [ ] **Step 3: Request explicit release authorization**

Present:

- commit hash;
- test/build results;
- manual acceptance result;
- all three artifact filenames, sizes, and hashes;
- confirmation that `v1.0.0` is unchanged.

Do not create a tag or external release without approval.

- [ ] **Step 4: Push, tag, and create the Release**

After approval:

```powershell
git push origin main
git tag -a v1.1.0 -m "Codex Quota HUD v1.1.0"
git push origin v1.1.0
gh release create v1.1.0 `
  .\artifacts\release\CodexQuotaHud-Setup-v1.1.0.exe `
  .\artifacts\release\CodexQuotaHud-v1.1.0-win-x64.zip `
  .\artifacts\release\SHA256SUMS.txt `
  --title "Codex Quota HUD 1.1.0" `
  --notes-file .\docs\releases\v1.1.0.md
```

- [ ] **Step 5: Verify the published release**

Run:

```powershell
gh release view v1.1.0 --json tagName,name,url,assets
git ls-remote --tags origin refs/tags/v1.0.0 refs/tags/v1.1.0
```

Download or inspect each asset and verify published size/hash against
`SHA256SUMS.txt`. Confirm GitHub Packages remains unused.

- [ ] **Step 6: Final handoff**

Report:

- Release URL;
- tested/tagged commit;
- published hashes;
- installed local state;
- unsigned SmartScreen limitation;
- `v1.0.0` unchanged;
- any remaining real-user feedback item.
