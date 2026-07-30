# Preview Exit and Open Installed App Design

## Goal

Add one control to developer preview mode that exits preview cleanly and then
opens the installed normal Codex Quota HUD.

## User experience

The preview control window adds a primary button at the bottom:

`退出预览并打开正式版`

The installed executable is resolved from the current user's standard install
path:

```text
%LOCALAPPDATA%\Programs\CodexQuotaHud\CodexQuotaHud.App.exe
```

When the executable exists, clicking the button requests a one-time handoff.
When it is missing, the button is disabled and the control window displays:

`未找到已安装正式版`

Normal window close, tray exit, and non-preview application exit do not start
another process.

## Lifecycle ordering

The preview process must not start the installed application from the button
handler. At that moment the preview still owns the single-instance mutex, so
the installed application would immediately exit.

The button handler records a one-time `open installed after exit` request and
then invokes the existing orderly exit path. `App.OnExit` performs all normal
cleanup first, including preview windows, tray resources, and
`SingleInstanceGuard`. Only after that cleanup completes does it start the
installed executable without `--preview`.

This uses lifecycle ordering rather than a fixed delay or helper process.

## Components

`InstalledAppLauncher` owns:

- resolving the installed executable path from Local AppData;
- reporting whether the installed executable is available;
- starting that exact absolute executable with no preview arguments.

The implementation accepts path and process-start delegates internally so
tests use controlled paths and do not start a real application.

`PreviewComposition` exposes a one-time `OpenInstalledRequested` event. The
control window raises a session action, the composition forwards it, and
`App` stores the pending request before calling `RequestExit`.

`App.OnExit` disposes the single-instance guard before calling the launcher.
The pending request is consumed with an atomic one-time guard.

## Error handling

If the installed executable is absent, the control is disabled and no exit is
requested.

If the file disappears after the button is enabled, or process start fails,
the preview still exits normally. The launch exception is caught and written
to `Trace` without restarting preview, showing a native alert, or leaving
cleanup incomplete.

Repeated clicks or repeated exit callbacks can request at most one installed
application launch.

## Verification

Test-driven implementation covers:

- exact installed path resolution under Local App Data;
- availability false for a missing executable;
- process start receives the exact absolute installed path and no arguments;
- disabled UI state and missing-install message;
- clicking the enabled button raises one handoff request and requests exit;
- repeated clicks do not duplicate the handoff;
- ordinary preview close does not request installed launch;
- `App` starts the installed application only after the cleanup action that
  releases the single-instance guard;
- launch failure is contained after cleanup.

Run the focused tests, then the complete 253-test suite and Release build.
The installed `v1.0.0` files, startup registration, release tag, and release
assets are not modified.

## Manual acceptance

1. Exit the currently running installed HUD.
2. Start developer preview from its desktop shortcut.
3. Click `退出预览并打开正式版`.
4. Confirm preview and its control window close.
5. Confirm the installed normal HUD starts without a command window.
6. Confirm the normal HUD reads real data and no preview control window
   remains.
