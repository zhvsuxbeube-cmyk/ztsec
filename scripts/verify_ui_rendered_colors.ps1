param(
    [Parameter(Mandatory = $true)]
    [string[]]$Screenshot,
    [string]$CloseHoverScreenshot,
    [int]$SidebarWidth = 240,
    [string]$ReportPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$target = [System.Drawing.Color]::FromArgb(80, 192, 144)
$selectedRowTarget = [System.Drawing.Color]::FromArgb(26, 32, 40)
$nearTargetThreshold = 3
$oldColors = @(
    [System.Drawing.Color]::FromArgb(55, 90, 125),    # previous Connections selected-row paint
    [System.Drawing.Color]::FromArgb(37, 150, 190),   # previous application accent target
    [System.Drawing.Color]::FromArgb(77, 130, 184),   # previous The Bezier Blue palette token
    [System.Drawing.Color]::FromArgb(0, 120, 212),    # previous WXI Blue palette token
    [System.Drawing.Color]::FromArgb(103, 145, 164),  # previous Convert icon color
    [System.Drawing.Color]::FromArgb(0, 191, 255),    # DeepSkyBlue
    [System.Drawing.Color]::FromArgb(173, 216, 230),  # LightBlue
    [System.Drawing.Color]::FromArgb(0, 255, 255)     # Cyan
)

function Get-ColorDistance([System.Drawing.Color]$a, [System.Drawing.Color]$b) {
    $dr = [int]$a.R - [int]$b.R
    $dg = [int]$a.G - [int]$b.G
    $db = [int]$a.B - [int]$b.B
    return [math]::Sqrt(($dr * $dr) + ($dg * $dg) + ($db * $db))
}

function Test-ColorNearTarget([System.Drawing.Color]$c, [System.Drawing.Color]$targetColor, [double]$threshold) {
    return (Get-ColorDistance $c $targetColor) -le $threshold
}

$report = New-Object System.Collections.Generic.List[string]
$failed = $false
$gridAccentFoundInAnyScreenshot = $false
$selectedRowFoundInAnyScreenshot = $false
$nativeCloseHoverFoundInAnyScreenshot = $false

foreach ($path in $Screenshot) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Rendered UI screenshot is missing: $path"
    }

    $bitmap = [System.Drawing.Bitmap]::new($path)
    try {
        $width = $bitmap.Width
        $height = $bitmap.Height
        if ($width -le 0 -or $height -le 0) { throw "Invalid screenshot dimensions for '$path'." }

        # Do not assume the screenshot pixel width equals the WinForms logical width:
        # Windows DPI scaling can make a 240 logical-pixel navigation rail render at a
        # different physical pixel width. Instead, discover the left-most accent rail
        # and then verify a second substantial accent region exists outside it.
        $targetInLeft = 0
        $targetOutsideLeft = 0
        $maxTargetX = -1
        $rowTargetCounts = New-Object 'int[]' $height
        $selectedRowCounts = New-Object 'int[]' $height
        $titlebarDarkBackgroundCount = 0
        $oldExactCounts = @{}
        $oldNearCounts = @{}
        foreach ($old in $oldColors) {
            $oldExactCounts[$old.ToArgb()] = 0
            $oldNearCounts[$old.ToArgb()] = 0
        }

        for ($y = 0; $y -lt $height; $y++) {
            for ($x = 0; $x -lt $width; $x++) {
                $c = $bitmap.GetPixel($x, $y)
                if ($c.R -eq $selectedRowTarget.R -and $c.G -eq $selectedRowTarget.G -and $c.B -eq $selectedRowTarget.B) {
                    $selectedRowCounts[$y]++
                }

                if (Test-ColorNearTarget $c $target $nearTargetThreshold) {
                    $rowTargetCounts[$y]++
                    if ($x -lt [math]::Min($SidebarWidth, $width)) {
                        $targetInLeft++
                        if ($x -gt $maxTargetX) { $maxTargetX = $x }
                    }
                }

                # The native DevExpress title host should remain part of the dark chrome, not a white caption canvas.
                if ($x -ge [math]::Min(48, $width - 1) -and $x -lt [math]::Max(48, $width - 220) -and $y -lt [math]::Min(28, $height)) {
                    if ($c.R -lt 80 -and $c.G -lt 80 -and $c.B -lt 80) { $titlebarDarkBackgroundCount++ }
                }

                $argb = $c.ToArgb()
                if ($oldExactCounts.ContainsKey($argb)) { $oldExactCounts[$argb]++ }
                foreach ($old in $oldColors) {
                    if ((Get-ColorDistance $c $old) -le 3) { $oldNearCounts[$old.ToArgb()]++ }
                }
            }
        }

        if ($maxTargetX -ge 0) {
            $outsideStart = [math]::Min($width - 1, $maxTargetX + 20)
            for ($y = 0; $y -lt $height; $y++) {
                for ($x = $outsideStart; $x -lt $width; $x++) {
                    if (Test-ColorNearTarget $bitmap.GetPixel($x, $y) $target $nearTargetThreshold) {
                        $targetOutsideLeft++
                    }
                }
            }
        }

        $maxRowTarget = ($rowTargetCounts | Measure-Object -Maximum).Maximum
        if ($null -eq $maxRowTarget) { $maxRowTarget = 0 }
        $maxSelectedRowTarget = ($selectedRowCounts | Measure-Object -Maximum).Maximum
        if ($null -eq $maxSelectedRowTarget) { $maxSelectedRowTarget = 0 }
        $gridAccentThisScreenshot = $targetOutsideLeft -ge 100 -and $maxRowTarget -ge 100
        $selectedRowThisScreenshot = $maxSelectedRowTarget -ge 100
        if ($gridAccentThisScreenshot) { $gridAccentFoundInAnyScreenshot = $true }
        if ($selectedRowThisScreenshot) { $selectedRowFoundInAnyScreenshot = $true }

        if ($targetInLeft -lt 100) {
            $failed = $true
            $report.Add("FAIL $([IO.Path]::GetFileName($path)): expected visible accent area in the navigation rail; detected only $targetInLeft target-like pixels within the first $([math]::Min($SidebarWidth, $width)) screenshot pixels.")
        } else {
            $report.Add("PASS $([IO.Path]::GetFileName($path)): navigation accent detected ($targetInLeft pixels within RGB-distance <=$nearTargetThreshold of rgb(80,192,144)).")
        }

        if ($gridAccentThisScreenshot) {
            $report.Add("PASS $([IO.Path]::GetFileName($path)): second accent region detected outside the navigation rail ($targetOutsideLeft target-like pixels; max row=$maxRowTarget).")
        } else {
            $report.Add("INFO $([IO.Path]::GetFileName($path)): no substantial second accent region detected outside the navigation rail (outside=$targetOutsideLeft, max row=$maxRowTarget).")
        }

        if ($selectedRowThisScreenshot) {
            $report.Add("PASS $([IO.Path]::GetFileName($path)): requested selected Connections-row color #1A2028 detected (max row=$maxSelectedRowTarget pixels).")
        }

        if ($titlebarDarkBackgroundCount -lt 100) {
            $failed = $true
            $report.Add("FAIL $([IO.Path]::GetFileName($path)): the FluentDesignForm title host did not render as the expected dark chrome; only $titlebarDarkBackgroundCount dark-background pixels were found in the caption area.")
        } else {
            $report.Add("PASS $([IO.Path]::GetFileName($path)): dark FluentDesignForm title host detected ($titlebarDarkBackgroundCount dark pixels).")
        }
        foreach ($old in $oldColors) {
            $exactCount = $oldExactCounts[$old.ToArgb()]
            $nearCount = $oldNearCounts[$old.ToArgb()]
            if ($exactCount -gt 0 -or $nearCount -ge 100) {
                $failed = $true
                $report.Add("FAIL $([IO.Path]::GetFileName($path)): stale old color near rgb($($old.R),$($old.G),$($old.B)) detected; exact=$exactCount, within RGB-distance <=3=$nearCount.")
            }
        }
    }
    finally {
        $bitmap.Dispose()
    }
}


