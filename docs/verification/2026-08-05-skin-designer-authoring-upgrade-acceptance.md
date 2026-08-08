# Skin Designer Authoring Upgrade Acceptance — v1.3.0 Source

## Scope and decision rule

This record covers Task 8 source verification and source-Designer acceptance
for the v1.3.0 authoring upgrade. Evidence states are `PASS`, `FAIL`,
`PARTIAL`, and `NOT RUN`. A row is `PASS` only when the exact automated or
manual behavior was directly observed. Automated coverage does not promote an
unobserved visual row.

The source under test is the canonical worktree
`C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731`,
branch `feat/inno-setup-installer-20260731`, starting at commit `39f44b2`
(`fix: distinguish cancelled cleanup warnings`). No Setup, install, package,
tag, push, or release action is in this task.

**Current decision: PARTIAL — source and compatibility gates PASS; direct
source-Designer GUI rows are blocked by the Windows automation runtime.** The
source window launched, but no input was sent after ownership validation
failed. The same installed-identity path is intentionally deferred to Task 9,
where the installed v1.3.0 Designer is the planned acceptance target.

## Automated source gates

| Status | Date/time (Asia/Tokyo) | Exact command | Expected | Observed | Evidence |
|---|---|---|---|---|---|
| PASS | 2026-08-08 15:54:07–15:54:11 +09:00 | `dotnet test .\tests\CodexQuotaHud.Core.Tests\CodexQuotaHud.Core.Tests.csproj -c Release --no-restore` | All Core tests pass; zero skipped | `75/75` passed, failed `0`, skipped `0`; duration `901 ms`; exit `0` | Final fresh VSTest summary |
| PASS | 2026-08-08 15:54:16–15:54:25 +09:00 | `dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore` | All Skins tests pass; zero skipped | `375/375` passed, failed `0`, skipped `0`; duration `5 s`; exit `0` | Final fresh VSTest summary |
| PASS | 2026-08-08 15:54:30–15:56:29 +09:00 | `dotnet test .\tests\CodexQuotaHud.App.Tests\CodexQuotaHud.App.Tests.csproj -c Release --no-restore` | All App/UI tests pass; zero skipped | `625/625` passed, failed `0`, skipped `0`; duration `1 m 54 s`; exit `0` | Final fresh VSTest summary |
| PASS | 2026-08-08 15:56:35–15:56:51 +09:00 | `dotnet test .\tests\CodexQuotaHud.SkinDesigner.Tests\CodexQuotaHud.SkinDesigner.Tests.csproj -c Release --no-restore` | All Designer tests pass; zero skipped | `424/424` passed, failed `0`, skipped `0`; duration `11 s`; exit `0` | Final fresh VSTest summary |
| PASS | 2026-08-08 15:54:07–15:56:51 +09:00 | Four serial project commands above | Exact fresh total, no failed or skipped tests | Core `75` + Skins `375` + App/UI `625` + Designer `424` = `1499/1499`; failed `0`, skipped `0` | Four final fresh VSTest summaries; no historical v1.2.3 counts reused |
| PASS | 2026-08-08 15:56:57–15:57:00 +09:00 | `dotnet build .\CodexQuotaHud.sln -c Release --no-restore` | Release solution builds with zero warnings/errors | Build succeeded; warnings `0`, errors `0`; duration `2.18 s`; exit `0` | Final fresh build stdout |
| PASS | 2026-08-08 15:57:00 +09:00 | `git diff --check` | No whitespace errors | No output; exit `0` | Final fresh command result |
| PASS | 2026-08-08 15:36:48–15:36:54 +09:00 | `dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ParseTheme_LegacyJsonDefaultsNewTextLayoutFieldsToZero\|FullyQualifiedName~Writers_UseCanonicalUtf8AndRoundTripByteForByte\|FullyQualifiedName~Validate_RejectsMinimumHudVersionAboveInstalledVersion"` | Legacy text layout defaults to `0/0`; canonical output writes each property once; newer-HUD manifests are rejected | `3/3` passed, failed `0`, skipped `0`; exit `0` | Fresh focused VSTest summary |

## Compatibility and package evidence

