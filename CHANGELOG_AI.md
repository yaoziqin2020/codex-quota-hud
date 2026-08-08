# AI Change Log

## 2026-08-09 — v1.3.0 installed candidate accepted for publication

- The user completed the installed hands-on checks for the exact fixed
  candidate built from `cd5634cfd7fd50b7ceb2875aa6661113cc5953cc`, reported
  no issues, and accepted it for Git synchronization and public v1.3.0 release.
- The ten automated Computer Use rows remain `NOT RUN` as honest historical
  evidence because both automation sessions failed before input. They are not
  rewritten as automated passes; the separate user-manual acceptance gate is
  `PASS`.
- Source `1561/1561`, Release build, package identity, installer matrix `9/9`,
  real upgrade, state restoration, and evidence review remain unchanged and
  green. Publication is now authorized; push, `main` integration, annotated
  tag, GitHub Release asset upload, CI, and public readback are the remaining
  closeout steps.

## 2026-08-08 — v1.3.0 fixed candidate rebuilt; installed UI blocked before input

- Reverified exact source `cd5634cfd7fd50b7ceb2875aa6661113cc5953cc`
  through fresh serial Release suites: Core `75/75`, Skins `375/375`, App/UI
  `625/625`, and Designer `486/486`; total `1561/1561`, failed `0`, skipped
  `0`. Release build warnings/errors were `0/0`; `git diff --check` emitted no
  output.
- Rebuilt Setup `100,073,545` bytes /
  `3d2a2e83275afa23c45debc641ae0efcdabde201a018bac9b788ce42dc3cc355`,
  normal-HUD ZIP `68,342,651` bytes /
  `f3c91d28812af5305499fb65ba8e80ff27f95ebbcc3728dddf546e17560c1f8b`,
  and 196-byte `SHA256SUMS.txt` /
  `15ee7c679d363ea1dfcc141d1228953d080178af72066dfcec069cbfd23dba00`.
  The manifest has exactly two matching lowercase lines; ZIP has exactly the
  five approved normal-HUD entries and no Designer. Setup/App/Designer are
  `NotSigned`.
- Passed the exact isolated installer matrix `9/9`: all seven normal scenarios
  and both committed-cleanup failure scenarios completed with checked cleanup.
- Added an evidence-review provenance supplement without replacing historical
  artifacts. `12-post-install.json` has a capture defect: its uninstall
  DisplayVersion is null and does not support that field; a literal read-only
  query in `19-uninstall-registry-supplement.json` returns `1.3.0` from the
  exact canonical HKCU key, while Setup-log lines show that key being recreated.
- Recorded both Computer Use attempts by actor/session, unique exact window,
  retained failed call names, allowed recovery, exact runtime error, zero
  inputs, and stop decision in `20-computer-use-attempts-supplement.json`.
  Per-call timestamps/full serialized tool objects were not retained; the
  attempts are bounded by exact launch/close evidence.
- Recorded exact source/build/diff/package/matrix commands, raw-log hashes,
  product-source parent identity, and a fresh aggregate smoke-root count of
  `0` in `21-command-state-provenance-supplement.json`. The literal backup/
  restore shell transcript was not retained, so the supplement explicitly
  limits provenance to the bounded method and named before/after evidence.
- Took a bounded stable backup after gracefully stopping the exact formal HUD.
  Real Setup selected startup plus Designer, omitted the ordinary desktop
  shortcut, and exited `0`. Installed App
  `8bf7cbbf51894338178e8fe3a17ceab2e9e6a1ba123ef87ad8d1dac87f4b638f`
  and Designer
  `ec4c947077af3575bc4236f6cc7753eaa224767e113906e904aa1c1c2b55b7ea`
  exactly match publish at `1.3.0.0 + cd5634c`; uninstall is `1.3.0`, startup
  is exact `--background`, Start-menu targets are exact, and bounded state was
  preserved. The maintainer preview shortcut was restored separately with
  exact `--preview`, not by Setup.
- Installed UI acceptance is entirely `NOT RUN`. The Task 8 agent and root
  independently read the full Computer Use guidance/confirmation policy,
  selected the unique exact installed Designer window, and received
  `node_repl exec context not found` from `get_window`/`get_window_state`
  before the first input. The documented refresh/reset/reinitialize/retry
  recovery reproduced the failure. The non-negotiable policy prohibited the
  prior UIA fallback, and source tests were not substituted for direct
  installed evidence.
- Therefore Undo/Redo and shortcuts, branch/save/reopen, image Discard/Save
  byte identity, picker cancellation, six auditions, Apply identity/actual HUD
  switch, untouched v1.2.3 import with effective `0/0`, tray actions, and
  active guide/audition close isolation all remain `NOT RUN`.
- Preserved the blocked-run state under ignored evidence, restored the stable
  backup exactly, and removed the controlled live fixture. Final state has
  34/34 files with zero non-settings and stable-settings differences; both
  exchange packages, startup, and preview shortcut are exact. The fixed formal
  HUD is running and Designer is closed.
- Overall status remains `FAIL — candidate not release-ready`. Task 4's
  `fbdf23c` failures remain historical evidence. No product-code change, push,
  merge, tag, upload, GitHub Release, public readback, or historical-asset
  mutation was performed.

## 2026-08-08 — v1.3.0 undo/redo candidate rejected after installed smoke

- Reverified source `fbdf23c659cc524224bcd51d2b1581efde43153f` with fresh
  serial Release suites: Core `75/75`, Skins `375/375`, App/UI `625/625`, and
  Designer `433/433`; total `1508/1508`, failed `0`, skipped `0`. Release build
  warnings/errors were `0/0`; `git diff --check` emitted no output.
