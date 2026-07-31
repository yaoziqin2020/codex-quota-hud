# Current Task

## v1.1.0 installer release candidate — manual acceptance pending

The corrected release candidate is in the feature worktree pending commit.
Fresh Release verification passed Core `55/55`, App/UI `331/331`, total
`386/386`; `dotnet build` reported `0` warnings and `0` errors. Packaging
produced:

- `CodexQuotaHud-Setup-v1.1.0.exe` — 50,880,224 bytes, SHA-256
  `628ba0cb457b93e1cd063fe3f954f09b9d1ab5747e0a0cb9f6e5fdae1185514a`
- `CodexQuotaHud-v1.1.0-win-x64.zip` — 68,202,773 bytes, SHA-256
  `5f834e0928b61d6ad96719fe8f0b82dafe832453aee6425123ef7d4c4d6b0f67`

The production Setup contains no internal smoke hooks. Four isolated smoke
scenarios passed using a separately compiled, temporary Setup that is not a
release asset: clean install; upgrade with task replacement; default uninstall
that preserves settings; and explicit purge uninstall.

Manual acceptance is partially complete. The earlier candidate upgraded the
real `v1.0.0` to `v1.1.0`, preserved settings, and created the expected startup,
Start-menu, and normal desktop entries. It also exposed a now-rejected preview
shortcut task. Its first uninstall attempt failed after stopping the HUD because
redirected 32-bit PowerShell could not inspect the 64-bit executable path.

The corrected Setup removes the production preview task, invokes native 64-bit
PowerShell, and retains the previous startup/desktop selections during upgrade.
Regression, full-suite, build, packaging, and four-scenario isolated smoke
verification pass. Real corrected-Setup overwrite also passed with settings
preserved and no preview shortcut. Default preserve uninstall, explicit purge
uninstall, settings restore, and final reinstall remain pending. Do not tag,
upload, or describe this candidate as manually accepted yet.

## Status

Version `1.0.0` is released. The isolated developer preview and the symmetric
installed/preview handoff are implemented on
`codex/preview-replaces-installed-app`. Automated regression verification is
complete; the two-direction desktop acceptance and branch integration remain.

## Last completed work

- Added low-quota color alerts across the floating HUD's five skins, collapsed
  edge bars, tray percentage icon, and detail rows. Values above `20%` retain
  normal skin colors; `>10%..20%` is Warning amber `#FFFFB547`; `<=10%` is
  Critical red `#FFFF5A67`. Primary and secondary quotas are colored
  independently in dual mode.
- The change is color-only: no flashing, popup, sound, setting, or refresh
  behavior changed. Developer Preview sliders are the manual boundary and
  mixed-state inspection tool.
- Automated Release verification passed the focused alert set `66/66`, Core
  `55/55`, App/UI `266/266`, total `321/321`, and a zero-warning, zero-error
  Release build. GUI/manual preview acceptance was not authorized or performed.
- Installed `v1.0.0`, deployment, release assets, and remote/push state remain
  unchanged.

- Added `--preview` with a real HUD and separate developer control panel.
- Added dual, five-hour-only, weekly-only, and no-quota synthetic states.
- Isolated preview mode from app-server, startup registration, and production
  settings.
- Added skin, percentage, animation, details, and four-edge preview controls.
- Added a one-click handoff that releases preview's single-instance lock
  before opening the installed normal HUD.
- Increased the preview control default size to `380 × 650` and persist its
  geometry independently from normal HUD settings.
- Launching the desktop development-preview shortcut now closes installed mode
  before preview opens. Listener-enabled installed builds exit through normal
  cleanup; legacy builds fall back only when their executable is the exact
  standard installation path. Same-name processes at other paths are not
  force-closed, and a replacement failure shows a message without opening
  preview.
- Retained the reverse handoff: `退出预览并打开正式版` closes preview first and
  then opens the installed executable.
- At that preview-handoff stage, the automated baseline was Core 55 + App/UI
  242 = 297 tests.
- Published the standalone public repository and Windows x64 release.
- Added MIT license, bilingual README, real five-skin preview, topics, CI, and
  release packaging.
- Unified public identity to `老姚` and removed the previous real-name text from
  current public history and release artifacts.
- Stabilized the asynchronous test wait used by GitHub Windows CI.
- Verified the latest GitHub CI run succeeded.

## Next continuation point

When GUI launch is authorized, use the Developer Preview sliders to inspect
the `21`, `20`, `11`, `10`, and `0` boundaries, plus dual mixed states, across
all five skins, full HUD, details, tray, and each collapsed edge side. Confirm
that colors return to normal above `20%` and that no flashing, popup, sound,
settings, or refresh behavior was introduced.

When GUI launch is authorized, perform the two-direction desktop acceptance
only after the existing preview shortcut targets the reviewed feature
artifact. Either merge the reviewed feature branch and rebuild the canonical
shortcut target, or temporarily retarget the shortcut to the feature
worktree's reviewed Release artifact. Record the exact tested Git commit and
artifact path or hash with the acceptance result.

Start the current installed `v1.0.0`, launch the `Codex Quota HUD 开发预览`
shortcut, and confirm the installed HUD disappears before preview opens.
Because `v1.0.0` predates the listener, this first direction should exercise
the legacy exact-path fallback. Then click `退出预览并打开正式版` and confirm
preview closes before exactly one installed tray/HUD returns.

The current source change did not upgrade or deploy installed `v1.0.0`.
Graceful signalling remains unverified until a listener-enabled build is
installed. No GUI process was launched for this handoff, so both manual
directions remain unchecked.

## Manual checks for future UI changes

- Primary and secondary monitor placement.
- Left, right, top, and bottom external-edge docking.
- Detail popup never overlaps the floating HUD.
- Single-click toggle and double-click refresh separation.
- Existing instance activation and tray percentage rendering.
- All five skins in both full HUD and edge-bar states.
- Installed-to-preview replacement through the legacy exact-path fallback.
- Preview-to-installed reverse handoff, including exactly one returned
  installed tray/HUD. The graceful listener path must be checked separately
  after a listener-enabled build is installed.

