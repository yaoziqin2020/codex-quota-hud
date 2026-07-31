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
requires no administrator permission, and selects startup plus the Developer
Preview desktop shortcut by default. It does not create a normal-HUD desktop
shortcut; the normal HUD starts in the background at sign-in and remains
available from the Start menu. ZIP users can also run the executable with
`--preview`.
It directly upgrades `v1.0.0` while preserving settings by default. Uninstall
preserves settings unless the user explicitly opts in to purge the exact
`%LOCALAPPDATA%\\CodexQuotaHud` directory.

The candidate Setup is unsigned, so SmartScreen may report an unknown
publisher; verify SHA-256 before running it. Final local assets are Setup
50,882,622 bytes / `2fe3877974a3ee24904b0c63f858e4f94eb61d3bbb0768cbbd951963fed3266c`
and ZIP 68,202,656 bytes /
`c6d563813ffc26a18a4fac1d82637db1cebf60c930aaf240defb93c31617544a`.

Production assets contain no internal smoke hooks. A temporary, separately
compiled Setup passed all four isolated smoke scenarios and was cleaned up; it
is not a release asset. Both real uninstall modes passed. The final Setup from
commit `1f15100` was installed with exit code `0`; the startup value, preview
shortcut arguments, installed binary hash, preserved settings, and uninstall
registration were verified on the real machine.

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

The real machine currently has the locally accepted `v1.1.0` candidate from
commit `1f15100`. It includes the installed-mode shutdown listener, so future
two-direction preview handoff acceptance exercises graceful signalling. The
public GitHub release remains `v1.0.0` until push/tag/upload is explicitly
authorized.

Do not treat the old conversation worktree under
`Documents\Codex\2026-07-27\new-chat` as the long-term project root.

## Quality baseline

- Core tests: 55
- App/UI tests: 332
- Total: 387
- 2026-07-31 Release verification: focused low-quota alert tests 66/66;
  full suite Core 55/55, App/UI 266/266, total 321/321; build zero warnings
  and zero errors. GUI/manual preview acceptance was not performed.
- 2026-07-31 final installer verification: Core 55/55, App/UI 332/332,
  total 387/387; build zero warnings and zero errors; four isolated installer
  scenarios and real-machine install/default-uninstall/purge-uninstall checks
  passed.
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

