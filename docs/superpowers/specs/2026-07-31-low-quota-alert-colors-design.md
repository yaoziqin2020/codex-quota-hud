# Low-Quota Alert Colors Design

## Goal

Make low remaining quota recognizable at a glance without replacing each
skin's visual identity or adding disruptive notifications.

The same semantic alert state must be used by the floating HUD, collapsed edge
bar, tray icon, and detail rows. In dual mode, the five-hour and weekly quotas
are evaluated independently.

## Alert levels

One shared policy classifies every finite, normalized remaining percentage:

| Remaining quota | Alert level | Presentation |
| --- | --- | --- |
| Greater than `20%` | `Normal` | Existing skin-specific colors |
| Greater than `10%` and at most `20%` | `Warning` | Amber |
| At most `10%` | `Critical` | Red |

Exact boundary behavior is therefore:

- `20.1%` is normal;
- `20%` is warning;
- `10.1%` is warning;
- `10%` is critical;
- `0%` is critical.

Missing quota is not an alert state. Existing single-mode and hidden-mode
selection remains authoritative: unavailable data does not become a red
`0%`.

Thresholds are fixed product behavior in this change. No settings, registry
values, JSON fields, or user-editable thresholds are added.

## Shared semantic model

Add `QuotaAlertLevel` with `Normal`, `Warning`, and `Critical`.

Add one policy boundary that accepts a remaining percentage and returns the
level. It normalizes non-finite and out-of-range values consistently with the
existing quota display state, while callers remain responsible for deciding
whether quota data exists.

Add a shared presentation palette for:

- WPF brushes used by skins, edge bars, and details;
- `System.Drawing.Color` used by the tray icon.

`Normal` does not resolve to one global blue. It tells each surface to retain
its current skin-specific color. `Warning` and `Critical` resolve to common,
accessible semantic hues so the same urgency is recognizable across all five
skins.

The shared semantic colors are:

- warning amber: `#FFFFB547`;
- critical red: `#FFFF5A67`.

WPF and tray rendering use the same RGB values. Both retain sufficient
contrast against the existing dark backgrounds.

## Floating HUD skins

Every skin continues to receive one `QuotaSkinState`. The state exposes
independently derived primary and secondary alert levels so individual skins
do not repeat threshold logic.

Each skin changes only quota-bearing elements:

- `HudDial`: primary arc and central percentage; secondary arc independently.
- `EnergyRing`: primary arc and central percentage; secondary arc
  independently.
- `LiquidGlass`: primary arc/fluid accent and central percentage; secondary
  arc independently.
- `Aurora`: primary arc and central percentage; secondary arc independently.
- `LiquidTank`: liquid fill/surface accent and central percentage; secondary
  outer arc independently.

Frames, tracks, labels, decorations, backgrounds, and ambient animation keep
their skin colors. The whole orb is never globally tinted.

Color changes are immediate state updates. They do not flash, pulse, change
rotation speed, or create a new animation clock. Existing idle, refreshing,
hidden, and animations-disabled behavior is unchanged.

In single mode, the only available quota is primary and drives the central
number and primary visual. In dual mode, primary and secondary colors are
independent; a critical weekly quota must not turn a healthy five-hour quota
red, and vice versa.

## Other surfaces

### Collapsed edge bar

The collapsed edge bar displays primary quota only. Its fill, outline accent,
and glow use the primary alert level. Normal mode retains the selected skin's
existing edge theme and material texture.

### Tray icon

The tray icon displays primary quota only. Its progress ring uses the primary
alert level; warning and critical override the normal skin accent. The numeric
text and background remain unchanged for legibility.

### Detail rows

Each available quota row carries its own numeric remaining percentage and
alert level. Only that row's remaining-percentage text uses the semantic
warning or critical color. Labels, reset time, and update metadata retain the
popup theme.

This requires detail presentation to retain a numeric percentage rather than
trying to parse the formatted `"20%"` string in XAML or code-behind.

## Data flow

1. Existing quota mapping produces remaining percentages and availability.
2. Existing display selection chooses dual, single, or hidden mode.
3. The shared alert policy classifies each available percentage.
4. `QuotaSkinState` carries primary and optional secondary alert levels.
5. Skin renderers apply the levels only to quota-bearing elements.
6. Edge, tray, and detail presenters use the same policy rather than
   duplicating comparisons.

No refresh, app-server, persistence, single-instance, preview-handoff, or
window-positioning behavior changes.

## Preview behavior

The existing developer-preview percentage sliders remain the manual visual
test tool. Moving a slider across `20%` and `10%` must immediately update every
currently visible affected surface.

Dual preview verifies mixed states such as:

- primary normal, secondary warning;
- primary critical, secondary normal;
- both warning;
- both critical.

No new preview control is required.

## Error handling and accessibility

- Percentages are normalized before classification.
- Missing values never become fabricated critical values.
- Color is supplemental: the numeric percentage, arc length, water level, and
  detail labels continue to communicate state without relying on color alone.
- No sound, toast, modal dialog, flashing, or automatic refresh is triggered
  by crossing a threshold.

## Verification

Test-driven implementation covers:

- policy boundaries at `20.1`, `20`, `10.1`, `10`, and `0`;
- normalization of negative, over-100, and non-finite inputs;
- primary and secondary levels remaining independent in dual mode;
- all five skins applying warning and critical colors only to quota-bearing
  elements;
- normal levels preserving each skin's current colors;
- single mode using the only available quota;
- LiquidTank liquid accents following the primary level;
- edge-bar alert overrides while preserving normal skin themes;
- tray-ring alert overrides with unchanged text/background;
- detail rows using their own numeric percentages and levels;
- hidden and missing-data behavior remaining unchanged;
- preview slider state propagating through production render paths.

Run focused policy and UI tests, then the complete solution test suite and a
Release build. Manual acceptance uses the preview sliders for every skin in
single and dual modes, including mixed primary/secondary levels.

## Documentation

Update `README.md`, `CURRENT_TASK.md`, `PROJECT_CONTEXT.md`, and
`CHANGELOG_AI.md` with the final behavior and verified test totals. Correct the
stale pre-fix test baseline in `PROJECT_CONTEXT.md` as part of this
documentation update.
