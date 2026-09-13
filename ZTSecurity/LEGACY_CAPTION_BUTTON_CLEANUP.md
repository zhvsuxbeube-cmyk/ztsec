# Legacy caption-button cleanup

`AccentCaptionCloseButton.cs` was a temporary custom overlay and is not part of the native DevExpress `FluentDesignForm` caption-button implementation.

The source tree should delete this legacy file. The Windows workflow includes a defensive migration step that removes it from a checkout if an older branch/commit still contains it, then the source-of-truth validation requires the file to be absent.

The project file does not compile this legacy source.
