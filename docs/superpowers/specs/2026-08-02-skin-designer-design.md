# Codex Quota HUD Optional Skin Designer — Design Specification

Date: 2026-08-02
Status: Approved for implementation planning

## 1. Decision summary

Codex Quota HUD will add a public, optional skin-design workflow without making
the normal HUD depend on design-time code.

- The normal Setup path remains lightweight. The new **Skin Designer** component
  is available in Setup and is **not selected by default**.
- Every installation, including one without the designer, keeps the five built-in
  skins and gains the ability to import, select, and remove safe `.cqskin` skin
  packages.
- Selecting the optional component installs a separate
  `Codex Quota HUD 皮肤设计器` application. The designer reuses the current
  synthetic Preview capabilities but presents them as a public product feature.
- The existing main-application `--preview` entry remains available as a hidden
  developer diagnostic. Setup does not create a shortcut or ordinary-user menu
  item for it.
- The first design template is **自由装饰环** (Free Decoration Ring). It supports
  a background-image slot, a center-image slot, and a transparent outer-decoration
  slot in one template.

This specification intentionally replaces the earlier rule that all Preview
capabilities must remain developer-only. The remaining release boundary is more
precise: ordinary users receive the productized, separately installed Skin
Designer; the raw `--preview` diagnostic remains hidden.

## 2. Goals

1. Let ordinary users import and run skins without installing design tools.
2. Let users who opt in during Setup create, preview, apply, edit, and export
   skins with their own local images.
3. Preserve the current HUD's startup reliability, quota behavior, five built-in
   skins, low-quota alert policy, detail popup, tray icon, and edge docking.
4. Make shared skin packages data-only and safe to validate before installation.
5. Keep normal HUD and designer process lifecycles independent.
6. Preserve backwards compatibility with existing settings and the hidden
   `--preview` diagnostic.

## 3. Non-goals for the first release

- No arbitrary XAML, DLL, JavaScript, PowerShell, executable, or plugin code in a
  skin package.
- No free-form vector drawing canvas or unrestricted element tree.
- No remote image URL downloading. The first release imports local PNG and JPEG
  files only.
- No marketplace, account system, cloud synchronization, ratings, or automatic
  third-party updates.
- No deletion or replacement of the five built-in skins.
- No change to quota acquisition or refresh behavior.
- No designer desktop shortcut or designer startup registration by default.

## 4. Product composition

### 4.1 Normal HUD

The normal `CodexQuotaHud.App` application continues to own quota acquisition,
the tray icon, settings, single-instance behavior, details, skin selection, and
edge docking. It adds:

- `导入皮肤…` in the tray skin menu;
- installed custom skins in the existing skin-selection list;
- safe removal for custom skins only;
- `打开皮肤设计器` when the optional designer executable is installed at the
  exact expected installation path.

The normal HUD never loads design-time assemblies and never executes package
content.

### 4.2 Optional Skin Designer

The designer is a separate Windows executable with its own single-instance
identity. It does not connect to `codex app-server`, register startup, or compete
for the normal HUD's single-instance lock.

It provides:

- new skin, open draft, edit installed custom skin, and import-then-edit;
- local image import;
- template parameter editing;
- synthetic quota states and a live production-equivalent HUD preview;
- automatic draft recovery, undo, and redo;
- install/apply to the local HUD;
- export to a shareable `.cqskin` package.

### 4.3 Shared skin runtime

A shared skinning library owns the data contracts, strict validation, package
reading/writing, template registry, and template renderer. Both applications use
this library so the designer cannot preview behavior that the normal HUD cannot
render.

The library exposes bounded contracts rather than design-tool UI types:

- `SkinManifest`
- `SkinTheme`
- `SkinAssetReference`
- `SkinPackageValidator`
- `SkinPackageInstaller`
- `ISkinTemplate`
- `SkinTemplateRegistry`
- `CustomSkinRenderer`

## 5. Skin package contract

### 5.1 File shape

`.cqskin` is a ZIP container with this logical structure:

```text
manifest.json
theme.json
assets/
  background.png|jpg
  center.png|jpg
  decoration.png
```

`manifest.json` contains:

- schema version;
- stable UUID skin ID;
- display name, author, semantic package version, and description;
- template ID;
- minimum compatible HUD version;
- declared asset names and SHA-256 hashes.

Display name and author are limited to 80 Unicode scalar values; description is
limited to 500. Author is informational metadata and is never presented as a
verified identity or signature.

`theme.json` contains only bounded template parameters. It never contains local
absolute paths, account state, quota values, window position, installed settings,
or executable expressions.

### 5.2 Collision behavior

- A new skin ID installs normally after preview and validation.
- The same ID with a newer or equal package version offers **Replace**, **Keep a
  copy**, or **Cancel**.
- Replace uses staging and atomic directory replacement after full validation.
- Keep a copy creates a new local UUID and records the original package ID as
  provenance. It never mutates the imported package file.
- Built-in identifiers are reserved and rejected for custom packages.

