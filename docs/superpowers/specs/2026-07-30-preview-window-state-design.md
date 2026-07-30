# Preview Control Window State Design

## Goal

Make the developer preview control window open large enough to show its normal
content without an initial scrollbar, and remember the user's last valid size
and position across preview sessions.

## Root cause

The control window is fixed at `360 × 560` while the recently added installed
app handoff button extends the content beyond that height. Its root is a
`ScrollViewer`, so WPF correctly displays a vertical scrollbar.

Preview currently uses only `InMemorySettingsStore`, by design, so resizing or
moving the control window cannot persist across processes.

## Default layout

The default control-window size becomes approximately `380 × 650`, with
minimum size `340 × 520`. At standard DPI this fits the current controls
without a vertical scrollbar.

The existing `ScrollViewer` remains with automatic vertical scrolling. It is a
safety fallback for small work areas, high DPI, accessibility scaling, or
future content growth.

## Independent persistence

Preview control-window geometry is stored separately at:

```text
%LOCALAPPDATA%\CodexQuotaHud\preview-window.json
```

The file contains only:

```json
{
  "Left": 100,
  "Top": 100,
  "Width": 380,
  "Height": 650
}
```

It does not contain simulated quota values, selected skin, animation state,
refresh state, reset times, or real HUD settings.

The normal `%LOCALAPPDATA%\CodexQuotaHud\settings.json` contract is unchanged.

## Validation and screen recovery

Loaded geometry is accepted only when:

- width and height are finite;
- width is at least `340`;
- height is at least `520`;
- position values are finite.

Invalid, missing, or malformed data falls back to defaults without preventing
preview startup.

Before applying a saved position, geometry is clamped to the nearest current
monitor work area using the project's existing window-positioning logic. This
recovers safely after monitor removal, resolution changes, or DPI changes.

If the current work area is smaller than the saved/default size, the window is
reduced to fit while respecting minimum dimensions where the work area allows
it. Scrolling then keeps every control reachable.

## Save behavior

The window state is saved after user-driven move or resize settles, and once
more during orderly close. Writes use a short debounce so drag/resize does not
write on every pixel.

Saving is atomic through a temporary file and replacement/move in the same
directory. Directory creation and file write failures are contained and
traced; they do not break preview use or exit.

Programmatic startup placement does not overwrite the loaded state before the
window is shown.

## Components

`PreviewWindowState` is the validated geometry record.

`PreviewWindowStateStore` owns the isolated path, tolerant JSON load, and
atomic save.

`PreviewControlWindow` loads state before display, clamps it against current
work areas, applies it, and schedules saves after `LocationChanged` and
`SizeChanged`.

`PreviewComposition` supplies the store to the window. No production HUD
view-model or normal settings type is extended.

## Verification

Test-driven implementation covers:

- exact independent Local App Data path;
- default `380 × 650` state;
- save and reload of finite valid geometry;
- malformed JSON, missing fields, non-finite values, and undersized values
  falling back safely;
- control window applying saved geometry;
- move/resize state reaching the preview-only store;
- existing WPF controls remaining reachable;
- normal settings file remaining untouched.

Run focused tests, then the complete 260-test suite and Release build. Manual
acceptance checks initial scrollbar absence at standard DPI, resize/position
restoration, and small-screen/high-DPI scroll fallback.
