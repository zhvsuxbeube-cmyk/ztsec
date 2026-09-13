# Connections telemetry protocol

The panel's Connections listener accepts the original `DATA:` prefix and a
backward-compatible pipe-delimited record.

Version 1 record:

```text
DATA:Country|Nickname|Tag|UserName|Version|Privileges|OS|GPU|CPU|RAM|Disk|AntiVirus|Uptime|AFKTime|Ping\n
```

The updated listener accumulates the first `DATA:` line until the newline is
received so normal TCP fragmentation cannot cause a partial record to be
parsed.

The Python `con.py` client keeps its TCP socket open and sends `HB\n` heartbeats.
Heartbeats have no command semantics; they are only there to keep a persistent
telemetry connection alive. When the socket fails, the client closes it and
reconnects using exponential backoff.

`IP` is taken from the TCP peer address by the panel rather than trusting a
client-supplied string. With the default local test:

```text
py con.py --ip 127.0.0.1 --port 4793
```

the panel therefore sees `127.0.0.1` as the source address.

## Windows data sources used by con.py

The script uses only Python standard-library modules. On Windows it uses
`ctypes` to call:

- `GetUserNameW` for the logged-in account name.
- `GlobalMemoryStatusEx` for physical RAM totals/usage.
- `GetLastInputInfo` for session idle time (AFK).
- `GetTickCount64` for system uptime.

Disk usage comes from Python's `shutil.disk_usage`; CPU/OS identity comes from
`platform`; administrative status is checked with `IsUserAnAdmin`.

GPU and antivirus are reported as `Unknown` rather than using deprecated shell
commands or fragile undocumented registry scraping.

## Scope

This update intentionally provides telemetry collection and connection
resiliency only. It does not add a remote command channel or remote-control
actions.
