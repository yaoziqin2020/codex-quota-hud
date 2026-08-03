# AI Change Log

## 2026-08-04 — v1.2.0 local candidate, About window, and preview recovery

- Added a compact `关于` window showing the running product version, author
  `老姚`, the exact GitHub repository link, and MIT license. HUD and tray menus
  share one coordinator, reopen the existing window instead of duplicating it,
  and dispose it with their owning composition.
- Fixed the Skin Designer regression where `None` hid the synthetic HUD but a
  later non-empty mode could not show it again. Preview-only work-area updates
  no longer re-arm the automatic-show suppression flag. Added the focused
  `WorkAreaUpdate_DoesNotPreventQuotaReturningAfterNoQuota` regression test.
- Fixed the formal-HUD startup regression introduced while repairing that
  Designer transition. Automatic-show suppression is now selected explicitly
  only by the synthetic Designer composition; normal windows never inherit it.
  Formal windows also synchronize quota that arrived before construction
  completed. Focused tests cover both quota-arrival orders and preserve the
  preview's intentionally hidden initial window.
- Latest verification passed Core `75/75`, Skins `325/325`, App/UI `609/609`,
  and Designer `334/334`, total `1343/1343` with `0` skipped. Release build
  completed with `0` warnings and `0` errors.
- Fixed the installed-host About crash reported during real GUI acceptance.
  Windows event evidence traced it to the unembedded `Assets/AppIcon.ico` WPF
  resource. Added a constructor regression test that reproduced the installed-
  host failure, embedded the icon, and contained optional About-window failures
  so they are reported without terminating the HUD or Designer and can be
  retried. The corrected installed binaries identify commit `bf8e16d`.
- Built the latest local v1.2.0 candidate from commit `c3d545e`. Setup SHA-256 is
  `869D197E530053313E9EB54F41FB239551BBF35177A381C9A69C0E88A0C0E576`;
  normal-only ZIP SHA-256 is
  `A99DB908F700E446A36BAD58655C79D583D658E4250A956ED2C01095239C6452`.
  Setup, App, and Designer remain `NotSigned`.
- Real local upgrade installation completed with exit code `0`. Installed App
  and Designer hashes match the packaged publish tree, the uninstall entry is
  `1.2.0`, startup remains formal `--background`, ordinary-user shortcuts have
  no `--preview`, and Designer is Start-menu-only. The maintainer desktop was
  then separately restored to only the Developer Preview shortcut with
  `--preview`, without changing the public Setup contract.
- Installed GUI regression acceptance passed `None -> Dual`, `None -> 5h`, and
  `None -> Week`. The `关于` item was visible in the real HUD context menu. The
  pre-fix window crash was reproduced and corrected as described above; the
  corrected real window still needs one visual acceptance click, so that row
  remains `NOT RUN`.
- A post-reboot report showed current tray quota while the formal HUD remained
  hidden. Source history traced this to commit `07b73d5`, which accidentally
  made Designer-only automatic-show suppression the default for every HUD
  window. Red/green tests reproduced both quota-after-window and quota-before-
  window startup orders. A temporary opt-in trace then observed a real formal
  startup execute `Show()` with `IsVisible=True`, `Visibility=Visible`, and a
  nonzero WPF handle. The trace code and local trace files were removed. The
  final installed binary reports
  `1.2.0+c3d545ea0cd709d291d22fa8486ca5f270695b20`; the user directly
  confirmed that the formal HUD is visible after the final reinstall.
- Distinguished two historical `0.0.0` candidates: the earlier isolated Task
  18 package was never run; a later production-structure candidate was
  mistakenly installed, then replaced by v1.2.0. Its obsolete Setup/ZIP were
  removed while its log and rollback backup were retained.
- No tag, push, upload, GitHub Release, or replacement of public v1.1.1 assets
  was performed.

## 2026-08-03 — Unreleased optional Skin Designer automated handoff

- Added the unreleased shared `CodexQuotaHud.Skins` contracts, strict
  data-only `.cqskin` validation/packaging/storage/runtime path, normal-HUD
  import and rendering, and the separate `CodexQuotaHud.SkinDesigner` process
  with isolated drafts/recovery, synthetic preview, apply, and export flows.
  The unreleased Setup definition keeps Designer visible but unchecked by
  default, adds only a Start-menu entry when selected, preserves all user data
  on component removal, and keeps the fallback ZIP normal-HUD-only.
- Verified source commit `168bf8b2a58062f86c35b203eff6cf269b52bad9` after a
  forced canonical restore. Fresh per-project evidence was Core `75/75`, Skins
  `325/325`, App/UI `590/590`, and Designer `334/334`. Two trustworthy
  full-solution reruns passed `1324/1324` with `0` skipped; the final run was
  independently counted from four external TRX files. Release build completed
  with `0` warnings and `0` errors, and `git diff --check` passed.
