# Skin Designer Visual Fixes Design

**Date:** 2026-08-04  
**Status:** Approved in conversation; written review pending  
**Related design:** `2026-08-04-animation-controls-design.md`

## Goal

Make the skin designer's synthetic-preview controls readable and usable, and make the custom-skin breathing and glow animation channels visibly match their names before creators fine-tune their intensity.

This change does not alter the `.cqskin` schema or the meaning of existing animation intensity fields.

## Confirmed Problems

1. Percent values in the light `ComboBox` surface are rendered with the window's near-white `TextBlock` foreground, so selected values and popup items are unreadable.
2. The synthetic-preview strip packs display mode, two quota controls, three state toggles, and five placement actions into one row. At practical window widths it is cramped and loses visual hierarchy.
3. The breathing animation is technically active but only scales the center image from its base scale to at most `1.08x`, with a long intensity-dependent cycle. On semi-transparent photographic center assets it is too subtle to identify.
4. The channel named glow currently animates `PrimaryProgress.Opacity`. It does not animate a glow layer, so its visible behavior does not match its label.

## Considered Animation Approaches

### Selected: semantic animation layers

- Keep the quota number and label stable.
- Animate only the center image for breathing, using a visibly bounded scale range.
- Add a dedicated ring-shaped glow layer behind the primary quota ring and animate that layer's opacity.
- Keep decoration rotation and floating behavior independent.

This is selected because each control produces an immediately recognizable effect without making quota text harder to read.

### Rejected: amplify the existing properties

Increasing center scale and primary-ring opacity variation would be smaller code, but the glow control would still not create a glow. It would preserve the naming mismatch.

### Rejected: pulse the entire HUD

Scaling the complete HUD and pulsing all ring content would be obvious, but the quota number would move and the widget would feel unstable. It also makes the four channels less independent.

## Synthetic Preview Strip

The existing strip remains one bordered region but becomes two rows.

### First row: quota data

- Display mode appears first with a Chinese field label.
- Five-hour quota and weekly quota each receive a flexible-width group.
- Each quota group contains its label, slider, and preset selector.
- The two quota groups divide the remaining width evenly.

### Second row: state and placement

- The left side contains the three state toggles: details, animations, and refreshing.
- The right side contains the four edge-placement actions and the expand action.
- Existing commands and synthetic-preview semantics remain unchanged.

At the minimum supported window width, both rows must remain inside the strip without horizontal clipping. The strip may grow vertically; it must not add a horizontal scrollbar.

## ComboBox Readability

The window's foreground continues to provide the default light text color for dark surfaces. The implicit `TextBlock` style must no longer force a local foreground that overrides a child control's foreground inheritance.

`ComboBox` continues to use a light background and dark foreground. Both the closed selection presenter and opened popup items must inherit that dark foreground. Focus and selected-item visuals must retain readable contrast.

## Breathing Motion

- Target: center image transform only.
- Quota number, label, tracks, and progress arcs remain stationary.
- Intensity `0` creates no breathing animation track.
- For intensity `i`, the center scale runs from `baseScale * (1 - 0.04i)` to `baseScale * (1 + 0.12i)`.
- Each half-cycle lasts `2.4 - i` seconds. The `Gentle` preset therefore remains visible during a short preview, while `Noticeable` is unmistakable without becoming a rapid bounce.
- Stopping animations restores the exact configured center scale.

## Animated Glow

- Add a dedicated ellipse aligned to the primary quota ring, behind ring content and above the static base/background layers.
- Its stroke and shadow use `theme.glowColor`.
- Static material glow remains controlled by `theme.glowIntensity`.
- Animated glow visibility and motion amplitude are controlled by `theme.animation.glowIntensity`.
- Intensity `0` creates no glow animation track and leaves the dedicated animation layer invisible.
- For intensity `i`, the dedicated layer pulses from opacity `0.08` to `0.15 + 0.75i`, using the same `2.4 - i` second half-cycle as breathing. At the approved `Noticeable` value (`0.9`), the range is `0.08` to `0.825` and must be immediately visible against the current Soft Rose test skin.
- Stopping animations hides the dedicated animated layer and leaves the static theme appearance intact.

## Animation Control Relationship

The separate approved animation-controls design remains authoritative for presets and advanced controls:

- `静止`, `柔和`, and `明显` presets.
- `高级细调` collapsed by default.
- Decoration rotation and floating disabled when no decoration image exists.
- Manual adjustment marks the state as custom.

This visual fix supplies meaningful renderer behavior for the breathing and glow fields; it does not add new fields or timelines.

## Accessibility and Interaction

- Preserve a continuous, unique tab order after adding preset buttons and reflowing the synthetic controls.
- Every interactive control keeps a non-empty automation name.
- ComboBox text must satisfy readable foreground/background contrast in normal, focused, selected, and opened states.
- Preset selection, sliders, toggles, and placement commands remain keyboard-operable.

## Skin Designer Application Icon

The skin designer receives its own application icon instead of the generic Windows executable icon.

- The icon remains visibly related to the main HUD icon by retaining the cyan double-ring motif and dark high-contrast base.
- A compact design-tool mark, such as a brush or stylus, distinguishes the designer from the ordinary HUD application.
- The icon contains no text and must remain legible at 16, 24, and 32 pixels for the title bar and taskbar.
- The delivered `.ico` contains appropriate Windows icon sizes through 256 pixels.
- The designer project embeds the icon as its `ApplicationIcon`; the main designer window uses the same icon.
- Installed designer executables and any optional designer shortcuts inherit this dedicated icon. The ordinary HUD icon and ordinary-user shortcuts remain unchanged.

An identical copy of the HUD icon was rejected because users could not reliably distinguish the two running applications. A visually unrelated icon was rejected because it would weaken the shared product identity.

## Testing

Automated coverage must prove:

1. ComboBox selection and popup content resolve to a dark foreground on the light surface.
2. The synthetic strip contains two layout rows and stays within the minimum-width window.
3. Breathing targets only the center-image scale and uses the intended nontrivial range.
4. Glow targets the dedicated glow layer rather than `PrimaryProgress.Opacity`.
5. Disabling or hiding animations restores exact base transforms and hides the animated glow layer.
6. Zero intensities create no channel track.
7. Existing renderer, preview, import, and animation-precedence tests still pass.
8. The designer project and main window both reference the dedicated icon resource.

Manual installed-build verification must check:

- opened percentage preset lists are readable;
- the two-row strip is usable at normal and minimum window widths;
- `柔和` breathing and glow are visible;
- `明显` breathing and glow are unmistakable;
- quota text remains stable;
- global animation off stops all four channels cleanly.
- the designer title bar, taskbar button, executable, and optional installed shortcut show the dedicated designer icon at normal Windows scaling.

## Out of Scope

- `.cqskin` schema changes;
- keyframes, timelines, or per-layer animation editors;
- new animation channels;
- changes to built-in skin motion;
- redesigning the main editor, preview stage, or primary action bar outside the reported controls.
