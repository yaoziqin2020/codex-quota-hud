# Current Task

## Unreleased v1.2.0 local candidate — PARTIAL

The current branch now includes the optional Skin Designer, the shared custom-
skin runtime, a compact About window, the Designer preview recovery fix for
`None -> Dual/5h/Week`, and the formal-HUD startup visibility regression fix.
It also includes immediate custom-skin catalog synchronization, the shared
`Documents\Codex Quota HUD Skins` exchange directory, direct custom-skin
selection, visible center-breathing and dedicated ring-glow animation, simple
`静止 / 柔和 / 明显` presets with collapsed advanced controls, compact grouped
synthetic-preview controls, readable light dropdowns, and a distinct Designer
application icon. Reopened drafts now also promote their replacement editor as
the owner for apply/export dialogs, so overwrite confirmation and result
dialogs remain usable after `打开草稿`. It is installed on the maintainer
machine as a local `1.2.0` acceptance candidate, but it is not tagged,
uploaded, or published.

Source commit `168bf8b2a58062f86c35b203eff6cf269b52bad9` adds the shared
custom-skin runtime/import path and a separate optional Skin Designer. It is
unreleased and is not part of the installed or published `v1.1.1` build.

### Current status

Automated evidence is `PASS with unresolved historical anomaly`; overall
feature acceptance is **PARTIAL — no public release is authorized**. The latest
serial Release runs passed Core `75/75`, Skins `331/331`, App/UI `609/609`, and
Designer `348/348`, for `1363/1363` with `0` skipped. Release build passed with
`0` warnings and `0` errors. The earlier exact security/rollback suites passed
Skins `223/223`, App `224/224`, and Designer `116/116`.

The local `v1.2.0` Setup upgrade completed with exit code `0`. Installed App
and Designer binaries are version `1.2.0.0`; both report product version
`1.2.0+7393ffc4983d03552314295fe74061781e0b1318`, and installed binaries
exactly match the packaged publish hashes. The uninstall entry reports
`1.2.0`; formal startup remains
`CodexQuotaHud.App.exe --background`. This local install selected startup and
the optional Designer, created the normal and Designer Start-menu shortcuts,
and created no normal desktop shortcut. After that product acceptance, the
maintainer desktop was separately restored to only
`Codex Quota HUD 开发预览` with `--preview`; this is local state, not Setup
behavior.

Installed-Designer GUI acceptance passed the user-reported regression paths:
selecting `None` hid the preview and each direct return to `Dual`, `5h`, and
`Week` made it visible again. The HUD context menu visibly contains `关于`.
The first packaged About build crashed at window construction because
`Assets/AppIcon.ico` had not been embedded as a WPF resource. Windows event
evidence identified the exact missing resource; a host-independent WPF
reproduction test failed before the fix and passes afterward. Commit `bf8e16d`
embeds the icon and also contains optional-window failures so they cannot crash
the HUD or Designer. The corrected package was rebuilt and reinstalled, and
its installed binaries match the corrected publish tree. Automated WPF tests
cover the installed-host resource construction, content, shared single-instance
coordinator, HUD/tray action wiring, failure containment, retry, and disposal.
The corrected real About window has not yet been visually accepted, so that
manual row remains `NOT RUN`, not `PASS`.

The user then reported that, after reboot, the tray had current quota data but
the formal HUD was absent. This is a new regression from preview recovery work,
not a historical startup race: commit `07b73d5` initialized automatic-show
suppression for every `QuotaOrbWindow`, while only Designer preview cleared it.
Commit `950b4d2` makes suppression an explicit Designer-preview-only choice.
Commit `c3d545e` also synchronizes a quota state that is already present when a
formal window finishes construction. Both arrival orders failed focused tests
before their fixes and now pass. A temporary opt-in runtime trace recorded the
real startup transition from hidden data to `model=True`, followed by
`Show()`, `IsVisible=True`, `Visibility=Visible`, and a nonzero WPF handle.
The temporary trace code and files were removed. The same startup fix remains
in the final packaged `7393ffc` executable, and the user directly confirmed
that the formal HUD is visible after the earlier diagnostic reinstall.

One earlier full-solution run at the same commit remains an unresolved anomaly:
`SkinPackageInstallerTests.Remove_DeletesExactlyOneCanonicalCustomDirectoryAndPreservesSiblings`
returned an invalid result, producing `1323` passed / `1` failed / `1324`
total. The assertion did not expose `result.Errors`. The exact test, its class,
the Skins project, eight parallel testhosts, cross-project load, and 1000 real
install/remove cycles all passed afterward. No root cause was proved and no
production or test change is claimed as a fix. Two later batches whose Skins
generated restore assets had been polluted by a diagnostic restore were
excluded rather than counted as passes; a forced canonical restore preceded
the final TRX-backed `1324/1324` run.

An earlier internal-only `0.0.0` package was compiled in a unique system-temp
root and was never run or installed. It contained exactly Setup, normal-only ZIP, and
`SHA256SUMS.txt`; the ZIP had exactly five approved entries and no Designer.
The Setup publish manifest contained exactly the normal HUD executable and
`designer/CodexQuotaHud.SkinDesigner.exe`. All three executables were
`NotSigned`, and the temporary root was deleted with a checked absent
postcondition. These hashes are ephemeral verification evidence, not release
hashes:

- Setup: `df89250dc2b68fa198196d48c6e2344efe4196c94c34f354728b2b66cc30cc8c`
- ZIP: `ca3ab3959633b241d2b4f709e2111a5e8e69cdfa64437f412df86933dea5d33d`
- `SHA256SUMS.txt`: `2db5e0053df13bedab36fec1e0b3de24b383c1b052df0775e20e6107f3a831b3`

