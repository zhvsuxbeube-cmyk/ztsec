# Caption close overlay startup fix

## Failure observed
The Windows CI runner reached application startup and then failed in `Form1.InstallStableCaptionPresentation()` with:

`System.ArgumentException: Control does not support transparent background colors.`

The custom `AccentCaptionCloseButton` had assigned `BackColor = Color.Transparent` from its constructor. This is not valid for a plain WinForms `Control` unless transparent-background support is explicitly enabled, and the caption host does not require simulated transparency for this overlay.

The source also contained a second, nested `AccentCaptionCloseButton` implementation inside `Form1`. That duplicate class was removed so the project has one unambiguous caption-button implementation: `AccentCaptionCloseButton.cs`.

## Remediation
The standalone caption-button control now:

- never assigns `Color.Transparent`;
- receives the same solid background color as `FluentDesignFormControl` when installed;
- refreshes that background during caption/DPI relayout;
- paints the close glyph from `UiTheme.AccentColor` (`#50C090` / `RGB(80,192,144)`).

This removes the startup exception and avoids a white/default WinForms background patch behind the custom glyph.
