# SpaceMouse input on Windows

Danslicer receives SpaceMouse reports through Windows Raw Input. The former `TDxInput.Device`
COM backend has been removed. `SixAxisSession` owns one receiver for all attached viewports;
only the current viewport consumes motion and buttons. Preferences can remain open while
tuning the current viewport.

`RawInputSpaceMouse` registers only Generic Desktop / Multi-axis Controller (usage 1/8).
A message-only window receives `WM_INPUT` and device arrival/removal notifications on the
Avalonia UI thread. It survives transfers between main and Support-editor windows. The
registration stays alive with no device attached, so reconnecting does not require restarting
the app. `IsConnected` describes registration health; the viewport indicator uses the number
of recognized devices. It cannot tell whether a wireless receiver's paired cap is awake.

Device metadata is cached. Motion packets update managed state; the viewport consumes the
latest sample on Avalonia animation ticks rather than an independent 15 ms timer. It does not
poll a driver or allocate COM wrappers. Normal frame intervals use elapsed-time scaling;
gaps over 45 ms resume with one 15 ms step instead of a catch-up jump. A 25 ms exponential
filter softens changes in deflection, with immediate stopping of each released axis and no
continued motion in the old direction on reversal. The deadzone is subtracted per axis to
avoid a step when crossing its threshold. This removes the COM call
path but cannot prevent a slow renderer from delaying the UI thread.

Accepted foreground HID batches now request a redraw immediately to wake the compositor.
The single animation loop still applies motion; the input callback never applies camera
movement itself. This avoids the measured roughly 100 ms idle animation-tick delays.

The driver must identify the application as Raw Input too. An old `SiOpenAppName`/S80
profile can fall back to keyboard/mouse emulation and turn Rx tilt into wheel zoom alongside
the native motion reports. The direct-executable physical trace captured 25 such wheel
events, each matching a camera-distance jump. `scripts/Set-SpaceMouseProfile.ps1` migrates
the existing per-user Danslicer.App.exe profile to RawInput and executable-based matching,
preserving device settings and creating a timestamped backup. It does not change other apps
or the driver's global configuration. Launch Danslicer.App.exe directly for profile matching.

The decoder supports separate translation/rotation reports, combined reports, padded button
bitmasks, and numbered short/long button reports. It checks report lengths and batch counts,
tracks button-down edges and keeps axes from different device handles separate. Translation
and rotation expire independently after 150 ms without updates; button traffic cannot keep
old motion alive. Removal clears the affected device and queued presses. Input from another
foreground process is discarded. Leaving the app while moving requires fresh neutral reports
before movement resumes. Existing viewport handoff protection also remains in place.

HID tabletop axes map to viewport axes as `(X, -Z, Y)`. Values are divided by the nominal
350-unit range, without clipping driver-scaled values. Camera rates convert back to nominal
units to retain the existing base rates. Saved sensitivity and inversion settings are unchanged;
the deadzone now consistently describes a fraction of nominal deflection. Driver profiles can
affect Raw Input too, so exact equivalence to previous COM sensitivity is not guaranteed.

## Verification

`RawSpaceMouseTests` covers decoding, batching, malformed reports, signed extremes, button
mapping/edges, stale samples, focus loss, device removal, and native Windows registration,
callback error containment, cleanup and recreation. `SixAxisSessionTests` covers retries,
exclusive ownership, neutral handoffs and timer jitter.

The real-app `--workspace-capture` run detected a SpaceMouse Pro (`046D:C62B`). All 16 workspace
checks passed, including preserving receiver identity through Support-editor open/close and
Preferences. Evidence is in `artifacts/spacemouse-raw-input/`. Physical cap motion and physical
unplug/replug were not exercised by this run.

The follow-up frame-pacing/smoothing change passed all 1,104 automated tests. Its native
workspace run (`artifacts/spacemouse-frame-smoothing/`) detected the Pro but stopped at the
workspace harness's unrelated `Settings host missing` assertion: the current UI replaces
the Guided popout while that assertion still expects 12 popouts. That run did not reach the
editor handoff checks.

The subsequent physical tilt test after the profile migration and HID wakeup change
(`artifacts/spacemouse-fixed-20260914-094214/`) recorded 1,670 HID reports, 776 camera
updates, zero wheel events (25 before migration), and zero dropped diagnostic records.
Frame intervals when applying motion were median 16.634 ms, p95 17.671 ms, maximum
33.489 ms. All intervals over 40 ms had neutral input. One distance/target reset occurred
during an input-neutral pause, consistent with reframing; its initiating UI action was
not logged. This run supports removal of the duplicate wheel zoom and active-motion
delay, but does not exercise unplug/replug. All 1,105 automated tests passed.

To check on hardware, use each translation and rotation axis, hold a steady deflection, release,
switch between main/editor/another app, and unplug/replug during motion. Confirm Fit, view and
rotation-lock buttons, then adjust sensitivity/inversion in Preferences if needed. Menu, numbered,
ISO, roll and modifier actions retain their previously unbound behaviour.

Set `DANSLICER_TRACE=1` to log device IDs, arrival/removal, HID motion-report counts and poll/render
timing. Native API failures are recorded in the application log even without this setting.

## References

- [Blender Windows input](https://github.com/blender/blender/blob/main/intern/ghost/intern/GHOST_SystemWin32.cc)
- [Blender NDOF motion and device maps](https://github.com/blender/blender/blob/main/intern/ghost/intern/GHOST_NDOFManager.cc)
- [Windows Raw Input registration](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawinputdevice)
- [Windows RAWHID batches](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-rawhid)
