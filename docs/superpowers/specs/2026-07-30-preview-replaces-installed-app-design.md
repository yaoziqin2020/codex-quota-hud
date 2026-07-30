# Preview Replaces Installed App Design

## Goal

Starting developer preview should first close a running installed
Codex Quota HUD, then open preview. This makes the existing reverse handoff
symmetrical: preview can already exit and open the installed app.

Only the installed executable at the project's standard current-user install
path may be force-closed. A development build, an unrelated process with the
same name, or an executable at another path must not be terminated.

## Startup flow

Normal and preview modes continue to share the existing single-instance mutex.
Preview performs the following sequence before composing any preview windows:

1. Try to acquire the single-instance mutex normally.
2. If acquisition succeeds, continue directly into preview.
3. If acquisition fails, signal a current-user named shutdown event.
4. Wait up to two seconds for a listening installed app to clean up and release
   the mutex.
5. If the mutex remains occupied, locate a running process whose normalized
   executable path exactly matches
   `%LOCALAPPDATA%\Programs\CodexQuotaHud\CodexQuotaHud.App.exe`.
6. Force-close only that exact installed process, wait for it to exit, and
   retry mutex acquisition.
7. Continue into preview only after the mutex is owned.

If no matching installed process can be found, process inspection is denied,
termination fails, or the mutex remains occupied, preview stops and displays a
clear error. It must not silently fail or broaden the process match.

## Graceful shutdown listener

After normal mode acquires the single-instance mutex, it starts a listener for
the current-user shutdown event. The listener waits off the WPF UI thread.
When signalled, it dispatches the existing `RequestExit` path onto the WPF
dispatcher.

This reuses the same orderly shutdown used by the tray exit command:
refresh coordination, app-server child process, monitor, tray icon, window,
view model, and mutex are cleaned up before the process exits.

Preview mode does not start this listener. Disposing normal mode stops the
listener without leaving a background wait or an owned event handle.

## Backward compatibility

The currently installed `v1.0.0` does not listen for the new shutdown event.
The exact-path process fallback exists specifically so the first preview launch
can replace that older installed build.

Once a listener-enabled build is installed, the normal path becomes graceful
shutdown. The force-close path remains a bounded compatibility and recovery
fallback.

## Components

`InstalledAppShutdownListener` owns the named event listener and invokes a
supplied shutdown callback. Its wait loop and disposal are independently
testable.

`InstalledAppShutdownCoordinator` owns the preview-side sequence: signal,
bounded wait, exact installed-path process lookup, termination fallback, and
mutex retry. Platform process operations and delays are injected behind a
small boundary for deterministic tests.

`App` selects launch mode before final single-instance handling. Normal mode
starts the listener after mutex acquisition. Preview delegates occupied-lock
recovery to the coordinator before constructing `PreviewComposition`.

The existing `InstalledAppLauncher` remains responsible only for the reverse
handoff from preview to installed mode.

## Error handling

All process objects and event handles are disposed. Per-process inspection
failures are contained and reported as a preview startup failure instead of
causing an unhandled exception.

The force-close timeout is bounded. Failure leaves preview closed and does not
attempt repeated termination. A user-facing message explains that the running
formal HUD could not be closed.

Normal startup behavior is unchanged when another instance owns the mutex:
the second normal launch exits without trying to replace the first instance.

## Verification

Test-driven implementation covers:

- preview entering immediately when the mutex is free;
- graceful event notification followed by successful mutex acquisition;
- exact installed-path fallback for a listener-incompatible installed build;
- path comparison being case-insensitive but otherwise exact;
- development and unrelated same-name processes never being terminated;
- termination, timeout, and access failures preventing preview startup;
- normal mode responding through the existing orderly exit callback;
- listener disposal releasing its background wait;
- normal launches retaining the existing single-instance behavior;
- reverse handoff from preview to installed mode remaining unchanged.

After focused tests, run the complete solution test suite and a Release build.
Manual acceptance should cover both directions:

1. Start installed mode, then use the developer preview shortcut and confirm
   installed mode closes before preview appears.
2. Use `退出预览并打开正式版` and confirm preview closes before installed mode
   reappears.

