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
| PASS | 2026-08-08 16:36:15.115–16:36:22.260 +09:00 | `dotnet test .\tests\CodexQuotaHud.Skins.Tests\CodexQuotaHud.Skins.Tests.csproj -c Release --no-restore --filter "FullyQualifiedName~ParseTheme_LegacyJsonDefaultsNewTextLayoutFieldsToZero\|FullyQualifiedName~Writers_UseCanonicalUtf8AndRoundTripByteForByte\|FullyQualifiedName~Validate_RejectsMinimumHudVersionAboveInstalledVersion"` | Legacy text layout defaults to `0/0`; canonical output writes each property once; newer-HUD manifests are rejected | `3/3` passed, failed `0`, skipped `0`; duration `26 ms`; exit `0` | Round 2 fresh focused VSTest summary |
| PASS | 2026-08-08 16:36:12.072–16:36:15.114 +09:00 | `dotnet build .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-restore` | Hardened durable probe compiles against production project references | Build succeeded; warnings `0`, errors `0`; duration `2.70 s`; exit `0` | Round 2 fresh build stdout |

## Compatibility and package evidence

| Status | Date/time (Asia/Tokyo) | Exact command or action | Expected | Observed | Evidence |
|---|---|---|---|---|---|
| PASS | 2026-08-08 16:28:19.724–16:28:21.796 +09:00 | Probe command P1 below | Pin untouched `柔光玫瑰.cqskin` by SHA/skin ID/minimum HUD/three assets before mutation; import through production services in explicit isolated state; export deterministic canonical `1.3.0` `7/6`; reject under HUD `1.2.3` | Exit `0`; legacy SHA-256 `cbcf4caff3238e9f4ee4ce247fb6b8b39652d6d2d7f444912853788a6684279f`, skin ID `08b02426-c826-4202-afb0-09d55e66af2e`, minimum `1.2.3`, asset count `3` with all hashes matched, property counts/effective values `0/0`; output SHA-256 `1abebad400fb5b60e0c667d14b0e3bc8ec67ca0e0ae955d2cdb5219b3b17b7d8`, version/minimum `1.3.0`, values/counts `7/6` and `1/1`, canonical `true/true`, three asset hashes matched; HUD `1.2.3` returned `version.incompatible` at `$.minimumHudVersion` | Hardened durable probe v1, raw summary P1 below |
| PASS | 2026-08-08 16:28:34.219–16:28:36.000 +09:00 | Probe command P2 below | Repeat the pinned production-path checks with untouched `雷光伙伴.cqskin` | Exit `0`; legacy SHA-256 `05c0228bb6b83b0becd6e4b6556e6990dcc329be3b8a176d3a20a3fe45572c56`, skin ID `75c7b76e-7b3a-4e51-83db-c404555a7a7e`, minimum `1.2.3`, asset count `3` with all hashes matched, property counts/effective values `0/0`; output SHA-256 `9931cebe6d9ff2994e6402f5bd48c016b41dbef0651188cdd916bd1e2e94d35e`, version/minimum `1.3.0`, values/counts `7/6` and `1/1`, canonical `true/true`, three asset hashes matched; HUD `1.2.3` returned `version.incompatible` at `$.minimumHudVersion` | Hardened durable probe v1, raw summary P2 below |

The replayable probe is tracked at
`tools/CodexQuotaHud.SkinDesignerCompatibilityProbe`. It references the
production Designer project and reads only the explicitly identity-pinned
`--legacy-package`. Before any mutation it requires the state and output roots
to be absent, non-root, distinct, non-overlapping, and free of existing
reparse-point ancestors. It emits JSON and performs no cleanup.
The imported draft ID and clock are fixed inside the probe, and each command
pins the expected output SHA-256. This makes package identity deterministic;
an unexpected serialization, asset, identity, or timestamp change fails the
run instead of silently changing the evidence artifact.

Probe command P1 (literal command executed):

```powershell
$legacy = 'C:\Users\yaozi\Documents\Codex Quota HUD Skins\柔光玫瑰.cqskin'
$state = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Rose-State'
$output = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Rose-Output\Task8-Compatibility-Copy.cqskin'
if (-not (Test-Path -LiteralPath $legacy -PathType Leaf)) { throw "Missing legacy package: $legacy" }
if (Test-Path -LiteralPath $state) { throw "State target already exists: $state" }
if (Test-Path -LiteralPath (Split-Path -Parent $output)) { throw "Output root already exists: $(Split-Path -Parent $output)" }
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
dotnet run --project .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-build -- --legacy-package $legacy --expected-legacy-sha256 cbcf4caff3238e9f4ee4ce247fb6b8b39652d6d2d7f444912853788a6684279f --expected-skin-id 08b02426-c826-4202-afb0-09d55e66af2e --expected-output-sha256 1abebad400fb5b60e0c667d14b0e3bc8ec67ca0e0ae955d2cdb5219b3b17b7d8 --expected-asset-count 3 --isolated-local-app-data $state --output-package $output
if ($LASTEXITCODE -ne 0) { throw "Probe exit code: $LASTEXITCODE" }
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
```

