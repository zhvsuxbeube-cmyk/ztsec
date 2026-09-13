# ZTSecurity UI Remediation — 2026-09-12

## Applied fixes

- Connections selected rows now render with the exact requested `#1A2028` background and white text.
- The inactive FluentDesignForm caption palette role `Key Brush Light` is now white, preventing the main `ZeroTrace Security 11` caption from turning green when a modal administration dialog disables the parent window.
- Sidebar category rules use a 32-logical-pixel right inset so the rule extends farther toward the native group minimize/expand control while still ending before it.
- The supplied SVG assets from `icons.zip` are deployed under `Resources/icons` and loaded natively as `SvgImage` instances for all sidebar and Connections context-menu category icons.
- Connections administration popup minimum width is 300 logical pixels so `Download and Update` is fully measurable and no longer clipped.

## Verification boundary

The supplied repository intentionally excludes its manifest-matched DevExpress runtime DLLs from `dependencies/`, so a Windows/DevExpress runtime build cannot be executed in this Linux environment. The source, project XML, configuration XML, icon inventory, and brace/parenthesis balance were checked here; the existing Windows CI workflow remains the authoritative compile/render gate.