| Status | Date/time (Asia/Tokyo) | Exact command or action | Expected | Observed | Evidence |
|---|---|---|---|---|---|
| PASS | 2026-08-08 15:35 +09:00 | PowerShell `Get-FileHash` plus `[System.IO.Compression.ZipFile]::OpenRead(...)` inspection of `C:\Users\yaozi\Documents\Codex Quota HUD Skins\柔光玫瑰.cqskin` | Identify an untouched v1.2.3 package and verify its archive hashes before import | SHA-256 `cbcf4caff3238e9f4ee4ce247fb6b8b39652d6d2d7f444912853788a6684279f`; manifest minimum HUD `1.2.3`; `textOffsetY` and `textLineGap` both absent; all three declared asset hashes match archive bytes | Direct read-only archive and hash inspection; five archive entries |
| PASS | 2026-08-08 15:35 +09:00 | PowerShell `Get-FileHash` plus `[System.IO.Compression.ZipFile]::OpenRead(...)` inspection of `C:\Users\yaozi\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin` | Preserve a second untouched v1.2.3 reference package | SHA-256 `05c0228bb6b83b0becd6e4b6556e6990dcc329be3b8a176d3a20a3fe45572c56`; manifest minimum HUD `1.2.3`; both new properties absent; all three declared asset hashes match archive bytes | Direct read-only archive and hash inspection; five archive entries |
| PASS | 2026-08-08 15:50:30–15:50:34 +09:00 | `dotnet run --project .\artifacts\acceptance-task8-harness\Task8CompatibilityHarness.csproj -c Release -- "C:\Users\yaozi\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin" "C:\Users\yaozi\Documents\CodexQuotaHud-Task8-IsolatedState-20260808" "C:\Users\yaozi\Documents\CodexQuotaHud-Task8-IsolatedOutput2-20260808\Task8-Compatibility-Copy.cqskin"` | Import an untouched v1.2.3 package through the production Designer document service without touching user state; missing layout fields become `0/0` | Isolated import succeeded as owned draft `88888888-8888-4888-8888-888888888888`; effective layout was exactly `0/0`; template minimum normalized from package `1.2.3` to draft `1.3.0`; exit `0` | Temporary harness referenced the canonical source projects and used production `SkinPackageReader` / `DesignerDocumentService`; isolated state root was removed afterward |
| PASS | 2026-08-08 15:50:30–15:50:34 +09:00 | Same isolated harness: set `TextOffsetY=7`, `TextLineGap=6`, package version/minimum HUD `1.3.0`, then build with `DraftPackageBuilder` and export with `SkinPackageWriter` | Non-zero output declares minimum HUD `1.3.0`; each new property occurs exactly once; JSON and hashes are canonical | Package SHA-256 `9931cebe6d9ff2994e6402f5bd48c016b41dbef0651188cdd916bd1e2e94d35e`; skin ID `75c7b76e-7b3a-4e51-83db-c404555a7a7e`; package/minimum HUD `1.3.0`; values `7/6`; property counts `1/1`; manifest/theme byte-equal canonical writers; all three declared asset hashes matched | Direct output archive readback before the isolated output root was removed |
| PASS | 2026-08-08 15:50:30–15:50:34 +09:00 | Validate the exported package with production `SkinPackageReader` configured as HUD `1.2.3` in the same isolated process | Reject before the package can be represented as compatible | Validation returned no value and `version.incompatible` at `$.minimumHudVersion`: `The skin requires a newer HUD version.` | Exact shared HUD import/compatibility path; maintainer installation and user state were not touched |

## Source Designer manual acceptance

The source Designer command is:

```powershell
dotnet run --project .\src\CodexQuotaHud.SkinDesigner\CodexQuotaHud.SkinDesigner.csproj -c Release --no-build
```

The visible source process was launched at 2026-08-08 15:40:47 +09:00 from
the canonical worktree. The runtime later blocked all input because it could
not establish the source window's ownership. No custom draft—original or
copy—was opened in the GUI.

