# UI Color Forensic Audit — DevExpress WinForms 24.2.3

## Scope and inventory

The supplied project is a classic .NET Framework 4.7.2 WinForms desktop application. It is **not** a web UI: there is no HTML DOM, CSS/SCSS/Less pipeline, TypeScript, or bundled browser stylesheet. The visual stack is:

- WinForms / `System.Drawing` painting in `Form1.cs` and `livechart.cs`.
- DevExpress WinForms controls, including `FluentDesignForm`, `AccordionControl`, `XtraGrid`, `SearchControl`, `XtraTabControl`, and map/chart controls.
- DevExpress v24.2.3 assemblies, confirmed by the project references, licenses, and dependency manifest.
- DevExpress Skin/SvgPalette configuration in `App.config`.
- Embedded SVG/image resources in the `.resx` files and `Resources/` directory.

The active DevExpress configuration is:

- Skin: `The Bezier`
- Palette: `Custom Palette #1`
- Configuration section: `DevExpress.LookAndFeel.Design.AppSettings`

The repository does not contain the DevExpress DLL payloads; `dependencies/` contains only `.gitkeep`. Therefore the Windows executable cannot be launched in this Linux execution environment.

## Root cause proven from the supplied screenshot and original source

The supplied screenshot is evidence of a **specific rendered color mismatch** in the Connections grid. Pixel analysis of `/mnt/data/1000441787.png` found:

- The target color `RGB(37,150,190)` is present in the sidebar selection, proving the requested accent was already being painted correctly in at least one surface.
- The selected Connections row is dominated by a darker/desaturated blue family around `RGB(58,92,128)`, not the target.
- In the original source, `Form1.cs` line 1296 used `Color.FromArgb(55, 90, 125)` inside `gridView.RowStyle` for selected rows. This is the direct application-side paint override responsible for that state.

The source/render chain was therefore:

`XtraGrid GridView`
→ `SetupGridControl()` in `Form1.cs`
→ `gridView.RowStyle`
→ `gridView.IsRowSelected(rowHandle)`
→ hard-coded `Color.FromArgb(55, 90, 125)`
→ rendered selected-row background

That declaration has been changed to the shared target accent.

## DevExpress documentation validation boundary

The requested live web research against the current DevExpress documentation could not be performed in this execution environment because external web access is disabled. I therefore did not represent a live documentation lookup as having occurred. The DevExpress version/framework diagnosis below is based on the repository's concrete project references, `licenses.licx`, dependency manifest, and the actual `DevExpress.LookAndFeel.Design.AppSettings` schema already present in `App.config`. The Windows CI continues to pin the build to Visual Studio 2022 for the v24.2.3 dependency set.

## DevExpress theme root cause

The original `App.config` defined a custom palette for the active `The Bezier` skin. Its palette contained competing blue roles:

- `The Bezier / Custom Palette #1 / Blue = 77,130,184`
- `The Bezier / Custom Palette #1 / altBlue = 77,130,184`
- `WXI / Custom Palette #1 / Blue = 0,120,212`

The active runtime selection is `The Bezier`, so the first two were relevant to DevExpress controls that consume the palette's blue role. They have been normalized to `37,150,190` while preserving the existing `Green`, `Red`, and `Yellow` semantic values.

This is intentionally a DevExpress palette-level fix rather than a stack of late `!important` overrides. `DefaultAppSkin` and `DefaultPalette` already select the custom palette, so editing the repository's custom palette is the correct source-controlled theme mechanism present in this project.

## Other accent/color paths audited

The original source also contained explicitly named blue/teal UI accents that were separate from the DevExpress palette:

- `Color.DeepSkyBlue` on the About labels.
- `Color.LightBlue` in DataTransfer/build output styling.
- `Color.Cyan` in build/package status output.
- `Color.FromArgb(103, 145, 164)` for the Convert navigation icon.
- A blue chart grid pen using `113,168,243` with alpha.

