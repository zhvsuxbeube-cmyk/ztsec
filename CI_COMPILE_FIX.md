# CI compile fix

The Windows build reported:

`CS1061: 'TextEdit' does not contain a definition for 'TextLength'`

The offending use in `Form1.cs` was changed from the WinForms `TextBoxBase.TextLength` API to DevExpress `TextEdit.Text.Length`.

DevExpress documents `TextEdit.Text`, `SelectionStart`, and `SelectionLength` as the supported editor APIs.

No other `TextLength` use belongs to a DevExpress `TextEdit`; the remaining `TextLength` references are on native `RichTextBox` controls.
