# Native Title-Bar and Sidebar UI Fix

## Native close button

The custom `AccentCaptionCloseButton` overlay has been removed. `FluentDesignForm` uses its real DevExpress caption buttons. The active `The Bezier / Custom Palette #1` palette assigns the caption-button color roles to `80,192,144` so the native close-button hover surface is intended to use `#50C090` while the native glyph remains white. CI verifies this from a dedicated active-window hover capture; it does not test the parent caption while a modal dialog is disabling it.

The application no longer paints or hit-tests a replacement close button.

## Title bar

The caption stays on the native `Form.Text` path and `HtmlText` is cleared. The inactive `Key Brush Light` palette role is white so the caption remains white when a modal child window disables the main form. `FluentDesignFormControl` uses its actual WinForms `BackColor` property (`26,26,26`); the unsupported `Appearance` API is not used. This avoids the white HTML-caption canvas visible in the previous screenshot.

## Sidebar

The expanded navigation rail target remains 240 logical pixels at 96 DPI and is converted to the active monitor DPI. Painted group rules and selected-item surfaces are inset from the right edge by 22 logical pixels so they stop before the native group expand/collapse button. The hit-test/navigation area itself remains full-width.
