# ZTSecurity UI Remediation — 2026-09-12

## Applied fixes

- Connections selected rows now render with the exact requested `#1A2028` background and white text.
- The inactive FluentDesignForm caption palette role `Key Brush Light` is now white, preventing the main `ZeroTrace Security 11` caption from turning green when a modal administration dialog disables the parent window.
- Sidebar category rules use a 32-logical-pixel right inset so the rule extends farther toward the native group minimize/expand control while still ending before it.
- The supplied SVG assets from `icons.zip` are deployed under `Resources/icons` and loaded natively as `SvgImage` instances for all sidebar and Connections context-menu category icons.
- Sidebar menu rows were tightened modestly to 36 logical pixels for groups and 28 logical pixels for items.
- Sidebar category labels were reduced to 10 pt while menu labels were reduced to 9 pt, preserving the category/menu hierarchy.
- Sidebar item hover and selected surfaces are custom-drawn to the same inset right edge as the category rules, preventing the skin hover shadow from extending to the sidebar edge.
- Connections context-menu parent and nested flyouts use a uniform 220 logical-pixel minimum width, removing the previous excessive trailing space while keeping `Download and Update` fully visible.

## Verification boundary

The supplied repository intentionally excludes its manifest-matched DevExpress runtime DLLs from `dependencies/`, so a Windows/DevExpress runtime build cannot be executed in this Linux environment. The source, project XML, configuration XML, icon inventory, and brace/parenthesis balance were checked here; the existing Windows CI workflow remains the authoritative compile/render gate.


## Notifications page implementation

Implemented a dedicated Notifications page using the existing ZTSecurity dark theme and shared `#50C090` accent. The page provides Windows and Telegram connection-alert channels, each with an explicit enabled/disabled control. Telegram includes Bot Token and Chat ID fields plus a `Test Connection` action.

Windows notification behavior follows the proven NTHelpdesk implementation pattern (`NotifyIcon.ShowBalloonTip`) and uses the built executable's own application icon as the notification/tray icon when Windows exposes it; the native balloon itself still uses the standard Windows Info glyph, matching the NTHelpdesk behavior.

Telegram sends the requested connection message format and now includes Name, Tag, IP and Country. New connection notifications are triggered only when a telemetry session creates a new connection row. Telegram Bot Tokens are persisted with Windows DPAPI under the current Windows user profile rather than stored in plaintext.

CI now validates the Notifications page structure, opens the real page through an application-owned smoke-test trigger, captures it as `gui-screenshot-3.png`, audits it alongside the existing UI screenshots, and packages all three images into `gui-screenshots.zip`.
