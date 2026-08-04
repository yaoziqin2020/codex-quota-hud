# 雷光伙伴皮肤实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 使用已安装的 Codex Quota HUD v1.2.3 Skin Designer 制作、应用、导出并验证原创自定义皮肤 `雷光伙伴`，不修改程序或发布新的 HUD 版本。

**Architecture:** 三张生成素材分别承担背景、中心角色和透明装饰职责；现有 `free-decoration-ring` 渲染器继续绘制动态额度圈、文字和所有动画。制作过程只写仓库忽略的工作素材、本机 Designer 草稿、本机已安装皮肤和 Documents 交换目录中的 `.cqskin`，不改动产品源码。

**Tech Stack:** Codex 内置 `image_gen`、`view_image`、图像生成 Skill 的 chroma-key 脚本、Codex Computer Use、Codex Quota HUD Skin Designer v1.2.3、PowerShell/.NET ZIP 与 SHA-256 校验。

## Global Constraints

- Canonical worktree: `C:\Users\yaozi\Documents\Codex\Projects\CodexQuotaHud\.worktrees\inno-setup-installer-20260731`；不得使用旧对话 worktree。
- Design authority: `docs/superpowers/specs/2026-08-05-lightning-companion-skin-design.md` at commit `3536284`.
- Character: original `圆耳电团兽`; no Pokémon/Pikachu names, silhouettes, marks, accessories, or downloaded copyrighted art.
- Metadata: display name `雷光伙伴`, author `老姚`, package version `1.0.0`, minimum HUD `1.2.3`.
- Template: `free-decoration-ring`; no HUD, Designer, installer, or `.cqskin` schema changes.
- Colors: primary `#FFFFD52E`, secondary `#FF4DE7FF`, base `#FF10141B` at `0.82`, glow `#FFFFE35A` at `0.62`.
- Ring geometry: diameter `102`, thickness `7`, gap `2`, start angle `270`; secondary diameter is `84` and its inner opening is `77`.
- Text: number `30`, label `14`, `semiBold`, `numberAboveLabel`; character composition must accommodate `100%` without ear contact.
- Animation: rotation `0.78`, breathing `0.88`, glow `0.92`, floating `0.18`, refresh speed `3.5x`, hold `3.0s`.
- Center transform starts at scale `1.03`, offset `0/0`; only Offset Y may be tuned within `-4..4` DIP with visual evidence.
- The breathing peak face diameter is approximately `72.881` DIP; the cyan ring must remain outside it with approximately `2.059` DIP radial clearance.
- The decoration asset is a transparent PNG containing translucent yellow halo, real rendered lightning/electric fragments, star glints, and sparks; no character glyphs, text, solid background, or fake third data ring.
- Do not overwrite an existing `雷光伙伴.cqskin`; if the target appears during execution, export a candidate file and ask before replacement.
- Preserve the current installed `柔光玫瑰` and its selection unless `雷光伙伴` is deliberately applied; do not delete the two historical Soft Rose export archives.
- `tmp/` is user-owned and remains untouched/uncommitted.
- Generated images and verification evidence stay under ignored `artifacts/`; do not commit them or push Git unless the user later asks.
- Real Dual quota availability is external. Synthetic Dual validation in Designer is required; if the live account still lacks both windows, report live Dual as not verified rather than simulating evidence in the formal HUD.

---

## File and Data Map

