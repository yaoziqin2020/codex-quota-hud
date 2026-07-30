# Current Task

## Status

Version `1.0.0` is released. The isolated developer preview and the symmetric
installed/preview handoff are implemented on
`codex/preview-replaces-installed-app`. Automated regression verification is
complete; the two-direction desktop acceptance and branch integration remain.

## Last completed work

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
- Increased the automated baseline to Core 55 + App/UI 242 = 297 tests.
- Published the standalone public repository and Windows x64 release.
- Added MIT license, bilingual README, real five-skin preview, topics, CI, and
  release packaging.
- Unified public identity to `老姚` and removed the previous real-name text from
  current public history and release artifacts.
- Stabilized the asynchronous test wait used by GitHub Windows CI.
- Verified the latest GitHub CI run succeeded.

## Next continuation point

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

