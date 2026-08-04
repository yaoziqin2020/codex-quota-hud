# Current Task

## v1.2.3 released — installed, accepted, and public assets verified

The v1.2.3 release preserves every built-in skin's existing refresh-speed
profile and adds a shared 1.5-second post-refresh hold. Custom skins can use an
absolute `0x..4x` refresh multiplier and a `0..3` second hold. Older packages
without either property default to `2x` and `1.5` seconds. Re-refresh restarts
one hold; animation disable, hide, skin switch, detach, and disposal cancel it
immediately. The optional Skin Designer exposes both values and now uses shared
dark-themed WPF dialogs for Designer-owned messages while retaining native
Windows open/save pickers.

Final source commit is
`c66cf9d5d135b864ad90af5c74455177902c7c04`. Fresh Release verification
passed Core `75/75`, Skins `355/355`, App/UI `622/622`, and Designer
`395/395`, totaling `1447/1447` with zero failed or
skipped. `dotnet build .\CodexQuotaHud.sln -c Release --no-restore` completed
with zero warnings and zero errors, and `git diff --check` passed. The first
full-test and package invocations were blocked only by sandboxed NuGet access;
their authorized exact reruns completed successfully.

One final closeout test invocation was deliberately excluded after the
installed Designer left open for GUI handoff held the product single-instance
mutex, causing exactly three guard tests to fail. After closing that exact
installed process, the unchanged full Release command passed `1447/1447`.

The first post-acceptance GitHub CI attempt exposed a pre-existing narrow
wall-clock assertion in a real-pipe test; the protocol result succeeded, but a
loaded Runner took `2.815 s` against a redundant `<1.9 s` assertion. A rerun
then exposed a different real-WPF shutdown guard fixed at three seconds. The
test-only stabilization removes redundant elapsed-time assertions, places the
real-window activation class in the existing serial WPF collection, and uses a
15-second deadlock guard without changing any product timeout. Focused tests
passed `33/33`; the unchanged full Release suite passed `1447/1447` locally.

