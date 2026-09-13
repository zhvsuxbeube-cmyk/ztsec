# Validation report

Date: 2026-09-10

## Python connector

- Python syntax compilation: passed with `py_compile`.
- CLI parsing: `--ip`, `--port`, and `--9` validated.
- Telemetry schema: 15 pipe-delimited fields validated.
- Multiple connections: 3 simultaneous connections accepted by a local mock TCP server.
- Reconnect behavior: verified after the server dropped the socket; the same connection tag reconnected.
- External Python packages: none.

## C# source checks

- Metrics dynamic page and sidebar code removed from `Form1.cs` and `Form1.Designer.cs`.
- C# brace/lexical structural check passed.
- `Disk` connection column and 15-field telemetry parser are present.
- Connections grid multi-row selection is enabled.
- Right-clicking an unselected row selects it while preserving an existing selection when the row is already selected.
- Listener binding is synchronous; the UI only reports the listener as running after the OS bind succeeds.
- Listening loop uses blocking `AcceptTcpClient`, and `Stop()` unblocks it cleanly.
- The prior ambiguous `ProgressBar` declaration was made explicitly `System.Windows.Forms.ProgressBar`.

The DevExpress/.NET Framework build itself could not be executed in this environment because the uploaded source archive does not contain the referenced DevExpress DLLs and no Visual Studio/MSBuild toolchain is installed here.
