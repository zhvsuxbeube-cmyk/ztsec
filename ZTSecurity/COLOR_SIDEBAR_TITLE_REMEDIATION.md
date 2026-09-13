# Color, Sidebar, and Title-Bar Remediation

## Current design tokens

- Application accent: `#50C090` / `RGB(80,192,144)`
- Expanded sidebar width: `240` logical pixels at 96 DPI
- Visual navigation-menu inset: `10` logical pixels on each side

## Sidebar

The main navigation is a DevExpress `AccordionControl` owned by `FluentDesignForm`. Its width is defined once as a 240-logical-pixel design target and converted through the current monitor DPI. The implementation distinguishes expanded and collapsed states so DPI transitions do not unexpectedly reopen a collapsed navigation rail.

## Theme

The project uses DevExpress `24.2.3`, `FluentDesignForm`, `The Bezier`, and `Custom/Custom Palette #1`. The palette's branding-role values relevant to the application accent are normalized to the current `UiTheme.AccentColor` where those roles intentionally represent the primary/accent treatment. Semantic warning/error/success/neutral colors are preserved where they have distinct meaning.

## Title bar

The close-button implementation remains the native DevExpress FluentDesignForm caption button. The CI probe verifies the actual non-client button while the main window is active; it does not substitute a custom close control.

## Verification

The Windows workflow performs rendered screenshot verification against the freshly built application. It requires target-accent pixels in the navigation, a separate content-state accent region, and the top-right close glyph. It also checks for known stale branding colors.
