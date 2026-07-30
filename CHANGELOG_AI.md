# AI Change Log

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

