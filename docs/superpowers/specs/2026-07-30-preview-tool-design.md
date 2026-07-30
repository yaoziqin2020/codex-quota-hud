# Codex Quota HUD Preview Tool Design

## Goal

Add an isolated developer preview mode that makes all quota display states
visually inspectable while the real five-hour quota is unavailable. The tool
must exercise the production HUD, details popup, skins, animations, tray, and
edge-docking behavior without connecting to `codex app-server` or changing the
user's real settings.

## Entry and isolation

Preview mode is entered only with:

```powershell
dotnet run --project .\src\CodexQuotaHud.App -- --preview
```

Normal launches remain unchanged. In preview mode the application:

- does not create `CodexProcessMonitor`, `RestartableQuotaClient`,
  `QuotaRefreshService`, or `CodexRunningCoordinator`;
- does not start or communicate with `codex app-server`;
- does not register the application for startup;
- uses an in-memory settings store and never writes the production settings
  file;
- continues to use the normal single-instance boundary so a preview and a
  production HUD cannot compete for the same desktop surface.

The preview entry is a developer capability. It is not exposed in the normal
HUD or tray menus, and packaged releases do not launch it by default.

## Architecture

The preview reuses `QuotaOrbViewModel` and `QuotaOrbWindow`. A
`PreviewQuotaRefreshController` implements `IQuotaRefreshController` and
publishes deterministic `QuotaRefreshState` values directly to the production
view model. An `InMemorySettingsStore` implements `ISettingsStore` for the
preview process.

`PreviewControlWindow` is a separate, non-topmost WPF window. It owns a small
preview session/controller that translates control changes into preview quota
states and window inspection commands. The HUD remains the production window;
the control window does not duplicate skin XAML or drawing logic.

Startup selects one of two composition roots:

- normal composition: the existing process monitor, app-server client,
  refresh service, settings store, HUD, and tray;
- preview composition: preview refresh controller, memory settings, HUD, tray,
  and preview control window.

Cleanup remains centralized in `App` and disposes only resources that were
created by the selected composition root.

## Preview states and controls

The initial preview state is:

- display mode: dual;
- five-hour remaining: 68%;
- weekly remaining: 34%;
- animations: enabled;
- refresh animation: idle;
- deterministic future reset times for both quota windows.

The control window provides:

- display state: dual, five-hour only, weekly only, or no quota;
- independent 0–100% controls for five-hour and weekly remaining quota;
- selection among all registered skins;
- animation enabled/disabled;
- idle/refreshing state;
- details popup open/close;
- force expanded;
- left, right, top, and bottom edge-preview commands.

The four display-state choices publish the same model shapes used by real
quota reads:

| Choice | Five-hour | Weekly | Expected display |
|---|---:|---:|---|
| Dual | Present | Present | Dual |
| Five-hour only | Present | Missing | Single, `5 小时` |
| Weekly only | Missing | Present | Single, `每周` |
| No quota | Missing | Missing | Hidden |

Changing a percentage or display-state control republishes a complete state.
The refreshing toggle changes only the animation flag and does not invent a
network operation.

## Window inspection behavior

The preview HUD preserves production dragging, click/double-click separation,
details placement, multi-monitor work-area selection, and delayed auto-hide.
Preview commands call narrow internal inspection methods on
`QuotaOrbWindow`; they do not fork the geometry implementation.

For edge inspection, each command moves the HUD to the corresponding external
edge of its nearest monitor and enters the same collapsed edge state used in
production. `Force expanded` returns the HUD to its production expanded
position. This allows immediate inspection while manual dragging remains
available for full multi-monitor acceptance.

Opening details uses the production details popup and its placement
calculation. The control window stays non-topmost so it does not obscure the
HUD during inspection.

When the no-quota state is selected, the HUD follows production behavior and
hides. The control window remains visible so another state can be selected.

Closing either the control window or the HUD requests an orderly exit from the
whole preview process.

## Data and settings safety

Preview data exists only in memory. It is visually synthetic and never
presented as a successful `app-server` read. Preview mode does not:

- save quota values or reset times;
- update `LastSuccessfulRefresh` in the production settings file;
- modify the user's selected skin, animation preference, or window position;
- read cookies, tokens, account details, or response bodies;
- open a network listener.

The preview control window is visibly titled `Codex Quota HUD — 开发预览` so
screenshots cannot easily be mistaken for a real quota reading.

## Error handling

Invalid percentages are clamped to `0..100` before state publication. Unknown
display-state or skin values are rejected by the preview controller.

Preview startup failures use the existing startup-failure cleanup path.
Because no external quota process exists in preview mode, closing the tool
cannot leave an app-server child process behind.

## Automated verification

Implementation follows test-driven development. Focused tests cover:

- `--preview` detection without changing `--background` semantics;
- normal startup registration remaining disabled in preview mode;
- all four preview choices producing the intended display state and labels;
- percentage clamping and deterministic reset data;
- refreshing and animation controls reaching the production skin state;
- preview settings remaining memory-only;
- skin selection using every registered production skin;
- preview window commands delegating to production popup and edge behavior;
- normal composition remaining unchanged.

After focused tests, run the complete solution test suite and Release build.
Packaging tests must continue to prove the normal install and release paths.

## Manual verification

Manual acceptance checks:

1. Launch with `--preview` while no real five-hour quota is available.
2. Inspect all five skins in dual, five-hour-only, and weekly-only modes.
3. Confirm the no-quota mode hides only the HUD and remains recoverable from
   the control window.
4. Inspect details content and placement for dual and both single modes.
5. Inspect idle, refreshing, and animations-disabled rendering.
6. Inspect left, right, top, and bottom collapsed edge bars for all five skins.
7. Drag across primary and secondary monitors with different DPI when
   available.
8. Close the tool, start the normal application, and confirm its prior skin,
   position, and animation setting were not changed.

Real `codex app-server` dual-window acceptance remains required when the
five-hour quota becomes available again. The preview validates rendering and
interaction, not the availability of upstream quota data.

## Out of scope

- Adding or redesigning a skin.
- Changing quota mapping or refresh cadence.
- Shipping a user-facing simulation mode.
- Persisting preview presets.
- Replacing real dual-window acceptance with synthetic data.
