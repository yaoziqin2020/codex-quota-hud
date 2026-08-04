# Themed Designer Dialogs and Refresh Animation Timing

Date: 2026-08-04
Target release: v1.2.3
Status: approved design, pending implementation plan

## Objective

Correct two user-visible gaps without expanding the skin system into a general
animation-programming environment:

1. Keep the Skin Designer visually coherent while modal windows are open and
   replace its Windows-default message boxes with Designer-themed dialogs.
2. Make refresh animation timing visible and useful by retaining the accelerated
   state for 1.5 seconds after a refresh completes, while allowing custom-skin
   authors to configure the refresh speed multiplier and completion hold time.

## Scope

### Included

- A complete Designer button template for normal, hover, pressed, focused,
  disabled, and default-action states.
- Designer-themed confirmation, collision, unsaved-changes, information,
  warning, and failure dialogs.
- Native Windows file-open and file-save pickers remain native.
- A global 1.5-second post-refresh hold for all five built-in skins.
- Refresh speed and hold settings for custom skins.
- Backward-compatible defaults for custom skins that predate the new fields.
- Live Designer preview of the refresh and post-refresh-hold phases.
- Serialization, validation, runtime, Designer, and regression tests.

### Excluded

- A custom file browser.
- Arbitrary keyframes, animation scripts, new animation types, or per-effect
  refresh overrides.
- Changes to the four existing animation intensities.
- Changes to quota fetching, retry policy, refresh frequency, or quota data.

## Designer visual system

### Buttons

The Designer will stop relying on the platform `Button` control template. Its
shared button style will provide a Designer-owned `ControlTemplate` with these
states:

- Normal: dark raised surface with the existing border and text colors.
- Hover: brighter border and raised surface.
- Pressed: accent-tinted surface with a small visual depression.
- Focused/default: existing cyan focus treatment remains visible.
- Disabled: the same dark surface and border at reduced opacity; it must not
  switch to a white Windows-default button when the owner window is disabled by
  a modal file picker or dialog.

The template applies to every Designer button, including header actions,
editor actions, preview controls, and footer output actions.

### Designer-owned dialogs

A reusable modal WPF dialog will use the same background, surface, text, muted
text, border, accent, focus, and button resources as the Designer. It supports:

- title and message;
- information, warning, error, and question icon treatments;
- one, two, or three actions;
- explicit default and cancel actions;
- owner centering, keyboard focus, Enter, Escape, and window-close behavior;
- accessible names for the dialog, message, and each action.

All Designer-owned message paths will use this component:

- unsaved draft: Save / Discard / Keep editing;
- export replacement confirmation;
- installed-skin collision: Replace / Keep copy / Cancel;
- successful output result;
- warning and failure result;
- other internal Designer notices currently implemented with `MessageBox`.

The file-open and file-save pickers remain native Windows dialogs. While they
are modal, the Designer owner is disabled, but its controls retain the dark
disabled appearance defined above.

## Refresh animation model

### User-facing settings

The existing four animation intensities remain unchanged. A separate
“刷新状态” group in the Animation editor exposes:

- Refresh speed multiplier: range 1.0–4.0, default 2.0.
- Completion hold: range 0.0–3.0 seconds, default 1.5 seconds.

The controls display their current numeric values. The existing “刷新中”
synthetic-preview checkbox remains the direct preview trigger:

- checking it enters accelerated refresh animation immediately;
- unchecking it simulates request completion and retains acceleration for the
  configured hold time;
- after the hold, preview animation returns to ordinary speed.

### Contract and compatibility

`SkinAnimationSettings` gains two values represented in `theme.json` as:

```json
{
  "animation": {
    "rotationIntensity": 0.45,
    "breathingIntensity": 0.45,
    "glowIntensity": 0.55,
    "floatingIntensity": 0.15,
    "refreshSpeedMultiplier": 2.0,
    "refreshHoldSeconds": 1.5
  }
}
```

Validation accepts `refreshSpeedMultiplier` from 1.0 through 4.0 and
`refreshHoldSeconds` from 0.0 through 3.0. Non-finite values are rejected.

When either field is absent, the reader supplies 2.0 and 1.5 respectively.
Therefore existing installed skins and old `.cqskin` files receive the newly
approved 1.5-second hold after the HUD upgrade. New drafts use the same values.
The writer emits both fields for newly saved, applied, or exported skins.

