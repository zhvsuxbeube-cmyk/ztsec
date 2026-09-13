# FluentDesignForm Title Bar / Close Button Forensic Fix

## Symptom

The rendered top-right close glyph did not follow the application accent even after the DevExpress palette's `Red`/`Blue` roles were normalized. The CI pixel audit correctly found zero pixels matching `RGB(80,192,144)` in the caption-button region.

## Root cause

The FluentDesignForm title bar/caption buttons are drawn by the DevExpress form-control/skin rendering path. Changing the generic DevExpress palette roles is not a reliable contract for the exact glyph color of the native-looking close button. The screenshot therefore remained authoritative: the expected accent color was not present in the close glyph.

## Remediation

The application now overlays a dedicated, DPI-aware `AccentCaptionCloseButton` as a child of `FluentDesignFormControl` at the exact top-right caption-button location. It:

- uses the shared `UiTheme.AccentColor` (`#50C090` / `RGB(80,192,144)`),
- covers the underlying DevExpress close glyph so the old red/white glyph cannot bleed through,
- draws the X directly with GDI+ using the exact accent token,
- scales its dimensions with the current monitor DPI,
- remains anchored to the right edge during maximize/restore and resize,
- owns the close click so the application's normal `Close()` path is retained.

The title text is separately repainted by `FluentDesignFormControl1_Paint` in a fixed left-origin region. The underlying `Form.Text` remains `ZeroTrace Security 11` for normal Windows/taskbar semantics, while the painted caption prevents maximize/restore re-layout from visibly shifting the title.

## Verification

The existing rendered screenshot audit checks the top-right caption region and requires at least two pixels within RGB distance 3 of `RGB(80,192,144)` while also rejecting the known former red glyph color. The audit runs against the freshly built Windows GUI screenshots.

This repository was source-validated in the current environment. Final Windows/DevExpress rendering remains a GitHub Actions runtime check because the local environment does not contain the Windows .NET Framework + DevExpress runtime.