- Preserved an unresolved earlier full-solution failure at the same commit:
  `SkinPackageInstallerTests.Remove_DeletesExactlyOneCanonicalCustomDirectoryAndPreservesSiblings`
  failed `Assert.True(result.IsValid)`, giving `1323` passed / `1` failed. Its
  original assertion did not expose `result.Errors`. Exact, class, Skins,
  eight-testhost parallel, cross-project, and 1000-operation stress evidence
  did not reproduce it, so no root cause or source fix is claimed. Two later
  no-test batches caused by a diagnostic restore polluting ignored Skins
  `obj` assets were explicitly excluded and the generated assets were repaired
  before the trustworthy TRX run.
- The three exact security/rollback filters passed Skins `223/223`, App
  `224/224`, and Designer `116/116`, all with `0` failed and `0` skipped.
- Built one real, internal-only `0.0.0` candidate without running Setup. Its
  ephemeral hashes were Setup
  `df89250dc2b68fa198196d48c6e2344efe4196c94c34f354728b2b66cc30cc8c`,
  normal-only ZIP
  `ca3ab3959633b241d2b4f709e2111a5e8e69cdfa64437f412df86933dea5d33d`,
  and checksum file
  `2db5e0053df13bedab36fec1e0b3de24b383c1b052df0775e20e6107f3a831b3`.
  The two checksum lines matched, the ZIP had exactly five approved entries
  and no Designer, and the Setup publish manifest had exactly the normal HUD
  plus `designer/CodexQuotaHud.SkinDesigner.exe`. Reproduced publish hashes
  matched the manifest; App, Designer, and Setup were all `NotSigned`.
- The first temporary manifest proxy failed before ISCC because it used a
  Windows PowerShell 5.1-incompatible API. That GUID root was checked clean and
  removed. A new GUID root with a compatible proxy produced the evidence above
  and was deleted with `ExistsAfter=False`.
- No GUI, internal/canonical Setup launch, install/uninstall, user-data change,
  sign-out, restart, version bump, tag, upload, public release, asset
  replacement, or push was performed. Every manual row remains
  `NOT RUN`; overall acceptance is `PARTIAL — no release is authorized`.

## 2026-08-01 — v1.1.1 ordinary-user installer correction

- Restored the approved release boundary: public Setup defaults to current-user
  startup plus the normal `Codex Quota HUD` desktop shortcut. The desktop and
  Start-menu shortcuts launch the real HUD without `--preview`.
- Removed Developer Preview from Setup tasks and icons. Upgrade explicitly
  removes the Developer Preview desktop shortcut created by `v1.1.0`; source
  and ZIP users retain explicit `CodexQuotaHud.App.exe --preview` access.
- Added project `AGENTS.md` rules separating maintainer-machine conveniences
  from public packaging. A later request that appears to reverse an approved
  product or release contract must be identified and reconfirmed before work.
- Updated the installer smoke contract to verify clean install, upgrade from
  the `v1.1.0` shortcut state, settings-preserving uninstall, and purge
  uninstall. The four isolated scenarios passed and cleaned up their state.
- Real candidate upgrade passed with exit code `0`: the normal desktop link had
  no arguments, the Developer Preview link was absent, startup remained
  `--background`, and two existing settings files retained their hashes.
- Fresh verification passed Core `55/55`, App/UI `333/333`, total `388/388`;
  Release build completed with `0` warnings and `0` errors.
- Published annotated tag `v1.1.1` at `6515e7c` and made its GitHub Release the
  latest ordinary-user release. Uploaded Setup, ZIP, and `SHA256SUMS.txt`; all
  online sizes and digests match the final local assets.
- Updated the retained `v1.1.0` Release with a prominent developer-oriented
  warning and a link-forward instruction to install `v1.1.1` or later.
- Installed the final published build locally. Its version is
  `1.1.1+6515e7c...` and binary hash matches the published payload. After Setup
  acceptance, restored the maintainer-only Developer Preview desktop shortcut
  separately while keeping formal startup and the normal Start-menu entry.

## 2026-08-01 — Public v1.1.0 release

- Finalized the bilingual README and release notes to match the shipped
  startup-plus-Developer-Preview shortcut behavior and `387/387` test baseline.
- Created and pushed annotated tag `v1.1.0` at `205e7e5`, then published the
  non-draft, non-prerelease GitHub Release as latest.
- Uploaded `CodexQuotaHud-Setup-v1.1.0.exe`,
  `CodexQuotaHud-v1.1.0-win-x64.zip`, and `SHA256SUMS.txt`. GitHub-reported
  sizes and SHA-256 digests match the local final assets.