Packages written with the new fields declare HUD v1.2.3 as their minimum.
Older packages remain readable; no archive layout or asset boundary changes.
HUD versions that predate v1.2.3 are not expected to accept packages containing
the new strict-JSON properties.

### Runtime state machine

The runtime distinguishes requested refresh state from effective animation
state:

```text
Idle -> Refreshing -> Hold -> Idle
             ^          |
             +----------+  a new refresh restarts refresh/hold timing
```

- Entering `Refreshing` cancels any prior hold and applies the configured speed
  multiplier once. Multipliers never stack.
- When the request stops refreshing, the effective state remains refreshing for
  the configured hold duration.
- The hold starts after request completion. Even when a cached or fast request
  finishes in a fraction of a second, the default accelerated presentation
  therefore remains visible for the full additional 1.5 seconds; it cannot
  collapse into the current sub-second flash.
- When the hold expires, animation returns smoothly to ordinary speed.
- A new refresh during the hold cancels the pending return and begins a fresh
  refresh cycle.
- Disabling animation, hiding the HUD, disposing the window, or switching skin
  cancels the hold immediately and leaves no background timer or animation
  clock attached to the old target.
- A zero-second hold restores idle immediately.

All five built-in skins use a fixed 2.0 multiplier and the global 1.5-second
hold. Custom skins use their package values. The refresh timing changes only
presentation; it never delays quota-result publication or changes network work.

The hold coordinator uses cancellation and an injectable delay/clock boundary
so a stale completion cannot restore a newly selected skin or a newer refresh
cycle. UI state changes are marshalled to the owning Dispatcher.

## Data flow

1. The Designer edits two numeric fields in the current draft.
2. Draft validation and preview update run through the existing single mutation
   path.
3. Save/apply/export serializes both fields into `theme.json` and records HUD
   v1.2.3 compatibility.
4. The HUD reader supplies defaults for old packages and validates explicit
   values for new packages.
5. The selected skin exposes its refresh timing to the shared animation-state
   coordinator.
6. Quota refresh state drives `Refreshing`; completion starts the hold without
   blocking quota presentation.
7. The effective state is applied to the built-in or custom renderer.

## Error handling

- Invalid refresh values produce the same field-specific validation errors as
  the existing animation settings and cannot be saved, applied, or exported.
- Dialog owner loss falls back to a centered Designer dialog on the active
  Dispatcher; it does not call a native `MessageBox`.
- Closing a themed dialog maps to its explicit cancel action.
- Dispatcher shutdown or window disposal cancels refresh holds without showing
  an error.
- File-picker cancellation remains a normal no-op.

## Verification

Automated coverage will prove:

- every Designer `Button` resolves to the custom template and stays dark while
  disabled by a modal owner state;
- hover, pressed, focus, disabled, default, and cancel dialog behavior;
- all former Designer `MessageBox` call sites use the themed dialog service;
- native file pickers remain the only approved native dialogs;
- old JSON without the two fields reads as 2.0 and 1.5;
- explicit boundary values round-trip and invalid values are rejected;
- new drafts, saved drafts, apply, export, and package readback preserve values;
- built-in and custom refresh state follows Idle -> Refreshing -> Hold -> Idle;
- zero hold, repeated refresh, animation disable, hide, skin switch, and dispose
  cancel or restart timing correctly without multiplier stacking;
- Designer synthetic preview visibly enters refresh, retains it after uncheck,
  and returns to idle using an injectable clock;
- full Core, Skins, App/UI, and Designer Release suites and solution build pass.

Manual acceptance will open a native file picker and confirm that the disabled
Designer buttons remain dark. It will also exercise each themed dialog shape
and visually verify a 2.0× refresh followed by a 1.5-second hold on one built-in
skin and one custom skin. A deliberately fast refresh must still show the full
additional 1.5-second accelerated phase before returning smoothly to idle.

## Release boundary

This is a v1.2.3 behavior and UI correction. It requires rebuilding and
reinstalling the HUD and optional Designer, updating release documentation, and
producing new immutable v1.2.3 assets. Existing v1.2.0, v1.2.1, and v1.2.2
tags and Release assets remain unchanged.
