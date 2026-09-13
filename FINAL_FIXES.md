# Final UI / Lifecycle Fix Audit

## Connections search

The Connections grid now has one authoritative search surface: a standalone DevExpress WinForms 24.2 `SearchControl` attached to `gridControl1`. The old embedded Find Panel is not used for the Connections tab.

The search icon and search editor are mutually exclusive states of the same top search surface. `Ctrl+F` opens and focuses the editor. `Esc` first clears an active query and only closes the surface on a subsequent `Esc` when the query is already empty.

The search editor uses only APIs verified against DevExpress 24.2:

- `SearchControl.Client`
- `RepositoryItemSearchControl.FindDelay`
- `RepositoryItemTextEdit.NullValuePrompt`

`FindNullPrompt` was never a valid 24.2 property and must not be reintroduced.

## Selection lifecycle

The connection selection fix remains based on DevExpress's native row-selection state. Pre-click state is captured in `MouseDown`; `RowClick` only schedules deselection when the row was already selected before the click. The deferred operation resolves by stable `ConnectionId` instead of a stale row handle.

`SelectionChanged` synchronizes the application's connection-selection state and clears it safely when DevExpress returns no selected rows after a clear.

## Horizontal scrollbar

The grid keeps `ColumnAutoWidth=false` and `HorzScrollVisibility=Always`. Application startup explicitly selects DevExpress `ScrollUIMode.Desktop`, which prevents Fluent/Touch scrollbar auto-collapse. This addresses the scrollbar thumb behavior separately from whether horizontal scrolling is available.

## Flat Windows runtime layout

The project keeps the manifest-approved third-party DLLs under the source `dependencies/` directory as compile-time inputs, but the Release MSBuild target copies those validated DLLs directly into `bin\\Release` beside the executable. The final runtime package is built from this flat Release output and explicitly rejects any nested `dependencies/` directory.

The .NET Framework application no longer probes a `dependencies` subdirectory. `Program.cs` resolves external managed assemblies from the application base directory, matching the flat runtime layout, while Windows native DLL resolution naturally starts from the application directory. The old application-configured `privatePath="dependencies"` was removed because that path is no longer part of the deployed runtime.

## Connections layout architecture

The Connections tab now has one explicit layout host with two non-overlapping regions: a fixed-height top search band and a `DockStyle.Fill` grid region. The collapsed search affordance is right-docked; the expanded search editor occupies roughly one quarter of the available header width, bounded between 220 and 520 pixels. The connection grid keeps horizontal scrolling available and puts IP Address, User Name, GPU, and Ping in the initial column order while retaining all other connection fields.


2026-09-12 follow-up: Removed the GridView.CustomDrawScroll override. The prior custom scrollbar renderer could throw System.ArgumentException from GDI+ DrawRectangle during scrollbar repaints, including while the UI was opening the Connections context menu. The grid now relies on the documented HorzScrollVisibility=Always setting together with the application-wide ScrollUIMode.Desktop, avoiding custom GDI painting on the DevExpress scrollbar.