# The native close-button hover probe is captured separately while the main window is
# active. Do not infer hover from a modal-dialog screenshot: the modal dialog disables
# the parent caption buttons, so the parent close button cannot be in a hover state.
if ([string]::IsNullOrWhiteSpace($CloseHoverScreenshot)) {
    $failed = $true
    $report.Add('FAIL: a dedicated native close-button hover screenshot was not supplied.')
} elseif (-not (Test-Path -LiteralPath $CloseHoverScreenshot -PathType Leaf)) {
    $failed = $true
    $report.Add("FAIL: native close-button hover screenshot is missing: $CloseHoverScreenshot")
} else {
    $hoverBitmap = [System.Drawing.Bitmap]::FromFile($CloseHoverScreenshot)
    try {
        $hoverGreen = 0
        $hoverWhite = 0
        $hoverOldRed = 0
        $hoverWidth = $hoverBitmap.Width
        $hoverHeight = $hoverBitmap.Height
        $hoverX0 = [math]::Max(0, $hoverWidth - 56)
        $hoverY1 = [math]::Min(36, $hoverHeight)
        for ($y = 0; $y -lt $hoverY1; $y++) {
            for ($x = $hoverX0; $x -lt $hoverWidth; $x++) {
                $c = $hoverBitmap.GetPixel($x, $y)
                if (Test-ColorNearTarget $c $target $nearTargetThreshold) { $hoverGreen++ }
                if ((Get-ColorDistance $c ([System.Drawing.Color]::White)) -le 3) { $hoverWhite++ }
                if ((Get-ColorDistance $c ([System.Drawing.Color]::FromArgb(190, 93, 84))) -le 3) { $hoverOldRed++ }
            }
        }
        $nativeCloseHoverFoundInAnyScreenshot = $hoverGreen -ge 100 -and $hoverWhite -ge 2
        if ($nativeCloseHoverFoundInAnyScreenshot) {
            $report.Add("PASS $([IO.Path]::GetFileName($CloseHoverScreenshot)): native FluentDesignForm close-button hover verified: green #50C090 background ($hoverGreen target-like pixels) with white X glyph ($hoverWhite white pixels).")
        } else {
            $failed = $true
            $report.Add("FAIL $([IO.Path]::GetFileName($CloseHoverScreenshot)): native close hover was not rendered as #50C090 + white X; green=$hoverGreen, white-glyph=$hoverWhite, stale-red=$hoverOldRed.")
        }
        if ($hoverOldRed -gt 0) {
            $failed = $true
            $report.Add("FAIL $([IO.Path]::GetFileName($CloseHoverScreenshot)): stale red caption-button pixels detected ($hoverOldRed within RGB-distance <=3 of rgb(190,93,84)).")
        }
    }
    finally {
        $hoverBitmap.Dispose()
    }
}

