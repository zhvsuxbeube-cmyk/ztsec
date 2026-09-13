# Native Caption Hover CI Forensic Fix

The rendered color audit was previously testing the native FluentDesignForm close button from `gui-screenshot-2.png` after the real Administration dialog had been opened.

That sequence cannot prove the parent close-button hover state: the Administration window is modal, so the parent FluentDesignForm is disabled while the dialog is active. Its native non-client caption button therefore does not enter the active-window hover state even when the cursor is positioned over the same screen coordinates. The observed result (`green=0`, `white-glyph=8`) is consistent with an inactive native caption button, not evidence that the native hover palette failed.

The CI probe now captures `gui-native-close-hover.png` **before** the modal Administration dialog is opened. The main window is explicitly foregrounded, the close-button center is derived from the current window DPI and native caption metrics, and `WM_NCHITTEST` must report `HTCLOSE` for the probe coordinates. The screenshot is then passed separately to `verify_ui_rendered_colors.ps1` via `-CloseHoverScreenshot`.

The normal two GUI screenshots remain unchanged for the application/content audit and for the packaged screenshot artifact.

This is a CI/test correction only. No fake caption button and no custom-drawn close glyph are introduced.
