# Codex Quota HUD — Project Context

## Purpose

Codex Quota HUD is an independent Windows desktop companion that displays the
remaining Codex five-hour and weekly quota reported by the local
`codex app-server`.

Repository: https://github.com/yaoziqin2020/codex-quota-hud

## v1.1.0 installer release candidate

The primary distribution path is the current-user Inno Setup executable
`CodexQuotaHud-Setup-v1.1.0.exe`; ZIP plus its PowerShell script is fallback
only, and GitHub Packages is unused. Setup offers English/Simplified Chinese,
requires no administrator permission, and selects startup and the normal
desktop shortcut by default. Release Setup contains no Developer Preview
shortcut option; ZIP users can explicitly run the executable with `--preview`.
It directly upgrades `v1.0.0` while preserving settings by default. Uninstall
preserves settings unless the user explicitly opts in to purge the exact
`%LOCALAPPDATA%\\CodexQuotaHud` directory.

The candidate Setup is unsigned, so SmartScreen may report an unknown
publisher; verify SHA-256 before running it. Corrected local assets are Setup
50,880,224 bytes / `628ba0cb457b93e1cd063fe3f954f09b9d1ab5747e0a0cb9f6e5fdae1185514a`
and ZIP 68,202,773 bytes /
`5f834e0928b61d6ad96719fe8f0b82dafe832453aee6425123ef7d4c4d6b0f67`.

Production assets contain no internal smoke hooks. A temporary, separately
compiled Setup passed all four isolated smoke scenarios and was cleaned up; it
is not a release asset. Corrected-Setup overwrite passed on the real machine;
both uninstall modes still require real-desktop acceptance.

## Product behavior

- WPF floating HUD with five animated skins.
- Dual quota display when both windows are available; graceful single-ring
  fallback when only one window is available.
- Background refresh every 60 seconds and manual refresh by double-click.
- Left-click toggles details; right-click opens the control menu.
- Edge docking on the external edges of multi-monitor layouts.
- Themed edge progress bars and a numeric system-tray icon.
- Low-quota colors apply independently to each available quota: values above
  `20%` keep the normal skin color, `>10%..20%` use Warning amber `#FFFFB547`,
  and `<=10%` use Critical red `#FFFF5A67`. They appear on all five floating-HUD
  skins, collapsed edge bars, tray icon, and detail rows. This is color-only:
  no flashing, popup, sound, settings, or refresh behavior changes.
- Single-instance behavior and current-user startup support.
- No browser-cookie scraping, credential storage, or listening network port.
- The development-preview shortcut replaces installed mode before preview
  opens. Listener-enabled installed builds use graceful normal cleanup;
  older builds use the fallback only at the exact standard installation path.
  Same-name processes elsewhere are never force-closed. A replacement failure
  is shown to the user and preview does not open.
- `退出预览并打开正式版` is the reverse handoff: it cleans up preview first and
  opens the installed executable at the exact standard installation path.

## Architecture

```text
src/CodexQuotaHud.Core/       quota models, mapping, refresh state, settings
src/CodexQuotaHud.App/        WPF UI, skins, tray, app-server integration
tests/                        Core and Windows UI tests
scripts/                      publish, install, uninstall, release packaging
docs/                         design, verification, screenshots, release notes
```

The app reads JSONL quota data through a local `codex app-server` subprocess.
It does not call a private web endpoint directly.

## Stable locations

- Public source: GitHub `main`
- Installed executable:
  `%LOCALAPPDATA%\Programs\CodexQuotaHud\CodexQuotaHud.App.exe`
- Release: `v1.0.0`
- Preferred new Codex project directory:
  `C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud`

The installed `v1.0.0` package has not been upgraded or deployed by the source
handoff work. It lacks the new listener, so it is the future manual target for
the legacy exact-path fallback. Graceful signalling can be manually checked
only after a listener-enabled build is installed.

Do not treat the old conversation worktree under
`Documents\Codex\2026-07-27\new-chat` as the long-term project root.

## Quality baseline

- Core tests: 55
- App/UI tests: 266
- Total: 321
- 2026-07-31 Release verification: focused low-quota alert tests 66/66;
  full suite Core 55/55, App/UI 266/266, total 321/321; build zero warnings
  and zero errors. GUI/manual preview acceptance was not performed.
- GitHub Windows CI covers restore, test, build, and self-contained publish.
- The asynchronous test wait helper is time-based to remain stable on slower
  GitHub Windows runners.

## Public identity and privacy

- Public author/display name: `老姚`
- GitHub account: `yaoziqin2020`
- Git uses the GitHub noreply address for new project commits.
- README, license, public history, release tag, and release asset were
  de-identified before this handoff.

## Working rules

- Confirm the canonical project directory and read this file,
  `CURRENT_TASK.md`, and `CHANGELOG_AI.md` before editing.
- Make narrow changes and preserve established interaction semantics.
- Treat multi-monitor edge behavior, click/double-click handling, and animation
  timing as regression-sensitive.
- The developer preview is entered only with `--preview`; it uses synthetic
  in-memory data and must never replace real app-server acceptance.
- Run focused tests for changed behavior, then the full 321-test suite.
- Use the Developer Preview sliders as the manual boundary and mixed-state
  tool for alert colors; inspect normal (`>20%`), Warning (`>10%..20%`), and
  Critical (`<=10%`) states independently for both quotas.
- Update the installed build only when the user asks to deploy locally.
- Do not move the release tag or replace release assets without an explicit
  release reason.
- Keep the two-direction desktop handoff unchecked until it is performed. Do
  not infer graceful listener acceptance from the legacy `v1.0.0` fallback.

