# probe-settings.ps1 - the settings window (ticket 15).
#
# Opens the settings deep-linked to the theme item (SHIYU_PROBE_ITEM), so
# the page carrying a segmented control with a CHECKED chip is on screen.
#
# Ticket 23 moved the theme item to the "general" page of the five-page
# nav redesign and widened the window 560 -> 880 DIP; the deep link still
# lands on the item by id, and the checked chip is still the page's one
# solid accent block, so only the window-width locator changed.
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
    $hwnd = Wait-ProbeWindow $p.Id 880
    if ($hwnd -eq [IntPtr]::Zero) {
        Add-Check 'window:settings-found' 'FAIL' 'no 880-DIP window of the probe pid'
        return $script:Checks
    }
    Add-Check 'window:settings-found' 'PASS' ("hwnd={0}" -f $hwnd)
    Move-WindowToProbeSpot $hwnd 2
    Start-Sleep -Milliseconds 1800   # deep-link pulse + settle

    $rect = Get-WindowRectInfo $hwnd 880
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

    # Ticket 27 / U-29: the nav buttons (panel content, and collapsed to
    # glyphs under 760 DIP), the search box and the settings-card icons must
    # all carry names. Assert a few known ones, then sweep: every interactive
    # element on the page must be named - an unnamed control is one Narrator
    # reads as "button".
    $navWin = $null
    foreach ($w in (Get-UiaWindowsOfPid $p.Id)) {
        $r = $w.Current.BoundingRectangle
        if ([Math]::Abs($r.Width - 880 * $rect.Scale) -le 20) { $navWin = $w; break }
    }
    $navTree = if ($navWin) { Get-UiaTree $navWin } else { @() }

    $wanted = @(
        (New-Zh @(0x5E38,0x89C4)),              # nav: general
        (New-Zh @(0x5FEB,0x6377,0x952E)),       # nav: hotkeys
        (New-Zh @(0x5173,0x4E8E)),              # nav: about
        (New-Zh @(0x641C,0x7D22,0x8BBE,0x7F6E)),# search box
        (New-Zh @(0x4E3B,0x9898))               # theme card icon named its label
    )
    $nameMiss = @()
    foreach ($want in $wanted) {
        if (-not ($navTree | Where-Object { $_.Name -eq $want })) { $nameMiss += $want }
    }
    Add-Check 'a11y:settings-known-names' `
        $(if ($nameMiss.Count -eq 0) { 'PASS' } else { 'FAIL' }) `
        ("{0} of {1} known names found" -f ($wanted.Count - $nameMiss.Count), $wanted.Count)

    # ScrollBar template parts (9-px RepeatButtons) are excluded: their peers
    # belong to the scroll bar as a whole, which UIA already exposes.
    $interactive = @($navTree | Where-Object {
        $_.Type -in @('Button','CheckBox','RadioButton','ComboBox','ListItem','TabItem') `
        -and $_.ClassName -ne 'RepeatButton' })
    $unnamed = @($interactive | Where-Object { [string]::IsNullOrWhiteSpace($_.Name) })
    Add-Check 'a11y:settings-all-controls-named' `
        $(if ($unnamed.Count -eq 0) { 'PASS' } else { 'FAIL' }) `
        ("{0} interactive elements, {1} unnamed" -f $interactive.Count, $unnamed.Count)
} finally {
    Stop-ProbeApp $p
}

return $script:Checks
