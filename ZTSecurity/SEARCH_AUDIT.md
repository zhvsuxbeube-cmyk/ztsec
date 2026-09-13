# Connection Search Audit

## Current implementation

The Connections tab uses a standalone DevExpress `SearchControl` (DevExpress WinForms 24.2) attached to `gridControl1` through the documented `SearchControl.Client` property. The grid's built-in Find Panel is disabled so there is only one search surface and it cannot alter the grid's header layout.

The search icon and search editor occupy the same dedicated top search surface. Clicking the icon swaps that surface to the `SearchControl` and focuses it. `Ctrl+F` on the Connections tab opens/focuses the same control. `Esc` clears the query first and then closes the search surface when it is already empty.

The search editor uses the documented 24.2 `RepositoryItemSearchControl.FindDelay` and `NullValuePrompt` properties. `FindNullPrompt` is not a DevExpress 24.2 API and must not be used.

The standalone `SearchControl` is responsible for searching the attached multi-column grid; DevExpress documents that a multi-column client is searched across its visible data columns. No code changes column visibility when a query is applied.

## Column preservation

The connection grid explicitly defines the intended visible fields and widths. Only the internal `ConnectionId` column is intentionally hidden. IP Address, User Name, RAM, GPU, CPU, Anti Virus, HWID, and the other display columns are not hidden by the search flow.

The search surface is hosted outside the grid itself, so expanding it consumes its own fixed top layout band rather than being painted over the grid header or first columns.

## Horizontal scrolling

The connection grid uses:

- `OptionsView.ColumnAutoWidth = false`
- `HorzScrollVisibility = ScrollVisibility.Always`
- application-level `WindowsFormsSettings.ScrollUIMode = ScrollUIMode.Desktop`

DevExpress documents `ScrollVisibility.Always` as making the horizontal scrolling element visible and documents Desktop scroll UI mode as disabling the auto-hide/auto-expand behavior found in Fluent/Touch modes. This specifically addresses the distinction between a scrollbar existing and its thumb collapsing to a thin stripe until hover.

## Version verification

The project references DevExpress 24.2 assemblies, including `DevExpress.XtraEditors.v24.2.dll` and `DevExpress.XtraGrid.v24.2.dll`. The relevant 24.2 API documentation was checked for:

- `SearchControl.Client`
- `SearchControl` / `RepositoryItemSearchControl`
- `RepositoryItemSearchControl.FindDelay`
- `RepositoryItemTextEdit.NullValuePrompt`
- `RepositoryItemSearchControl.ShowClearButton` / `ShowSearchButton`
- `GridView.HorzScrollVisibility`
- `WindowsFormsSettings.ScrollUIMode`
- grid Find Panel keyboard behavior and lifecycle

The prior CI failure was caused by the non-existent `FindNullPrompt` property and is corrected to `NullValuePrompt`.