| Path | Responsibility |
| --- | --- |
| `artifacts/skins/lightning-companion/source-assets/background.png` | Generated dark electric ambience, no character or UI |
| `artifacts/skins/lightning-companion/source-assets/center.png` | Generated B-layout original creature portrait |
| `artifacts/skins/lightning-companion/source-assets/decoration-chroma.png` | Generated magenta-key source for halo/electric decoration |
| `artifacts/skins/lightning-companion/source-assets/decoration.png` | Final transparent decoration after chroma removal |
| `artifacts/skins/lightning-companion/verification/` | Ignored screenshots, alpha statistics, package inventory, and hashes |
| `%LOCALAPPDATA%\CodexQuotaHud\designer\drafts\<new-draft-guid>\` | New independent Designer draft and copied assets |
| `%LOCALAPPDATA%\CodexQuotaHud\skins\<new-skin-guid>\` | Installed `雷光伙伴` selected by Apply to HUD |
| `%USERPROFILE%\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin` | Final shareable package |

No tracked production file is created or modified by the execution tasks. The only Git-tracked deliverables are the already committed design spec and this plan.

---

### Task 1: Preflight and Safe Working Boundary

**Files:**
- Create directory: `artifacts/skins/lightning-companion/source-assets/`
- Create directory: `artifacts/skins/lightning-companion/verification/`
- Read only: `%LOCALAPPDATA%\CodexQuotaHud\settings.json`
- Read only: `%LOCALAPPDATA%\CodexQuotaHud\skins\*/manifest.json`
- Read only: `%LOCALAPPDATA%\CodexQuotaHud\designer\drafts\*/draft.json`

**Interfaces:**
- Consumes: canonical worktree and installed v1.2.3 binaries.
- Produces: collision-free output paths and a baseline inventory for later comparison.

- [ ] **Step 1: Verify worktree and tracked state**

Run:

```powershell
git status --short --branch
git rev-parse --show-toplevel
```

Expected: root is the canonical worktree; only the known user-owned `tmp/` may be untracked before execution; spec/plan commits are local and no product code is modified.

- [ ] **Step 2: Verify installed executables and versions**

Run:

```powershell
Get-Item -LiteralPath 'C:\Users\yaozi\AppData\Local\Programs\CodexQuotaHud\CodexQuotaHud.App.exe', 'C:\Users\yaozi\AppData\Local\Programs\CodexQuotaHud\designer\CodexQuotaHud.SkinDesigner.exe' |
  Select-Object FullName, Length, LastWriteTime, @{Name='FileVersion';Expression={$_.VersionInfo.FileVersion}}
```

Expected: both binaries exist and report `FileVersion = 1.2.3.0`; their current installed
`ProductVersion` begins with `1.2.3+`.

- [ ] **Step 3: Resolve output collisions without deleting anything**

Run:

```powershell
Test-Path -LiteralPath 'C:\Users\yaozi\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin'
Get-ChildItem -LiteralPath 'C:\Users\yaozi\AppData\Local\CodexQuotaHud\skins' -Directory |
  ForEach-Object {
    $manifest = Join-Path $_.FullName 'manifest.json'
    if (Test-Path -LiteralPath $manifest) {
      Get-Content -Raw -Encoding UTF8 -LiteralPath $manifest | ConvertFrom-Json
    }
  } | Select-Object skinId, displayName, packageVersion
```

Expected: no `雷光伙伴.cqskin` and no installed `雷光伙伴` exist. If either exists, do not overwrite or delete it; use `雷光伙伴-candidate.cqskin` for this run and report the collision.

- [ ] **Step 4: Create ignored working directories**

Run:

```powershell
New-Item -ItemType Directory -Force -Path `
  'artifacts\skins\lightning-companion\source-assets', `
  'artifacts\skins\lightning-companion\verification' | Select-Object FullName