GitHub Actions run
[`30938108523`](https://github.com/yaoziqin2020/codex-quota-hud/actions/runs/30938108523)
then passed on commit `0f42c5d6b83b3ec97ba423ea8eaa20e9f8f9d010`.
Its complete Test and Build steps passed, the ephemeral CI candidates built,
and the isolated installer smoke matrix completed successfully.

The canonical release files are:

- Setup: `artifacts\release\CodexQuotaHud-Setup-v1.2.3.exe` — 100,048,867
  bytes — SHA-256
  `579C1CE73392970E93323C99600F013950CC463BC4BB5C4B305085584F743F80`
- ZIP: `artifacts\release\CodexQuotaHud-v1.2.3-win-x64.zip` — 68,335,551
  bytes — SHA-256
  `16BF2692D591D039014CD2976CD639DE5B5C599F19826B8A07E7E0A1631504D1`
- `artifacts\release\SHA256SUMS.txt` — 196 bytes — SHA-256
  `DCE728EE15522FFDAABF841C11F90310DDC5342A920B01EADA41730FA04D2771`

The checksum manifest has exactly two lowercase entries and both match. The
ZIP has exactly the five approved normal-HUD fallback entries and no Designer.
The Setup publish tree has exactly the App plus
`designer\CodexQuotaHud.SkinDesigner.exe`; the optional-component installer
matrix confirms Designer is absent by default and present only when selected.
App and Designer are `1.2.3.0` and report
`1.2.3+c66cf9d5d135b864ad90af5c74455177902c7c04`. Setup reports product version
`1.2.3`. Setup, App, and Designer are all `NotSigned`, with no signer or
timestamper certificate.

All seven normal isolated installer scenarios and both committed-cleanup
failure/retry scenarios passed against the final Setup in `946.3 s`. Final
checks found zero installer-smoke roots and zero installer processes.

The real v1.2.2-to-v1.2.3 upgrade was then run with startup and Designer.
Setup exited `0`; the retained log is
`C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731\artifacts\release\CodexQuotaHud-Setup-v1.2.3-install.log`.

Before/after inventory contained 23 user-data files; none was missing and none
changed hash. Installed App/Designer hashes exactly match publish, both report
`1.2.3.0` plus final commit `c66cf9d`, the uninstall entry reports `1.2.3`, and
startup is still the installed formal HUD with `--background`. Normal HUD and
Designer Start-menu shortcuts exist. The ordinary Setup correctly created no
Developer Preview desktop entry; the maintainer-only desktop shortcut was
restored separately with `--preview`. The installed formal HUD process is
running.

Installed Designer GUI smoke passed the default `2.0×`/`1.5 秒` display,
`0×` pause safety, `4.0×`/`3.0 秒` extremes, speed/hold preservation across
all three presets, one dark unsaved dialog, dark disabled background controls,
and a native file picker rooted at the Designer drafts directory. GUI coverage
of exact motion timing, all dialog variants, legacy-package output, and
sign-out/restart remains partial or not run. On 2026-08-05, the user completed
hands-on testing of this exact installed candidate, reported no issues, and
accepted it for Git synchronization.

Annotated tag `v1.2.3` points to `0f42c5d6b83b3ec97ba423ea8eaa20e9f8f9d010`.
The [public GitHub Release](https://github.com/yaoziqin2020/codex-quota-hud/releases/tag/v1.2.3)
is Latest, non-draft, and non-prerelease. Its three uploaded assets have the
exact local names and sizes above. A fresh public download of all three files
reproduced their local SHA-256 values exactly. GitHub `main` and the retained
feature branch were fast-forwarded directly without a pull request; the
release tag remains fixed at the tested release-evidence commit.

## v1.2.2 skin metadata correction — released

The v1.2.2 patch removes the misleading `作者（未验证）` import label and
derives the effective minimum HUD version from the selected skin template.
The current `free-decoration-ring` template requires HUD `1.2.0`. New drafts
use that value automatically; older drafts, installed skins, and packages
recorded as `1.1.1` are normalized in memory when opened for editing, so their
next save/apply/export writes the corrected value. Import preview also shows
the effective template minimum for older packages. The `.cqskin` schema and
installer structure are unchanged.

Fresh serial Release verification passed Core `75/75`, Skins `335/335`,
App/UI `609/609`, and Designer `348/348`, totaling `1367/1367` with zero
skipped. Release build passed with zero warnings and zero errors. Focused tests
were first observed failing against the old author/minimum-version behavior and
then passing after the fix.

The v1.2.2 production assets are:

- Setup: `CodexQuotaHud-Setup-v1.2.2.exe` — 100,046,941 bytes — SHA-256
  `B9BBB5D10377374AF3FA4A8B078DC4C87973682FBFA98A333F49316D04D0A4E9`
- ZIP: `CodexQuotaHud-v1.2.2-win-x64.zip` — 68,330,913 bytes — SHA-256
  `F0D0E59BA056E5BB6A05E35E3FA080C1816416C3A304ACAFFCFDCD9631B8A498`
- `SHA256SUMS.txt` — 196 bytes — SHA-256
  `97C6A94C9FDE3BEF4422CFD614ACDDCF8928F303598937089C635288D2E299E5`

The ZIP contains exactly the five approved normal-HUD fallback entries and no
Designer. Setup contains the normal HUD plus the optional Designer. Setup and
both executables are `NotSigned`. All seven normal isolated installer scenarios
and both committed-cleanup failure scenarios passed; no installer temp root or
installer process remained.

The real local v1.2.1-to-v1.2.2 upgrade completed with exit code `0`. Installed
App and Designer are `1.2.2.0`, report
`1.2.2+2d53407bca90b580c937f56137872dee178352ff`, and exactly match the
packaged publish hashes. The uninstall entry is `1.2.2`; startup remains formal
`--background`; normal and Designer Start-menu shortcuts exist; no normal
desktop shortcut exists. The maintainer-only Developer Preview shortcut was
separately restored with `--preview`.

The selected custom skin, animation state, installed-skin payload, Designer
drafts, recovery data, and preview-window state were retained. The installed
formal HUD started and refreshed successfully. The installed Designer opened
with its complete editor/preview surface and closed normally. The exact import
metadata presentation is covered by the WPF regression test and packaged-binary
identity; the installed HUD import dialog was not separately opened for a
visual screenshot.

GitHub `main` was fast-forwarded without a PR to release-evidence commit
`29b9475e75247e7880d97b9103fb36b8d5232e90`. CI run
[`30897439435`](https://github.com/yaoziqin2020/codex-quota-hud/actions/runs/30897439435)
passed on attempt 2: restore, `1367/1367` tests, build, Inno Setup installation,
ephemeral packaging, and all nine isolated installer scenarios completed
successfully. Attempt 1 had already completed its production assertions before
an unrelated local-control test timed out during final cleanup; the exact local
test then passed `10/10` before the successful complete rerun.

Annotated tag `v1.2.2` points to the evidence commit. The public
[GitHub Release](https://github.com/yaoziqin2020/codex-quota-hud/releases/tag/v1.2.2)
is Latest, non-draft, and non-prerelease. Its three online asset names, sizes,
and SHA-256 digests exactly match the local files above. Historical v1.2.0 and
v1.2.1 tags and assets were not modified.

## v1.2.1 animation correction — released

The user approved a v1.2.1 patch after direct visual inspection in the source
Designer. The custom quota-ring glow peak is capped below the solid progress
arc, and decoration floating now has practical travel and timing across the
existing 0–1 control range. The `.cqskin` schema, current skin parameters,
installer component boundary, and ordinary-user shortcut behavior are
unchanged. Production packaging, local upgrade acceptance, `main` integration,
tag, GitHub Release, and online-asset verification are complete. The published
v1.2.0 assets and tag remain unchanged.

Serial Release verification passed Core `75/75`, Skins `335/335`, App/UI
`609/609`, and Designer `348/348`, totaling `1367/1367` with zero skipped.
The Release solution build passed with zero warnings and zero errors.

The current `柔光玫瑰` parameters were exported to
`Documents\\Codex Quota HUD Skins\\柔光玫瑰.cqskin` and independently read back
from `theme.json`. Applying them to the installed v1.2.0 HUD updated the skin
files and exercised catalog refresh successfully, but the formal visual still
used the old animation formula because the installed executable was v1.2.0.

The production v1.2.1 assets are:

- Setup: `CodexQuotaHud-Setup-v1.2.1.exe` — 100,052,875 bytes — SHA-256
  `E01E942A03B6F31BE66794997676A6C21DA73F54B838E11513226C888524F572`
- ZIP: `CodexQuotaHud-v1.2.1-win-x64.zip` — 68,330,307 bytes — SHA-256
  `36880B6D47AB731CC4B560D6E9CAA71BB7744C4081C6759189EFC0B721710F6F`
- `SHA256SUMS.txt` — 196 bytes — SHA-256
  `31EC5130D49BC6F000A5E9BFAC1A3B5A12BD3DD858D7FED2E8967BD67FDB507E`

The ZIP has exactly the five approved fallback entries and contains no
Designer. The Setup payload contains the normal HUD plus the optional Designer.
The Setup and both executables are `NotSigned`. All seven normal isolated
installer scenarios and both committed-cleanup failure scenarios passed and
left no failed or active temporary root.

The real local v1.2.0-to-v1.2.1 upgrade completed with exit code `0`. Installed
App and Designer are version `1.2.1.0`, report product version
`1.2.1+b1126e34f0a06a0e93458848ca347bce85d808bb`, and exactly match the packaged
publish hashes. The uninstall entry is `1.2.1`; startup remains formal
`--background`; normal and Designer Start-menu shortcuts exist; no normal
desktop shortcut exists. The maintainer-only `Codex Quota HUD 开发预览` desktop
shortcut was separately restored with `--preview`.

The upgrade retained the selected `柔光玫瑰` skin, its assets/theme, the current
draft and recovery document, and preview-window state. `settings.json` was
normally re-saved when the v1.2.0 HUD exited, but its window position,
animation flag, custom-skin selection, and refresh state remained intact. The
installed formal HUD started and refreshed quota successfully. The installed
Designer opened with the complete editor/preview surface and then closed
normally. Source Designer motion was visually accepted; the installed
transparent HUD could not be independently captured as a targetable window.

GitHub `main` was fast-forwarded without a PR to evidence commit
`509314e88312ab0fee7cab1f26521ee51449cc0b`. CI run
[`30888862526`](https://github.com/yaoziqin2020/codex-quota-hud/actions/runs/30888862526)
passed restore, all tests, build, Inno Setup installation, ephemeral packaging,
and the full isolated installer smoke matrix. Annotated tag `v1.2.1` points to
that evidence commit. The public GitHub Release is Latest, non-draft, and
non-prerelease. Its three online asset sizes and SHA-256 digests exactly match
the local files above; the v1.2.0 Release was not modified.

## v1.2.0 released — historical baseline

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
machine and publicly released as `v1.2.0`.

Source commit `168bf8b2a58062f86c35b203eff6cf269b52bad9` is the shared
custom-skin runtime/import and optional Skin Designer base. The final packaged
code is `7393ffc4983d03552314295fe74061781e0b1318`; release tag `v1.2.0`
points to evidence commit `4a7c4c150315a37807a97b17d5cb4605236bf84c`.

### Current status

Automated evidence is `PASS with unresolved historical anomaly`; remaining
manual-matrix coverage is still **PARTIAL**, but the user explicitly authorized
the public release after reviewing that scope. The latest
serial Release runs passed Core `75/75`, Skins `331/331`, App/UI `609/609`, and
Designer `348/348`, for `1363/1363` with `0` skipped. Release build passed with
`0` warnings and `0` errors. The earlier exact security/rollback suites passed
Skins `223/223`, App `224/224`, and Designer `116/116`.

GitHub `main` was fast-forwarded to the tag commit without a PR. Public Release
`v1.2.0` was published on 2026-08-04, is neither draft nor prerelease, and is
the repository's Latest Release. Setup, ZIP, and `SHA256SUMS.txt` are all
uploaded; GitHub's online asset sizes and SHA-256 digests match the local
release files. Public `v1.1.1` assets were not replaced. Main-branch CI now
uses version `0.0.0` for its ephemeral package/smoke candidate instead of
mislabeling current source as `1.1.1`.

The first post-merge Windows CI run exposed one checkout-dependent test
failure: raw-string canonical JSON expectations inherited CRLF while the
serializer correctly emitted LF. The test expectation now explicitly
normalizes to LF; production serialization and published binaries are
unchanged. The replacement CI run is the verification gate for this fix.

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

The public v1.2.0 artifacts are:

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

v1.2.1 packaging, local installation, `main` fast-forward, green CI, tag, and
public GitHub Release are complete. The installed formal HUD is running and the
installed Designer was closed after launch acceptance. Continue from the manual matrix in
`docs/verification/2026-08-02-optional-skin-designer-acceptance.md`. Every
Designer layout/DPI/image/slot rows, fresh install without Designer, component
removal, uninstall, sign-out, restart, and the real About-window visual row are
not all complete. Do not move tags or replace the three public v1.2.0/v1.2.1
assets; any correction must use an explicitly approved new version. No PR is
needed for this completed release.

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

