# Skin Designer Undo/Redo UI Design

## Goal

Expose the Skin Designer's existing bounded draft history through discoverable
UI controls and standard keyboard shortcuts. An author can undo a meaningful
draft edit, redo it, and immediately see the restored values and preview.

## Approved approach

Use two compact top-toolbar buttons labeled `撤销` and `重做`, plus `Ctrl+Z`
and `Ctrl+Y`. This was selected over shortcuts-only UI, which is not
discoverable, and a full history panel, which is unnecessary for v1.3.0.

## Scope and behavior

- `撤销` invokes the current `SkinDraftSession.TryUndo()` operation.
- `重做` invokes the current `SkinDraftSession.TryRedo()` operation.
- The buttons and shortcuts share the same commands; there are no separate
  keyboard-only code paths.
- `撤销` is disabled when `CanUndo` is false. `重做` is disabled when
  `CanRedo` is false.
- Command availability updates after every undoable edit, undo, redo, image
  history boundary, new document, open/import/edit-installed document
  transition, and disposal.
- Undo/redo restores the parameter and metadata snapshot already tracked by
  the bounded history, including text layout, colors, ring settings,
  animation, and descriptive fields.
- The existing `MeaningfulChange` path refreshes editor controls, dirty state,
  recovery scheduling, and the live synthetic preview after undo/redo.
- A new meaningful edit after undo truncates the redo branch, preserving the
  current `DraftHistory` contract.
- History remains capped at 100 snapshots.

Image files are deliberately not copied into the 100-entry in-memory history.
A successful image replacement or removal becomes a new history boundary:
the resulting draft remains dirty relative to the last named save, but undo
cannot cross back over the image operation. This prevents a restored image
reference from pointing at bytes that the transactional image workflow has
already replaced or removed.

Undo/redo also does not reverse external side effects: file-picker choices,
named save writes, Apply-to-HUD, export, window movement, preview tools, or
package installation. Preview-only guides and animation audition never enter
history.

The editor publishes a real `Current` property-change notification after every
accepted meaningful change. Controls that are not bound to `Current`
(image-transform sliders and the two text enum selectors) are synchronized by
one guarded window method. Undo/redo therefore restores the visible editor,
the session document, and the live preview as one observable state without
creating a second revision while controls are being synchronized.

Installed acceptance exposed a pre-existing destructive image-storage defect:
the Designer changed or deleted mutable slot files before named Save, so
Discard could leave saved JSON pointing at missing or silently replaced bytes.
Draft assets therefore use an optional Designer-only immutable storage
locator. New image bytes are written as content-addressed leaves named
`assets/sha256-<64 lowercase hex>.<png|jpg>`; `RelativePath` remains the
canonical package path used by `.cqskin`, Apply, export, and runtime preview.
Legacy drafts without the locator continue to resolve their existing canonical
slot files. Replacement/removal never deletes a named or recovery-referenced
blob, and v1.3.0 performs no eager blob garbage collection. Named Save commits
the new locator through the existing atomic JSON swap; Discard deletes only the
working recovery JSON, so the last named JSON still resolves its exact old
bytes.

This is an internal Designer draft extension only: draft schema remains `1`,
legacy three-property references stay readable, and `.cqskin` schema and HUD
minimum-version rules do not change. Once a draft is saved with the optional
locator it requires Designer v1.3.0 or newer to reopen; older Designers already
reject unknown draft properties and must not silently reinterpret it.

## UI and accessibility

- Place `撤销` and `重做` in a compact edit-history toolbar at the top of the
  editor column. Do not crowd the existing four document buttons.
- Use the existing Designer dark button style and compact dimensions so the
  current minimum-width layout remains usable.
- Give the controls stable names `UndoDraftButton` and `RedoDraftButton` and
  explicit automation names that include their shortcuts.
- Bind window-level input gestures `Ctrl+Z` and `Ctrl+Y` to the same commands.
- Keyboard shortcuts must not execute a disabled command.

## Architecture

1. Expose read-only `CanUndo` and `CanRedo` from `SkinDraftSession` by
   delegating to `DraftHistory`.
2. Add a history-boundary operation that resets `DraftHistory` to the current
   image-mutated document without changing the named-save dirty baseline.
3. Route successful image replacement/removal commits through that boundary;
   ordinary parameter/metadata edits continue to push undoable snapshots.
4. Store newly imported draft images as immutable content-addressed blobs and
   keep package paths separate from Designer storage locators.
5. Add two Designer commands owned by `DesignerViewModel`. They call the
   session operations and publish `CanExecuteChanged` after undoable changes
   and image boundaries.
6. Publish `PropertyChanged(nameof(Current))` and synchronize unbound editor
   controls after meaningful changes.
7. Bind toolbar buttons and window input bindings to those commands.
8. Dispose/unsubscribe the commands with `DesignerViewModel`; no global input
   hook or process-wide state is introduced.

## Error and lifetime handling

- A command whose history direction is unavailable is a no-op through
  `CanExecute=false`.
- Undo/redo uses the already validated stored snapshots; it does not create a
  second validation or persistence path.
- Command notifications must tolerate Dispatcher shutdown and must not keep a
  closed Designer alive.
- Document replacement continues to create a fresh `SkinDraftSession`, so
  history cannot cross between two drafts.

## Verification

Tests are written before production changes and must first fail because the
UI commands/bindings do not exist.

- Session tests: initial disabled state, edit enables undo, undo enables redo,
  redo restores the edit, a branched edit disables redo, and an image history
  boundary disables both directions while preserving dirty state.
- View-model command tests: `CanExecute` transitions and the restored draft/
  preview values are exact.
- WPF layout tests: both named buttons exist, use Designer styling, expose the
  correct automation text, fit the minimum-width toolbar, and bind to the
  expected commands.
- Input-binding tests: `Ctrl+Z` and `Ctrl+Y` resolve to the same commands as
  the buttons.
- Image workflow tests: a successful replacement/removal establishes the
  history boundary; failed or cancelled mutations do not clear prior history.
- Persistence tests: replace/remove then Discard reopens the exact named JSON
  and bytes; Save reopens replacement bytes; recovery reopens its own immutable
  bytes; legacy drafts without a storage locator still open.
- Real-window tests: Undo/Redo refresh bound fields, image transforms, text
  selectors, and value labels without producing an extra revision.
- Focused Designer tests pass, followed by the full four-assembly serial
  Release suite and a zero-warning Release build.
- Rebuild Setup/ZIP as v1.3.0, rerun the applicable installer checks, upgrade
  the local installation, and directly test edit → undo → redo → save/reopen
  before resuming the remaining installed-smoke checks.

Remote push, `main` integration, tag, and GitHub Release remain blocked until
the user accepts the newly installed candidate.