if (-not $gridAccentFoundInAnyScreenshot -and -not $selectedRowFoundInAnyScreenshot) {
    $failed = $true
    $report.Add('FAIL: no screenshot contained either a substantial application accent region outside the navigation rail or the requested #1A2028 selected Connections-row region. A sidebar-only match is insufficient to prove the Connections content state rendered correctly.')
}

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path (Get-Location) 'ui-color-audit.txt'
}

# Persist the report from inside the audit script itself. Do not rely on Tee-Object to
# capture Write-Host output: in PowerShell 7, Write-Host writes to the Information stream,
# so a normal success-stream pipeline can legitimately produce no report file even when
# the visible audit output says PASS. The report is therefore written before the script
# returns or throws.
$reportLines = [System.Collections.Generic.List[string]]::new()
foreach ($line in $report) {
    $reportLines.Add($line)
}

if ($failed) {
    $reportLines.Add('Rendered UI color verification: FAIL.')
} else {
    $reportLines.Add('Rendered UI color verification: PASS.')
}

$reportDirectory = Split-Path -Parent $ReportPath
if (-not [string]::IsNullOrWhiteSpace($reportDirectory)) {
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
}
Set-Content -LiteralPath $ReportPath -Value $reportLines -Encoding utf8

foreach ($line in $reportLines) { Write-Host $line }
if ($failed) {
    throw 'Rendered UI color verification failed. The build must not be considered visually verified.'
}
