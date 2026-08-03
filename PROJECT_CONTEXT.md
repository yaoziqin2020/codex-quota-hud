# Codex Quota HUD — Project Context

## Purpose

Codex Quota HUD is an independent Windows desktop companion that displays the
remaining Codex five-hour and weekly quota reported by the local
`codex app-server`.

Repository: https://github.com/yaoziqin2020/codex-quota-hud

## v1.1.1 release

The primary distribution path is the current-user Inno Setup executable
`CodexQuotaHud-Setup-v1.1.1.exe`; ZIP plus its PowerShell script is fallback
only, and GitHub Packages is unused. Setup offers English/Simplified Chinese,
requires no administrator permission, and selects startup plus creation of the
normal desktop shortcut by default. Desktop and Start-menu shortcuts launch
the real HUD without `--preview`. Setup contains no Developer Preview entry;
ZIP and source users retain explicit `--preview` access.

It directly upgrades `v1.0.0` and `v1.1.0`, removes the Developer Preview
desktop shortcut created by `v1.1.0`, and preserves settings by default.
Uninstall preserves settings unless the user explicitly opts in to purge the
exact `%LOCALAPPDATA%\\CodexQuotaHud` directory. The retained `v1.1.0` Release
is explicitly labeled developer-oriented and directs ordinary users to
`v1.1.1` or later.

The Setup is unsigned, so SmartScreen may report an unknown publisher; verify
SHA-256 before running it. Published assets are Setup 50,875,857 bytes /
`714714fdabdeafa9382ee797bd4aa6ef4ac50172ba4211af784b4c600072bfec`
and ZIP 68,202,621 bytes /
`8210b0e1f1c490ee0f39c4ed9d8c8dd4d68d5cd261ee56d005df733a184a1bb`.

Production assets contain no internal smoke hooks. A temporary, separately
compiled Setup passed all four isolated smoke scenarios and was cleaned up; it
is not a release asset. The final Setup from tag commit `6515e7c` installed
with exit code `0`; its installed binary matches the published binary, startup
uses `--background`, the normal desktop shortcut has no arguments, the
Developer Preview shortcut is absent, and two existing settings files retained
their hashes. After public-behavior acceptance, the maintainer machine was
separately customized to keep only a local Developer Preview desktop shortcut;
that local convenience is not Setup behavior.

## Unreleased optional Skin Designer source

Source commit `168bf8b2a58062f86c35b203eff6cf269b52bad9` contains an
unreleased custom-skin runtime and separate Skin Designer. This work is not
part of `v1.1.1`, has not been installed on the maintainer machine, and has no
public tag, Setup, ZIP, or GitHub Release.

Dependency direction is intentionally one-way:

```text
CodexQuotaHud.Core
        ↑
CodexQuotaHud.Skins ← CodexQuotaHud.App
        ↑                    ↑
CodexQuotaHud.SkinDesigner ──┘
```

`CodexQuotaHud.Skins` owns schema-v1 contracts, strict JSON/archive/image
validation, deterministic data-only packaging, exact-root installed-skin
storage, the template registry, and runtime rendering. The normal HUD consumes
that shared runtime and has no project or assembly dependency on Designer; it
may discover and launch the optional Designer executable at its exact installed
path. Designer uses Skins for contracts, validation, storage, packaging, and
output, and uses App for the existing synthetic Preview/UI composition plus
typed local-control activation. It has its own mutex and draft/recovery
lifecycle.

The exact per-user storage boundary is:

- settings root: `%LOCALAPPDATA%\CodexQuotaHud`
- installed custom skins: `%LOCALAPPDATA%\CodexQuotaHud\skins\<skin-guid>`
- Designer projects: `%LOCALAPPDATA%\CodexQuotaHud\designer\drafts\<draft-guid>`
- named and recovery documents: `draft.json` and `recovery.json` inside that
  exact draft directory
- bounded import/install operation storage: `%LOCALAPPDATA%\CodexQuotaHud\imports`