```

Expected: both directories resolve inside the canonical worktree's ignored `artifacts/` tree.

- [ ] **Step 5: Record the baseline**

Capture in the execution log: current selected skin key, installed custom-skin count, absence of the Soft Rose draft, and the one retained installed `柔光玫瑰` skin ID `08b02426-c826-4202-afb0-09d55e66af2e`.

---

### Task 2: Generate and Validate Background and Center Assets

**Files:**
- Create: `artifacts/skins/lightning-companion/source-assets/background.png`
- Create: `artifacts/skins/lightning-companion/source-assets/center.png`

**Interfaces:**
- Consumes: approved A1a/B visual specification.
- Produces: opaque 1024×1024 background and center assets for Task 4.

- [ ] **Step 1: Generate the background with `image_gen`**

Use the built-in image generation tool with this prompt and no reference image:

```text
Create a square 1024×1024 premium desktop HUD skin background. Deep charcoal and dark navy electric ambience, subtle radial depth, restrained warm electric-yellow ambient light and small cyan accents, sparse tiny energy dust, soft cinematic volumetric glow, elegant and polished like a high-end desktop collectible. Keep the center calm and dark enough for a circular mascot and white quota text. No character, no rings, no UI, no symbols, no words, no logo, no watermark, no border. Full-bleed square composition, readable when reduced to 132×132 pixels.
```

Save the returned image as `background.png` under the source-assets directory.

- [ ] **Step 2: Inspect the background at full and HUD scale**

Use `view_image` at original detail, then inspect a 132×132 thumbnail. Reject and regenerate if the image contains text, a bright central object, a recognizable logo, or dense high-frequency noise that competes with the center character.

- [ ] **Step 3: Generate the center creature with `image_gen`**

Use this prompt and no copyrighted character reference:

```text
Create a square 1024×1024 original soft-3D mascot portrait for a circular desktop HUD. Subject: an entirely original round-eared electric creature, warm luminous yellow fur, rounded circular face, two soft ears placed high and spread outward with a wide empty gap between their inner edges, tall clear forehead, glossy dark oval eyes slightly below center, tiny friendly mouth slightly above the lower third, subtle orange cheeks, gentle cyan rim light, premium soft toy / desk collectible material, delicate fur texture, calm charming expression. Composition requirement: the live text “100%” will be drawn across the forehead between ears and eyes, and a small period label will be drawn directly below the mouth; leave those exact regions visually clean. Keep the head visually centered, not floating upward. Use a dark charcoal/navy circular-friendly opaque background matching an electric HUD. No text, no numbers, no rings, no lightning glyphs, no logo, no watermark, no resemblance to Pikachu or any existing franchise character.
```

Save the returned image as `center.png`.

- [ ] **Step 4: Inspect B-layout safety**

Use `view_image` and evaluate a circular 64×64 crop. Confirm the ears are spread far enough for `100%`, the eyes and mouth occupy the space between the two live text lines, and the face remains visually centered.

- [ ] **Step 5: Use image editing only if the first center result violates the layout**

If the first result is otherwise stylistically correct but the ears or facial features are misplaced, call `image_gen` in edit mode with `center.png` as the referenced image and this exact correction:

```text
Preserve the same original creature, material, colors, lighting, and background. Move both ears farther outward and slightly higher to create a clean forehead text gap; move the eyes slightly lower and the mouth slightly higher. Keep the whole round head centered and keep all areas free of text, numbers, rings, logos, and watermarks.
```

Replace `center.png` only after inspecting the edited result and confirming it improves the text-safe composition.

---

### Task 3: Generate and Extract the Transparent Effect Layer

**Files:**
- Create: `artifacts/skins/lightning-companion/source-assets/decoration-chroma.png`
- Create: `artifacts/skins/lightning-companion/source-assets/decoration.png`

**Interfaces:**
- Consumes: yellow/cyan palette and ring safe zones from the spec.
- Produces: a transparent PNG used by Designer's Decoration slot.

- [ ] **Step 1: Generate a keyed decoration source**

Call `image_gen` with this prompt and no reference image:

```text
Create a square 1024×1024 VFX sprite sheet as one unified transparent-effect composition, but render it on a perfectly flat solid chroma-magenta background #FF00FF for later removal. Effects only: a broad soft translucent electric-yellow halo feathering outward from an implied central circle, several elegant rendered yellow lightning fragments and electric arcs outside that circle, a few small cyan star glints, and sparse warm-yellow/cyan energy sparks. The center must remain open for a mascot face and live text. Do not draw a continuous solid ornament ring, a third progress ring, any character, letters, lightning font glyphs, words, logo, watermark, border, texture, shadow, or gradient in the magenta background. The magenta must be uniform all the way to every canvas edge.
```

Save as `decoration-chroma.png`.

- [ ] **Step 2: Inspect the keyed source before removal**

Use `view_image`. Confirm the background is uniformly magenta, the halo is translucent-looking rather than a solid yellow disk, the electric fragments sit mainly outside the data-ring region, and no text-like lightning symbols are present.

- [ ] **Step 3: Remove the chroma key with the provided script**

Run:

```powershell
& 'C:\Users\yaozi\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' `
  'C:\Users\yaozi\.codex\skills\.system\imagegen\scripts\remove_chroma_key.py' `
  --input 'artifacts\skins\lightning-companion\source-assets\decoration-chroma.png' `
  --out 'artifacts\skins\lightning-companion\source-assets\decoration.png' `
  --key-color '#FF00FF' `
  --soft-matte `
  --transparent-threshold 24 `
  --opaque-threshold 135 `
  --edge-feather 1 `
  --spill-cleanup `
  --force
