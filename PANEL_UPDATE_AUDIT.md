# Panel Update/Remote Command Audit

Date: 2026-09-15

## Findings fixed

### 1. C# definite-assignment failure in `Form1.cs`

The remote file-command dialog previously declared `updateRow` and referenced `updateRow` from inside its own lambda before definite assignment. MSBuild correctly rejected this as CS0165. The UI update operation is now a separate `UpdateFileCommandRow` method, so the worker delegate does not recursively reference an unassigned local.

### 2. Pending-command registration race

The panel previously wrote the command to the `NetworkStream` and only then inserted the request into `pendingCommands`. A fast agent can acknowledge immediately after the final request byte arrives, so the server reader could consume the ACK before the pending entry existed. File commands now register their result state before any request bytes are written. Normal commands use the same ordering.

### 3. Stale watchdog race

Pending commands are keyed by connection and command. A previous timeout task could remove a newer request that reused the same key. Watchdog expiry now compares the exact `PendingCommand` instance before removing it.

### 4. Successful-result retention

`CompletePendingCommand` previously retained successful results for every command, even commands whose caller never polled `completedCommandResults`. File-command requests explicitly opt into result retention; ordinary commands do not.

### 5. Worker/UI shutdown race

The dialog's worker tasks can outlive the dialog. UI updates are now marshalled with `BeginInvoke` only when a handle exists, and disposal/handle-shutdown exceptions are handled. The update operation itself never recursively invokes the worker delegate.

### 6. Selection-state concurrency

`selectedConnectionIds` was modified by the client-handler worker while also being manipulated by UI selection code. Access is now guarded by `connectionStateLock`, and UI code snapshots the prior selection before requesting telemetry refreshes.

## API research applied

Microsoft WinForms documentation states that controls are thread-affine and cross-thread control operations must be marshalled; `Invoke` and `BeginInvoke` are the supported mechanisms. `BeginInvoke` can fail when no suitable handle exists or while the owning UI thread is no longer processing messages, so the update helper checks handle/disposal state and handles the shutdown race.

DevExpress documentation confirms that `GridView.GetSelectedRows()` returns row handles for the active data View. The panel obtains selected connection IDs from the `GridView` on the UI thread before launching worker tasks; it does not read grid controls from the worker threads.

## Verification expectations

The Windows CI build remains authoritative for MSBuild/DevExpress compilation because this environment does not contain the Visual Studio/MSBuild toolchain. The repository now runs a Python source-contract regression before the Windows build, while the normal CI `msbuild /restore /t:Rebuild` remains the actual compiler check.
