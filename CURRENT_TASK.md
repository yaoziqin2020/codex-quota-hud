# Current Task

## Status

Version `1.0.0` is released and the `main` branch is healthy. There is no
unfinished implementation task at handoff.

## Last completed work

- Published the standalone public repository and Windows x64 release.
- Added MIT license, bilingual README, real five-skin preview, topics, CI, and
  release packaging.
- Unified public identity to `老姚` and removed the previous real-name text from
  current public history and release artifacts.
- Stabilized the asynchronous test wait used by GitHub Windows CI.
- Verified the latest GitHub CI run succeeded.

## Next continuation point

Start from a fresh clone of GitHub `main` in:

`C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud`

Before making a new change:

1. Pull `origin/main`.
2. Confirm `git status` is clean.
3. Read `PROJECT_CONTEXT.md` and `CHANGELOG_AI.md`.
4. State the exact UI or behavior change and its verification surface.

## Manual checks for future UI changes

- Primary and secondary monitor placement.
- Left, right, top, and bottom external-edge docking.
- Detail popup never overlaps the floating HUD.
- Single-click toggle and double-click refresh separation.
- Existing instance activation and tray percentage rendering.
- All five skins in both full HUD and edge-bar states.

