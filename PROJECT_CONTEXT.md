# Codex Quota HUD — Project Context

## Purpose

Codex Quota HUD is an independent Windows desktop companion that displays the
remaining Codex five-hour and weekly quota reported by the local
`codex app-server`.

Repository: https://github.com/yaoziqin2020/codex-quota-hud

## v1.2.3 installed local candidate — user acceptance pending

The v1.2.3 work combines two approved corrections. Skin Designer-owned
messages now use accessible dark-themed WPF dialogs and a shared Designer
palette/button template; native Windows file-open and file-save pickers remain
unchanged. Refresh animation now distinguishes requested and effective state:
the five built-in skins keep their established speed profiles and hold the
accelerated presentation for 1.5 seconds after completion. Custom skins support
an absolute `0x..4x` multiplier and `0..3` second hold. Old packages without
the new fields default to `2x`/`1.5s`; new Designer output writes both canonical
properties and declares HUD `1.2.3`.

Final source is commit
`c66cf9d5d135b864ad90af5c74455177902c7c04`. Fresh Release verification is
Core `75/75`, Skins `355/355`, App/UI `622/622`, and Designer `395/395`,
totaling `1447/1447`, with zero failed or skipped. Release build is zero
warnings/errors and `git diff --check` passes. The final production-structure
package workflow created these local-only files:

- Setup: 100,048,867 bytes / SHA-256
  `579C1CE73392970E93323C99600F013950CC463BC4BB5C4B305085584F743F80`
- normal-HUD-only ZIP: 68,335,551 bytes / SHA-256
  `16BF2692D591D039014CD2976CD639DE5B5C599F19826B8A07E7E0A1631504D1`
- `SHA256SUMS.txt`: 196 bytes / SHA-256
  `DCE728EE15522FFDAABF841C11F90310DDC5342A920B01EADA41730FA04D2771`

The manifest's two entries match. The ZIP contains exactly App, README,
LICENSE, install script, and uninstall script; it contains no Designer. The
Setup publish tree contains exactly App and the optional Designer executable.
Both executables are version `1.2.3.0` and report
`1.2.3+c66cf9d5d135b864ad90af5c74455177902c7c04`; Setup reports `1.2.3`.
Setup and both executables are unsigned.

All seven normal isolated installer scenarios and both committed-cleanup
failure/retry scenarios passed against the final Setup in `946.3 s` and left
no smoke root or installer process.

The real machine was upgraded silently with startup plus the optional Designer;
Setup exited `0`. All 23 pre-existing product-data files retained their paths
and hashes. Installed App and Designer are `1.2.3.0`, their product versions
contain the final `c66cf9d` source commit, and their hashes exactly match the
publish tree. The uninstall entry reports `1.2.3`; startup remains the formal
HUD with `--background`; the normal HUD and Designer Start-menu entries exist.
Setup created no maintainer preview shortcut, so the separate desktop
`Codex Quota HUD 开发预览` shortcut was restored locally with `--preview`.

Installed Designer GUI smoke confirmed the dark main surface, default
`2.0×`/`1.5 秒` controls, `0×` safe pause, `4.0×`/`3.0 秒` extremes, all three
presets preserving speed/hold, a dark unsaved-change dialog, dark disabled
owner buttons, and the native picker opening at the Designer drafts directory.
The formal installed HUD process is running. Exact motion timing across every
built-in/custom combination, old-package save/apply/export, every dialog kind,
sign-out/restart, and user practical acceptance are still not fully verified.

v1.2.3 has not been tagged, uploaded, pushed, merged to `main`, or published.
Remote release work remains forbidden until the user explicitly accepts this
exact installed candidate.

## v1.1.1 historical release

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

## v1.2.0 release and optional Skin Designer

Commit `168bf8b2a58062f86c35b203eff6cf269b52bad9` is the base custom-skin
runtime/Designer handoff. The final package is built from
`7393ffc4983d03552314295fe74061781e0b1318`, installed on the maintainer
machine as `1.2.0`, and published under tag `v1.2.0`. The tag points to
evidence commit `4a7c4c150315a37807a97b17d5cb4605236bf84c`; `main` was
fast-forwarded without a PR.

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
- user-facing package exchange directory:
  `%USERPROFILE%\Documents\Codex Quota HUD Skins`

The v1.2.0 Setup always includes normal HUD import/runtime
support and exposes **Install Skin Designer / 安装皮肤设计器** as a visible,
unchecked optional component. Selecting it adds the Designer below
`%LOCALAPPDATA%\Programs\CodexQuotaHud\designer` and a Start-menu-only entry;
it adds no Designer desktop shortcut or startup value. Rerun removal preserves
settings, installed skins, drafts/recovery, and imports. The fallback ZIP stays
normal-HUD-only and still supports `.cqskin` validation/import/rendering.

