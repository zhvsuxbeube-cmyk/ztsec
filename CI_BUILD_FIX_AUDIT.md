# CI Build Fix Audit

This revision corrects the Windows CI compiler failures found in the panel and agent sources.

## Panel

- `GridView` does not expose the WinForms `Control.IsDisposed` property. The supported DevExpress view lifecycle member is `IsDisposing`; the associated `GridControl` remains a WinForms `Control` and can use `IsDisposed`/`Disposing`.
- Async command state (`resultKey`, `pendingId`, and `isUpdateCommand`) is initialized before the worker `try` so the exception handler can reference it safely.
- A completed command result is not erased by a later worker exception when the receive thread has already retired the matching pending generation.
- CI dialog rendering uses two queued UI turns before publishing `Rendered=True`; the workflow then waits a short compositor-settling interval before capture.

## Agent

- `getrandom` is pinned to 0.2.17 and the matching `getrandom::getrandom` API is used.
- `UpdateHandoff::wait_admission` is `pub(crate)` so the networking layer can invoke it.
- The session passes its actual `port` through to `spawn_successor`.

Verified locally: source/contract tests, Python syntax, workflow YAML parsing, and archive integrity. Full Visual Studio/MSBuild and Windows Cargo/E2E execution must be performed on the Windows runner.
