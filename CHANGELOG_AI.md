# AI Change Log

## 2026-07-31 — Low-quota alert colors

- Added a shared alert policy across all five floating-HUD skins, collapsed
  edge bars, tray percentage icon, and detail rows. Above `20%` retains the
  normal skin color; `>10%..20%` is Warning amber `#FFB547`; `<=10%` is
  Critical red `#FF5A67`.
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