The clearly intentional application-accent usages were normalized to the exact target token. The LiveChart's primary series is now `RGB(37,150,190)` and its grid uses the same hue at an explicit alpha, while the chart's secondary/tertiary series remains intentionally distinct.

Semantic/error/warning/success colors were **not** globally recolored. Existing reds, oranges, yellows, neutral grays, and the tertiary chart series remain available for their semantic purposes.

## Source-of-truth cleanup

A shared C# token was added in `UiTheme.cs`:

- `AccentRed = 37`
- `AccentGreen = 150`
- `AccentBlue = 190`
- `AccentHex = "#2596BE"`
- `AccentRgb = "rgb(37, 150, 190)"`
- `AccentColor = Color.FromArgb(37, 150, 190)`

`Form1.cs` keeps a local semantic alias named `SidebarAccentColor`, but it now points to `UiTheme.AccentColor`. `livechart.cs` uses the same shared token rather than defining a second accent color.

DevExpress palette values remain in `App.config` because that configuration is independently consumed by the DevExpress skin loader. The Windows CI diagnostics cross-check the C# and DevExpress sides rather than pretending an App.config value can reference a C# constant.

## Global color transformation audit

The original designer also contained `Form1.Opacity = 0.99D`. That is a post-paint whole-window alpha transform, so even an exact control color could be composited before capture. It has been changed to `1D` to remove that global color transformation and preserve the exact opaque target color.

The repository was searched for literals and semantic color paths in C#, config, resource XML, and SVG. No live application stylesheet pipeline, browser CSS, CSS variables, JavaScript DOM style assignment, or TypeScript theme code exists in this project.

## Sidebar width trace

The navigation component is `DevExpress.XtraBars.Navigation.AccordionControl`, assigned to `Form1.NavigationControl` on a `FluentDesignForm`.

Original layout source:

- `SidebarWidth = 240` in `Form1.cs`.
- `accordionControl1.Size = new Size(240, 902)` in `Form1.Designer.cs`.
- `fluentDesignFormContainer1.Location = new Point(240, 30)` in the designer and the same offset was applied at runtime.

The corrected source of truth is:

`private const int SidebarWidth = 280;`

Both runtime sizing and designer geometry are aligned to 280 logical pixels. The container remains `DockStyle.Fill`, so the content area is still managed by the existing FluentDesignForm layout rather than being given a fixed width.

## Rendered verification: what is and is not proven here

The supplied screenshot was independently pixel-inspected and proves the original visual mismatch. It cannot prove the post-change application state because it predates the code modifications.

The repository's Windows CI now launches the freshly built application, creates real local connection rows, deliberately selects a real grid row in CI smoke mode, captures the actual GUI, and performs a rendered pixel audit. The audit now requires both:

1. a substantial target-colored region in the navigation rail; and
2. a separate substantial target-colored content-state region outside the navigation rail, so a sidebar-only match cannot make the check pass.

The audit also fails on the known former brand colors when they appear at meaningful levels.

This environment cannot execute that final Windows/DevExpress render step because Windows/.NET Framework MSBuild and the private third-party DevExpress DLL payloads are unavailable here. It would be inaccurate to claim a fresh post-change screenshot was executed locally.

## Target-color arithmetic

The authoritative numeric source is the supplied hex/RGB value:

- `#2596BE`
- `RGB(37,150,190)`
- `rgba(37,150,190,1)`

The supplied HSL is slightly rounded/incorrect in hue. Standard RGB→HSL conversion gives approximately `hsl(195.69°, 67.40%, 44.51%)`; implementation therefore follows the exact hex/RGB requirement.

White text over `#2596BE` has a calculated WCAG contrast ratio of about `3.40:1`, so the existing white selected-state text is not a 4.5:1 normal-text pairing. It was intentionally left unchanged because the requested scope is a color-system correction rather than an accessibility redesign; the exact target background must remain `#2596BE`.
