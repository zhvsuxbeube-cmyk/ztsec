# Data and Command Audit

## Persistent data

Notification settings are stored only as compact JSON at:

`ZTSecurity/settings/notifications/notifications.json`

The runtime no longer reads or writes notification XML. The Telegram bot token remains DPAPI-protected before it is written to JSON.

## Agent commands

Panel → agent commands:

- `CMD:SLEEP`
- `CMD:HIBERNATE`
- `CMD:RESTART`
- `CMD:SHUTDOWN`
- `CMD:CLOSE`
- `CMD:RECONNECT`
- `CMD:BLOCK`

Agent acknowledgements:

- `ACK:<COMMAND>` — accepted
- `ERR:<COMMAND>` — reserved for command execution failure paths

`REQ:DATA`, `HB`, `PONG`, and `DATA:` remain unchanged for telemetry.