Current automated evidence is `PASS with unresolved historical anomaly`:
serial Release runs passed Core `75/75`, Skins `331/331`, App/UI `609/609`,
and Designer `348/348`, totaling `1363/1363`; the Release build reported zero
warnings/errors. The production installer passed seven isolated normal
install/upgrade/component/uninstall scenarios plus two committed-cleanup
failure scenarios. One earlier full run failed a single exact-directory Remove
assertion; isolated, class, project, parallel, cross-project, and 1000-operation
stress investigation did not reproduce it or capture `result.Errors`, so no
root cause or source fix is claimed. The final local Setup and ZIP hashes are
`A87631B96F21EF6C8E35B14F4ED64E411243D5DC071ABE680704C673225746DC`
and `044BD0B844AC7922E76B9EDD1C143152194B39A33CF231F159DDF3FB53E02D4D`.
The local install matches the packaged App/Designer hashes. GitHub Release
`v1.2.0` is public, Latest, non-draft, and non-prerelease; Setup, ZIP, and
`SHA256SUMS.txt` are uploaded and their online sizes/digests match the local
files. Some manual
layout/DPI/image, uninstall, sign-out, restart, and About-window rows remain
unrun; the manual matrix remains `PARTIAL` and must not be described as fully
accepted even though the user explicitly authorized the release with those
gaps recorded.

Designer output dialogs resolve against a shared loaded-window owner rather
than `Application.MainWindow`. This is required because loading or creating a
document replaces and closes the original editor window. The installed
reopened-draft workflow now reaches overwrite confirmation and the success
result, and the applied custom-skin directory contains the decoration asset and
animation parameters from the saved draft.

## v1.2.2 skin metadata correction

The v1.2.2 patch removes the universal `作者（未验证）` label because the
application has no author identity-verification system. It adds a template-
owned minimum HUD version to `ISkinTemplate`; the current
`free-decoration-ring` template declares `1.2.0`. Import preview displays the
higher of the package declaration and template minimum. The Designer derives
the value for new drafts and normalizes older drafts/packages in memory without
downgrading any higher future declaration. The next save/apply/export persists
the corrected value. No `.cqskin` schema or installer boundary changed.

Serial Release verification is Core `75/75`, Skins `335/335`, App/UI `609/609`,
and Designer `348/348`, totaling `1367/1367` with zero skipped. Release build
has zero warnings and zero errors. All nine isolated installer scenarios pass
and leave no smoke root or installer process.

The production Setup is 100,046,941 bytes with SHA-256
`B9BBB5D10377374AF3FA4A8B078DC4C87973682FBFA98A333F49316D04D0A4E9`.
The normal-only ZIP is 68,330,913 bytes with SHA-256
`F0D0E59BA056E5BB6A05E35E3FA080C1816416C3A304ACAFFCFDCD9631B8A498`.
`SHA256SUMS.txt` is 196 bytes with SHA-256
`97C6A94C9FDE3BEF4422CFD614ACDDCF8928F303598937089C635288D2E299E5`.

The maintainer machine was upgraded from v1.2.1 to v1.2.2 with exit code `0`.
Installed App and Designer match the publish tree and report
`1.2.2+2d53407bca90b580c937f56137872dee178352ff`. Formal startup, custom-skin
selection, installed skins, drafts, recovery, and preview state were retained.
The formal HUD refreshed successfully; the installed Designer opened and
closed normally. The maintainer desktop again contains only the separately
restored Developer Preview shortcut with `--preview`.

GitHub `main` passed the complete release gate at evidence commit
`29b9475e75247e7880d97b9103fb36b8d5232e90`. CI run `30897439435` succeeded
on attempt 2 after restore, all `1367/1367` tests, build, ephemeral packaging,
and all nine installer scenarios. Attempt 1 had timed out only while an
unrelated local-control test awaited final cleanup after its production
assertions; the exact local regression set passed `10/10` before the clean
rerun.

Annotated tag `v1.2.2` points to the evidence commit. The public v1.2.2 Release
is Latest, non-draft, and non-prerelease. Its Setup, ZIP, and checksum-file
names, sizes, and online SHA-256 digests exactly match the local release files.
Historical v1.2.0 and v1.2.1 tags and assets remain unchanged.

## v1.2.1 animation correction