- Rebuilt the current assets: Setup `100,056,769` bytes / SHA-256
  `a3352f5e74e186cb698431897d0b991fde41a2e1de86047547e6a9d5c55a8d2d`,
  normal-HUD ZIP `68,342,354` bytes /
  `61d9d04b2c5495dc041fc2e2a528dfa31908b4a196449303b353cbe88f32a2ef`,
  and 196-byte `SHA256SUMS.txt` /
  `df99aad23eed4882e173076bbac2ed1f924ba94a1be9b96d9da202cccb8b1751`.
  The manifest has exactly two matching lowercase lines; ZIP has exactly five
  normal-HUD entries and no Designer. Setup/App/Designer are `NotSigned`.
- Passed all nine isolated installer scenarios with checked cleanup and zero
  final current-run smoke roots/processes. Two stale historical isolated-test
  uninstall entries whose install roots were already absent remain outside the
  current matrix roots and were deliberately not changed.
- Upgraded the real local install with Designer + startup selected and normal
  desktop icon omitted. Setup exited `0`; installed App
  `940ca077805b0eb12ec200fa8ee56aef8a265726403ce88aea9db32d1188f5bc`
  and Designer
  `27521dcca14b2e5eb55c01270093557956a2b77f79e1d1e638b332dcd03895f0`
  match publish at `1.3.0.0 + fbdf23c`. Startup remained exact
  `--background`; Setup created no desktop product link. The exact preexisting
  maintainer standard-App `--preview` shortcut was restored separately and is
  not installer behavior.
- Direct installed undo/redo smoke found a release-blocking stale-control
  defect. Undo/Redo and `Ctrl+Z`/`Ctrl+Y` changed preview geometry and history
  availability, but did not update the edited slider/value. Branch invalidation
  and save/reopen persistence passed, but the required observable control +
  preview round trip did not.
- Found a second destructive installed defect: after successful decoration
  removal, choosing Discard at the dirty prompt closed the draft but left
  `assets\decoration.png` physically deleted while saved `draft.json` still
  referenced it. Reopening reported `document.asset-missing`.
- Installed rows total `5 PASS / 1 FAIL / 3 PARTIAL / 2 NOT RUN`. Six nonzero
  animation auditions and Apply-to-HUD result/actual formal-HUD selection
  passed. Post-Designer-close isolation is `PARTIAL`: guides were Off and
  audition was All before close, so active-state cleanup was not exercised.
  Old-package import stopped when the
  filename failed exact round-trip verification; the picker was safely
  cancelled. Tray operation stopped when exact icon hit-testing failed before
  input; no right-click was sent.
- Restored all bounded user state. The 33 non-settings files match preinstall
  bytes/hashes; settings stable fields and selected skin match with only the
  allowed refresh advance; both exchange packages and the maintainer preview
  shortcut match exactly. Formal HUD was relaunched; Designer is closed.
- Overall status is `FAIL — candidate not release-ready`. No product-code fix,
  push, merge, tag, upload, GitHub Release, or public readback was performed.

## 2026-08-08 — v1.3.0 local candidate packaged and installed

- Prepared candidate documentation for the Skin Designer authoring upgrade
  without claiming package, installer, installed-GUI, user-acceptance, or
  remote-release evidence before it exists.
- The two new persisted controls are text vertical offset (`-32..32 DIP`) and
  number/time line-gap increment (`-16..32 DIP`). Missing v1.2.x properties
  default to `0/0`; new output declares minimum HUD `1.3.0` under schema `1`.
- Added Designer-only click-through composition guides, non-persistent
  animation-channel audition, and exact `free-decoration-ring` preview
  Dual/5h/Week/None role guidance. The five built-in skins retain their
  established center/inner 5h and outer Week geometry.
- Corrected the native export picker to start in the shared skin exchange
  directory and expanded Apply/Export feedback to include exact identity and
  truthful activation disposition.
- Task 8 source evidence remains `1499/1499` with zero failed/skipped, a
  zero-warning/error Release build, and clean diff check. Task 9 then executed
  each of the ten installed-GUI rows individually with direct or honest blocked
  evidence: `6 PASS / 3 PARTIAL / 1 NOT RUN`.
- Packaged Setup `100,056,640` bytes / `ceccf8c0…e3ff`, normal-only ZIP
  `68,341,872` bytes / `9b91351d…0fba`, and a 196-byte checksum manifest.
  Manifest, exact ZIP/publish structure, App/Designer/Setup versions, and all
  three `NotSigned` results pass.
- All seven normal isolated installer scenarios and both committed-cleanup
  failure scenarios passed. The one sandbox-denied diagnostic is excluded and
  its exact root was safely removed; final smoke-root count is zero.
- Real Setup selected startup + Designer and omitted the normal desktop icon;
  exit was `0`. The verified upgrade scope is installed App/Designer matching
  publish at `1.3.0.0 + aecaea1`, correct uninstall/startup/Start-menu state,
  preserved data, and successful binary launch. Thirty-four state files and
  two exchange packages were preserved, with only the expected refresh
  timestamp advance.
- Setup left zero product desktop links before local customization. The
  maintainer preview shortcut was then separately restored from its exact
  backup with `--preview`; this is explicitly not Setup behavior.
- Formal HUD and installed Designer are running. Direct installed GUI checks
  passed text offset, line gap, guides/click-through, refresh-state restore,
  `free-decoration-ring` display semantics, and the export picker.
  Undo/redo/save/reopen, legacy
  same-context visual equivalence, and distinct animation-channel visuals are
  partial. Apply was stopped before input when the exact identity gate failed
  twice and remains `NOT RUN`; user practical acceptance is also `NOT RUN`.