### 5.3 Compatibility behavior

- Unknown schema versions, unknown template IDs, or a minimum HUD version above
  the installed version are rejected with a specific reason.
- Unknown optional properties in a supported schema are rejected in the first
  release rather than silently ignored, keeping rendering deterministic.
- Package import first produces a safe preview. It does not immediately change
  the selected formal skin.

## 6. Free Decoration Ring template

The first template has three independent optional image slots:

1. **Background** — local PNG or JPEG, clipped to the HUD body.
2. **Center** — avatar, logo, or small illustration inside a bounded center mask.
3. **Outer decoration** — transparent PNG rendered around the quota body.

Each slot supports bounded position, scale, rotation, opacity, and crop focus.
The template additionally supports:

- primary and secondary ring colors;
- base background color and opacity;
- ring diameter, thickness, gap, and start angle within safe ranges;
- glow color and intensity;
- safe number/label size, weight, and predefined placement;
- rotation, breathing, glow, and floating animation intensity.

The first schema uses normalized and device-independent bounds: image X/Y offset
`-50..50` DIP, scale `0.25..3.0`, rotation `-180..180` degrees, opacity
`0..1`, ring thickness `2..16` DIP, ring gap `2..24` DIP, start angle
`0..359` degrees, text size `12..34` DIP, and effect intensity `0..1`. Values
outside these ranges are validation errors rather than silently clamped package
input. Interactive designer controls themselves stop at the same limits.

System-owned quota rings, labels, and numbers remain in protected display layers.
Image layers cannot cover them. Low-quota warning and critical colors remain
controlled by the existing `QuotaAlertPolicy`; a package cannot disable or
redefine those product alerts. The template adapts automatically to single,
dual, and hidden quota modes.

The template registry is versioned so later releases can add liquid, instrument,
rectangular panel, or other templates without changing existing packages.

## 7. Designer experience

### 7.1 Layout

The approved layout is a desktop split view:

- left: categorized parameter controls;
- right: always-visible live HUD preview;
- bottom: fast synthetic-state controls.

The initial window must remain usable at 100%, 125%, 150%, and 200% DPI and on a
small work area. The parameter side scrolls independently; the preview and
primary save/apply actions remain visible.

### 7.2 Left-side sections

1. **Basic information** — name, author, version, and description.
2. **Images** — background, center, and outer-decoration slots with replace,
   remove, crop, position, scale, rotation, and opacity.
3. **Quota rings** — safe ring geometry and single/dual layout parameters.
4. **Colors and effects** — normal colors, base background, and glow.
5. **Text** — bounded size, weight, and predefined safe placement.
6. **Animation** — rotation, breathing, glow, and floating intensity. The normal
   HUD's global animation switch always takes precedence.

### 7.3 Preview states

The bottom test strip provides:

- dual, five-hour-only, weekly-only, and no-quota modes;
- `100`, `68`, `21`, `20`, `11`, `10`, and `0` percent presets;
- independent mixed primary/secondary warning states;
- detail popup open/closed;
- top, bottom, left, and right edge-collapse previews;
- animations enabled/disabled and refresh-in-progress states.

These controls reuse the current synthetic Preview controller and production HUD
view. They do not write real quota or normal HUD settings.

### 7.4 Editing and output

- A one-second debounce writes an atomic recovery draft after each meaningful
  change.
- Undo and redo retain the latest 100 in-memory edit states.
- `Save draft` creates a named local project.
- `Apply to HUD` validates and installs the package, then requests activation by
  the normal HUD.
- `Export package` creates a deterministic `.cqskin` archive with asset hashes.
- Closing with unsaved edits presents Save, Discard, and Cancel.

## 8. Runtime application and inter-process behavior

The designer never directly mutates a running HUD's in-memory skin state.

1. It exports the current draft into a staging directory.
2. The shared validator validates the complete staged package.
3. The installer atomically promotes it into the installed-skins directory.
4. The designer sends an `ActivateSkin` command through the existing per-user
   local control mechanism, extended with typed commands.
5. The normal HUD validates the installed package again, switches the skin, and
   persists the selection.

If the HUD is not running, the designer starts the exact installed executable
with a bounded activate-skin argument. If live activation fails, the installed
package remains available but the current formal skin is unchanged; the designer
reports that the user can apply it from the HUD menu. No force termination is
used.

## 9. Storage and lifecycle

```text
%LOCALAPPDATA%\CodexQuotaHud\skins\<skin-id>\
%LOCALAPPDATA%\CodexQuotaHud\designer\drafts\<draft-id>\
%LOCALAPPDATA%\CodexQuotaHud\imports\<operation-id>\
```

- Imported package assets are copied into owned storage. They never depend on
  the original image or package remaining in place.
- Import staging directories are operation-scoped and removed after success or
  failure.
- Removing the optional designer component leaves installed skins and drafts.
- Normal uninstall preserves settings, skins, and drafts.
- The existing explicit purge choice removes the exact settings root, including
  skins and drafts, after the same path-safety checks used by current uninstall.