Later, a separate production-structure `0.0.0` candidate was mistakenly
installed on the maintainer machine during package acceptance. It has now been
replaced by the verified local `1.2.0` candidate. Its obsolete Setup and ZIP
were removed; its install log and the pre-install rollback backup were retained
as evidence.

The current local candidate artifacts are:

- Setup: `CodexQuotaHud-Setup-v1.2.0.exe` — SHA-256
  `A87631B96F21EF6C8E35B14F4ED64E411243D5DC071ABE680704C673225746DC`
- ZIP: `CodexQuotaHud-v1.2.0-win-x64.zip` — SHA-256
  `044BD0B844AC7922E76B9EDD1C143152194B39A33CF231F159DDF3FB53E02D4D`

The production installer was also exercised through all seven isolated
install/upgrade/component/uninstall scenarios and both committed-cleanup
failure scenarios; every scenario passed and its temporary root was removed.
The final local reinstall completed with exit code `0`. The installed App and
Designer hashes match the final publish tree, startup is still `--background`,
normal and Designer Start-menu shortcuts exist, and the maintainer desktop was
restored to only `Codex Quota HUD 开发预览` with `--preview`.

The reopened-draft apply regression was reproduced against the old installed
candidate: the command returned without confirmation because output dialogs
still targeted the closed original editor. Commit `7393ffc` adds one shared
loaded-window owner, a focused replacement-window regression test, and the
corrected package. Installed GUI acceptance then opened the saved `柔光玫瑰`
draft, replaced the existing skin through the confirmation dialog, received
the success result, and started the HUD. The installed package now contains
`assets/decoration.png` and the saved rotation/breathing/glow values. Formal-HUD
rotation was not independently captured by automation, so direct visual
confirmation remains a manual check.

### Next continuation point

The installed Designer was closed for the final mutex-safe test run. Continue
from the manual matrix in
`docs/verification/2026-08-02-optional-skin-designer-acceptance.md`. Every
Designer layout/DPI/image/slot rows, fresh install without Designer, component
removal, uninstall, sign-out, restart, and the real About-window visual row are
not all complete. The authorized local version bump/package/install work is
complete. It does not authorize a tag, upload, GitHub Release, or replacement
of `v1.1.1` assets. The user separately authorized pushing this feature branch;
do not create a PR and do not push `main` unless separately requested.

If the storage Remove anomaly recurs, first preserve and expose the returned
error code/location/message and failing temp-root evidence; do not add retries,
sleeps, broad cleanup, or weaker path assertions without a proved root cause.

## v1.1.1 — released

The ordinary-user installer correction is published from tag commit `6515e7c`.
Fresh Release verification passed Core `55/55`, App/UI `333/333`, total
`388/388`; `dotnet build` reported `0` warnings and `0` errors. Final packaging
produced:

- `CodexQuotaHud-Setup-v1.1.1.exe` — 50,875,857 bytes, SHA-256
  `714714fdabdeafa9382ee797bd4aa6ef4ac50172ba4211af784b4c600072bfec`
- `CodexQuotaHud-v1.1.1-win-x64.zip` — 68,202,621 bytes, SHA-256
  `8210b0e1f1c490ee0f39c4ed9d8c8dd4d68d5cd261ee56d005df733a184a1bb`

The production Setup contains no internal smoke hooks. A separately compiled
temporary Setup passed clean install, upgrade/task replacement, default
settings-preserving uninstall, and explicit purge uninstall, then its isolated
files and registry values were removed with checked postconditions.

Real-machine acceptance is complete. The final Setup installed with exit code
`0`; installed and published executable hashes match. Startup runs the formal
HUD with `--background`. Setup's default desktop shortcut launches the real HUD
without arguments, the Start-menu entry remains formal, the `v1.1.0` Developer
Preview shortcut is removed, and two existing settings files retained their
hashes. After that acceptance, the maintainer desktop was separately customized
to keep only `Codex Quota HUD 开发预览` with `--preview`; this is local state and
is not created by Setup.

GitHub Release `v1.1.1` is public, marked latest, and is neither a draft nor a
prerelease. Setup, ZIP, and `SHA256SUMS.txt` are uploaded and their online sizes
and SHA-256 digests match the local release assets. The retained `v1.1.0`
Release is labeled developer-oriented and directs ordinary users to `v1.1.1`.

## v1.1.1 historical status

Version `1.1.1` is released. The implementation is integrated into `main`, tag
`v1.1.1` is pushed, the GitHub Release and all three assets are public, and the
published Setup is installed locally. No release action remains.

## v1.1.1 historical completed work

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
  remote state; the later installer work described above installed `v1.1.1`
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

## Historical optional v1.1.1 GUI checks

When GUI launch is authorized, use the Developer Preview sliders to inspect
the `21`, `20`, `11`, `10`, and `0` boundaries, plus dual mixed states, across
all five skins, full HUD, details, tray, and each collapsed edge side. Confirm
that colors return to normal above `20%` and that no flashing, popup, sound,
settings, or refresh behavior was introduced.

For optional GUI acceptance, start the installed normal HUD, then launch the
desktop `Codex Quota HUD 开发预览` shortcut and confirm the normal HUD exits
before preview opens. Click `退出预览并打开正式版` and confirm preview closes
before exactly one installed HUD returns. The installed `v1.1.1` release is
listener-enabled, so this now exercises graceful signalling rather than the
legacy fallback. Record tag commit `6515e7c` and the final Setup hash above.

These checks belong to the released `v1.1.1` history. They are not the active
continuation point for the unreleased Designer work; use the Task 18 manual
matrix linked at the top of this file. Do not reopen the completed installer
release unless a concrete issue appears.

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