| Status | Date/time (Asia/Tokyo) | Command or action | Expected | Observed | Evidence |
|---|---|---|---|---|---|
| PASS | 2026-08-08 15:40:47 +09:00 | Exact source command above | Already-built source Designer opens from the canonical worktree | Source PID `12404`, main-window handle `6029600`, title `Codex Quota HUD 皮肤设计器`, responding | Process path and main-window readback; process later closed without UI input |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Text offset control on copied draft | Both text lines move together | No input was sent: Window2 enumerated the source handle but attributed it to the installed Designer identity, so ownership validation blocked the action | Runtime ownership diagnostics; no visual fact inferred from automated tests |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Positive and negative line-gap control | Lines expand/contract equally without changing their midpoint | Blocked before input for the same ownership reason | Runtime ownership diagnostics |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Undo, redo, save, and reopen | Both non-zero values restore exactly | Blocked before input; no GUI draft copy was created | Runtime ownership diagnostics |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Set text layout to `0/0` | Appearance matches the v1.2.3 baseline | Blocked before input; numerical/compatibility tests are not visual evidence | Runtime ownership diagnostics |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Enable `构图参考线` | Guides align with outer/inner rings, peak center container, and both text lines; overlay remains click-through | Blocked before input | Runtime ownership diagnostics |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Switch audition selector through `全部`, `中心呼吸`, `光晕脉冲`, `环形运动`, `进度明暗`, and `刷新加速` | Every mode is visibly distinct; returning to `全部` restores the saved animation mix | Blocked before input; no mode was visually observed | Runtime ownership diagnostics |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Enter and leave `刷新加速` | Former `刷新中` checkbox state is restored | Blocked before input | Runtime ownership diagnostics |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Switch Dual / 5h / Week / None | Hints match displayed rings; Dual outer = 5h and inner = Week | Blocked before input | Runtime ownership diagnostics |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Open export picker | Native picker starts in `C:\Users\yaozi\Documents\Codex Quota HUD Skins` with a leaf `.cqskin` filename | Blocked before input; production option-builder coverage passed in the full Designer suite, but no native picker was observed | Runtime ownership diagnostics |
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Apply copied skin to HUD | Result names exact display name, version, and skin ID and truthfully reports live activation disposition | Blocked before input; installed HUD was never contacted by the source Designer | Runtime ownership diagnostics and unchanged process/state evidence |

## State preservation and exclusions

| Status | Date/time (Asia/Tokyo) | Command or action | Expected | Observed | Evidence |
|---|---|---|---|---|---|
| PASS | 2026-08-08 15:37:35 +09:00 | Copy current `%LOCALAPPDATA%\CodexQuotaHud` and `Documents\Codex Quota HUD Skins` into ignored `artifacts\acceptance-task8-state-backup-20260808` | Preserve exact pre-run Designer/HUD/exchange state before GUI work | Backed up `34` product-state files and `2` exchange packages, `24,064,751` bytes total. Pre-run selected skin is `custom:75c7b76e-7b3a-4e51-83db-c404555a7a7e`; animations enabled; installed formal HUD PID `9284` was running | Bounded filesystem copy and direct settings/process readback |
| PASS | 2026-08-08 15:38:37 +09:00 | Compare Designer-state file list and SHA-256 after an observation-runtime failure and source-process shutdown | Failed automation attempt leaves Designer data unchanged | Designer file count remained `22`; `Compare-Object` returned no path/hash differences; installed formal HUD remained the only product process | Fresh recursive hash comparison and process inventory |
| PASS | 2026-08-08 15:51:37–15:52:05 +09:00 | Close source process, compare recursive SHA-256/path inventories to the bounded backup, then remove the backup | Original HUD/Designer/exchange state remains intact; isolated draft/output is removed; formal HUD retains the prior selected skin | Designer `22/22`, installed skins `10/10`, imports `0/0`, and exchange packages `2/2` had zero path/hash differences. `preview-window.json` hash matched. HUD left/top, animations, and selected skin `custom:75c7b76e-7b3a-4e51-83db-c404555a7a7e` matched. Only `LastSuccessfulRefresh` advanced normally while the unchanged installed HUD PID `9284` kept running. All isolated harness state/output roots and the backup were then removed with checked absent postconditions | Recursive `Get-FileHash` comparisons, semantic settings comparison, process inventory, and explicit cleanup postconditions |
| PASS | 2026-08-08 +09:00 | Inspect tracked worktree and task boundaries | `tmp/` remains unread, untouched, unstaged, and uncommitted; no package/install/release action | No `tmp/` content was read or touched. No packaging, install, push, tag, or release action was performed | Commands used explicit project/source/test/docs paths; tracked status was clean before evidence creation |

## Known limitations

- Two distinct Computer Use runtime failures prevented source GUI acceptance.
  This agent's native `sky.list_windows()` and `sky.list_apps()` both returned
  `EnumWindows failed: 系统找不到指定的文件。 (0x80070002)`. The controller's
  healthy Window2 channel then enumerated the source handle but misattributed
  it to the installed Designer, so ownership validation correctly refused
  input. Its explicit-path launch alternative returned
  `node_repl exec context not found` even after a kernel reset. No ownership
  check was bypassed and no PowerShell UIA or legacy helper was used.
- No screenshot is committed because no material layout fact was directly
  observed. A launch-only screenshot would not prove any acceptance row.
- Task 9 installed v1.3.0 smoke remains the planned place to exercise these
  exact GUI rows under the correctly installed application identity.