## 10. Import security

The importer performs all checks before package promotion:

- accept only regular files in a ZIP container;
- reject absolute paths, `..`, alternate separators that escape the root,
  reparse points, duplicate normalized names, encrypted entries, and unsupported
  compression methods;
- cap package size at 50 MB, extracted size at 64 MB, entry count at 64, and each
  image at 16 MB;
- accept PNG and JPEG only in the first release;
- decode image content rather than trusting extensions;
- cap decoded dimensions at 8192 × 8192 and the combined decoded pixel budget at
  67,108,864 pixels across all assets;
- strictly parse JSON with bounded string lengths, finite numeric values, known
  properties, safe ranges, and exact schema/template versions;
- verify declared SHA-256 hashes;
- reject executables, scripts, DLLs, XAML, symbolic links, and undeclared files.

Skin import opens no network connection and reads no browser cookies, account
credentials, conversations, or quota response bodies.

## 11. Error handling and fallback

- A package validation error identifies the failing field or asset without
  installing partial state.
- A corrupt installed custom skin causes the HUD to select `HudDial` for the
  current run, persist the safe fallback, and show one actionable error.
- Missing optional designer files hide or disable the designer menu entry; they
  do not affect HUD startup.
- A draft load failure preserves the damaged draft, creates no replacement over
  it, and offers to start a new draft.
- Failed package replacement restores the prior installed directory.
- Failed live activation preserves the previous formal skin.

## 12. Settings migration

The runtime introduces a string selection key with explicit namespaces:

```text
builtin:HudDial
builtin:EnergyRing
builtin:LiquidGlass
builtin:Aurora
builtin:LiquidTank
custom:<uuid>
```

On first load after upgrade:

1. If the new selection key exists and validates, use it.
2. Otherwise map the legacy `SelectedSkin` enum to the matching `builtin:` key.
3. If neither validates, use `builtin:HudDial`.

The migration does not modify unrelated settings. Existing users retain the
same built-in skin selection. The five built-in enum identifiers and their
renderers remain stable for compatibility and tests.

## 13. Setup behavior

The Inno Setup component page adds:

```text
[ ] 安装皮肤设计器
```

It is not selected by default.

The component name and description are localized in Simplified Chinese and
English using the same installer language selection as the existing Setup.

- Without it, Setup installs the normal HUD, five built-in skins, custom-skin
  runtime, and import UI.
- With it, Setup additionally installs the designer executable and design-time
  resources and creates a Start-menu entry.
- It does not create a designer desktop shortcut or startup registration.
- Rerunning Setup supports adding or removing the component.
- Removing the component removes only designer program files and its Start-menu
  entry.
- Upgrade preserves installed skins and drafts.
- Public Setup still creates only the normal HUD desktop shortcut when that
  existing default task is selected. It never creates a raw `--preview`
  shortcut.

## 14. Verification contract

### 14.1 Automated coverage

- manifest/theme serialization and strict validation;
- deterministic export and hash verification;
- traversal, duplicate path, oversized archive, oversized decoded image,
  spoofed image, illegal JSON, reserved ID, and incompatible-version rejection;
- atomic install, replace, keep-copy, rollback, remove, and cleanup;
- legacy settings migration and safe fallback;
- custom skin registration, selection, persistence, and built-in protection;
- all Free Decoration Ring image slots and parameter boundaries;
- single, dual, hidden, refreshing, animation-disabled, details, and four-edge
  states;
- existing `21/20/11/10/0` alert boundaries and independent dual alerts;
- draft recovery, unsaved-change handling, undo, and redo;
- separate main/designer single-instance identities;
- apply command success, missing HUD, failed activation, and fallback;
- installer default-without-designer, selected-with-designer, add/remove
  component, upgrade preservation, normal uninstall preservation, and explicit
  purge scenarios;
- full regression suite for all five built-in skins and existing interactions.

### 14.2 Manual Windows acceptance

- 100%, 125%, 150%, and 200% DPI;
- small work area and long Chinese names;
- opaque JPEG, transparent PNG edges, high-resolution image, and crop controls;
- simultaneous normal HUD and designer operation;
- apply, close designer, restart HUD, sign out/in, and Windows restart recovery;
- export on a designer installation and import on a machine without the designer;
- four monitor-edge collapse directions and primary/secondary monitor placement;
- normal Setup without the designer and optional-component Setup with it.

## 15. Completion criteria

The feature is complete only when:

1. Default Setup remains a working lightweight HUD installation without the
   designer.
2. That installation can safely import and use a `.cqskin` package.
3. The optional designer can create a Free Decoration Ring skin with local
   images, preview all required states, apply it, and export it.
4. A second installation without the designer can import that exported package.
5. Invalid packages cannot execute code, escape owned directories, partially
   install, or corrupt the selected formal skin.
6. Existing settings and all five built-in skins remain compatible.
7. Automated and real Windows acceptance evidence is recorded before packaging
   or release claims.
