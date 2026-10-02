# probe-settings.ps1 - the settings window (ticket 15).
#
# Opens the settings deep-linked to the theme item (SHIYU_PROBE_ITEM), so
# the page carrying a segmented control with a CHECKED chip is on screen.
#
# Checks:
#   - defect: the checked segment's text contrast < 4.5:1 (review 3.9 P0 -
#     the implicit TextBlock style forces Brush.Text over the accent fill)
#
# ASCII only (see lib.ps1 header).

param([string]$Exe = '')

. (Join-Path $PSScriptRoot 'lib.ps1')

if ($Exe -eq '') { $Exe = Get-DefaultProbeExe }
Reset-Checks

$dataDir = Join-Path $env:TEMP 'shiyu-probe-run\settings'
& (Join-Path $PSScriptRoot 'seed.ps1') -DataDir $dataDir | Out-Null

$p = Start-ProbeApp -Exe $Exe -DataDir $dataDir -Cmd 'settings' -Item 'theme'
try {
    $hwnd = Wait-ProbeWindow $p.Id 560
    if ($hwnd -eq [IntPtr]::Zero) {
        Add-Check 'window:settings-found' 'FAIL' 'no 560-DIP window of the probe pid'
        return $script:Checks
    }
    Add-Check 'window:settings-found' 'PASS' ("hwnd={0}" -f $hwnd)
    Move-WindowToProbeSpot $hwnd 2
    Start-Sleep -Milliseconds 1800   # deep-link pulse + settle

    $rect = Get-WindowRectInfo $hwnd 560
    $shot = Get-WindowShot $hwnd
    if (-not $shot) {
        Add-Check 'defect:settings-segment-contrast' 'FAIL' 'screenshot failed'
        return $script:Checks
    }
    [void](Save-Shot $shot 'settings-theme')

    # The checked chip is the window's one solid accent block. Its median
    # colour is the "background"; the text is the ink core inside the chip's
    # box (eroded 2px to drop the corner anti-aliasing against the page).
    # In the defect state R1 forces dark Brush.Text over the accent; fixed,
    # it is white TextOnAccent - both states measured by one contrast.
    $accents = @($AccentLight + $AccentDark)
    $blob = [Shiyu.Probe.Pixels]::AccentBlob($shot, $accents, 90)
    if ($blob[0] -ne 1) {
        Add-Check 'defect:settings-segment-contrast' 'FAIL' `
            'no solid accent chip found on the theme page (deep link failed?)'
        return $script:Checks
    }

    $bg = @($blob[6], $blob[7], $blob[8])
    $inset = [int][Math]::Ceiling(2 * $rect.Scale)
    $text = @([Shiyu.Probe.Pixels]::InkCoreColor($shot,
        $blob[1] + $inset, $blob[2] + $inset, $blob[3] - 2 * $inset, $blob[4] - 2 * $inset, $bg, 90))
    if ($text[0] -lt 0) {
        Add-Check 'defect:settings-segment-contrast' 'FAIL' 'no ink inside the checked chip'
        return $script:Checks
    }
    $ratio = Get-ContrastRatio $text $bg
    $detail = ("checked chip at {0},{1} {2}x{3}px; bg rgb={4}; text rgb={5}; contrast {6:N2}:1" -f `
        $blob[1], $blob[2], $blob[3], $blob[4], ($bg -join ','), ($text -join ','), $ratio)
    if ($ratio -lt 4.5) {
        Add-Check 'defect:settings-segment-contrast' 'FAIL' $detail
    } else {
        Add-Check 'defect:settings-segment-contrast' 'PASS' $detail
    }
} finally {
    Stop-ProbeApp $p
}

return $script:Checks
