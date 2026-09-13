# DPI / Sidebar Forensic Fix

## Evidence

The project is a .NET Framework 4.7.2 WinForms application using DevExpress v24.2.3 and `FluentDesignForm` + `AccordionControl`. The sidebar had a 280-pixel value applied after `InitializeComponent()`, while the designer used `AutoScaleMode.Font` and `App.config` selected `DPIAwarenessMode=System`. That combination can produce machine-dependent geometry: Windows/WinForms may scale designer coordinates, then the runtime assignment writes an unscaled 280 device-pixel width back over the scaled value.

## Corrective architecture

- The product sidebar target is now explicitly a **240 logical-pixel (96-DPI) width**.
- Runtime width is converted using `dpi / 96.0`, so 125% produces 350 device pixels, 150% produces 420, etc.
- Sidebar group/item heights and icon bitmap sizes are scaled from the same 96-DPI design units.
- WinForms `AutoScaleMode` is changed to `Dpi` and the form designer baseline is `96 x 96`, matching the logical-unit model.
- .NET Framework WinForms `PerMonitorV2` awareness is enabled in `App.config`, so the process can adapt when the window moves between monitors with different DPI.
- The runtime no longer forces an absolute `Location` on the fill container; the left-docked navigation control and `FluentDesignForm` docking engine own that layout relationship.
- `OnDpiChanged` reapplies scaled expanded geometry without reopening a collapsed navigation rail.

## CI validation

The application now reports `Dpi`, the actual device-pixel sidebar width, the 240 logical-pixel target, and whether the measured width matches the DPI-scaled target. The workflow explicitly requires that diagnostic to pass in addition to the screenshot-based visual checks.

## Compile regression fixed

The CI-only file watcher previously used an untyped anonymous method with `new Thread(delegate { ... })`, which is ambiguous against the .NET `Thread(ThreadStart)` / `Thread(ParameterizedThreadStart)` overloads under the actual MSBuild compiler. The call is now explicitly typed as `new Thread((ThreadStart)delegate { ... })`.

## Verification boundary

The repository was source-inspected and its XML/YAML parsed in this environment. A Windows + .NET Framework + DevExpress runtime build cannot be executed here because the third-party DevExpress DLL payloads are intentionally external to the supplied source and this environment is not a Windows desktop runtime. The GitHub Actions build remains the authoritative runtime gate.

## Follow-up UI polish
- The rail target is now 240 logical pixels at 96 DPI.
- Painted sidebar menu surfaces are inset by 8 logical pixels on each side so they do not visually occupy the entire rail.
- The form title now uses the native Text caption path rather than HTMLText to avoid caption metric changes during maximize/restore and DPI transitions.
- The active The Bezier palette uses #50C090 for the close-glyph Red/altRed role; application-owned semantic error colors remain explicit and are not globally remapped.