- Removed only the Task 9-created temporary draft after exact absolute-boundary
  and no-reparse validation, reopened the installed Designer, and reverified
  `34` state files with non-settings diffs `0`, stable settings/selected skin,
  and both exchange package names/hashes unchanged. No temporary installed skin
  exists; only the normal refresh timestamp advanced.
- Three additional plan-required installed checks remain `NOT RUN`: operating
  the installed tray menu, importing an old package through the installed
  product, and directly inspecting the formal HUD for guide/audition leakage
  after Designer closes. Task 8 source compatibility/isolation automation does
  not substitute for installed smoke; overall Task 9 remains `PARTIAL`, and
  remote release Step 7 remains prohibited.
- Ordinary Setup contract remains unchanged: startup and normal desktop icon
  default selected and deselectable, no `--preview`; Designer optional and
  unchecked; ZIP normal-HUD-only; Setup removes preview entries. The
  maintainer-only preview shortcut is snapshotted/restored separately and is
  not Setup behavior.
- No push, `main` integration, tag, GitHub Release, upload, public readback, or
  historical-asset mutation is authorized before explicit user acceptance.

## 2026-08-05 — v1.2.3 public release completed

- Fast-forwarded both `main` and the retained feature branch directly to
  `0f42c5d6b83b3ec97ba423ea8eaa20e9f8f9d010` without a pull request, after
  adding the project timing-test stability rule and test-only CI fixes.