The unreleased Setup definition always includes normal HUD import/runtime
support and exposes **Install Skin Designer / 安装皮肤设计器** as a visible,
unchecked optional component. Selecting it adds the Designer below
`%LOCALAPPDATA%\Programs\CodexQuotaHud\designer` and a Start-menu-only entry;
it adds no Designer desktop shortcut or startup value. Rerun removal preserves
settings, installed skins, drafts/recovery, and imports. The fallback ZIP stays
normal-HUD-only and still supports `.cqskin` validation/import/rendering.

Current automated evidence is `PASS with unresolved anomaly`: trustworthy
full-solution reruns passed `1324/1324`, the Release build reported zero
warnings/errors, and the three explicit security/rollback filters passed
`223/223`, `224/224`, and `116/116`. One earlier full run at the same commit
failed a single exact-directory Remove assertion; isolated, class, project,
parallel, cross-project, and 1000-operation stress investigation did not
reproduce it or capture `result.Errors`, so no root cause or source fix is
claimed. Every GUI, real Setup, sign-out, and restart row remains `NOT RUN`.
Overall acceptance is `PARTIAL — no release is authorized`.

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
src/CodexQuotaHud.Skins/      shared skin contracts, validation, storage, renderer
src/CodexQuotaHud.App/        WPF UI, skin import/runtime, tray, app-server integration
src/CodexQuotaHud.SkinDesigner/ separate designer, drafts, preview, apply/export
tests/                        Core, Skins, App/UI, and Designer tests
scripts/                      publish, install, uninstall, release packaging
docs/                         design, verification, screenshots, release notes
```

The app reads JSONL quota data through a local `codex app-server` subprocess.
It does not call a private web endpoint directly.

## Stable locations

- Public source: GitHub `main`
- Installed executable:
  `%LOCALAPPDATA%\Programs\CodexQuotaHud\CodexQuotaHud.App.exe`
- Release: `v1.1.1`
- Preferred new Codex project directory:
  `C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud`

The real machine currently has the published `v1.1.1` build from tag commit
`6515e7c`. It includes the installed-mode shutdown listener, so future
two-direction preview handoff acceptance exercises graceful signalling.

Do not treat the old conversation worktree under
`Documents\Codex\2026-07-27\new-chat` as the long-term project root.

## Quality baseline

- Core tests: 55
- App/UI tests: 333
- Total: 388
- 2026-07-31 Release verification: focused low-quota alert tests 66/66;
  full suite Core 55/55, App/UI 266/266, total 321/321; build zero warnings
  and zero errors. GUI/manual preview acceptance was not performed.
- 2026-07-31 final installer verification: Core 55/55, App/UI 332/332,
  total 387/387; build zero warnings and zero errors; four isolated installer
  scenarios and real-machine install/default-uninstall/purge-uninstall checks
  passed.
- 2026-08-03 unreleased optional Designer evidence at `168bf8b`: current
  per-project totals Core 75, Skins 325, App/UI 590, Designer 334; two
  trustworthy full reruns passed 1324/1324 after one unresolved earlier
  one-test storage failure. Security/rollback filters passed 223, 224, and 116
  tests; build was zero warnings/errors. Internal `0.0.0` Setup/ZIP inspection
  passed without installation. Manual/real-Windows acceptance remains NOT RUN,
  so this is not a release baseline.
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
- Run focused tests for changed behavior, then the current full solution suite;
  do not reuse the historical `v1.1.1` 388-test count for unreleased source.
- Use the Developer Preview sliders as the manual boundary and mixed-state
  tool for alert colors; inspect normal (`>20%`), Warning (`>10%..20%`), and
  Critical (`<=10%`) states independently for both quotas.
- Update the installed build only when the user asks to deploy locally.
- Do not move the release tag or replace release assets without an explicit
  release reason.
- Keep the two-direction desktop handoff unchecked until it is performed. Do
  not infer graceful listener acceptance from the legacy `v1.0.0` fallback.

