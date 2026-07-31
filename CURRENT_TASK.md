# Current Task

## v1.1.0 — released

The installer implementation is published from tag commit `205e7e5`.
Fresh Release verification passed Core `55/55`, App/UI `332/332`, total
`387/387`; `dotnet build` reported `0` warnings and `0` errors. Final packaging
produced:

- `CodexQuotaHud-Setup-v1.1.0.exe` — 50,884,027 bytes, SHA-256
  `9704080b7136273ac182bdb9a816553ebda62438e2efe1b7f3e639417c76b1cf`
- `CodexQuotaHud-v1.1.0-win-x64.zip` — 68,202,697 bytes, SHA-256
  `9b8e64d7b8d14cd6fdc3d0321b04d3299873e39051bb9aef39a62ce01f2e5594`

The production Setup contains no internal smoke hooks. A separately compiled
temporary Setup passed clean install, upgrade/task replacement, default
settings-preserving uninstall, and explicit purge uninstall, then its isolated
files and registry values were removed with checked postconditions.

Real-machine acceptance is complete for overwrite install and both uninstall
modes. The final Setup installed with exit code `0`; installed and published
executable hashes match. Startup runs the formal HUD with `--background`. The
desktop contains only `Codex Quota HUD 开发预览` with `--preview`; the legacy
normal desktop link is absent. The normal Start-menu entry remains. Both
uninstall modes and settings preservation passed real acceptance. The final published
Setup was installed again with exit code `0`; the installed executable version
is `1.1.0+205e7e5...` and its hash matches the published binary.

GitHub Release `v1.1.0` is public, marked latest, and is neither a draft nor a
prerelease. Setup, ZIP, and `SHA256SUMS.txt` are uploaded and their online sizes
and SHA-256 digests match the local release assets.

## Status

Version `1.1.0` is released. The implementation is integrated into `main`, tag
`v1.1.0` is pushed, the GitHub Release and all three assets are public, and the
published Setup is installed locally. No release action remains.

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
- That alert-only change did not itself alter installation, release assets, or
  remote state; the later installer work described above installed `v1.1.0`
  locally.

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

For optional GUI acceptance, start the installed normal HUD, then launch the
desktop `Codex Quota HUD 开发预览` shortcut and confirm the normal HUD exits
before preview opens. Click `退出预览并打开正式版` and confirm preview closes
before exactly one installed HUD returns. The installed `v1.1.0` release is
listener-enabled, so this now exercises graceful signalling rather than the
legacy fallback. Record tag commit `205e7e5` and the final Setup hash above.

Future work should start from user feedback or the optional GUI checks above;
do not reopen the completed installer release unless a concrete issue appears.

## Manual checks for future UI changes

- Primary and secondary monitor placement.
- Left, right, top, and bottom external-edge docking.
- Detail popup never overlaps the floating HUD.
- Single-click toggle and double-click refresh separation.
- Existing instance activation and tray percentage rendering.
- All five skins in both full HUD and edge-bar states.
- Installed-to-preview replacement through graceful listener shutdown.
- Preview-to-installed reverse handoff, including exactly one returned
  installed tray/HUD.

