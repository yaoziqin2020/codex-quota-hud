# Current Task

## Status

Version `1.0.0` is released. The isolated developer preview is implemented on
`codex/preview-tool`; automated verification is complete, while final visual
acceptance and branch integration remain.

## Last completed work

- Added `--preview` with a real HUD and separate developer control panel.
- Added dual, five-hour-only, weekly-only, and no-quota synthetic states.
- Isolated preview mode from app-server, startup registration, and production
  settings.
- Added skin, percentage, animation, details, and four-edge preview controls.
- Added a one-click handoff that releases preview's single-instance lock
  before opening the installed normal HUD.
- Increased the automated baseline to 260 tests.
- Published the standalone public repository and Windows x64 release.
- Added MIT license, bilingual README, real five-skin preview, topics, CI, and
  release packaging.
- Unified public identity to `老姚` and removed the previous real-name text from
  current public history and release artifacts.
- Stabilized the asynchronous test wait used by GitHub Windows CI.
- Verified the latest GitHub CI run succeeded.

## Next continuation point

Run final visual acceptance from the feature worktree, then integrate the
feature branch into `main` without using the old conversation worktree:

```powershell
dotnet run --project .\src\CodexQuotaHud.App -- --preview
```

The installed HUD currently owns the single-instance lock, so exit it from the
tray before launching preview. The 2026-07-30 smoke attempt correctly exited
without changing the production settings file.

The new handoff button is covered by cleanup-order and process-launch tests.
Its end-to-end visual handoff has not yet been run because that requires
temporarily exiting the currently installed HUD.

## Manual checks for future UI changes

- Primary and secondary monitor placement.
- Left, right, top, and bottom external-edge docking.
- Detail popup never overlaps the floating HUD.
- Single-click toggle and double-click refresh separation.
- Existing instance activation and tray percentage rendering.
- All five skins in both full HUD and edge-bar states.

