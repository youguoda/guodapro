# probe-quickbar.ps1 - the quick paste bar (ticket 15).
#
# Checks:
#   - the seeded history is listed
#   - defect: the main row text is the smallest type on the surface - it
#     falls back to the system default 12 DIP while its own meta line is 14
#     (review 3.7 P0). Measured as the main Text element's rect height in
#     DIP: a 12-DIP font yields a ~16-DIP line box, 14+ yields 18+.
#   - defect: the Enter target is nearly invisible - the selected row
#     carries no token-accent marking, only the Aero2 focus look (review
#     3.7 P0). Posted VK_DOWN moves the selection to row 2 first.
#
# ASCII only (see lib.ps1 header). No Enter is ever posted - Enter pastes
# into whatever was foreground.

param([string]$Exe = '')

. (Join-Path $PSScriptRoot 'lib.ps1')

if ($Exe -eq '') { $Exe = Get-DefaultProbeExe }
Reset-Checks

$dataDir = Join-Path $env:TEMP 'shiyu-probe-run\quickbar'
& (Join-Path $PSScriptRoot 'seed.ps1') -DataDir $dataDir | Out-Null

$VK_DOWN = 0x28
$p = Start-ProbeApp -Exe $Exe -DataDir $dataDir -Cmd 'quickbar'
try {
    $hwnd = Wait-ProbeWindow $p.Id 460
    if ($hwnd -eq [IntPtr]::Zero) {
        Add-Check 'window:quickbar-found' 'FAIL' 'no 460-DIP window of the probe pid'
        return $script:Checks
    }
    Add-Check 'window:quickbar-found' 'PASS' ("hwnd={0}" -f $hwnd)
    Move-WindowToProbeSpot $hwnd 4
    Start-Sleep -Milliseconds 1200

    $rect = Get-WindowRectInfo $hwnd 460
    $win = $null
    foreach ($w in (Get-UiaWindowsOfPid $p.Id)) {
        $r = $w.Current.BoundingRectangle
        if ([Math]::Abs($r.Width - 460 * $rect.Scale) -le 20) { $win = $w; break }
    }
    $tree = if ($win) { Get-UiaTree $win } else { @() }

    $items = @($tree | Where-Object { $_.Type -eq 'ListItem' } | Sort-Object T)
    Add-Check 'data:quickbar-rows' `
        $(if ($items.Count -ge 5) { 'PASS' } else { 'FAIL' }) `
        ("list items realized: {0} (seeded 17)" -f $items.Count)

    # --- defect: main text smaller than its own meta ------------------------
    # Row 1 renders two Text elements: main preview (no FontSize set in the
    # item template -> system default 12) and meta (Size.Caption 14). Their
    # rect heights in DIP separate the two states.
    $texts = @($tree | Where-Object { $_.Type -eq 'Text' -and $_.W -gt 30 } | Sort-Object T)
    if ($texts.Count -ge 2) {
        $main = $texts[0]
        $meta = $texts[1]
        $mainH = [Math]::Round($main.H / $rect.Scale, 1)
        $metaH = [Math]::Round($meta.H / $rect.Scale, 1)
        $detail = ("row 1 main line box {0} DIP, meta line box {1} DIP (12-DIP font ~16; 14+ ~18.6)" -f $mainH, $metaH)
        if ($mainH -lt 17.0) {
            Add-Check 'defect:quickbar-tiny-main-text' 'FAIL' $detail
        } else {
            Add-Check 'defect:quickbar-tiny-main-text' 'PASS' $detail
        }
    } else {
        Add-Check 'defect:quickbar-tiny-main-text' 'FAIL' 'row text elements not found'
    }

    # --- defect: invisible selection ---------------------------------------
    if ($items.Count -ge 2) {
        Send-ProbeKey $hwnd $VK_DOWN -Extended
        Start-Sleep -Milliseconds 500
        Send-ProbeKey $hwnd $VK_DOWN -Extended -KeyUp
        Start-Sleep -Milliseconds 500

        $shot = Get-WindowShot $hwnd
        if ($shot) { [void](Save-Shot $shot 'quickbar-selection') }
        $row2 = $items[1]
        $rl = $row2.L - $rect.L; $rt = $row2.T - $rect.T
        $rw = $row2.W; $rh = $row2.H
        if ($shot -and $rw -gt 0 -and $rh -gt 0) {
            $nLight = [Shiyu.Probe.Pixels]::CountColor($shot, $rl, $rt, $rw, $rh, $AccentLight, 40)
            $nDark  = [Shiyu.Probe.Pixels]::CountColor($shot, $rl, $rt, $rw, $rh, $AccentDark, 40)
            $accentPx = [Math]::Max($nLight, $nDark)
            $detail = ("selected row 2 at {0},{1} {2}x{3}px carries {4} accent pixels (Aero2 blue is not the token accent)" -f `
                $rl, $rt, $rw, $rh, $accentPx)
            if ($accentPx -lt 40) {
                Add-Check 'defect:quickbar-invisible-selection' 'FAIL' $detail
            } else {
                Add-Check 'defect:quickbar-invisible-selection' 'PASS' $detail
            }
        } else {
            Add-Check 'defect:quickbar-invisible-selection' 'FAIL' 'screenshot failed'
        }
    } else {
        Add-Check 'defect:quickbar-invisible-selection' 'SKIP' 'fewer than two rows realized'
    }

    # close the surface politely (Esc); never Enter - Enter pastes
    Send-ProbeKey $hwnd 0x1B
    Start-Sleep -Milliseconds 300
    Send-ProbeKey $hwnd 0x1B -KeyUp
} finally {
    Stop-ProbeApp $p
}

return $script:Checks