Raw summary P1 (exact JSON stdout, with JSON's Unicode escaping preserved):

```json
{
  "ProbeVersion": 1,
  "StartedAtUtc": "2026-08-08T07:28:20.5357231+00:00",
  "CompletedAtUtc": "2026-08-08T07:28:21.7281897+00:00",
  "LegacyPackagePath": "C:\\Users\\yaozi\\Documents\\Codex Quota HUD Skins\\\u67D4\u5149\u73AB\u7470.cqskin",
  "LegacyPackageSha256": "cbcf4caff3238e9f4ee4ce247fb6b8b39652d6d2d7f444912853788a6684279f",
  "ExpectedSkinId": "08b02426-c826-4202-afb0-09d55e66af2e",
  "ExpectedAssetCount": 3,
  "LegacyDeclaredMinimumHudVersion": "1.2.3",
  "LegacyTextOffsetPropertyCount": 0,
  "LegacyTextLineGapPropertyCount": 0,
  "LegacyEffectiveTextOffsetY": 0,
  "LegacyEffectiveTextLineGap": 0,
  "LegacyAssetHashes": [
    { "Path": "assets/background.jpg", "DeclaredSha256": "ada9d14333d0b8a08ce59d64bed1ffc33e6503ed3b141ab4dc9d1721c47af192", "ActualSha256": "ada9d14333d0b8a08ce59d64bed1ffc33e6503ed3b141ab4dc9d1721c47af192", "Matches": true },
    { "Path": "assets/center.jpg", "DeclaredSha256": "ada9d14333d0b8a08ce59d64bed1ffc33e6503ed3b141ab4dc9d1721c47af192", "ActualSha256": "ada9d14333d0b8a08ce59d64bed1ffc33e6503ed3b141ab4dc9d1721c47af192", "Matches": true },
    { "Path": "assets/decoration.png", "DeclaredSha256": "9dae04de8c4ad70c8f3cca5876b33313a971f0ffb4963ba5d659e74d1625418e", "ActualSha256": "9dae04de8c4ad70c8f3cca5876b33313a971f0ffb4963ba5d659e74d1625418e", "Matches": true }
  ],
  "ImportedDraftId": "88888888-8888-4888-8888-888888888888",
  "ImportedMinimumHudVersion": "1.3.0",
  "OutputPackagePath": "C:\\Users\\yaozi\\Documents\\CodexQuotaHud-Task8-Probe-R2-Rose-Output\\Task8-Compatibility-Copy.cqskin",
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
$state = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Thunder-State'
$output = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Thunder-Output\Task8-Compatibility-Copy.cqskin'
if (-not (Test-Path -LiteralPath $legacy -PathType Leaf)) { throw "Missing legacy package: $legacy" }
if (Test-Path -LiteralPath $state) { throw "State target already exists: $state" }
if (Test-Path -LiteralPath (Split-Path -Parent $output)) { throw "Output root already exists: $(Split-Path -Parent $output)" }
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
dotnet run --project .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-build -- --legacy-package $legacy --expected-legacy-sha256 05c0228bb6b83b0becd6e4b6556e6990dcc329be3b8a176d3a20a3fe45572c56 --expected-skin-id 75c7b76e-7b3a-4e51-83db-c404555a7a7e --expected-output-sha256 9931cebe6d9ff2994e6402f5bd48c016b41dbef0651188cdd916bd1e2e94d35e --expected-asset-count 3 --isolated-local-app-data $state --output-package $output
if ($LASTEXITCODE -ne 0) { throw "Probe exit code: $LASTEXITCODE" }
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
```

Raw summary P2 (exact JSON stdout, with JSON's Unicode escaping preserved):

```json
{
  "ProbeVersion": 1,
  "StartedAtUtc": "2026-08-08T07:28:34.8841221+00:00",
  "CompletedAtUtc": "2026-08-08T07:28:35.9317897+00:00",
  "LegacyPackagePath": "C:\\Users\\yaozi\\Documents\\Codex Quota HUD Skins\\\u96F7\u5149\u4F19\u4F34.cqskin",
  "LegacyPackageSha256": "05c0228bb6b83b0becd6e4b6556e6990dcc329be3b8a176d3a20a3fe45572c56",
  "ExpectedSkinId": "75c7b76e-7b3a-4e51-83db-c404555a7a7e",
  "ExpectedAssetCount": 3,
  "LegacyDeclaredMinimumHudVersion": "1.2.3",
  "LegacyTextOffsetPropertyCount": 0,
  "LegacyTextLineGapPropertyCount": 0,
  "LegacyEffectiveTextOffsetY": 0,
  "LegacyEffectiveTextLineGap": 0,
  "LegacyAssetHashes": [
    { "Path": "assets/background.png", "DeclaredSha256": "8d7f4532fb5eedd38ad7c0302bffc3071703d580662d73b1452bc70029de614a", "ActualSha256": "8d7f4532fb5eedd38ad7c0302bffc3071703d580662d73b1452bc70029de614a", "Matches": true },
    { "Path": "assets/center.png", "DeclaredSha256": "9cdfb970ec479d6aeed081d2f03d6447ebed3ce4e34e79f1d383add86c452827", "ActualSha256": "9cdfb970ec479d6aeed081d2f03d6447ebed3ce4e34e79f1d383add86c452827", "Matches": true },
    { "Path": "assets/decoration.png", "DeclaredSha256": "7982a24b411c7db8dc4c77b3a37a6b6ef538b6c709bcd0e2f280eaa20397c53b", "ActualSha256": "7982a24b411c7db8dc4c77b3a37a6b6ef538b6c709bcd0e2f280eaa20397c53b", "Matches": true }
  ],
  "ImportedDraftId": "88888888-8888-4888-8888-888888888888",
  "ImportedMinimumHudVersion": "1.3.0",
  "OutputPackagePath": "C:\\Users\\yaozi\\Documents\\CodexQuotaHud-Task8-Probe-R2-Thunder-Output\\Task8-Compatibility-Copy.cqskin",
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

### Negative boundary and identity evidence

| Status | Date/time (Asia/Tokyo) | Invocation | Expected | Observed |
|---|---|---|---|---|
| PASS | 2026-08-08 16:29:18.408–16:29:19.336 +09:00 | N1 below: pre-existing output root | Fail before creating state or output file | Exit `1`; `The explicit output root must not already exist`; state absent `true`; output file absent `true` |
| PASS | 2026-08-08 16:29:35.272–16:29:36.214 +09:00 | N2 below: overlapping absent roots | Fail before creating either root | Exit `1`; `must be distinct and non-overlapping`; overlap root absent `true` |
| PASS | 2026-08-08 16:29:48.959–16:29:49.724 +09:00 | N3 below: wrong expected legacy SHA-256 | Reject identity before mutation | Exit `1`; `legacy package SHA-256 did not match`; state/output roots absent `true/true` |
| PASS | 2026-08-08 16:30:03.801–16:30:04.752 +09:00 | N4 below: correctly hashed v1.3.0 package offered as legacy | Require legacy minimum exactly `1.2.3` before mutation | Exit `1`; `legacy minimum HUD version was not exactly 1.2.3`; state/output roots absent `true/true` |

N1 literal setup and invocation:

```powershell
$legacy = 'C:\Users\yaozi\Documents\Codex Quota HUD Skins\柔光玫瑰.cqskin'
$state = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-Existing2-State'
$outputRoot = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-Existing2-Output'
$output = Join-Path $outputRoot 'Task8-Compatibility-Copy.cqskin'
if (Test-Path -LiteralPath $state) { throw "State exists: $state" }
if (Test-Path -LiteralPath $outputRoot) { throw "Output root exists before setup: $outputRoot" }
[IO.Directory]::CreateDirectory($outputRoot) | Out-Null
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
dotnet run --project .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-build -- --legacy-package $legacy --expected-legacy-sha256 cbcf4caff3238e9f4ee4ce247fb6b8b39652d6d2d7f444912853788a6684279f --expected-skin-id 08b02426-c826-4202-afb0-09d55e66af2e --expected-output-sha256 1abebad400fb5b60e0c667d14b0e3bc8ec67ca0e0ae955d2cdb5219b3b17b7d8 --expected-asset-count 3 --isolated-local-app-data $state --output-package $output
$probeExit = $LASTEXITCODE
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
[pscustomobject]@{ ExpectedFailure = ($probeExit -ne 0); ExitCode = $probeExit; StateAbsent = -not (Test-Path -LiteralPath $state); OutputFileAbsent = -not (Test-Path -LiteralPath $output) }
if ($probeExit -eq 0 -or (Test-Path -LiteralPath $state) -or (Test-Path -LiteralPath $output)) { throw 'Preexisting-output negative probe did not fail before mutation.' }
```

N2 literal invocation:

```powershell
$legacy = 'C:\Users\yaozi\Documents\Codex Quota HUD Skins\柔光玫瑰.cqskin'
$state = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-Overlap'
$outputRoot = Join-Path $state 'Output'
$output = Join-Path $outputRoot 'Task8-Compatibility-Copy.cqskin'
if (Test-Path -LiteralPath $state) { throw "Overlap root exists: $state" }
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
dotnet run --project .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-build -- --legacy-package $legacy --expected-legacy-sha256 cbcf4caff3238e9f4ee4ce247fb6b8b39652d6d2d7f444912853788a6684279f --expected-skin-id 08b02426-c826-4202-afb0-09d55e66af2e --expected-output-sha256 1abebad400fb5b60e0c667d14b0e3bc8ec67ca0e0ae955d2cdb5219b3b17b7d8 --expected-asset-count 3 --isolated-local-app-data $state --output-package $output
$probeExit = $LASTEXITCODE
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
[pscustomobject]@{ ExpectedFailure = ($probeExit -ne 0); ExitCode = $probeExit; OverlapRootAbsent = -not (Test-Path -LiteralPath $state) }
if ($probeExit -eq 0 -or (Test-Path -LiteralPath $state)) { throw 'Overlap negative probe did not fail before mutation.' }
```

N3 literal invocation:

```powershell
$legacy = 'C:\Users\yaozi\Documents\Codex Quota HUD Skins\柔光玫瑰.cqskin'
$state = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-WrongHash-State'
$outputRoot = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-WrongHash-Output'
$output = Join-Path $outputRoot 'Task8-Compatibility-Copy.cqskin'
if ((Test-Path -LiteralPath $state) -or (Test-Path -LiteralPath $outputRoot)) { throw 'Wrong-hash roots must start absent.' }
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
dotnet run --project .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-build -- --legacy-package $legacy --expected-legacy-sha256 0000000000000000000000000000000000000000000000000000000000000000 --expected-skin-id 08b02426-c826-4202-afb0-09d55e66af2e --expected-output-sha256 1abebad400fb5b60e0c667d14b0e3bc8ec67ca0e0ae955d2cdb5219b3b17b7d8 --expected-asset-count 3 --isolated-local-app-data $state --output-package $output
$probeExit = $LASTEXITCODE
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
[pscustomobject]@{ ExpectedFailure = ($probeExit -ne 0); ExitCode = $probeExit; StateAbsent = -not (Test-Path -LiteralPath $state); OutputRootAbsent = -not (Test-Path -LiteralPath $outputRoot) }
if ($probeExit -eq 0 -or (Test-Path -LiteralPath $state) -or (Test-Path -LiteralPath $outputRoot)) { throw 'Wrong-hash negative probe did not fail before mutation.' }
```

N4 literal invocation:

```powershell
$legacy = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Rose-Output\Task8-Compatibility-Copy.cqskin'
$state = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-WrongVersion-State'
$outputRoot = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-WrongVersion-Output'
$output = Join-Path $outputRoot 'Task8-Compatibility-Copy.cqskin'
if ((Test-Path -LiteralPath $state) -or (Test-Path -LiteralPath $outputRoot)) { throw 'Wrong-version roots must start absent.' }
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
dotnet run --project .\tools\CodexQuotaHud.SkinDesignerCompatibilityProbe\CodexQuotaHud.SkinDesignerCompatibilityProbe.csproj -c Release --no-build -- --legacy-package $legacy --expected-legacy-sha256 1abebad400fb5b60e0c667d14b0e3bc8ec67ca0e0ae955d2cdb5219b3b17b7d8 --expected-skin-id 08b02426-c826-4202-afb0-09d55e66af2e --expected-output-sha256 0000000000000000000000000000000000000000000000000000000000000000 --expected-asset-count 3 --isolated-local-app-data $state --output-package $output
$probeExit = $LASTEXITCODE
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
[pscustomobject]@{ ExpectedFailure = ($probeExit -ne 0); ExitCode = $probeExit; StateAbsent = -not (Test-Path -LiteralPath $state); OutputRootAbsent = -not (Test-Path -LiteralPath $outputRoot) }
if ($probeExit -eq 0 -or (Test-Path -LiteralPath $state) -or (Test-Path -LiteralPath $outputRoot)) { throw 'Wrong-version negative probe did not fail before mutation.' }
```

An earlier N1 driver attempt used unsupported `New-Item -LiteralPath`, so the
setup directory was not created and the probe completed a normal positive
run. It is excluded from negative PASS evidence; its two explicit roots were
validated and removed by the single cleanup block C1 below.

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
| NOT RUN | 2026-08-08 15:40–15:43 +09:00 | Switch audition selector through `全部`, `转圈`, `呼吸`, `光晕`, `浮动`, and `刷新加速` | Every mode is visibly distinct; returning to `全部` restores the saved animation mix | Blocked before input; no mode was visually observed | Runtime ownership diagnostics |
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
| PASS | 2026-08-08 16:31:13.934–16:31:14.001 +09:00 | Single cleanup block C1 below | Validate every exact historical/current acceptance root before any deletion, then remove only validated existing roots and assert every root absent | `VALIDATED_COUNT=20`; seven historical roots already absent; positive R2 roots removed with file counts `3/1/3/1`; excluded diagnostic roots removed with `3/1`; N1 setup root removed with `0`; all other negative roots already absent; `ABSENT_COUNT=20` | Literal C1 block and raw summary; no other recursive cleanup recipe is published |
| PASS | 2026-08-08 16:38:17.195–16:38:17.327 +09:00 | Boundary command S5 below | Round 2 tracked changes remain confined to acceptance evidence/probe; no whitespace errors | Exactly the acceptance document and probe `Program.cs` were reported; diff check produced no output, exit `0` | Literal read-only boundary command S5 |

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

Cleanup block C1 (the one literal recursive cleanup recipe in this record):

```powershell
$canonicalWorktree = [IO.Path]::GetFullPath('C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731')
$canonicalArtifacts = [IO.Path]::GetFullPath((Join-Path $canonicalWorktree 'artifacts'))
$documentsRoot = [IO.Path]::GetFullPath('C:\Users\yaozi\Documents')
$documentsAcceptancePrefix = 'CodexQuotaHud-Task8-'
$expectedTargets = @(
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731\artifacts\acceptance-task8-state-backup-20260808'; Leaf = 'acceptance-task8-state-backup-20260808'; Boundary = 'WorktreeArtifacts' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-IsolatedState-20260808'; Leaf = 'CodexQuotaHud-Task8-IsolatedState-20260808'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-IsolatedOutput2-20260808'; Leaf = 'CodexQuotaHud-Task8-IsolatedOutput2-20260808'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-Rose-State-20260808-r1'; Leaf = 'CodexQuotaHud-Task8-Probe-Rose-State-20260808-r1'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-Rose-Output-20260808-r1'; Leaf = 'CodexQuotaHud-Task8-Probe-Rose-Output-20260808-r1'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-Thunder-State-20260808-r1'; Leaf = 'CodexQuotaHud-Task8-Probe-Thunder-State-20260808-r1'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-Thunder-Output-20260808-r1'; Leaf = 'CodexQuotaHud-Task8-Probe-Thunder-Output-20260808-r1'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Rose-State'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Rose-State'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Rose-Output'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Rose-Output'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Thunder-State'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Thunder-State'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Thunder-Output'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Thunder-Output'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-Existing-State'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Neg-Existing-State'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-Existing-Output'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Neg-Existing-Output'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-Existing2-State'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Neg-Existing2-State'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-Existing2-Output'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Neg-Existing2-Output'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-Overlap'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Neg-Overlap'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-WrongHash-State'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Neg-WrongHash-State'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-WrongHash-Output'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Neg-WrongHash-Output'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-WrongVersion-State'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Neg-WrongVersion-State'; Boundary = 'DocumentsAcceptance' },
  [pscustomobject]@{ Path = 'C:\Users\yaozi\Documents\CodexQuotaHud-Task8-Probe-R2-Neg-WrongVersion-Output'; Leaf = 'CodexQuotaHud-Task8-Probe-R2-Neg-WrongVersion-Output'; Boundary = 'DocumentsAcceptance' }
)
function Test-SameOrDescendant([string] $Candidate, [string] $Path) {
  $candidatePrefix = $Candidate.TrimEnd('\') + '\'
  return $Path.Equals($Candidate, [StringComparison]::OrdinalIgnoreCase) -or $Path.StartsWith($candidatePrefix, [StringComparison]::OrdinalIgnoreCase)
}
function Assert-NoReparseTree([string] $Target) {
  if (-not (Test-Path -LiteralPath $Target)) { return }
  $queue = [Collections.Generic.Queue[string]]::new()
  $queue.Enqueue($Target)
  while ($queue.Count -gt 0) {
    $current = $queue.Dequeue()
    $currentItem = Get-Item -LiteralPath $current -Force
    if (($currentItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse point rejected: $($currentItem.FullName)" }
    if (-not $currentItem.PSIsContainer) { throw "Cleanup root is not a directory: $($currentItem.FullName)" }
    foreach ($child in @(Get-ChildItem -LiteralPath $currentItem.FullName -Force)) {
      if (($child.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Reparse point rejected: $($child.FullName)" }
      if ($child.PSIsContainer) { $queue.Enqueue($child.FullName) }
    }
  }
}
$validated = [Collections.Generic.List[string]]::new()
foreach ($entry in $expectedTargets) {
  $full = [IO.Path]::GetFullPath($entry.Path)
  $volumeRoot = [IO.Path]::GetPathRoot($full)
  if ([string]::IsNullOrWhiteSpace($volumeRoot) -or $full.TrimEnd('\').Equals($volumeRoot.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)) { throw "Volume/root cleanup target rejected: $full" }
  if (-not [IO.Path]::GetFileName($full).Equals($entry.Leaf, [StringComparison]::Ordinal)) { throw "Unexpected cleanup leaf: $full" }
  if ($entry.Boundary -eq 'WorktreeArtifacts') {
    if (-not [IO.Path]::GetDirectoryName($full).Equals($canonicalArtifacts, [StringComparison]::OrdinalIgnoreCase)) { throw "Wrong worktree cleanup boundary: $full" }
  } elseif ($entry.Boundary -eq 'DocumentsAcceptance') {
    if (-not [IO.Path]::GetDirectoryName($full).Equals($documentsRoot, [StringComparison]::OrdinalIgnoreCase) -or -not $entry.Leaf.StartsWith($documentsAcceptancePrefix, [StringComparison]::Ordinal)) { throw "Wrong Documents acceptance boundary: $full" }
  } else { throw "Unknown cleanup boundary label: $($entry.Boundary)" }
  Assert-NoReparseTree $full
  $validated.Add($full)
}
if ($validated.Count -ne $expectedTargets.Count -or @($validated | Select-Object -Unique).Count -ne $validated.Count) { throw 'Cleanup target count/uniqueness validation failed.' }
for ($left = 0; $left -lt $validated.Count; $left++) {
  for ($right = $left + 1; $right -lt $validated.Count; $right++) {
    if ((Test-SameOrDescendant $validated[$left] $validated[$right]) -or (Test-SameOrDescendant $validated[$right] $validated[$left])) { throw "Overlapping cleanup roots rejected: $($validated[$left]) <> $($validated[$right])" }
  }
}
Get-Date -Format "START yyyy-MM-dd HH:mm:ss.fff zzz"
Write-Output "VALIDATED_COUNT=$($validated.Count)"
foreach ($full in $validated) {
  if (Test-Path -LiteralPath $full) {
    $fileCount = @(Get-ChildItem -LiteralPath $full -Recurse -File -Force).Count
    Remove-Item -LiteralPath $full -Recurse -Force
    Write-Output "REMOVED=$full FILES=$fileCount"
  } else {
    Write-Output "ALREADY_ABSENT=$full"
  }
}
foreach ($full in $validated) {
  if (Test-Path -LiteralPath $full) { throw "Cleanup absence assertion failed: $full" }
}
Write-Output "ABSENT_COUNT=$($validated.Count)"
Get-Date -Format "END yyyy-MM-dd HH:mm:ss.fff zzz"
```

C1 raw summary: `VALIDATED_COUNT=20`; removed file counts were R2 positive
`3/1/3/1`, excluded diagnostic `3/1`, and N1 setup `0`; every other exact
root was already absent; `ABSENT_COUNT=20`.

Boundary command S5 (literal command executed after the round 2 correction;
the path list is the complete permitted Task 8 correction scope):

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

## Task 9 installed-candidate acceptance

Task 9 targets product source
`aecaea11f3ecf60738f46d8907ade63b3e081cb8`. It must stop after local
installed-candidate handoff and explicit user acceptance request. No `main`
integration, push, `v1.3.0` tag, GitHub Release, upload, public readback, or
historical-asset mutation is part of Steps 1–6.

### Candidate, installer, and local-upgrade gates

| Status | Date/time (Asia/Tokyo) | Gate | Expected | Observed / evidence |
|---|---|---|---|---|
| PASS | 2026-08-08 15:54:07–15:56:51 +09:00 | Task 8 serial source suites | All four Release assemblies pass with zero skipped | Core `75/75`, Skins `375/375`, App/UI `625/625`, Designer `424/424`; total `1499/1499`, failed `0`, skipped `0` |
| PASS | 2026-08-08 15:56:57–15:57:00 +09:00 | Task 8 Release build and diff | Zero warnings/errors and clean diff | Build warnings `0`, errors `0`; `git diff --check` no output |
| PASS | 2026-08-08 16:54:56–16:57:20 +09:00 | `.\scripts\package-release.ps1 -Version 1.3.0` | Produce Setup, normal-only ZIP, and two-line checksum manifest from one publish payload | Initial sandbox run failed only on blocked NuGet (`NU1301/NU1900`) and cleaned up; authorized identical rerun exited `0`, published App/Designer, and Inno Setup 6.7.3 compiled Setup successfully in `72.047 s` |
| PASS | 2026-08-08 17:06:51.943 +09:00 | Candidate identity/boundaries | Exact sizes/SHA-256, manifest/ZIP/publish identity, App/Designer/Setup versions, `NotSigned`; ordinary Setup contract unchanged | Setup `100,056,640` / `ceccf8c0…e3ff`; ZIP `68,341,872` / `9b91351d…0fba`; manifest `196` / `9f0be51c…f771`; two manifest hashes match; ZIP exactly five normal-HUD entries; publish exactly App + Designer; App/Designer `1.3.0.0` + `aecaea1`; Setup `1.3.0`; all three `NotSigned` with no certificates. Full local record: `artifacts/release/v1.3.0-candidate-identity.txt` |
| PASS | 2026-08-08 approximately 17:10–17:27 +09:00 | `.\scripts\test-installer.ps1 -Version 1.3.0 -InstallerPath .\artifacts\release\CodexQuotaHud-Setup-v1.3.0.exe` | Seven normal and two committed-cleanup scenarios pass; zero final smoke roots/processes; production install/data untouched | Authorized identical rerun exited `0`: `fresh-default`, `fresh-designer`, `add-designer`, `remove-designer`, `upgrade-selected`, `uninstall-preserve`, `uninstall-purge`, `cleanup-legacy-failure`, and `cleanup-designer-failure` all passed with checked cleanup. Initial sandbox run is excluded: isolated HKCU key creation was denied with error `5`. Its exact diagnostic root was inspected, validated within system temp with no reparse tree, removed, and final smoke-root count was `0` at 17:28:16.718. Full local record: `artifacts/release/v1.3.0-installer-matrix.txt` |
| PASS | 2026-08-08 17:30:34.354 +09:00 | Bounded pre-install snapshot | Record installed binaries/hashes/version, uninstall/startup, settings/selected skin, installed skins, drafts/recovery/imports/exchange, and shortcuts | Before snapshot captured v1.2.3 App/Designer/uninstall, exact `--background` startup, selected `custom:75c7b76e-7b3a-4e51-83db-c404555a7a7e`, `34` state files (`10` installed-skin, `22` Designer draft-tree including `2` recovery, `0` imports), `2` exchange files, normal+Designer Start links, one desktop preview link with exact `--preview`, and one formal HUD process. Preview binary backup hash matched; `before.json` SHA `3c84c365…8a10` |
| PASS | 2026-08-08 17:32:26.920–17:32:47.211 +09:00 | Real silent v1.3.0 upgrade | Exit `0`; select Designer + startup and omit normal desktop icon to preserve maintainer preference | Verified Setup hash then invoked `/SILENT /SUPPRESSMSGBOXES /NORESTART /TASKS="startup" /TYPE=custom /COMPONENTS=designer`; exit `0`; retained 6,197-byte install log SHA `428b9824…a09b` |
| PASS | 2026-08-08 17:33:43.791 +09:00 | Post-install product verification | Installed App/Designer `1.3.0.0`, hashes match publish, data unchanged except expected refresh timestamps, startup `--background`, Start menu correct, no Setup preview entry | App/Designer hashes match publish and product versions contain full `aecaea1`; uninstall `1.3.0`; exact startup; `34` state files with non-settings diffs `0`; stable settings equal and only `LastSuccessfulRefresh` advanced; exchange `2/2` diffs `0`; desktop product links `0`; Start menu normal+Designer with no arguments. Snapshot SHA `22de5e20…af1f` |
| PASS | 2026-08-08 17:34:07.995 +09:00 | Maintainer preview restoration | After product verification, separately recreate/restore only `Codex Quota HUD 开发预览` with exact `--preview`; explicitly not Setup behavior | Copied the exact preinstall `.lnk` backup only after product verification; source/destination SHA both `6afe8a88…4abf`; standard installed App target, exact `--preview`, final product desktop count `1`. Restoration record explicitly labels separate maintainer customization |
| PASS | 2026-08-08 17:34:23.855–17:34:34.939 +09:00 | Formal HUD and installed Designer launch | Both exact installed binaries launch; Designer remains open for installed-GUI acceptance | Formal HUD PID `17188`; installed Designer PID `11716`, HWND `983264`, title `Codex Quota HUD 皮肤设计器`; both exact installed paths and responding. Window2 failed twice with `node_repl exec context not found`, so the authorized fallback below attached only to this revalidated PID/HWND |
| PASS | 2026-08-08 18:09:33–18:10:54 +09:00 | GUI-smoke state restoration | Remove only Task 9 temporary state, preserve all pre-existing user state, and leave the installed applications running | After the bounded GUI run, the exact Task 9 temporary draft `e5adef09-aaa4-41c0-8741-132c7fe109b2` was removed only after the installed Designer stopped and the absolute drafts-root boundary plus complete no-reparse tree were revalidated. The never-created temporary installed skin is absent. Final state is back to `34` files with non-settings path/hash diffs `0`; stable settings fields and selected skin are equal; the two exchange packages have identical names/hashes; only the expected refresh timestamp advanced. Formal HUD PID `17188` remains responding; installed Designer was reopened from the exact v1.3.0 path as PID `21728`, HWND `1312598`, and left open. Ignored evidence: `artifacts/acceptance-task9-uia/state-backup/final-restored-state.json` (`de83fccb…12c4`) |
| NOT RUN | — | User practical acceptance | User tests and explicitly accepts the exact installed candidate | Not run |

Public installer contract for this candidate remains: startup and the normal
`Codex Quota HUD` desktop shortcut are selected by default and may be
deselected; the shortcut has no `--preview`; Designer is visible and unchecked
by default; ZIP contains the normal HUD fallback only; Setup removes Developer
Preview entries. The maintainer real-install command intentionally selects
startup and Designer while omitting the normal desktop icon to preserve this
machine's existing preference. The later preview-shortcut restoration is a
separate local customization, not an installer result.

### Installed Designer GUI rows

Window2 identified the installed Designer uniquely, but two capture attempts
failed with `node_repl exec context not found`. Per the authorized bounded
fallback, an ignored temporary .NET `System.Windows.Automation` harness
attached only after validating the exact installed executable, PID `11716`,
HWND `983264`, title, and responding state. It enumerated `192` accessible
nodes, interacted only with that Designer, captured only process-owned windows,
and wrote screenshots/JSON under ignored
`artifacts/acceptance-task9-uia/evidence/`. Each status below comes from direct
installed-UI observation; source tests do not substitute for missing visual
evidence.

| Status | Date/time (Asia/Tokyo) | Installed action | Expected | Observed / evidence |
|---|---|---|---|---|
| PASS | 2026-08-08 17:50:02–17:50:27 +09:00 | Text offset control on a copied/temporary draft | Both text lines move together | At `0`, Number/Label tops were `241/274`; at `+12 DIP`, both became `247/280`, the same `+6 px` movement at preview scale. Direct before/after screenshots agree. Restored `0` and original geometry |
| PASS | 2026-08-08 17:50:53–17:51:05 +09:00 | Positive and negative line-gap control | Lines expand/contract equally without changing their midpoint | `+16 DIP` produced line centers `256/285.5`; `-8 DIP` produced `262/279.5`. Both share midpoint `270.75`, while direct screenshots show expansion/contraction. Restored `0` and original `241/274` tops |
| PARTIAL | 2026-08-08 17:43:43–18:08:41 +09:00 | Undo, redo, save, and reopen | Both non-zero values restore exactly | The exact `192`-node installed tree exposes Save/Open but no Undo/Redo control, and the installed window has no input binding for those operations. Save/reopen was not attempted after the later identity gate failed twice before input. The full expected chain therefore cannot be claimed; temporary values were independently restored |
| PARTIAL | 2026-08-08 17:49:33 +09:00 | Set text layout to `0/0` | Appearance matches the v1.2.3 baseline | Installed v1.3.0 at exact `0/0` was directly captured and measured (`241/274` text tops), but no v1.2.3 same-context visual capture exists. Compatibility tests are not visual evidence, so visual equivalence remains unproven |
| PASS | 2026-08-08 17:52:16–17:53:35 +09:00 | Enable `构图参考线` | Guides align with both rings, peak center container, and text lines and remain click-through | Direct screenshots show dashed outer/inner rings, peak center box, and both text guides aligned. UIA FromPoint at four preview locations returned the same underlying custom/number/label targets with guides off and on, so the overlay did not intercept them. Restored Off |
| PARTIAL | 2026-08-08 17:59:57–18:00:37 +09:00 | Switch `全部`, `转圈`, `呼吸`, `光晕`, `浮动`, `刷新加速` | Modes are distinct; returning to `全部` restores saved mix | All six actual installed labels were selected individually and direct screenshots/state were captured, then `全部` was restored. The temporary blank draft had no decoration and saved rotation/floating intensity `0`, so every channel could not be shown as visually distinct in this context |
| PASS | 2026-08-08 18:00:29–18:01:26 +09:00 | Enter and leave `刷新加速` | Former `刷新中` checkbox state restores | From saved Off, audition forced On+disabled and leaving restored Off+enabled. From saved On, audition kept On+disabled and leaving restored On+enabled. Final state returned to the original Off+enabled and `全部` |
| PASS | 2026-08-08 17:58:02–17:58:34 +09:00 | Switch Dual / 5h / Week / None | Hints match rings; Dual outer = 5h, inner = Week | 5h showed `5h：单圈显示 5 小时额度` and `68%/5小时`; Week showed `Week：单圈显示每周额度` and `34%/每周`; None hid the preview. Dual restored the preview, double rings, and exact `Dual：外圈 = 5 小时，内圈 = 每周` hint |
| PASS | 2026-08-08 18:02:51–18:04:01 +09:00 | Open export picker | Native picker starts in `C:\Users\yaozi\Documents\Codex Quota HUD Skins` with a leaf `.cqskin` name | The process-owned native picker opened at breadcrumb `文档 > Codex Quota HUD Skins`; filename was leaf-only `未命名皮肤.cqskin` with the CQSKIN filter. Escape cancelled without saving; the app-owned `操作已取消` result was dismissed, and exchange files remained unchanged |
| NOT RUN | 2026-08-08 18:07:57–18:08:41 +09:00 | Apply copied/temporary skin to HUD | Result shows exact display name/version/skin ID and truthful activation disposition | Both the action attempt and the one explicitly authorized read-only retry stopped before input because the harness observed the expected path/responding process but transient `MainWindowHandle=0` and empty title, failing the exact identity gate. Apply was never invoked, the HUD was never contacted, selected skin stayed `custom:75c7b76e-7b3a-4e51-83db-c404555a7a7e`, and no temporary installed skin was created |

### Additional plan-required installed gates

These gates are independent of the ten-row Designer GUI aggregate above.
Task 8 source automated compatibility/isolation tests and a responding
installed process do not substitute for direct installed smoke evidence.

| Status | Date/time (Asia/Tokyo) | Installed gate | Expected | Observed / evidence |
|---|---|---|---|---|
| NOT RUN | — | Installed formal-HUD tray menu | Open and operate the installed tray menu after upgrade | The formal HUD process launched and remained responding, but its tray menu was not opened or operated |
| NOT RUN | — | Installed old-package import | Import a legacy package through the installed product and observe the compatibility result | Source compatibility suites/probes passed, but no legacy package was imported through the installed HUD or Designer; source automation is not installed smoke |
| NOT RUN | — | Close Designer, then inspect formal HUD for preview-tool leakage | After the installed Designer closes, the formal HUD shows neither composition guides nor audition-isolated animation | The Designer was stopped and reopened during state restoration, but the formal HUD was not directly inspected for guide/audition leakage after that close; process continuity and source isolation tests do not prove the visual/runtime result |

Installed-GUI total: `6 PASS / 3 PARTIAL / 1 NOT RUN`. Overall Task 9 is
`PARTIAL — installed candidate handed off, user acceptance pending`.
Packaging, isolated installer testing, the real upgrade, and bounded installed
GUI evidence are recorded honestly. The row 3 installed-UI contract gap, row 4
missing same-context legacy visual, row 6 unsuitable animation fixture, row 10
identity-gate block, all three additional installed gates above, and user
practical acceptance remain open. The real-upgrade PASS scope is limited to
Setup exit/identity, data preservation, startup/shortcut/uninstall state, and
binary launch; it does not include the unrun tray/import/isolation checks.
Remote release Step 7 remains prohibited.
