# Skin Designer Authoring Upgrade Acceptance — v1.3.0 Source

## Scope and decision rule

This record covers Task 8 source verification and source-Designer acceptance
for the v1.3.0 authoring upgrade. Evidence states are `PASS`, `FAIL`,
`PARTIAL`, and `NOT RUN`. A row is `PASS` only when the exact automated or
manual behavior was directly observed. Automated coverage does not promote an
unobserved visual row.

The source under test is the canonical worktree
`C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731`,
branch `feat/inno-setup-installer-20260731`, at source commit
`39f44b265deaf8bb9973152b5aa018e987b3e676`
(`fix: distinguish cancelled cleanup warnings`). The initial acceptance
evidence was recorded separately by commit
`a42863ec73eb1e3d5cdce7b4b56d55373ff7d9d9`
(`test: record designer authoring upgrade acceptance`). No Setup, install,
package, tag, push, or release action is in this task.

**Current decision: PARTIAL — this is neither full manual acceptance nor a
release-ready result.** Source and compatibility gates PASS, but all ten
direct source-Designer GUI rows are `NOT RUN` because the Windows automation
runtime blocked ownership validation. The source window launched, but no
input was sent. Task 9 must execute and record each of the ten GUI rows
individually against the installed v1.3.0 Designer before any full-manual-
acceptance or release-ready claim.

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
| PASS | 2026-08-08 16:12:44.319–16:12:50.525 +09:00 | `dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ParseTheme_LegacyJsonDefaultsNewTextLayoutFieldsToZero\|FullyQualifiedName~Writers_UseCanonicalUtf8AndRoundTripByteForByte\|FullyQualifiedName~Validate_RejectsMinimumHudVersionAboveInstalledVersion"` | Legacy text layout defaults to `0/0`; canonical output writes each property once; newer-HUD manifests are rejected | `3/3` passed, failed `0`, skipped `0`; duration `20 ms`; exit `0` | Review-fix fresh focused VSTest summary |
| PASS | 2026-08-08 16:16:43.713–16:16:46.367 +09:00 | `dotnet build .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-restore` | Durable probe compiles against production project references | Build succeeded; warnings `0`, errors `0`; duration `2.39 s`; exit `0` | Review-fix fresh build stdout |

## Compatibility and package evidence

| Status | Date/time (Asia/Tokyo) | Exact command or action | Expected | Observed | Evidence |
|---|---|---|---|---|---|
| PASS | 2026-08-08 16:12:12.353–16:12:14.351 +09:00 | Probe command P1 below | Import untouched `柔光玫瑰.cqskin` through production services in explicit isolated state; legacy fields default to `0/0`; export canonical `1.3.0` `7/6`; reject under HUD `1.2.3` | Exit `0`; legacy SHA-256 `cbcf4caff3238e9f4ee4ce247fb6b8b39652d6d2d7f444912853788a6684279f`, property counts `0/0`, effective `0/0`; output SHA-256 `1abebad400fb5b60e0c667d14b0e3bc8ec67ca0e0ae955d2cdb5219b3b17b7d8`, version/minimum `1.3.0`, values/counts `7/6` and `1/1`, canonical `true/true`, three asset hashes matched; HUD `1.2.3` returned `version.incompatible` at `$.minimumHudVersion` | Durable probe v1, raw summary P1 below |
| PASS | 2026-08-08 16:12:23.389–16:12:25.084 +09:00 | Probe command P2 below | Repeat the same production-path checks with untouched `雷光伙伴.cqskin` | Exit `0`; legacy SHA-256 `05c0228bb6b83b0becd6e4b6556e6990dcc329be3b8a176d3a20a3fe45572c56`, property counts/effective values `0/0`; output SHA-256 `9931cebe6d9ff2994e6402f5bd48c016b41dbef0651188cdd916bd1e2e94d35e`, version/minimum `1.3.0`, values/counts `7/6` and `1/1`, canonical `true/true`, three asset hashes matched; HUD `1.2.3` returned `version.incompatible` at `$.minimumHudVersion` | Durable probe v1, raw summary P2 below |

The replayable probe is tracked at
`tools/CodexQuotaHud.SkinDesignerCompatibilityProbe`. It references the
production Designer project, reads only the explicit `--legacy-package`,
requires absent explicit state/output targets, emits JSON, and leaves cleanup
of only those explicit targets to the caller.

