# Title-bar close button compile fix

The previous source revision referenced `AccentCaptionCloseButton` from `Form1.cs` but did not include its source file in the project/archive. That caused the Windows build to fail with CS0246.

This revision adds `AccentCaptionCloseButton.cs` and explicitly includes it in `ZeroTrace Security Official.csproj`.

The control is a small WinForms `Control` that paints the close glyph directly from `UiTheme.AccentColor` and is intentionally independent of the DevExpress skin's semantic `Red` role. It is DPI-aware through `DeviceDpi` and preserves the existing 48-logical-pixel caption-button hit area.