- Installed the published Setup locally with exit code `0`. The installed
  executable reports `1.1.0+205e7e5...`, matches the published binary hash,
  retains formal `--background` startup, and keeps the desktop shortcut on
  `--preview`.
- Made the GitHub Actions Test step explicitly use Windows PowerShell, matching
  the installer scripts and later workflow steps. This prevents PowerShell 7's
  module path from being inherited by nested Windows PowerShell test processes
  and hiding the built-in `Get-FileHash` command.
- Made shortcut inspection locale-neutral in installer smoke tests. It still
  verifies the exact executable path and `--preview`; when WScript cannot read
  a Unicode `.lnk` filename on an English runner, it copies the same shortcut
  to a temporary ASCII-only filename and asks WScript to parse that copy. The
  temporary link is removed with checked cleanup. Added a regression test;
  the current source baseline is Core `55/55`, App/UI `333/333`, total
  `388/388`.
- Put the STA-based WPF UI test classes in one non-parallel xUnit collection.
  This removes a runner-timing race between concurrently constructed WPF
  controls that could make an otherwise passing skin test fail intermittently.

## 2026-07-31 — Final installer shortcut and uninstall acceptance

- Changed the release Setup default to current-user startup plus a Developer
  Preview desktop shortcut. The normal HUD no longer gets a desktop shortcut;
  upgrades explicitly remove the legacy managed normal desktop link.
- Moved the purge-settings choice to a usable pre-uninstall form and delayed
  process shutdown until uninstall is confirmed. Added final removal of exact
  legacy install residue after Inno cleanup.
- Verification passed Core `55/55`, App/UI `332/332`, total `387/387`, a
  zero-warning/zero-error Release build, and all four isolated installer smoke
  scenarios. Real default-preserve and explicit-purge uninstall acceptance also
  passed.
- Installed the final Setup from commit `1f15100` with exit code `0`. The real
  machine now has formal `--background` startup, one desktop Developer Preview
  shortcut with `--preview`, no normal desktop shortcut, and unchanged settings
  hashes. Final asset hashes are recorded in `PROJECT_CONTEXT.md`.

## 2026-07-31 — Release Setup preview isolation and lifecycle fix

- Removed the Developer Preview shortcut option from the production Setup;
  development preview remains explicitly available to ZIP/source users through
  `CodexQuotaHud.App.exe --preview`.
- Changed Inno lifecycle execution from redirected 32-bit PowerShell to native
  64-bit PowerShell. This prevents install/uninstall from exiting with code 1
  while inspecting the exact path of the 64-bit HUD process.
- Corrected candidate verification passed Core `55/55`, App/UI `331/331`,
  total `386/386`, Release build with zero warnings/errors, and all four
  isolated installer smoke scenarios. Corrected Setup SHA-256 is
  `628ba0cb457b93e1cd063fe3f954f09b9d1ab5747e0a0cb9f6e5fdae1185514a`.
- Real corrected-Setup overwrite passed: both formal tasks remained selected,
  settings were preserved, and no Developer Preview shortcut was created.

## 2026-07-31 — v1.1.0 installer release-candidate verification

- Made Setup the documented primary path; ZIP/PowerShell is fallback and
  GitHub Packages is unused. The documentation covers automatic
  English/Simplified-Chinese selection, current-user/no-admin install,
  startup/normal-shortcut defaults, ZIP-only explicit `--preview` launch, direct
  upgrade, settings preservation, opt-in purge, unsigned SmartScreen notice,
  and SHA-256 verification.
- At `04049208e13c579307a421fc96318b82cb6b5da8`, fresh Release verification
  passed Core 55/55, App/UI 328/328, total 383/383; Release build was 0
  warnings and 0 errors.
- Packaged local candidate assets: Setup 50,880,280 bytes / SHA-256
  `65d7dea7992f1c3ca21cfc75b19b7c7148bd8f25e6fd03f439ad07259705c5aa`; ZIP
  68,202,715 bytes / SHA-256
  `572b9d741098ceb055c5601fc7d149547d74e5361f21bc70f48501e673418019`.
- The four isolated smoke scenarios passed using a temporary smoke-only Setup:
  clean install, upgrade/task replacement, default preserve uninstall, and
  explicit purge uninstall. Production assets do not contain internal smoke
  hooks, and the temporary smoke build was not released.
- No real Setup has been launched. The installed v1.0.0, its Apps & Features
  registration, shortcuts, startup, settings, and uninstall behavior remain
  unaccepted pending explicit GUI/install authorization. No release/tag/upload
  claim is made.

## 2026-07-31 — Low-quota alert colors