Probe command P1 (literal command executed):

```powershell
$legacy = 'C:\Users\yaozi\Documents\Codex Quota HUD Skins\柔光玫瑰.cqskin'
$state = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-Rose-State-20260808-r1'
$output = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-Rose-Output-20260808-r1\Task8-Compatibility-Copy.cqskin'
if (-not (Test-Path -LiteralPath $legacy -PathType Leaf)) { throw "Missing legacy package: $legacy" }
if (Test-Path -LiteralPath $state) { throw "State target already exists: $state" }
if (Test-Path -LiteralPath (Split-Path -Parent $output)) { throw "Output root already exists: $(Split-Path -Parent $output)" }
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
dotnet run --project .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-build -- --legacy-package $legacy --isolated-local-app-data $state --output-package $output
if ($LASTEXITCODE -ne 0) { throw "Probe exit code: $LASTEXITCODE" }
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
```

Raw summary P1 (exact JSON stdout, with JSON's Unicode escaping preserved):

```json
{
  "ProbeVersion": 1,
  "StartedAtUtc": "2026-08-08T07:12:13.1069233+00:00",
  "CompletedAtUtc": "2026-08-08T07:12:14.2810247+00:00",
  "LegacyPackagePath": "C:\\Users\\yaozi\\Documents\\Codex Quota HUD Skins\\\u67D4\u5149\u73AB\u7470.cqskin",
  "LegacyPackageSha256": "cbcf4caff3238e9f4ee4ce247fb6b8b39652d6d2d7f444912853788a6684279f",
  "LegacyDeclaredMinimumHudVersion": "1.2.3",
  "LegacyTextOffsetPropertyCount": 0,
  "LegacyTextLineGapPropertyCount": 0,
  "LegacyEffectiveTextOffsetY": 0,
  "LegacyEffectiveTextLineGap": 0,
  "ImportedDraftId": "88888888-8888-4888-8888-888888888888",
  "ImportedMinimumHudVersion": "1.3.0",
  "OutputPackagePath": "C:\\Users\\yaozi\\Documents\\CodexQuotaHud-Task8-Probe-Rose-Output-20260808-r1\\Task8-Compatibility-Copy.cqskin",
  "OutputPackageSha256": "1abebad400fb5b60e0c667d14b0e3bc8ec67ca0e0ae955d2cdb5219b3b17b7d8",
  "OutputSkinId": "08b02426-c826-4202-afb0-09d55e66af2e",
  "OutputPackageVersion": "1.3.0",
  "OutputMinimumHudVersion": "1.3.0",
  "OutputTextOffsetY": 7,
  "OutputTextLineGap": 6,
  "TextOffsetPropertyCount": 1,
  "TextLineGapPropertyCount": 1,
  "ManifestCanonical": true,
  "ThemeCanonical": true,
  "AssetHashes": [
    { "Path": "assets/background.jpg", "DeclaredSha256": "ada9d14333d0b8a08ce59d64bed1ffc33e6503ed3b141ab4dc9d1721c47af192", "ActualSha256": "ada9d14333d0b8a08ce59d64bed1ffc33e6503ed3b141ab4dc9d1721c47af192", "Matches": true },
    { "Path": "assets/center.jpg", "DeclaredSha256": "ada9d14333d0b8a08ce59d64bed1ffc33e6503ed3b141ab4dc9d1721c47af192", "ActualSha256": "ada9d14333d0b8a08ce59d64bed1ffc33e6503ed3b141ab4dc9d1721c47af192", "Matches": true },
    { "Path": "assets/decoration.png", "DeclaredSha256": "9dae04de8c4ad70c8f3cca5876b33313a971f0ffb4963ba5d659e74d1625418e", "ActualSha256": "9dae04de8c4ad70c8f3cca5876b33313a971f0ffb4963ba5d659e74d1625418e", "Matches": true }
  ],
  "OldHudVersion": "1.2.3",
  "OldHudValid": false,
  "OldHudErrors": [
    { "Code": "version.incompatible", "Location": "$.minimumHudVersion", "Message": "The skin requires a newer HUD version." }
  ]
}
```

Probe command P2 (literal command executed):

```powershell
$legacy = 'C:\Users\yaozi\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin'
$state = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-Thunder-State-20260808-r1'
$output = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-Thunder-Output-20260808-r1\Task8-Compatibility-Copy.cqskin'
if (-not (Test-Path -LiteralPath $legacy -PathType Leaf)) { throw "Missing legacy package: $legacy" }
if (Test-Path -LiteralPath $state) { throw "State target already exists: $state" }
if (Test-Path -LiteralPath (Split-Path -Parent $output)) { throw "Output root already exists: $(Split-Path -Parent $output)" }
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
dotnet run --project .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-build -- --legacy-package $legacy --isolated-local-app-data $state --output-package $output
if ($LASTEXITCODE -ne 0) { throw "Probe exit code: $LASTEXITCODE" }
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
```

Raw summary P2 (exact JSON stdout, with JSON's Unicode escaping preserved):

```json
{
  "ProbeVersion": 1,
  "StartedAtUtc": "2026-08-08T07:12:24.0082161+00:00",
  "CompletedAtUtc": "2026-08-08T07:12:25.0122442+00:00",
  "LegacyPackagePath": "C:\\Users\\yaozi\\Documents\\Codex Quota HUD Skins\\\u96F7\u5149\u4F19\u4F34.cqskin",
  "LegacyPackageSha256": "05c0228bb6b83b0becd6e4b6556e6990dcc329be3b8a176d3a20a3fe45572c56",
  "LegacyDeclaredMinimumHudVersion": "1.2.3",
  "LegacyTextOffsetPropertyCount": 0,
  "LegacyTextLineGapPropertyCount": 0,
  "LegacyEffectiveTextOffsetY": 0,
  "LegacyEffectiveTextLineGap": 0,
  "ImportedDraftId": "88888888-8888-4888-8888-888888888888",
  "ImportedMinimumHudVersion": "1.3.0",
  "OutputPackagePath": "C:\\Users\\yaozi\\Documents\\CodexQuotaHud-Task8-Probe-Thunder-Output-20260808-r1\\Task8-Compatibility-Copy.cqskin",
  "OutputPackageSha256": "9931cebe6d9ff2994e6402f5bd48c016b41dbef0651188cdd916bd1e2e94d35e",
  "OutputSkinId": "75c7b76e-7b3a-4e51-83db-c404555a7a7e",
  "OutputPackageVersion": "1.3.0",
  "OutputMinimumHudVersion": "1.3.0",
  "OutputTextOffsetY": 7,
  "OutputTextLineGap": 6,
  "TextOffsetPropertyCount": 1,
  "TextLineGapPropertyCount": 1,
  "ManifestCanonical": true,
  "ThemeCanonical": true,
  "AssetHashes": [
    { "Path": "assets/background.png", "DeclaredSha256": "8d7f4532fb5eedd38ad7c0302bffc3071703d580662d73b1452bc70029de614a", "ActualSha256": "8d7f4532fb5eedd38ad7c0302bffc3071703d580662d73b1452bc70029de614a", "Matches": true },
    { "Path": "assets/center.png", "DeclaredSha256": "9cdfb970ec479d6aeed081d2f03d6447ebed3ce4e34e79f1d383add86c452827", "ActualSha256": "9cdfb970ec479d6aeed081d2f03d6447ebed3ce4e34e79f1d383add86c452827", "Matches": true },
    { "Path": "assets/decoration.png", "DeclaredSha256": "7982a24b411c7db8dc4c77b3a37a6b6ef538b6c709bcd0e2f280eaa20397c53b", "ActualSha256": "7982a24b411c7db8dc4c77b3a37a6b6ef538b6c709bcd0e2f280eaa20397c53b", "Matches": true }
  ],
  "OldHudVersion": "1.2.3",
  "OldHudValid": false,
  "OldHudErrors": [
    { "Code": "version.incompatible", "Location": "$.minimumHudVersion", "Message": "The skin requires a newer HUD version." }
  ]
}
```

After the summaries were captured, the caller removed only the four explicit
`CodexQuotaHud-Task8-Probe-*` isolated roots after checking their resolved
paths and rejecting reparse points. At `2026-08-08 16:12:38.102 +09:00`, all
four postconditions were `ABSENT=True`.

## Source Designer manual acceptance

The source Designer command is:

```powershell
dotnet run --project .\src\CodexQuotaHud.SkinDesigner\CodexQuotaHud.SkinDesigner.csproj -c Release --no-build
```

The visible source process was launched at 2026-08-08 15:40:47 +09:00 from
the canonical worktree. The runtime later blocked all input because it could
not establish the source window's ownership. No custom draft—original or
copy—was opened in the GUI. The `15:38:37 +09:00` shutdown in state evidence
S2 was an earlier diagnostic attempt; it occurred before, and is not the
shutdown of, this documented `15:40:47 +09:00` source launch.

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
| PASS | 2026-08-08 15:37:35 +09:00 | State command S1 below | Preserve exact pre-run Designer/HUD/exchange state before GUI work | Backed up `34` product-state files and `2` exchange packages, `24,064,751` bytes total. Pre-run selected skin was `custom:75c7b76e-7b3a-4e51-83db-c404555a7a7e`; animations enabled; installed formal HUD PID `9284` was running | Literal bounded backup/readback command S1 and its stdout summary |
| PASS | 2026-08-08 15:38:37 +09:00 | State command S2 below | Earlier failed diagnostic attempt leaves Designer data unchanged | Designer file count remained `22`; path/hash differences `0`; installed formal HUD remained the only product process | Literal recursive inventory command S2 and raw summary; this shutdown preceded the documented 15:40:47 launch |
| PASS | 2026-08-08 15:51:37–15:51:52 +09:00 | State command S3 below | Original HUD/Designer/exchange state remains intact after the documented source process closes | Designer `22/22`, installed skins `10/10`, imports `0/0`, and exchange packages `2/2` had zero path/hash differences. `preview-window.json` hash matched. HUD left/top `-450/0`, animations `true`, and selected skin `custom:75c7b76e-7b3a-4e51-83db-c404555a7a7e` matched. Only `LastSuccessfulRefresh` advanced from `2026-08-08T06:36:49.0269403+00:00` to `2026-08-08T06:51:04.0367408+00:00`; installed HUD PID `9284` kept running | Literal inventories/settings command S3 and raw summary |
| PASS | 2026-08-08 15:52:05 +09:00 | State command S4 below | Remove only bounded acceptance roots after verification | Backup contained `36` files / `24,064,751` bytes before removal; backup and explicit isolated harness roots were absent afterward | Literal guarded cleanup command S4 and raw postconditions |
| PASS | 2026-08-08 16:17:03.711–16:17:03.821 +09:00 | Boundary command S5 below | Tracked changes remain confined to acceptance evidence/probe; no whitespace errors | Exactly the acceptance document and two probe files were reported; diff check produced no output, exit `0` | Literal read-only boundary command S5 |

State command S1 (literal command executed; stdout summary recorded in the
first row):

```powershell
$stateRoot = Join-Path $env:LOCALAPPDATA 'CodexQuotaHud'
$exchangeRoot = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Codex Quota HUD Skins'
$backupRoot = Join-Path (Get-Location) 'artifacts\acceptance-task8-state-backup-20260808'
if (Test-Path -LiteralPath $backupRoot) { throw "Backup target already exists: $backupRoot" }
New-Item -ItemType Directory -Path $backupRoot | Out-Null
if (Test-Path -LiteralPath $stateRoot) { Copy-Item -LiteralPath $stateRoot -Destination (Join-Path $backupRoot 'CodexQuotaHud') -Recurse -Force }
if (Test-Path -LiteralPath $exchangeRoot) { Copy-Item -LiteralPath $exchangeRoot -Destination (Join-Path $backupRoot 'Codex Quota HUD Skins') -Recurse -Force }
$stateFiles = @(Get-ChildItem -LiteralPath (Join-Path $backupRoot 'CodexQuotaHud') -Recurse -File -Force)
$exchangeFiles = @(Get-ChildItem -LiteralPath (Join-Path $backupRoot 'Codex Quota HUD Skins') -Recurse -File -Force)
[pscustomobject]@{ ProductStateFiles = $stateFiles.Count; ExchangePackages = $exchangeFiles.Count; Bytes = (($stateFiles + $exchangeFiles) | Measure-Object Length -Sum).Sum }
Get-Content -Raw -LiteralPath (Join-Path $stateRoot 'settings.json')
Get-Process -Id 9284 | Select-Object Id, Path, Responding
Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
```

State command S2 (literal command executed after stopping the earlier
diagnostic source process, before the documented 15:40:47 launch):

```powershell
function Get-Inventory([string] $root) {
  @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($root.Length).TrimStart('\'); Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
  })
}
$before = Get-Inventory (Join-Path (Get-Location) 'artifacts\acceptance-task8-state-backup-20260808\CodexQuotaHud\Designer')
$after = Get-Inventory (Join-Path $env:LOCALAPPDATA 'CodexQuotaHud\Designer')
$difference = @(Compare-Object $before $after -Property Path, Hash)
[pscustomobject]@{ Before = $before.Count; After = $after.Count; Differences = $difference.Count }
Get-Process | Where-Object { $_.Path -like '*CodexQuotaHud*' } | Select-Object Id, Path, Responding
Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
```

Raw S2 summary: `Before=22; After=22; Differences=0`; the only remaining
product process was installed formal HUD PID `9284`.

State command S3 (literal final comparison commands):

```powershell
function Get-Inventory([string] $root) {
  @(Get-ChildItem -LiteralPath $root -Recurse -File -Force | ForEach-Object {
    [pscustomobject]@{ Path = $_.FullName.Substring($root.Length).TrimStart('\'); Hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
  })
}
$backup = Join-Path (Get-Location) 'artifacts\acceptance-task8-state-backup-20260808'
$live = Join-Path $env:LOCALAPPDATA 'CodexQuotaHud'
foreach ($relative in @('Designer','Skins','Imports')) {
  $before = Get-Inventory (Join-Path $backup "CodexQuotaHud\$relative")
  $after = Get-Inventory (Join-Path $live $relative)
  [pscustomobject]@{ Area = $relative; Before = $before.Count; After = $after.Count; Differences = @(Compare-Object $before $after -Property Path, Hash).Count }
}
$beforeExchange = Get-Inventory (Join-Path $backup 'Codex Quota HUD Skins')
$afterExchange = Get-Inventory (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'Codex Quota HUD Skins')
[pscustomobject]@{ Area = 'Exchange'; Before = $beforeExchange.Count; After = $afterExchange.Count; Differences = @(Compare-Object $beforeExchange $afterExchange -Property Path, Hash).Count }
Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $backup 'CodexQuotaHud\preview-window.json'), (Join-Path $live 'preview-window.json')
Get-Content -Raw -LiteralPath (Join-Path $backup 'CodexQuotaHud\settings.json'), (Join-Path $live 'settings.json')
Get-Process -Id 9284 | Select-Object Id, Path, Responding
Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
```

Raw S3 inventory summary: `Designer=22/22/0; Skins=10/10/0;
Imports=0/0/0; Exchange=2/2/0`; the two preview hashes were both
`348C130F8FE476DD7CE3748D60320A8EBA5D85ECCD4603F6AE985B12544610F0`.

State command S4 (literal guarded cleanup commands):

```powershell
$roots = @(
  (Join-Path (Get-Location) 'artifacts\acceptance-task8-state-backup-20260808'),
  'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-IsolatedState-20260808',
  'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-IsolatedOutput2-20260808'
)
$backup = $roots[0]
[pscustomobject]@{ Files = @(Get-ChildItem -LiteralPath $backup -Recurse -File -Force).Count; Bytes = (Get-ChildItem -LiteralPath $backup -Recurse -File -Force | Measure-Object Length -Sum).Sum }
foreach ($root in $roots) {
  if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
  [pscustomobject]@{ Path = $root; Absent = -not (Test-Path -LiteralPath $root) }
}
Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz'
```

Boundary command S5 (literal command executed after the review correction;
the path list is the complete non-ignored Task 8 correction scope):

```powershell
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
git status --short -- docs/verification/2026-08-05-skin-designer-authoring-upgrade-acceptance.md tools/CodexQuotaHud.SkinDesignerCompatibilityProbe/CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj tools/CodexQuotaHud.SkinDesignerCompatibilityProbe/Program.cs
git -c core.autocrlf=false diff --check
if ($LASTEXITCODE -ne 0) { throw "git diff --check exit code: $LASTEXITCODE" }
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
```

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
- Task 9 installed v1.3.0 smoke must execute, observe, and record each of the
  ten table rows individually under the correctly installed application
  identity. Until all ten have direct evidence, Task 8 remains `PARTIAL` and
  is neither full manual acceptance nor release-ready evidence.
