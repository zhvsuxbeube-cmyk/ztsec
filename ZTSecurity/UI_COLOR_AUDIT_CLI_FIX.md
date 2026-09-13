# UI Color Audit CLI Invocation Fix

## Failure evidence

GitHub Actions reached the `Verify rendered UI color from captured screenshots` step, but PowerShell failed before the audit script could run:

`verify_ui_rendered_colors.ps1: A positional parameter cannot be found that accepts argument '$PWD'.`

The workflow was launching a second `pwsh -File` process and supplying a comma-separated array expression directly on the native command line. That makes the PowerShell/native argument parser responsible for converting expressions into arguments before the target script parameter binder sees them. The failure is therefore an invocation/parsing issue, not a rendered-color result.

## Remediation

The workflow now:

1. Builds the two absolute screenshot paths first.
2. Stores them in an explicit PowerShell array.
3. Invokes `verify_ui_rendered_colors.ps1` directly with `& $script -Screenshot $screenshots -SidebarWidth 280`.
4. Pipes the script output to `Tee-Object` for the audit report.
5. Lets the audit script's terminating exception propagate through a local `try/catch`.

This removes the fragile cross-process `pwsh -File` argument boundary entirely.

## Important validation boundary

This environment does not include PowerShell 7/Windows/.NET Framework + DevExpress, so the exact GitHub runner command cannot be executed here. The corrected workflow syntax was inspected as source and the old `pwsh -File ... -Screenshot (Join-Path ...) , (Join-Path ...)` invocation was removed.


## Follow-up failure: PASS output followed by missing report

The next runner execution proved the screenshot audit itself was healthy: both screenshots passed the target-color checks and both contained a substantial accent region outside the navigation rail. The failure occurred afterward because the workflow expected `Tee-Object` to create `ui-color-audit.txt`, while the audit script emits its report with `Write-Host`. In PowerShell 7, that output is on the Information stream rather than the ordinary success stream used by `Tee-Object` unless streams are explicitly redirected.

The remediation is now architectural rather than another invocation workaround: `verify_ui_rendered_colors.ps1` accepts `-ReportPath` and writes the complete report itself before returning or throwing. The workflow passes that path directly and treats the existence/size check as a postcondition. On audit failure, the workflow also prints the saved report before rethrowing the actual audit error.

This makes the reported `Rendered UI color verification: PASS` and the report artifact two outputs of the same script execution instead of two loosely coupled mechanisms.