- Added a shared alert policy across all five floating-HUD skins, collapsed
  edge bars, tray percentage icon, and detail rows. Above `20%` retains the
  normal skin color; `>10%..20%` is Warning amber `#FFFFB547`; `<=10%` is
  Critical red `#FFFF5A67`.
- Dual quotas are classified and colored independently. The change adds no
  flashing, popup, sound, setting, or refresh behavior. Developer Preview
  sliders remain the manual boundary and mixed-state inspection tool.
- Automated Release verification passed focused alert tests 66/66, Core 55/55,
  App/UI 266/266, total 321/321, and a zero-warning, zero-error Release build.
  GUI/manual preview acceptance was not authorized or performed.
- Installed `v1.0.0`, deployment, release assets, and remote/push state were
  not changed.

## 2026-07-30 — Symmetric installed/preview handoff

- Launching the desktop `Codex Quota HUD 开发预览` shortcut now replaces the
  installed HUD before preview creates its HUD or control window.
- A listener-enabled installed build receives the shutdown request and exits
  through normal application cleanup. Older installed builds use the fallback
  only when the running executable is exactly
  `%LOCALAPPDATA%\Programs\CodexQuotaHud\CodexQuotaHud.App.exe`; a same-name
  process at another path is never force-closed.
- If installed-mode replacement fails, the app displays a failure message and
  does not open preview. The reverse handoff remains
  `退出预览并打开正式版`, which cleans up preview before starting the exact
  installed executable.
- Verified by automated tests: Core 55/55, App/UI 231/231, total 286/286, plus
  a Release build with zero warnings and zero errors.
- The installed `v1.0.0` package and release assets were not upgraded,
  deployed, or changed. No GUI process was launched for this task, so both
  manual handoff directions remain unchecked. The current `v1.0.0` can later
  verify the legacy exact-path fallback; graceful signalling remains unverified
  until a listener-enabled build is installed.

## 2026-07-30 — Preview control window geometry

- Increased the preview control default size to `380 × 650` so standard-DPI
  startup shows the full current control set without an initial scrollbar.
- Added isolated geometry persistence at
  `%LOCALAPPDATA%\CodexQuotaHud\preview-window.json`.
- Restores and clamps valid size/position while retaining scrolling for small
  screens and high DPI.
- Does not persist simulated quota, skin, animation, or normal HUD settings.
- Verified Core 55/55, App/UI 212/212, total 267/267, and a Release build with
  zero warnings and zero errors.

## 2026-07-30 — Preview handoff to installed HUD

- Added `退出预览并打开正式版` to the developer preview control window.
- Resolves the installed HUD only from the current-user Local App Data
  installation path and disables the button when that executable is missing.
- Defers process start until preview windows, tray resources, and the
  single-instance guard have been cleaned up.
- Contains missing-file and launch failures without interrupting shutdown.
- Verified Core 55/55, App/UI 205/205, total 260/260, and a Release build with
  zero warnings and zero errors.
- End-to-end visual handoff remains a manual check; the installed `v1.0.0`
  files and release assets were not modified.

## 2026-07-30 — Isolated developer preview

- Added `--preview` composition before normal process-monitor and app-server
  construction.
- Added deterministic dual, five-hour-only, weekly-only, and no-quota states
  with adjustable percentages.
- Added an in-memory settings store and separate developer control window.
- Reused the production HUD, five skins, details popup, tray, animation state,
  monitor work-area selection, and edge geometry.
- Serialized the real-window preview tests after full-suite verification
  exposed a parallel WPF/WinForms resource conflict.
- Verified Core 55/55, App/UI 198/198, total 253/253, and a Release build with
  zero warnings and zero errors.
- The installed `v1.0.0` build and release assets were not changed.

## 2026-07-30 — Project handoff

- Confirmed `main` is synchronized with
  `https://github.com/yaoziqin2020/codex-quota-hud.git`.
- Prepared the repository to move out of the original long-running Codex
  conversation and into a standalone Codex project.
- Added `PROJECT_CONTEXT.md`, `CURRENT_TASK.md`, and `CHANGELOG_AI.md` so a new
  task can recover product intent, architecture, verification baseline, public
  identity, and the next continuation point without relying on chat history.
- Canonical new local project path:
  `C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud`.

## 2026-07-29 — Public 1.0.0

- Published the public repository and `v1.0.0` Windows x64 release.
- Added CI, MIT license, bilingual README, five-skin preview, repository topics,
  installer/uninstaller, and release packaging.
- Completed public-name privacy cleanup to `老姚`.
- Replaced the release package after privacy cleanup.
- Stabilized a timing-sensitive refresh-service test; focused test passed 15
  consecutive runs, full local suite passed 232/232, and GitHub CI passed.

