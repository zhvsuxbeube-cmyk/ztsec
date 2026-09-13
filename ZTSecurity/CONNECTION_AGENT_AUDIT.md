# ZTSecurity Connection/Agent Audit

## Changes

- Removed `Logs Checker / Parser` from the XtraTab page collection and its sidebar navigation.
- Connection grid uses a dedicated `DataTable` and a hidden `ConnectionId` key so each TCP session is tracked independently, including multiple sessions from one IP.
- Added visible `HWID` as the final connection column.
- Connection telemetry parser now accepts the 15-field telemetry record emitted by `ztsec_agent.py`.
- Connection rows are inserted/updated on the UI thread and removed when their exact TCP session closes.
- Routine TCP disconnect exceptions (`IOException`, `ObjectDisposedException`, `SocketException`) are treated as normal disconnect paths and are not logged as errors.
- Listener binding now occurs synchronously in `StartServer`, so the UI is not told that a port is listening until the socket is actually bound.
- DevExpress Find Panel is configured for all visible connection columns with `Contains` matching and Enter-to-search behavior.
- Right-click on an unselected connection selects that row; right-clicking an already-selected row preserves the multi-selection.
- Selected rows receive a full-row highlight instead of the default focused-cell appearance.
- `con.py` was renamed to `ztsec_agent.py` because `CON` is a reserved Windows device name and breaks Windows Git checkout.
- Agent supports `--n N` and `--N N` as equivalent connection-count arguments.
- Agent default is `--ip 127.0.0.1` and `--port 4793`.
- Agent maintains persistent TCP sessions, sends one telemetry snapshot, then lightweight `HB` heartbeats, and reconnects with bounded exponential backoff.
- Agent supplies GPU name(s) using the Windows `Win32_VideoController` WMI class through built-in Windows PowerShell, with a `Get-WmiObject` compatibility fallback.
- Agent supplies registered antivirus product name(s) from Windows Security Center's `AntiVirusProduct` provider through PowerShell, with a WMI compatibility fallback and Defender fallback.
- Agent supplies a Windows MachineGuid-based HWID via Python's standard-library `winreg`.

## Telemetry record

The line-oriented protocol is:

`DATA:Country|Nickname|Tag|UserName|Version|Privileges|OS|GPU|CPU|RAM|AntiVirus|Uptime|AFKTime|Ping|HWID`

Heartbeats:

`HB`

The panel uses the TCP peer address as the authoritative IP shown in the Connections grid.

## Verification

- `ztsec_agent.py` parsed successfully with Python AST and `py_compile`.
- CLI help executes successfully.
- `--n` and `--N` are both accepted by argparse.
- Generated telemetry contains exactly 15 fields.
- A local socket integration test accepted two simultaneous telemetry connections and confirmed both 15-field `DATA:` records.
- C# brace balance was checked after edits.
- Full DevExpress/.NET Framework compilation could not be performed in this Linux environment because the repository targets .NET Framework 4.7.2 and depends on the included DevExpress desktop assemblies/toolchain that are not executable here.

## Windows API research

GPU collection uses `Win32_VideoController`; Microsoft documents it as the WMI class representing video controller hardware/capabilities.

Antivirus collection was aligned with Microsoft's Windows Security Center API model. Microsoft's Windows Classic Samples include `WscApiSample.cpp`, which enumerates security products through `IWSCProductList`/`IWscProduct`. The Python connector remains dependency-free and uses built-in PowerShell/WMI access rather than third-party packages.

## Selection-refresh protocol

`REQ:DATA` is a server-to-agent control line. The agent answers with `PONG` immediately, then emits a fresh `DATA:` snapshot. This keeps Ping measurement independent of potentially slower WMI/PowerShell collection.

The initial `DATA:` line is still sent immediately when a TCP session is established. After that, `HB` is liveness-only; it never mutates the grid's dynamic values. The panel only accepts RAM/Uptime/AFK/Ping refreshes when a matching selection-generated request is pending.

## Follow-up connection correctness fixes

- Arbitrary/non-ZTSecurity lines arriving on the listener are now ignored silently rather than logged as "unsupported client messages". This prevents noisy false-positive warnings from localhost port probes or unrelated local traffic.
- The server no longer uses `TcpClient.Connected` as the read-loop condition. TCP EOF (`ReadLine() == null`) and socket exceptions while the server is running are used instead, avoiding the cached-state behavior that can make a healthy session appear disconnected.
- Client connected/disconnected logs are emitted only after a valid 15-field `DATA:` record has been accepted, so unknown traffic cannot generate client lifecycle false positives.
- Server shutdown is not reported as a client disconnect. Session cleanup still removes the exact connection row and closes the exact socket.
- The agent keeps a persistent TCP session and sends `HB` heartbeats only for liveness. Dynamic telemetry is not refreshed by heartbeats.
- Selecting a connection now sends `REQ:DATA`. The agent immediately replies with `PONG`, then collects and returns a fresh telemetry snapshot. The panel measures Ping from the request/PONG round trip.
- Existing rows retain static identity/configuration fields. RAM, Uptime, AFK Time, and Ping are refreshed only after an explicit selection-triggered request.
- An explicit selected-row toggle was added: clicking an already-selected row unselects it; selecting a new row still requests fresh telemetry.
- The connections grid uses a dedicated collapsed search icon at the right side of a separate search header. Clicking it swaps that header to a constrained standalone DevExpress `SearchControl`; the grid's embedded Find Panel remains disabled.
- The horizontal scrollbar is explicitly configured as `ScrollVisibility.Always`, with `ColumnAutoWidth=false`.
- The main form is clamped to the active monitor's Windows working area on load/move so its bottom edge stays above the taskbar on shorter displays.
