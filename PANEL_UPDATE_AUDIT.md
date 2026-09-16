
## 2026-09-16 UI/update hardening

- Remote execution/update popups now use a DevExpress GridView with real data rows, compact `UserName@ComputerName` identity, status, and progress columns.
- Update payload bytes/hash/base64 are snapshotted once per dialog instead of re-reading and re-encoding once per selected connection.
- CI trigger watchers no longer delete their trigger files while another process may still hold them open; consumption is represented by the dedicated consumed marker and CI performs run-start cleanup.
- The update agent candidate handshake remains fail-closed: server admission plus the old-agent localhost handoff must both succeed before the old agent sends `ACK:UPDATE:` and exits.
- Successor replacement errors after parent shutdown now attempt to restart the original executable at its original path.