- GitHub Actions run
  [`30938108523`](https://github.com/yaoziqin2020/codex-quota-hud/actions/runs/30938108523)
  passed the complete Test and Build steps, ephemeral CI candidate packaging,
  and isolated installer smoke matrix.
- Created annotated tag `v1.2.3` at `0f42c5d` and published
  [Codex Quota HUD v1.2.3](https://github.com/yaoziqin2020/codex-quota-hud/releases/tag/v1.2.3)
  as Latest, non-draft, and non-prerelease.
- Uploaded only the approved Setup, normal-HUD ZIP, and `SHA256SUMS.txt` assets.
  Their public API names, sizes, and digests match local artifacts. All three
  assets were then downloaded afresh; each downloaded SHA-256 reproduced its
  local value exactly.

## 2026-08-05 — v1.2.3 final local candidate installed and user-accepted

- Added a shared requested/effective refresh animation state machine. Built-in
  skin speed profiles are unchanged and receive a 1.5-second completion hold.
  Custom skins support an absolute `0x..4x` refresh multiplier and `0..3`
  second hold. Re-refresh restarts one hold; disable, hide, target switch,
  detach, and dispose cancel immediately without timer or multiplier stacking.
- Extended the strict skin contract with canonical
  `refreshSpeedMultiplier`/`refreshHoldSeconds` properties. Missing properties
  default to `2x`/`1.5s`, so old packages require no migration. New Designer
  output writes both fields and declares minimum HUD `1.2.3`.
- Added the two Skin Designer controls and real custom-preview wiring, including
  `0x` paused WPF clocks, 0.1-step quantization, accessible names, and shared
  validation/draft-dirty behavior.
- Moved the Designer palette and button template to shared resources and
  replaced Designer-owned native message boxes with typed accessible
  dark-themed WPF dialogs. Native Windows open/save pickers remain allowed.
- Final branch review fixed three release-gate defects: animation presets now
  preserve refresh speed/hold, the currently focused dialog action wins Enter,
  and pressed buttons show accent tint plus a 1-DIP depression. Final source is
  `c66cf9d5d135b864ad90af5c74455177902c7c04`.
- Fresh Release verification passed Core `75/75`, Skins `355/355`, App/UI
  `622/622`, and Designer `395/395`, totaling `1447/1447`, zero failed/skipped.
  Release build completed with zero warnings/errors; `git diff --check` passed.
- Built local candidate Setup
  `579C1CE73392970E93323C99600F013950CC463BC4BB5C4B305085584F743F80`
  (100,048,867 bytes), ZIP
  `16BF2692D591D039014CD2976CD639DE5B5C599F19826B8A07E7E0A1631504D1`
  (68,335,551 bytes), and 196-byte checksum manifest
  `DCE728EE15522FFDAABF841C11F90310DDC5342A920B01EADA41730FA04D2771`.
  The manifest matches both artifacts; ZIP has exactly five approved entries
  and no Designer; Setup publish has exactly App plus optional Designer.
- App and Designer report
  `1.2.3+c66cf9d5d135b864ad90af5c74455177902c7c04`; Setup reports product
  version `1.2.3`. Setup and both executables are `NotSigned`.
- All seven normal isolated installer scenarios plus both committed-cleanup
  failure/retry scenarios passed against the final Setup in `946.3 s`, with
  zero final smoke roots or installer processes.
- Installed the final Setup locally over v1.2.2 with startup plus optional
  Designer; exit code was `0`. All 23 existing user-data files retained their
  paths and hashes. Installed App/Designer versions and hashes match publish,
  uninstall reports `1.2.3`, startup remains formal `--background`, and the
  formal HUD process is running. The maintainer-only `--preview` desktop
  shortcut was restored separately after Setup, preserving ordinary-user Setup
  semantics.
- Installed Designer GUI smoke verified the dark main window, default
  `2.0×`/`1.5 秒`, `0×` pause safety, `4.0×`/`3.0 秒` extremes, all three
  presets preserving both values, a dark unsaved dialog, dark disabled owner
  controls, and the native picker rooted at the drafts directory. The smoke
  modified only an unnamed temporary draft and restored a new default draft;
  no user skin was saved, applied, exported, or overwritten.
- The user completed hands-on testing of the exact installed candidate,
  reported no issues, and accepted it for Git synchronization. Exact motion
  timing across all skin combinations, every dialog variant, legacy-package
  output, and sign-out/restart remain partial or `NOT RUN`; those honest manual
  limits remain unchanged by the later public release.
- The first two post-acceptance CI attempts exposed two pre-existing timing
  assumptions under Runner load: a successful real-pipe result failed a
  redundant `<1.9 s` wall-clock assertion at `2.815 s`, then a real-WPF test
  exceeded its three-second cleanup guard. Test-only stabilization removes the
  redundant elapsed assertions, places the real-window activation class in the
  existing serial WPF collection, and uses a 15-second deadlock guard. Product
  timeout behavior is unchanged. Focused tests passed `33/33`; the full local
  Release suite passed `1447/1447`.

## 2026-08-04 — v1.2.2 skin metadata correction released

- Replaced the import-preview label `作者（未验证）` with `作者`. Codex Quota
  HUD currently has no author identity-verification system, so it no longer
  presents every package author as universally unverified.
- Added `MinimumHudVersion` to the versioned skin-template contract. The only
  current template, `free-decoration-ring`, declares its true minimum as HUD
  `1.2.0`. Import preview displays the effective maximum of the package and
  template declarations.
- New Designer drafts derive compatibility from the template. Opening an older
  draft, editing an installed skin, or importing a package for editing raises
  an incorrect `1.1.1` declaration to `1.2.0` in memory; the next save, apply,
  or export persists it. Higher future declarations are not downgraded.
- Updated the source/debug HUD and Designer runtime baseline to `1.2.0`, while
  published builds still detect and use their actual assembly version.
- Added focused regression coverage for the author label, effective import
  compatibility, template metadata, new-draft generation, and old draft/package
  normalization. Fresh serial Release verification passed Core `75/75`, Skins
  `335/335`, App/UI `609/609`, and Designer `348/348` (`1367/1367`, skipped
  `0`); Release build reported zero warnings and zero errors.
- Packaged Setup `B9BBB5D10377374AF3FA4A8B078DC4C87973682FBFA98A333F49316D04D0A4E9`
  (100,046,941 bytes), ZIP
  `F0D0E59BA056E5BB6A05E35E3FA080C1816416C3A304ACAFFCFDCD9631B8A498`
  (68,330,913 bytes), and the 196-byte checksum manifest. The ZIP has exactly
  five approved entries and no Designer; all three binaries are unsigned.
- All seven normal isolated installer scenarios and both committed-cleanup
  failure scenarios passed with no residual smoke root/process. The real
  v1.2.1-to-v1.2.2 upgrade then exited `0`; installed App/Designer match the
  publish tree and report
  `1.2.2+2d53407bca90b580c937f56137872dee178352ff`.
- Retained formal startup, the selected custom skin, installed skin payload,
  Designer drafts/recovery, and preview state. Restored the maintainer-only
  `--preview` desktop shortcut separately. The installed formal HUD refreshed;
  the installed Designer opened and closed normally.
- Fast-forwarded `main` without a PR to release-evidence commit `29b9475`.
  CI run `30897439435` passed on attempt 2, including `1367/1367` tests,
  build, ephemeral packaging, and all nine installer scenarios. Attempt 1
  timed out only in an unrelated local-control test's final cleanup after its
  production assertions; its exact local regression set passed `10/10` before
  the complete successful rerun.
- Created annotated tag `v1.2.2` at `29b9475` and published the GitHub Release
  as Latest, non-draft, and non-prerelease. All three online asset names, sizes,
  and SHA-256 digests match the local production files exactly. Historical
  v1.2.0/v1.2.1 tags and assets are unchanged.

## 2026-08-04 — v1.2.1 animation correction released

- Reduced the custom quota-ring glow peak from `.15 + .75 * intensity` to
  `.15 + .25 * intensity`, while retaining the existing `.08` floor and
  timing. At maximum intensity the full-ring glow now tops out at `.40`, so it
  cannot visually compete with the solid progress arc and resemble 100%.
- Replaced decoration floating's effectively invisible `±2 * intensity`
  travel and `2.5 / intensity` half-cycle with a direct `±8 * intensity`
  travel and `3.2 - 1.6 * intensity` half-cycle. The existing Gentle `.15`
  and Noticeable `.25` presets now produce visible motion, while zero still
  disables the animation.
- Added focused motion-profile tests for approved glow and floating ranges.
  The `.cqskin` schema and stored animation parameters remain unchanged;
  existing packages pick up the new behavior from the upgraded runtime.
- Serial Release verification passed Core `75/75`, Skins `335/335`, App/UI
  `609/609`, and Designer `348/348`, totaling `1367/1367` with zero skipped.
  The Release solution build completed with zero warnings and zero errors.
- Confirmed the Designer's `应用到 HUD` operation did install the user's current
  `柔光玫瑰` parameters and refresh the running HUD. The still-old formal visual
  was expected because the installed v1.2.0 executable retained the old
  renderer; no additional same-selection live-reload defect was found.
- Packaged production Setup, normal-HUD-only ZIP, and `SHA256SUMS.txt`. Setup
  SHA-256 is `E01E942A03B6F31BE66794997676A6C21DA73F54B838E11513226C888524F572`;
  ZIP SHA-256 is
  `36880B6D47AB731CC4B560D6E9CAA71BB7744C4081C6759189EFC0B721710F6F`.
  The ZIP contains exactly five approved entries; Setup contains the normal
  HUD plus the optional Designer. Setup and both executables remain unsigned.
- All seven normal isolated installer scenarios and both committed-cleanup
  failure scenarios passed. The real v1.2.0-to-v1.2.1 upgrade then completed
  with exit code `0`; installed App and Designer exactly match the publish
  tree and report
  `1.2.1+b1126e34f0a06a0e93458848ca347bce85d808bb`.
- Retained the selected custom skin, its payload, the current Designer draft
  and recovery document, and preview-window state. Formal startup remains
  `--background`, ordinary-user Start-menu shortcuts are correct, and the
  maintainer desktop was separately restored to only Developer Preview with
  `--preview`. The installed formal HUD refreshed successfully and the
  installed Designer opened and closed normally.
- Fast-forwarded GitHub `main` without a PR to evidence commit `509314e`.
  CI run `30888862526` passed restore, `1367/1367` tests, build, ephemeral
  packaging, and all nine isolated installer scenarios. Created and pushed
  annotated tag `v1.2.1` at that evidence commit.
- Published GitHub Release `v1.2.1` as Latest, non-draft, and non-prerelease.
  Uploaded Setup (100,052,875 bytes), ZIP (68,330,307 bytes), and
  `SHA256SUMS.txt` (196 bytes). GitHub's online sizes and SHA-256 digests match
  the local release assets exactly. The v1.2.0 tag and assets were not changed.

## 2026-08-04 — v1.2.0 released with optional Skin Designer

- Completed the custom-skin workflow follow-up: imported skins appear without
  restart, custom names select directly while deletion remains a separate
  action, HUD and Designer use the shared
  `Documents\Codex Quota HUD Skins` exchange directory, and initial Designer
  preview animations start immediately.
- Simplified animation authoring to `静止 / 柔和 / 明显` presets with a
  collapsed `高级细调` section. Decoration rotation/floating controls are
  disabled with an explanation until a transparent decoration exists. Center
  breathing now scales only the center image, while a dedicated quota-ring
  glow layer pulses independently and leaves text stationary.
- Rebuilt the Designer control strip from the user's installed screenshot:
  quota preset boxes are fixed-width beside their labels, sliders get the
  remaining width, and compact adjacent `预览状态` / `停靠预览` groups replace
  the screen-wide split. Light dropdown items use dark readable text. Added a
  distinct cyan-ring/rose-gold-brush Designer icon with seven Windows sizes.
- The first fresh App run caught a regression introduced by the new shared
  catalog synchronization helper: after a committed removal, a failed runtime
  fallback prevented the menu snapshot from publishing. Root cause was the
  helper's early return. It now always publishes the durable snapshot while
  still returning runtime synchronization failure to activation callers; the
  exact failure and all 22 skin-management tests pass.
- Added a compact `关于` window showing the running product version, author
  `老姚`, the exact GitHub repository link, and MIT license. HUD and tray menus
  share one coordinator, reopen the existing window instead of duplicating it,
  and dispose it with their owning composition.
- Fixed the Skin Designer regression where `None` hid the synthetic HUD but a
  later non-empty mode could not show it again. Preview-only work-area updates
  no longer re-arm the automatic-show suppression flag. Added the focused
  `WorkAreaUpdate_DoesNotPreventQuotaReturningAfterNoQuota` regression test.
- Fixed the formal-HUD startup regression introduced while repairing that
  Designer transition. Automatic-show suppression is now selected explicitly
  only by the synthetic Designer composition; normal windows never inherit it.
  Formal windows also synchronize quota that arrived before construction
  completed. Focused tests cover both quota-arrival orders and preserve the
  preview's intentionally hidden initial window.
- Fixed a second replacement-window lifecycle regression: after `打开草稿` or
  another successful document replacement, apply/export dialogs still used the
  closed original window as owner and apply could silently stop before overwrite
  confirmation. A shared loaded-window owner is now promoted by every active
  editor. The focused regression test verifies that the replacement editor is
  the output owner.
- Latest serial Release verification passed Core `75/75`, Skins `331/331`,
  App/UI `609/609`, and Designer `348/348`, total `1363/1363` with `0` skipped.
  Release build
  completed with `0` warnings and `0` errors.
- Fixed the installed-host About crash reported during real GUI acceptance.
  Windows event evidence traced it to the unembedded `Assets/AppIcon.ico` WPF
  resource. Added a constructor regression test that reproduced the installed-
  host failure, embedded the icon, and contained optional About-window failures
  so they are reported without terminating the HUD or Designer and can be
  retried. The corrected installed binaries identify commit `bf8e16d`.
- Built an earlier local v1.2.0 candidate from commit `c3d545e`. Setup SHA-256 was
  `869D197E530053313E9EB54F41FB239551BBF35177A381C9A69C0E88A0C0E576`;
  normal-only ZIP SHA-256 is
  `A99DB908F700E446A36BAD58655C79D583D658E4250A956ED2C01095239C6452`.
  Setup, App, and Designer remain `NotSigned`.
- Replaced the earlier candidates with the final public package from
  `7393ffc`. Setup
  SHA-256 is
  `A87631B96F21EF6C8E35B14F4ED64E411243D5DC071ABE680704C673225746DC`;
  normal-only ZIP SHA-256 is
  `044BD0B844AC7922E76B9EDD1C143152194B39A33CF231F159DDF3FB53E02D4D`.
  All seven isolated installer scenarios and both committed-cleanup failure
  scenarios passed before the later Designer-only window-owner fix and were
  not repeated for that fix. The final reinstall exited `0`; installed
  App/Designer report `1.2.0+7393ffc4983d03552314295fe74061781e0b1318` and match the
  packaged publish hashes. Startup remains formal `--background`; the
  maintainer desktop was restored to only the `--preview` shortcut.
- Installed GUI acceptance reopened the saved `柔光玫瑰` draft, displayed the
  existing-ID confirmation from the replacement editor, replaced the skin,
  displayed the success result, and started the HUD. Disk inspection confirmed
  `assets/decoration.png` plus rotation `0.7987`, breathing `0.9004`, and glow
  `0.8994`. Automated capture did not independently prove the formal HUD's
  visible rotation, so that last visual observation is not claimed as PASS.
- Real local upgrade installation completed with exit code `0`. Installed App
  and Designer hashes match the packaged publish tree, the uninstall entry is
  `1.2.0`, startup remains formal `--background`, ordinary-user shortcuts have
  no `--preview`, and Designer is Start-menu-only. The maintainer desktop was
  then separately restored to only the Developer Preview shortcut with
  `--preview`, without changing the public Setup contract.
- Installed GUI regression acceptance passed `None -> Dual`, `None -> 5h`, and
  `None -> Week`. The `关于` item was visible in the real HUD context menu. The
  pre-fix window crash was reproduced and corrected as described above; the
  corrected real window still needs one visual acceptance click, so that row
  remains `NOT RUN`.
- A post-reboot report showed current tray quota while the formal HUD remained
  hidden. Source history traced this to commit `07b73d5`, which accidentally
  made Designer-only automatic-show suppression the default for every HUD
  window. Red/green tests reproduced both quota-after-window and quota-before-
  window startup orders. A temporary opt-in trace then observed a real formal
  startup execute `Show()` with `IsVisible=True`, `Visibility=Visible`, and a
  nonzero WPF handle. The trace code and local trace files were removed. The
  current installed binary reports
  `1.2.0+7393ffc4983d03552314295fe74061781e0b1318`; the user directly
  confirmed that the formal HUD is visible after the final reinstall.
- Distinguished two historical `0.0.0` candidates: the earlier isolated Task
  18 package was never run; a later production-structure candidate was
  mistakenly installed, then replaced by v1.2.0. Its obsolete Setup/ZIP were
  removed while its log and rollback backup were retained.
- Fast-forwarded GitHub `main` from `7783286` to evidence commit `4a7c4c1`
  without a PR, created and pushed annotated tag `v1.2.0`, and published the
  non-draft, non-prerelease Latest GitHub Release. Uploaded
  `CodexQuotaHud-Setup-v1.2.0.exe` (100,049,805 bytes),
  `CodexQuotaHud-v1.2.0-win-x64.zip` (68,329,992 bytes), and
  `SHA256SUMS.txt` (196 bytes). GitHub's online SHA-256 digests for the two
  packages match the final hashes recorded above. Existing public `v1.1.1`
  assets were not replaced.
- The release was explicitly authorized after the user reviewed the documented
  verification scope. Remaining layout/DPI/image, component-removal,
  uninstall, sign-out/restart, formal-HUD rotation, and real About-window
  visual rows remain `NOT RUN`; the historical unreproduced storage anomaly is
  still recorded and was not reclassified as fixed.
- Changed main-branch CI packaging from the stale public version `1.1.1` to an
  ephemeral `0.0.0` candidate. The runner still builds and smoke-tests the full
  production package structure, but no longer gives current-source CI output a
  previously released version number.
- Fixed the Windows CI-only canonical JSON assertion: C# raw-string expected
  values inherited CRLF from checkout while the serializer correctly emitted
  canonical LF. The test now normalizes only its expected multiline strings to
  LF before byte comparison; production serialization is unchanged.

## 2026-08-03 — Unreleased optional Skin Designer automated handoff

- Added the unreleased shared `CodexQuotaHud.Skins` contracts, strict
  data-only `.cqskin` validation/packaging/storage/runtime path, normal-HUD
  import and rendering, and the separate `CodexQuotaHud.SkinDesigner` process
  with isolated drafts/recovery, synthetic preview, apply, and export flows.
  The unreleased Setup definition keeps Designer visible but unchecked by
  default, adds only a Start-menu entry when selected, preserves all user data
  on component removal, and keeps the fallback ZIP normal-HUD-only.
- Verified source commit `168bf8b2a58062f86c35b203eff6cf269b52bad9` after a
  forced canonical restore. Fresh per-project evidence was Core `75/75`, Skins
  `325/325`, App/UI `590/590`, and Designer `334/334`. Two trustworthy
  full-solution reruns passed `1324/1324` with `0` skipped; the final run was
  independently counted from four external TRX files. Release build completed
  with `0` warnings and `0` errors, and `git diff --check` passed.
- Preserved an unresolved earlier full-solution failure at the same commit:
  `SkinPackageInstallerTests.Remove_DeletesExactlyOneCanonicalCustomDirectoryAndPreservesSiblings`
  failed `Assert.True(result.IsValid)`, giving `1323` passed / `1` failed. Its
  original assertion did not expose `result.Errors`. Exact, class, Skins,
  eight-testhost parallel, cross-project, and 1000-operation stress evidence
  did not reproduce it, so no root cause or source fix is claimed. Two later
  no-test batches caused by a diagnostic restore polluting ignored Skins
  `obj` assets were explicitly excluded and the generated assets were repaired
  before the trustworthy TRX run.
- The three exact security/rollback filters passed Skins `223/223`, App
  `224/224`, and Designer `116/116`, all with `0` failed and `0` skipped.
- Built one real, internal-only `0.0.0` candidate without running Setup. Its
  ephemeral hashes were Setup
  `df89250dc2b68fa198196d48c6e2344efe4196c94c34f354728b2b66cc30cc8c`,
  normal-only ZIP
  `ca3ab3959633b241d2b4f709e2111a5e8e69cdfa64437f412df86933dea5d33d`,
  and checksum file
  `2db5e0053df13bedab36fec1e0b3de24b383c1b052df0775e20e6107f3a831b3`.
  The two checksum lines matched, the ZIP had exactly five approved entries
  and no Designer, and the Setup publish manifest had exactly the normal HUD
  plus `designer/CodexQuotaHud.SkinDesigner.exe`. Reproduced publish hashes
  matched the manifest; App, Designer, and Setup were all `NotSigned`.
- The first temporary manifest proxy failed before ISCC because it used a
  Windows PowerShell 5.1-incompatible API. That GUID root was checked clean and
  removed. A new GUID root with a compatible proxy produced the evidence above
  and was deleted with `ExistsAfter=False`.
- No GUI, internal/canonical Setup launch, install/uninstall, user-data change,
  sign-out, restart, version bump, tag, upload, public release, asset
  replacement, or push was performed. Every manual row remains
  `NOT RUN`; overall acceptance is `PARTIAL — no release is authorized`.

## 2026-08-01 — v1.1.1 ordinary-user installer correction

- Restored the approved release boundary: public Setup defaults to current-user
  startup plus the normal `Codex Quota HUD` desktop shortcut. The desktop and
  Start-menu shortcuts launch the real HUD without `--preview`.
- Removed Developer Preview from Setup tasks and icons. Upgrade explicitly
  removes the Developer Preview desktop shortcut created by `v1.1.0`; source
  and ZIP users retain explicit `CodexQuotaHud.App.exe --preview` access.
- Added project `AGENTS.md` rules separating maintainer-machine conveniences
  from public packaging. A later request that appears to reverse an approved
  product or release contract must be identified and reconfirmed before work.
- Updated the installer smoke contract to verify clean install, upgrade from
  the `v1.1.0` shortcut state, settings-preserving uninstall, and purge
  uninstall. The four isolated scenarios passed and cleaned up their state.
- Real candidate upgrade passed with exit code `0`: the normal desktop link had
  no arguments, the Developer Preview link was absent, startup remained
  `--background`, and two existing settings files retained their hashes.
- Fresh verification passed Core `55/55`, App/UI `333/333`, total `388/388`;
  Release build completed with `0` warnings and `0` errors.
- Published annotated tag `v1.1.1` at `6515e7c` and made its GitHub Release the
  latest ordinary-user release. Uploaded Setup, ZIP, and `SHA256SUMS.txt`; all
  online sizes and digests match the final local assets.
- Updated the retained `v1.1.0` Release with a prominent developer-oriented
  warning and a link-forward instruction to install `v1.1.1` or later.
- Installed the final published build locally. Its version is
  `1.1.1+6515e7c...` and binary hash matches the published payload. After Setup
  acceptance, restored the maintainer-only Developer Preview desktop shortcut
  separately while keeping formal startup and the normal Start-menu entry.

## 2026-08-01 — Public v1.1.0 release

- Finalized the bilingual README and release notes to match the shipped
  startup-plus-Developer-Preview shortcut behavior and `387/387` test baseline.
- Created and pushed annotated tag `v1.1.0` at `205e7e5`, then published the
  non-draft, non-prerelease GitHub Release as latest.
- Uploaded `CodexQuotaHud-Setup-v1.1.0.exe`,
  `CodexQuotaHud-v1.1.0-win-x64.zip`, and `SHA256SUMS.txt`. GitHub-reported
  sizes and SHA-256 digests match the local final assets.
- Installed the published Setup locally with exit code `0`. The installed
  executable reports `1.1.0+205e7e5...`, matches the published binary hash,
  retains formal `--background` startup, and keeps the desktop shortcut on
  `--preview`.
- Made the GitHub Actions Test step explicitly use Windows PowerShell, matching
  the installer scripts and later workflow steps. This prevents PowerShell 7's
  module path from being inherited by nested Windows PowerShell test processes
  and hiding the built-in `Get-FileHash` command.
- Made shortcut inspection locale-neutral in installer smoke tests. It still
  verifies the exact executable path and `--preview`; when WScript cannot read
  a Unicode `.lnk` filename on an English runner, it copies the same shortcut
  to a temporary ASCII-only filename and asks WScript to parse that copy. The
  temporary link is removed with checked cleanup. Added a regression test;
  the current source baseline is Core `55/55`, App/UI `333/333`, total
  `388/388`.
- Put the STA-based WPF UI test classes in one non-parallel xUnit collection.
  This removes a runner-timing race between concurrently constructed WPF
  controls that could make an otherwise passing skin test fail intermittently.

## 2026-07-31 — Final installer shortcut and uninstall acceptance

- Changed the release Setup default to current-user startup plus a Developer
  Preview desktop shortcut. The normal HUD no longer gets a desktop shortcut;
  upgrades explicitly remove the legacy managed normal desktop link.
- Moved the purge-settings choice to a usable pre-uninstall form and delayed
  process shutdown until uninstall is confirmed. Added final removal of exact
  legacy install residue after Inno cleanup.
- Verification passed Core `55/55`, App/UI `332/332`, total `387/387`, a
  zero-warning/zero-error Release build, and all four isolated installer smoke
  scenarios. Real default-preserve and explicit-purge uninstall acceptance also
  passed.
- Installed the final Setup from commit `1f15100` with exit code `0`. The real
  machine now has formal `--background` startup, one desktop Developer Preview
  shortcut with `--preview`, no normal desktop shortcut, and unchanged settings
  hashes. Final asset hashes are recorded in `PROJECT_CONTEXT.md`.

## 2026-07-31 — Release Setup preview isolation and lifecycle fix

- Removed the Developer Preview shortcut option from the production Setup;
  development preview remains explicitly available to ZIP/source users through
  `CodexQuotaHud.App.exe --preview`.
- Changed Inno lifecycle execution from redirected 32-bit PowerShell to native
  64-bit PowerShell. This prevents install/uninstall from exiting with code 1
  while inspecting the exact path of the 64-bit HUD process.
- Corrected candidate verification passed Core `55/55`, App/UI `331/331`,
  total `386/386`, Release build with zero warnings/errors, and all four
  isolated installer smoke scenarios. Corrected Setup SHA-256 is
  `628ba0cb457b93e1cd063fe3f954f09b9d1ab5747e0a0cb9f6e5fdae1185514a`.
- Real corrected-Setup overwrite passed: both formal tasks remained selected,
  settings were preserved, and no Developer Preview shortcut was created.

## 2026-07-31 — v1.1.0 installer release-candidate verification

- Made Setup the documented primary path; ZIP/PowerShell is fallback and
  GitHub Packages is unused. The documentation covers automatic
  English/Simplified-Chinese selection, current-user/no-admin install,
  startup/normal-shortcut defaults, ZIP-only explicit `--preview` launch, direct
  upgrade, settings preservation, opt-in purge, unsigned SmartScreen notice,
  and SHA-256 verification.
- At `04049208e13c579307a421fc96318b82cb6b5da8`, fresh Release verification
  passed Core 55/55, App/UI 328/328, total 383/383; Release build was 0
  warnings and 0 errors.
- Packaged local candidate assets: Setup 50,880,280 bytes / SHA-256
  `65d7dea7992f1c3ca21cfc75b19b7c7148bd8f25e6fd03f439ad07259705c5aa`; ZIP
  68,202,715 bytes / SHA-256
  `572b9d741098ceb055c5601fc7d149547d74e5361f21bc70f48501e673418019`.
- The four isolated smoke scenarios passed using a temporary smoke-only Setup:
  clean install, upgrade/task replacement, default preserve uninstall, and
  explicit purge uninstall. Production assets do not contain internal smoke
  hooks, and the temporary smoke build was not released.
- No real Setup has been launched. The installed v1.0.0, its Apps & Features
  registration, shortcuts, startup, settings, and uninstall behavior remain
  unaccepted pending explicit GUI/install authorization. No release/tag/upload
  claim is made.

## 2026-07-31 — Low-quota alert colors

- Added a shared alert policy across all five floating-HUD skins, collapsed
  edge bars, tray percentage icon, and detail rows. Above `20%` retains the
  normal skin color; `>10%..20%` is Warning amber `#FFFFB547`; `<=10%` is
  Critical red `#FFFF5A67`.
- Dual quotas are classified and colored independently. The change adds no
  flashing, popup, sound, setting, or refresh behavior. Developer Preview
  sliders remain the manual boundary and mixed-state inspection tool.
- Automated Release verification passed focused alert tests 66/66, Core 55/55,
  App/UI 266/266, total 321/321, and a zero-warning, zero-error Release build.
  GUI/manual preview acceptance was not authorized or performed.
- Installed `v1.0.0`, deployment, release assets, and remote/push state were
  not changed.

## 2026-07-30 — Symmetric installed/preview handoff

- Launching the desktop `Codex Quota HUD 开发预览` shortcut now replaces the
  installed HUD before preview creates its HUD or control window.
- A listener-enabled installed build receives the shutdown request and exits
  through normal application cleanup. Older installed builds use the fallback
  only when the running executable is exactly
  `%LOCALAPPDATA%\Programs\CodexQuotaHud\CodexQuotaHud.App.exe`; a same-name
  process at another path is never force-closed.
- If installed-mode replacement fails, the app displays a failure message and
  does not open preview. The reverse handoff remains
  `退出预览并打开正式版`, which cleans up preview before starting the exact
  installed executable.
- Verified by automated tests: Core 55/55, App/UI 231/231, total 286/286, plus
  a Release build with zero warnings and zero errors.
- The installed `v1.0.0` package and release assets were not upgraded,
  deployed, or changed. No GUI process was launched for this task, so both
  manual handoff directions remain unchecked. The current `v1.0.0` can later
  verify the legacy exact-path fallback; graceful signalling remains unverified
  until a listener-enabled build is installed.

## 2026-07-30 — Preview control window geometry

- Increased the preview control default size to `380 × 650` so standard-DPI
  startup shows the full current control set without an initial scrollbar.
- Added isolated geometry persistence at
  `%LOCALAPPDATA%\CodexQuotaHud\preview-window.json`.
- Restores and clamps valid size/position while retaining scrolling for small
  screens and high DPI.
- Does not persist simulated quota, skin, animation, or normal HUD settings.
- Verified Core 55/55, App/UI 212/212, total 267/267, and a Release build with
  zero warnings and zero errors.

## 2026-07-30 — Preview handoff to installed HUD

- Added `退出预览并打开正式版` to the developer preview control window.
- Resolves the installed HUD only from the current-user Local App Data
  installation path and disables the button when that executable is missing.
- Defers process start until preview windows, tray resources, and the
  single-instance guard have been cleaned up.
- Contains missing-file and launch failures without interrupting shutdown.
- Verified Core 55/55, App/UI 205/205, total 260/260, and a Release build with
  zero warnings and zero errors.
- End-to-end visual handoff remains a manual check; the installed `v1.0.0`
  files and release assets were not modified.

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

