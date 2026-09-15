# Panel update / CI automation audit

## Findings

### 1. CI trigger deletion race
`SetupCiSmokeAutomation` and `SetupCiNotificationsAutomation` observed trigger files with `File.Exists` and then immediately called `File.Delete`. On Windows, an unrelated handle can still hold the file without delete sharing, so the delete can raise `IOException` with a sharing violation. The trigger is now treated as an immutable one-shot request; the consumed marker is the authoritative hand-off signal and CI cleanup removes triggers between runs. This avoids turning a diagnostic/automation file operation into a race.

### 2. File-command ACK race
`ShowFileCommandDialog` previously sent the command before inserting its `(connection, command)` key into `pendingCommands`. A fast agent response could therefore be consumed by `HandleClient` before the sender-side polling state existed. Registration now occurs before the first socket write.

### 3. WinForms/DevExpress UI threading
Worker threads marshal UI work with `BeginInvoke`; grid selection/data operations remain on the form UI thread. DevExpress documents that controls should not be accessed from non-UI threads without proper `Invoke`/`BeginInvoke` coordination.

### 4. Catch ordering
`ObjectDisposedException` handlers precede `InvalidOperationException` handlers because `ObjectDisposedException` derives from `InvalidOperationException`.

## References
- Microsoft: `File.Delete` documents that deletion can fail when a file is in use.
- Microsoft: `FileShare` documents explicit sharing semantics for subsequent operations.
- DevExpress: WinForms controls require proper UI-thread marshaling for multithreaded access.
- DevExpress: `ColumnView.GetSelectedRows` / grid APIs are UI control APIs and should be used on the owning UI thread.