The approved v1.2.1 patch changes only the shared
`free-decoration-ring` runtime motion profile. Full-ring glow opacity is capped
at `.40` even at maximum animation intensity, keeping it visually subordinate
to the solid progress arc. Decoration floating now maps the existing 0–1
intensity to `±8 * intensity` pixels and a `3.2 - 1.6 * intensity` second
half-cycle; zero remains disabled. No contract, schema, asset, manifest,
installer component, shortcut, startup, storage, or import boundary changes.
Existing `.cqskin` packages receive the corrected behavior when rendered by a
v1.2.1 HUD.

The v1.2.1 serial Release gate passed Core `75/75`, Skins `335/335`, App/UI
`609/609`, and Designer `348/348`, totaling `1367/1367` with zero skipped. The
Release solution build completed with zero warnings and zero errors.

The source Designer preview was visually accepted before packaging. Its current
`柔光玫瑰` state was exported and the package's four animation values were read
back successfully. Applying that draft to the installed v1.2.0 HUD updated and
reloaded the installed skin, but the formal HUD correctly continued to show the
old formula until its executable was upgraded.

The v1.2.1 production Setup is 100,052,875 bytes with SHA-256
`E01E942A03B6F31BE66794997676A6C21DA73F54B838E11513226C888524F572`.
The normal-HUD-only ZIP is 68,330,307 bytes with SHA-256
`36880B6D47AB731CC4B560D6E9CAA71BB7744C4081C6759189EFC0B721710F6F`.
`SHA256SUMS.txt` is 196 bytes with SHA-256
`31EC5130D49BC6F000A5E9BFAC1A3B5A12BD3DD858D7FED2E8967BD67FDB507E`.
The Setup and both packaged executables are unsigned. Seven normal isolated
installer scenarios plus both committed-cleanup failure scenarios passed.

The maintainer machine was upgraded in place from v1.2.0 to v1.2.1 with Setup
exit code `0`. Installed App and Designer exactly match the publish tree and
report `1.2.1+b1126e34f0a06a0e93458848ca347bce85d808bb`. Startup remains formal
`--background`; the normal and Designer Start-menu entries exist; the desktop
contains only the separately restored Developer Preview shortcut with
`--preview`. The selected custom skin, installed skin payload, Designer draft
and recovery document, and preview-window state were retained. The installed
formal HUD started and refreshed successfully, and the installed Designer
opened and closed normally.

GitHub `main` was fast-forwarded without a PR to evidence commit
`509314e88312ab0fee7cab1f26521ee51449cc0b`. CI run `30888862526` passed all
steps, including the full isolated installer matrix. Annotated tag `v1.2.1`
points to the evidence commit. The public v1.2.1 Release is Latest, non-draft,
and non-prerelease; its three online asset sizes and SHA-256 digests match the
local release files exactly. The v1.2.0 tag and assets were not changed.

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
- Installed release: `v1.2.2`
- Latest public release: `v1.2.2`
- Preferred new Codex project directory:
  `C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud`

The real machine currently has the verified `v1.2.2` package code from
`2d53407`. It includes the optional Designer, because that component was
selected for maintainer acceptance. The maintainer-only desktop Developer
Preview shortcut is local customization and is not public Setup behavior.

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
- 2026-08-03 pre-release optional Designer evidence at `168bf8b`: current
  per-project totals Core 75, Skins 325, App/UI 590, Designer 334; two
  trustworthy full reruns passed 1324/1324 after one unresolved earlier
  one-test storage failure. Security/rollback filters passed 223, 224, and 116
  tests; build was zero warnings/errors. Internal `0.0.0` Setup/ZIP inspection
  passed without installation. This historical gate did not itself authorize a
  release.
- 2026-08-04 v1.2.0 release baseline: Core 75/75, Skins 331/331, App/UI
  609/609, Designer 348/348, total 1363/1363 with zero skipped; Release build
  zero warnings/errors. Final Setup/ZIP hashes are recorded above, installer
  scenarios passed, and GitHub online asset metadata matches local artifacts.
  The documented manual matrix gaps and one unreproduced historical storage
  anomaly remain open evidence.
- GitHub Windows CI covers restore, test, build, and self-contained publish. Its
  package/smoke candidate uses ephemeral version `0.0.0`, not a public release
  version, and is not uploaded.
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
  do not reuse the historical `v1.1.1` 388-test count for current source.
- Use the Developer Preview sliders as the manual boundary and mixed-state
  tool for alert colors; inspect normal (`>20%`), Warning (`>10%..20%`), and
  Critical (`<=10%`) states independently for both quotas.
- Update the installed build only when the user asks to deploy locally.
- Do not move the release tag or replace release assets without an explicit
  release reason.
- Keep the two-direction desktop handoff unchecked until it is performed. Do
  not infer graceful listener acceptance from the legacy `v1.0.0` fallback.

