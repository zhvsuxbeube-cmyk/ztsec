# ZTSecurity Final Engineering Audit

## Scope

This audit covers the supplied source repository, the Connections WinForms layout, the runtime dependency loading architecture, the Windows Release MSBuild target, and the GitHub Actions packaging/runtime-validation workflow.

## Audit #1 — implementation review

- Verified the Connections tab has an explicit `connectionsLayoutHost` with a top search band and a fill grid. The grid is no longer a sibling relying on mixed runtime docking/z-order behavior.
- Verified the collapsed search affordance is right-docked and the expanded search editor is constrained to roughly 25% of the current search-band width, with 220–520 pixel usability bounds.
- Verified IP Address, User Name, GPU, and Ping remain visible grid columns and are moved into the initial visible order; the remaining fields stay available via horizontal scrolling.
- Verified `ConnectionId` remains the only intentionally hidden connection column.
- Verified the CI-only application diagnostics directly exercise search open/close state and require at least two real connection rows before reporting PASS.
- Verified the Release MSBuild target copies manifest-approved DLLs directly to `$(OutputPath)` rather than creating `$(OutputPath)\dependencies`.
- Verified `App.config` no longer declares `privatePath="dependencies"` and `Program.cs` resolves assemblies from the application base directory.
- Verified the workflow validates source inputs, DLL SHA-1/size/MZ integrity, PE/CLR metadata, flat output, runtime data, real GUI visibility, real connection-agent data, responsive resize states, screenshot creation, and the exact 500,000-byte screenshot packaging rule.

## Audit #2 — independent regression review

- Searched the source for stale `dependencies` runtime loader/probing references: none remain in application code or configuration. Remaining `dependencies` references are limited to compile-input preparation, manifest validation, and explicit negative checks that reject nested runtime dependency directories.
- Searched for the prior embedded Find Panel control path: no `ShowFindPanel`/`HideFindPanel` logic remains. The standalone SearchControl is the single Connections search implementation.
- Rechecked C# brace/parenthesis balance and Python bytecode compilation.
- Re-parsed `App.config`, the project file, and the workflow YAML successfully.
- Rechecked that `CopyRuntimeDependencies` has no destination path beneath `$(OutputPath)`.
- Rechecked that the package step copies exactly the executable, executable config, manifest-approved DLLs, and the manifest runtime data file, with no nested directories.
- Rechecked that EXE-only staging contains exactly one file.
- Rechecked the screenshot rule: PNG is retained at or below 500,000 bytes and is replaced by a dedicated ZIP above that threshold.
- Removed brittle keyboard-injection dependence from CI; the application itself exercises the search state transitions, while the workflow uses native window APIs for show/restore/maximize/resize and screenshot capture.

## Runtime/build limitation in this environment

The supplied ZIP intentionally contains only `dependencies/.gitkeep` and does not contain the 45 manifest-locked third-party DLLs or the manifest-locked `world-administrative-boundaries.shp`. The supplied Linux environment also does not contain a Windows/.NET Framework/DevExpress build toolchain. Running the repository's dependency-preparation script from a correctly structured project parent therefore reports all 45 DLLs and the runtime shapefile as missing.

Because the required binary inputs and Windows GUI runtime are absent, a genuine Release EXE build, Windows process launch, GUI screenshot capture, and release ZIP generation could not be truthfully performed in this environment. No fabricated executable, screenshot, or release package has been substituted.

## CI completion path

The updated Windows workflow is the executable validation path once the manifest-matching external payloads are supplied. It launches the newly built Release executable with the CI smoke environment flag, starts a real local `ztsec_agent.py` connection fan-out, validates the live GUI/window state, exercises maximized/restored/narrow sizing, requires the UI diagnostics and real connection rows to pass, captures the actual application window, and creates the flat runtime and EXE-only artifacts.


2026-09-12 follow-up: Removed the GridView.CustomDrawScroll override. The prior custom scrollbar renderer could throw System.ArgumentException from GDI+ DrawRectangle during scrollbar repaints, including while the UI was opening the Connections context menu. The grid now relies on the documented HorzScrollVisibility=Always setting together with the application-wide ScrollUIMode.Desktop, avoiding custom GDI painting on the DevExpress scrollbar.