```

Expected: a PNG with alpha, no magenta canvas, and preserved soft yellow halo.

- [ ] **Step 4: Verify alpha numerically**

Run:

```powershell
& 'C:\Users\yaozi\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe' -c "from PIL import Image; p=r'artifacts\skins\lightning-companion\source-assets\decoration.png'; im=Image.open(p).convert('RGBA'); a=im.getchannel('A'); h=a.histogram(); print({'size': im.size, 'alpha_minmax': a.getextrema(), 'transparent': h[0], 'partial': sum(h[1:255]), 'opaque': h[255]})"
```

Expected: size `(1024, 1024)`, alpha minimum `0`, alpha maximum `255`, and nonzero transparent and partial pixel counts.

- [ ] **Step 5: Inspect the transparent result on dark and checkerboard backgrounds**

Use `view_image` at original detail. Reject the asset if any magenta fringe, rectangular background, hard-edged yellow fog, clipped glow, or fake continuous progress ring remains. If fringe remains, rerun Step 3 with `--transparent-threshold 34 --opaque-threshold 150 --edge-contract 1` while keeping the other flags, then repeat Step 4.

---

### Task 4: Create the Designer Draft and Enter Exact Parameters

**Files:**
- Create through Designer: `%LOCALAPPDATA%\CodexQuotaHud\designer\drafts\<new-draft-guid>\draft.json`
- Create through Designer: `%LOCALAPPDATA%\CodexQuotaHud\designer\drafts\<new-draft-guid>\assets\*`

**Interfaces:**
- Consumes: three validated Task 2/3 assets and all values in Global Constraints.
- Produces: a saved independent `雷光伙伴` draft that later tasks apply and export.

- [ ] **Step 1: Initialize Computer Use correctly**

In the persistent Node REPL, run once:

```js
if (!globalThis.sky) {
  const { setupComputerUseRuntime } = await import("C:/Users/yaozi/.codex/plugins/cache/openai-bundled/computer-use/26.727.51351/scripts/computer-use-client.mjs");
  await setupComputerUseRuntime({ globals: globalThis });
}
```

Then read all three mandatory documents before UI action:

```js
await sky.documentation("guidance");
await sky.documentation("api");
await sky.documentation("confirmations");
```

Use the returned confirmation rules for every subsequent Windows action.

- [ ] **Step 2: Launch the installed Designer**

Launch exactly:

```text
C:\Users\yaozi\AppData\Local\Programs\CodexQuotaHud\designer\CodexQuotaHud.SkinDesigner.exe
```

Target the Designer window through Computer Use and choose `新建`. Do not open or overwrite any existing unnamed draft.

- [ ] **Step 3: Enter metadata**

Set:

```text
项目/显示名称: 雷光伙伴
作者: 老姚
皮肤版本: 1.0.0
最低 HUD 版本: 1.2.3
描述: 原创圆耳电团兽主题，以电光黄、炭黑与冰蓝呈现充能、呼吸和环形光效。
模板: free-decoration-ring
```

- [ ] **Step 4: Import the three exact assets**

Use the native file picker for each Designer slot:

```text
Background -> ...\source-assets\background.png
Center     -> ...\source-assets\center.png
Decoration -> ...\source-assets\decoration.png
```

Confirm the preview updates after each import and that the Decoration slot shows transparency rather than a rectangular background.

- [ ] **Step 5: Enter image transforms**

Set:

```text
Background: Offset 0/0, Scale 1.00, Rotation 0, Opacity 1.00, Crop 0.5/0.5
Center:     Offset 0/0, Scale 1.03, Rotation 0, Opacity 1.00, Crop 0.5/0.5
Decoration: Offset 0/0, Scale 1.00, Rotation 0, Opacity 1.00, Crop 0.5/0.5
```

- [ ] **Step 6: Enter theme, ring, and text values**

Set exactly:

```text
Primary ring: #FFFFD52E
Secondary ring: #FF4DE7FF
Base: #FF10141B at 0.82
Glow: #FFFFE35A at 0.62
Diameter 102; Thickness 7; Gap 2; Start 270
Number 30; Label 14; SemiBold; Number Above Label
```

- [ ] **Step 7: Enter animation values**

Set exactly:

```text
Rotation 0.78
Breathing 0.88
Glow pulse 0.92
Floating 0.18
Refresh speed 3.5x
Refresh hold 3.0s
```

- [ ] **Step 8: Save the draft and resolve its new ID**

Choose `保存草稿`, then read the newly created draft directories and identify the one whose `draft.json` has `displayName = 雷光伙伴`. Record its draft ID and skin ID in the execution log. Confirm the Soft Rose draft remains absent.

---

### Task 5: Tune the Approved B Layout in Synthetic Preview

**Files:**
- Modify only through Designer: the new `雷光伙伴` draft.
- Create evidence: `artifacts/skins/lightning-companion/verification/designer-*.png`

**Interfaces:**
- Consumes: saved draft from Task 4.
- Produces: visually accepted Dual/Single states without changing fixed product behavior.

- [ ] **Step 1: Validate the approved Dual reference state**

In the Designer preview controls set:

```text
Mode Dual
5-hour 68
Weekly 34
Details off
Animations on
Refreshing off
```

Capture a screenshot. Confirm `68%` sits between ears and eyes without touching the ear roots, `5 小时` sits directly below the mouth, the cyan ring remains outside the face, and the yellow halo does not overexpose the rings.

- [ ] **Step 2: Validate the longest number**

Set the 5-hour value to `100`. Capture a screenshot and confirm `100%` remains clear of both ears. If it touches, adjust only Center Offset Y within `-4..4` DIP; do not move the head visibly off center or change text spacing.

- [ ] **Step 3: Validate the breathing peak clearance**

Observe at least two breathing cycles with intensity `0.88`. Capture or inspect the maximum face expansion and confirm it does not cover the cyan ring. If antialiasing or the generated face visually crosses the ring despite the calculated clearance, reduce Center Scale in increments of `0.01`, stopping at the first value that clears the ring; record the final scale.

- [ ] **Step 4: Validate Single modes**

Check both `5h only` and `Weekly only`. Confirm the sole live window uses the existing primary yellow ring, the label identifies the window correctly, and the absent cyan secondary ring is not visible.

- [ ] **Step 5: Validate all animation channels independently**

Observe and record:

```text
Rotation -> transparent halo/electric fragments move around the center
Breathing -> only the center creature expands/contracts
Glow -> data-ring glow visibly pulses
Floating -> decoration drifts subtly without shaking
Refreshing -> all enabled animation tracks run at 3.5x and visibly remain accelerated after refresh completion for the configured 3.0s state
```

Toggle global animation off and back on; all custom animation must stop and resume.

- [ ] **Step 6: Save the final tuned values**

Choose `保存草稿` again. Read `draft.json` and confirm the saved theme contains the final center transform, exact ring/text/color values, and all six animation fields.

---

### Task 6: Apply to HUD and Export the Shareable Package

**Files:**
- Create through Designer: `%LOCALAPPDATA%\CodexQuotaHud\skins\<new-skin-guid>\*`
- Create through Designer: `%USERPROFILE%\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin`

**Interfaces:**
- Consumes: final saved draft from Task 5.
- Produces: selected installed skin and one export package for Tasks 7/8.

- [ ] **Step 1: Apply the final draft to HUD**

Choose `应用到 HUD`. Allow Designer to start the exact installed HUD if it is not running. Confirm the action succeeds and the HUD immediately switches to `雷光伙伴`.

- [ ] **Step 2: Verify installed catalog state**

Read installed manifests. Expected custom skins:

```text
柔光玫瑰 -> exactly one installed directory
雷光伙伴 -> exactly one installed directory
```

Confirm settings `SelectedSkinKey` points to the new `雷光伙伴` skin ID. Do not remove Soft Rose.

- [ ] **Step 3: Export without overwriting**

Choose `导出皮肤包`. The native picker must default to the shared Documents exchange directory. Save as:

```text
C:\Users\yaozi\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin
```

If the exact path exists, cancel overwrite and save `雷光伙伴-candidate.cqskin`; do not replace the existing file without user approval.

- [ ] **Step 4: Confirm draft, installed skin, and package share one skin ID**

Read `draft.json`, installed `manifest.json`, and package `manifest.json`. The `skinId` must be identical across all three, and `displayName` must be `雷光伙伴`.

---

### Task 7: Independently Validate Package Structure and Values

**Files:**
- Read: `%USERPROFILE%\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin`
- Create: `artifacts/skins/lightning-companion/verification/package-inventory.txt`
- Create: `artifacts/skins/lightning-companion/verification/package-sha256.txt`

**Interfaces:**
- Consumes: exported package from Task 6.
- Produces: independent structural, metadata, asset, and hash evidence.

- [ ] **Step 1: Check package size and hash**

Run:

```powershell
Get-Item -LiteralPath 'C:\Users\yaozi\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin' |
  Select-Object FullName, Length, LastWriteTime
