# CI Administration Dialog Automation — Forensic Fix

## Failure evidence

The GitHub Actions run successfully captured `gui-screenshot-1.png`, then timed out waiting for `ci-administration-dialog-opened.flag`.
The diagnostic explicitly reported:

`trigger still present (CI UI timer did not consume it)`

This proves the failure occurred **before** `ShowAdministrationDialog("Download [ One ]")`: the workflow's filesystem trigger was not being consumed by the application automation hook.

## Root cause

`SetupCiSmokeAutomation()` previously depended on `System.Windows.Forms.Timer.Tick` to observe `ci-open-download-one.flag`.
That creates an unnecessary UI-thread dependency for cross-process CI signaling. The timer had no independent readiness/consumption handshake, so the workflow could only infer failure from the trigger file remaining in place.

## Remediation

The CI-only hook now uses a small background `Thread` to poll the application's own `AppDomain.CurrentDomain.BaseDirectory` for the trigger. Once found, it deletes the trigger, records a consumption marker, and marshals the **real** dialog invocation to the WinForms UI thread with `BeginInvoke`.

The workflow now has an explicit handshake:

1. Wait for `ci-administration-automation-ready.flag` from the running EXE.
2. Create `ci-open-download-one.flag` in the same directory.
3. Wait for `ci-open-download-one-consumed.flag`.
4. Wait for the existing `ci-administration-dialog-opened.flag` raised by the dialog's real `Shown` event.
5. Fail with the automation error file and path information if any step breaks.

The previous `System.Windows.Forms.Timer` implementation is removed rather than layered underneath another override.

## Verification performed here

- `Form1.cs` has balanced `{}` and `()` delimiters.
- No references to the removed `ciSmokeAutomationTimer` remain.
- The updated workflow references the same output directory as the EXE and now validates the runtime automation handshake.
- The repository was re-packed after the change.

A Windows/DevExpress GUI build cannot be executed in this Linux environment, so the next GitHub Actions run is the required runtime verification. The workflow is intentionally fail-closed if the handshake or real dialog `Shown` marker does not occur.

Live web research of DevExpress documentation could not be performed in this session because external web access is disabled. This fix is based on the actual project source, the reported CI failure, and WinForms threading/message-loop behavior visible in the codebase.
