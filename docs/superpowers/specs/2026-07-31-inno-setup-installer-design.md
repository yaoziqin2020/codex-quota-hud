# Codex Quota HUD Inno Setup Installer Design

## Goal

Replace the command-line-only primary installation experience with a
double-click Windows setup wizard while retaining the existing ZIP and
PowerShell workflow as an advanced-user fallback.

The first installer release will be `v1.1.0`.

## User experience

The primary GitHub Release asset is:

```text
CodexQuotaHud-Setup-v1.1.0.exe
```

Double-clicking it opens a standard Inno Setup wizard:

1. Select Simplified Chinese or English. The initial language follows the
   Windows display language.
2. Show the welcome page and application/version identity.
3. Show installation tasks.
4. Show the ready-to-install summary.
5. Install or upgrade with visible progress.
6. Show completion, with `启动 Codex Quota HUD` selected by default.

The wizard installs only for the current user and never requests elevation.
The destination is fixed:

```text
%LOCALAPPDATA%\Programs\CodexQuotaHud
```

The user cannot redirect the production installer to another directory. This
preserves the existing install-path, handoff, process-targeting, and uninstall
safety contracts.

## Installation tasks

The task page exposes exactly three choices:

| Task | Default | Result |
|---|---:|---|
| Start Codex Quota HUD with Windows | Selected | Register the existing current-user `Run\CodexQuotaHud` value |
| Create the normal desktop shortcut | Selected | Shortcut starts `CodexQuotaHud.App.exe` normally |
| Create the Developer Preview desktop shortcut | Not selected | Shortcut starts the same EXE with `--preview` |

The normal Start Menu shortcut is always created. The Developer Preview is not
a second executable and does not install a second copy of the application.

When a task is deselected during an upgrade, Setup removes the corresponding
startup registration or shortcut instead of silently retaining an older
selection. Inno Setup may remember the previous task choices for later
upgrades, but a clean first install uses the defaults above.

## Installer technology

Use Inno Setup 6 with a repository-owned definition:

```text
installer/CodexQuotaHud.iss
```

Use a stable, repository-defined `AppId` for every future version. This keeps
upgrades in one Windows Apps & Features entry and one uninstall log.

The installer metadata includes:

- product name `Codex Quota HUD`;
- public publisher name `老姚`;
- version supplied by the build command;
- the existing application icon;
- repository and support URLs;
- current-user uninstall information;
- Windows 10/11 x64 architecture constraints.

The installer is unsigned for `v1.1.0`. It must not claim a verified Windows
publisher. README and release notes explain the possible SmartScreen or
unknown-publisher warning.

## Version source

The release version is supplied once to the packaging entry point:

```powershell
.\scripts\package-release.ps1 -Version 1.1.0
```

That value drives:

- the .NET application assembly/file/product version;
- Inno Setup `AppVersion` and display version;
- Setup and ZIP filenames;
- the staging-directory name;
- release-note checks;
- the checksum manifest.

The build fails when any generated artifact reports a different version.

## Install and upgrade flow

### Clean install

1. Validate that the embedded published payload contains exactly the expected
   application executable.
2. Validate the fixed current-user destination and reject reparse-point path
   components.
3. Install the self-contained `CodexQuotaHud.App.exe` and the Inno uninstaller.
4. Apply the three selected tasks.
5. Register one Apps & Features entry.
6. Launch the installed normal executable only from the completion action.

### Upgrade from the script-installed `v1.0.0`

The old package has no Inno uninstall entry. Setup therefore treats an
executable at the exact standard install path as a legacy installation:

1. Request graceful shutdown through the existing named shutdown event.
2. Wait for the installed single-instance mutex to be released.
3. If the legacy build cannot receive the event, fall back only to a process
   whose normalized executable path exactly equals the standard installed
   executable.
4. Never stop another same-name executable, a development build, or a process
   whose executable path cannot be inspected.
5. Back up the legacy installed payload before replacement.
6. Install `v1.1.0`, create the stable Inno uninstall record, and apply the
   selected tasks.
7. Delete the backup only after installation completes successfully.

### Later Inno upgrades

All later installers reuse the same `AppId` and destination. They update the
existing Apps & Features entry rather than creating another entry. Upgrade
does not require a prior uninstall.

### Failure and cancellation

Inno Setup reverts changes made by an incomplete setup. The installer adds an
explicit legacy-payload backup because `v1.0.0` was installed outside Inno's
uninstall history.

If shutdown, backup, extraction, copy, registry, shortcut, or final validation
fails:

- do not launch the new executable;
- restore the legacy payload when migration had started;
- retain `%LOCALAPPDATA%\CodexQuotaHud`;
- show a bilingual, actionable error;
- leave no staging directory or second Apps & Features entry.

No installation failure may require a reboot to recover the HUD.

## Settings and user data

Install and upgrade never modify:

```text
%LOCALAPPDATA%\CodexQuotaHud\settings.json
%LOCALAPPDATA%\CodexQuotaHud\preview-window.json
```

The directory may also contain future user-scoped state; Setup treats the
whole `%LOCALAPPDATA%\CodexQuotaHud` directory as user data rather than
application payload.