Get-FileHash -Algorithm SHA256 -LiteralPath 'C:\Users\yaozi\Documents\Codex Quota HUD Skins\雷光伙伴.cqskin'
```

Expected: package is below 50 MiB and has a stable SHA-256 recorded for handoff.

- [ ] **Step 2: Inspect ZIP entries and reject extras**

Open the archive read-only with `System.IO.Compression.ZipFile`. Expected logical entries are exactly:

```text
manifest.json
theme.json
assets/background.<png-or-jpg>
assets/center.<png-or-jpg>
assets/decoration.png
```

Reject any executable, script, XAML, remote URL file, nested archive, or unexpected entry.

- [ ] **Step 3: Verify manifest values**

Assert:

```text
displayName = 雷光伙伴
author = 老姚
packageVersion = 1.0.0
minimumHudVersion = 1.2.3
templateId = free-decoration-ring
assets count = 3
```

- [ ] **Step 4: Verify theme values**

Assert every Global Constraint color, geometry, text, image-transform, and animation value. Recalculate:

```text
secondary diameter = 102 - 2 × (7 + 2) = 84
secondary inner opening = 84 - 7 = 77
breathing peak face = 64 × finalCenterScale × (1 + 0.12 × 0.88)
radial clearance = (77 - peakFace) / 2 > 0
```

If Task 5 reduced center scale, use that recorded final scale rather than `1.03`.

- [ ] **Step 5: Verify asset hashes**

For each manifest asset, compute SHA-256 from the exact ZIP entry bytes and compare it to the manifest declaration. Confirm the decoded images are at most 8192 per dimension, below 16 MiB each, and the decoration image contains a nontrivial alpha channel.

- [ ] **Step 6: Exercise the normal HUD import path without creating a duplicate**

Open the package in the formal HUD import preview and verify the Chinese metadata and image preview. Cancel before installation because the same skin ID is already installed by Apply to HUD; this validates the package without creating a duplicate formal skin.

---

### Task 8: Formal HUD Runtime and Restart Acceptance

**Files:**
- Create evidence: `artifacts/skins/lightning-companion/verification/hud-*.png`
- Read only: `%LOCALAPPDATA%\CodexQuotaHud\settings.json`

**Interfaces:**
- Consumes: selected installed skin and validated package.
- Produces: runtime acceptance evidence and a user-ready state for hands-on testing.

- [ ] **Step 1: Verify immediate switching**

Through the formal HUD skin menu, switch from `雷光伙伴` to `柔光玫瑰` and back. Confirm both render immediately and exactly one menu entry exists for each custom skin.

- [ ] **Step 2: Exercise normal HUD surfaces**

With `雷光伙伴` selected, verify details closed/open, dock left/right/up/down, expand/collapse, tray refresh, animations off/on, and current low/normal quota coloring. Confirm no clipping, text collision, or opaque decoration rectangle.

- [ ] **Step 3: Restart the formal HUD**

Exit through the tray/menu, then start exactly:

```text
C:\Users\yaozi\AppData\Local\Programs\CodexQuotaHud\CodexQuotaHud.App.exe
```

Confirm `雷光伙伴` remains selected, renders immediately, appears once in the menu, and animations resume according to settings.

- [ ] **Step 4: Record the live Dual limitation honestly**

If the real account exposes both 5-hour and weekly windows, capture the live Dual result. If not, record `NOT VERIFIED — live account did not expose both windows`; retain Designer synthetic Dual evidence from Task 5 and do not claim live Dual success.

- [ ] **Step 5: Leave the system ready for user testing**

Leave the formal HUD running with `雷光伙伴` selected and the Designer open on the saved `雷光伙伴` draft. Do not close or delete the new draft/package before the user's hands-on review.

---

### Task 9: Final Handoff and Workspace Integrity

**Files:**
- Read: final draft, installed skin, package, verification evidence.
- Do not modify: product source, installer, release assets, `tmp/`, historical Soft Rose exports.

**Interfaces:**
- Consumes: all prior task evidence.
- Produces: a concise completion report and exact continuation point.

- [ ] **Step 1: Capture final identifiers and hashes**

Report the new draft GUID, new skin GUID, installed directory, export path, package byte length, SHA-256, final center scale/offset, and the six animation values.

- [ ] **Step 2: Recheck cleanup and counts**

Confirm the Soft Rose draft remains absent, installed Soft Rose count is one, installed Lightning Companion count is one, and the two historical Soft Rose export archives remain untouched.

- [ ] **Step 3: Recheck Git integrity**

Run:

```powershell
git status --short --branch
git diff --check
```

Expected: no product-code or tracked implementation changes; ignored artifacts do not appear; user-owned `tmp/` remains untouched.

- [ ] **Step 4: State verification boundaries**

The final report must separate:

```text
Verified: generated assets, alpha, Designer synthetic modes, saved draft, Apply to HUD, package structure/hashes, immediate switch, restart persistence.
Not verified if unavailable: real live Dual quota data.
User action: inspect the running HUD and Designer and approve or request visual tuning before any Git push or broader release work.
```

No Git push, Setup/ZIP rebuild, tag, PR, or GitHub Release is part of this plan.