## Uninstall flow

The Inno uninstaller:

1. Requests graceful shutdown and uses the same exact-path legacy fallback.
2. Removes the installed executable and Inno-managed files.
3. Removes the normal and Developer Preview shortcuts created by Setup.
4. Removes only the `CodexQuotaHud` current-user startup value.
5. Removes the Apps & Features entry.

The uninstall UI offers:

```text
同时删除个人设置和预览窗口状态
```

or its English equivalent. It is not selected by default.

When not selected, `%LOCALAPPDATA%\CodexQuotaHud` remains untouched. When
selected, the uninstaller removes only that exact directory after the same
normalization, boundary, and reparse-point checks used by the existing
PowerShell uninstaller. It must never recursively delete Local AppData,
`Programs`, the user profile, or a computed parent.

## Release artifacts

`v1.1.0` contains:

```text
CodexQuotaHud-Setup-v1.1.0.exe
CodexQuotaHud-v1.1.0-win-x64.zip
SHA256SUMS.txt
```

`Setup.exe` is the recommended download. The ZIP retains the existing
PowerShell install and uninstall scripts for advanced users. It does not call,
embed, or depend on Setup.

`SHA256SUMS.txt` contains one unambiguous SHA-256 line for each release asset.
The checksums are generated only after both final artifacts exist.

Both binaries are attached to the GitHub Release. Nothing is published to
GitHub Packages because this project is not distributing a NuGet, npm, or
container package.

## Build pipeline

Add:

```text
installer/CodexQuotaHud.iss
scripts/build-installer.ps1
```

Update:

```text
scripts/publish.ps1
scripts/package-release.ps1
tests/CodexQuotaHud.App.Tests/Packaging/PackagingScriptTests.cs
```

`build-installer.ps1`:

- accepts a validated semantic version;
- requires the exact production publish directory;
- resolves the expected Inno Setup 6 compiler;
- invokes `ISCC.exe` non-interactively;
- fails on any non-zero compiler exit;
- verifies the exact Setup output path;
- does not download a compiler or mutate machine-wide configuration.

`package-release.ps1` performs one publish, then builds both artifacts from
that exact payload and writes the checksum manifest. A failed Setup build must
not leave a release directory that looks complete.

## Automated verification

Tests cover:

- semantic-version validation and one-source version propagation;
- fixed `AppId`, product identity, architecture, current-user privilege mode,
  and destination;
- bilingual language entries;
- the exact three task definitions and defaults;
- normal and Developer Preview shortcut targets and arguments;
- startup registration creation and removal;
- exact-path process shutdown and refusal to stop same-name processes
  elsewhere;
- legacy `v1.0.0` detection, backup, successful migration, and rollback;
- settings preservation on install, upgrade, and default uninstall;
- explicit settings deletion with boundary and reparse-point rejection;
- stable Apps & Features identity across upgrades;
- Setup, ZIP, and checksum filenames;
- absence of unexpected secrets, credentials, or user-specific paths.

The real Inno compiler must build the installer during Release verification.
Static text search alone is not sufficient evidence.

A disposable Windows environment or isolated test mode verifies silent clean
install, repeated install/upgrade, default uninstall, and purge-settings
uninstall without touching the developer's production installation.

## Manual acceptance and release gate

Before creating `v1.1.0`, use the final reviewed artifact on the Windows
desktop:

1. Start the currently installed script-based `v1.0.0`.
2. Run the bilingual Setup wizard without elevation.
3. Confirm graceful/fallback replacement and exactly one running installed
   HUD.
4. Confirm existing skin, position, animation, and preview-window state remain.
5. Confirm the normal desktop shortcut exists by default.
6. Confirm the Developer Preview shortcut is absent by default.
7. Upgrade again with Developer Preview selected and verify both directions of
   the installed/preview handoff.
8. Confirm task deselection removes its shortcut or startup registration.
9. Uninstall with settings preservation and confirm the settings remain.
10. Reinstall, uninstall with settings deletion selected, and confirm only the
    exact settings directory is removed.
11. Confirm Apps & Features shows one `Codex Quota HUD 1.1.0` entry throughout
    upgrade and no entry after uninstall.
12. Confirm Setup and ZIP hashes match `SHA256SUMS.txt`.

Only after automated verification and this manual acceptance pass may the
agent create the `v1.1.0` tag, GitHub Release, and assets.

## Explicit non-goals

- No automatic updater.
- No administrator or all-users installation.
- No custom WPF bootstrapper.
- No MSI or MSIX.
- No code-signing purchase or signing workflow in `v1.1.0`.
- No GitHub Packages publication.
- No replacement or retagging of `v1.0.0`.
- No second Developer Preview binary.

## References

- Inno Setup overview: https://jrsoftware.org/isinfo.php
- Stable `AppId`: https://jrsoftware.org/ishelp/topic_setup_appid.htm
- File replacement rules: https://jrsoftware.org/ishelp/topic_filessection.htm
- Cancellation behavior: https://jrsoftware.org/isfaq.php
