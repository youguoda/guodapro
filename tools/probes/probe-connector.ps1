# probe-connector.ps1 - the preview connector curve (ticket 20 / U-03).
#
# Parks the bar low on the primary screen so the preview panel must be
# clamped upward by the placement rules - the one everyday arrangement where
# panel and row are genuinely OFFSET and the connector curve appears (the
# aligned case draws the 2 DIP accent bridge instead, by design). Holds
# Space on a lower row, then:
#   - defect: accent curve ink inside the preview rectangle. The capture is
#     of the CONNECTOR sheet itself (the curve's own window): a stale DPI
#     factor renders the curve deep inside the panel, and a z-order slip
#     presses it over the panel. The panel rect is shrunk a few px so a dot
#     legitimately centred ON the edge does not count.
#   - defect: the preview overlapping the bar (U-03 P1: two floats never
#     overlap; the panel anchors to the bar's outer edge + 8).
#
# ASCII only (see lib.ps1 header).

param([string]$Exe = '')

. (Join-Path $PSScriptRoot 'lib.ps1')

if ($Exe -eq '') { $Exe = Get-DefaultProbeExe }
Reset-Checks

$VK_DOWN  = 0x28
$VK_SPACE = 0x20

$dataDir = Join-Path $env:TEMP 'shiyu-probe-run\connector'
# Seed from the SAME build the probe runs (-o overrides leave the worktree's
# own bin absent; the seeder must reference the exe's Core, not a stale one).
& (Join-Path $PSScriptRoot 'seed.ps1') -DataDir $dataDir -AppBin (Split-Path -Parent $Exe) | Out-Null

$p = Start-ProbeApp -Exe $Exe -DataDir $dataDir -Cmd 'bar'
try {
    $bar = Wait-ProbeWindow $p.Id 384
    if ($bar -eq [IntPtr]::Zero) {
        Add-Check 'defect:connector-inside-panel' 'FAIL' 'no 384-DIP bar window of the probe pid'
        Add-Check 'defect:preview-bar-overlap' 'FAIL' 'no bar window; overlap unmeasurable'
        return $script:Checks
    }

    Start-Sleep -Milliseconds 1200   # entrance + content settle
    $barRect = Get-WindowRectInfo $bar 384

    # Park the bar low: its lower rows sit past the work-area bottom, so the
    # panel placement clamps the panel UP and the offset earns the curve.
    Add-Type -AssemblyName System.Windows.Forms
    $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
    $barTop = $wa.Bottom - [int][Math]::Round(230 * $barRect.Scale)
    # SWP_NOSIZE(0x1) | SWP_NOZORDER(0x4) | SWP_NOACTIVATE(0x10)
    [void][Shiyu.Probe.Native]::SetWindowPos($bar, [IntPtr]::Zero, 100, $barTop, 0, 0, 0x15)
    Start-Sleep -Milliseconds 600

    # Nine Downs select the long-text row (row 9 of the seeded list): its
    # panel is tall, so the upward clamp - and the offset - are large.
    for ($i = 0; $i -lt 9; $i++) {
        Send-ProbeKey $bar $VK_DOWN
        Start-Sleep -Milliseconds 120
    }
    Start-Sleep -Milliseconds 700

    # Space held (no key-up) previews the active row in full.
    Send-ProbeKey $bar $VK_SPACE
    Start-Sleep -Milliseconds 1600   # panel measure + placement + connector draw

    # Classify the pid's visible windows: the bar is known; the preview
    # carries a window title, the connector sheet carries none. The sheet
    # sizes itself to the curve - right after Space it can still be mid-
    # layout (observed 257x66 instead of the spanning size), so a too-small
    # sheet re-waits and re-classifies before anyone measures it.
    $preview = [IntPtr]::Zero
    $sheet = [IntPtr]::Zero
    for ($attempt = 0; $attempt -lt 3; $attempt++) {
        $preview = [IntPtr]::Zero
        $sheet = [IntPtr]::Zero
        foreach ($h in [Shiyu.Probe.Native]::ListWindows()) {
            if ([Shiyu.Probe.Native]::PidOf($h) -ne $p.Id) { continue }
            if ($h -eq $bar) { continue }
            if ([Shiyu.Probe.Native]::TitleLenOf($h) -gt 0) { $preview = $h } else { $sheet = $h }
        }

        if ($sheet -ne [IntPtr]::Zero) {
            $sr = [Shiyu.Probe.Native]::RectOf($sheet)
            if ($sr[2] -ge 200 -and $sr[3] -ge 200) { break }
        }

        Start-Sleep -Milliseconds 900   # let the connector settle to full size
    }

    if ($preview -eq [IntPtr]::Zero) {
        Add-Check 'defect:connector-inside-panel' 'FAIL' 'preview window not found (Space path broken?)'
        Add-Check 'defect:preview-bar-overlap' 'FAIL' 'preview window not found'
        return $script:Checks
    }

    # --- defect: preview overlapping the bar -----------------------------
    $barRect = Get-WindowRectInfo $bar 384
    $pv = [Shiyu.Probe.Native]::RectOf($preview)
    $ovW = [Math]::Max(0, [Math]::Min($barRect.L + $barRect.W, $pv[0] + $pv[2]) - [Math]::Max($barRect.L, $pv[0]))
    $ovH = [Math]::Max(0, [Math]::Min($barRect.T + $barRect.H, $pv[1] + $pv[3]) - [Math]::Max($barRect.T, $pv[1]))
    $overlapPx = $ovW * $ovH
    Add-Check 'defect:preview-bar-overlap' `
        $(if ($overlapPx -eq 0) { 'PASS' } else { 'FAIL' }) `
        ("bar {0}x{1} at {2},{3}; preview {4}x{5} at {6},{7}; overlap {8} px^2 (anchor: bar outer edge + 8)" -f `
            $barRect.W, $barRect.H, $barRect.L, $barRect.T, $pv[2], $pv[3], $pv[0], $pv[1], $overlapPx)

    # --- defect: curve ink inside the panel ------------------------------
    if ($sheet -eq [IntPtr]::Zero) {
        # No sheet at all: either the bridge took over (offset not met) or
        # the connector was never created. Both leave nothing to leak, but
        # only the bridge is the designed answer - report it honestly.
        Add-Check 'defect:connector-inside-panel' 'FAIL' `
            'connector sheet window not found (curve never appeared though the layout offsets panel and row)'
        return $script:Checks
    }

    $sh = [Shiyu.Probe.Native]::RectOf($sheet)

    # The sheet must SPAN panel and row for "inside the panel" to mean
    # anything. A sheet smaller than the panel is the aligned arrangement:
    # the 2 DIP accent bridge is drawn by design and there is no offset
    # curve to leak - reporting that honestly (SKIP) instead of measuring
    # the whole sheet as if it were the panel interior (a false FAIL).
    if ($sh[2] -lt $pv[2] -or $sh[3] -lt $pv[3]) {
        Add-Check 'defect:connector-inside-panel' 'SKIP' `
            ("sheet {0}x{1} does not span the panel {2}x{3}: aligned arrangement, the 2 DIP bridge is by design" -f `
                $sh[2], $sh[3], $pv[2], $pv[3])
        Send-ProbeKey $bar $VK_SPACE -KeyUp
        return $script:Checks
    }

    $shot = Get-WindowShot $sheet
    if ($shot) { [void](Save-Shot $shot 'connector-sheet') }

    # Panel rect relative to the sheet's own origin, shrunk by an inset that
    # covers a 5 DIP endpoint dot centred ON the boundary (half the dot plus
    # anti-aliasing fringe) - ink further in than that is the defect.
    $inset = [int][Math]::Ceiling(6 * $barRect.Scale)
    # Clamp the relative rect into the sheet bitmap: on multi-monitor DPI
    # placements the sheet can move between the two rect snapshots, and a
    # negative origin crashes InkBox (out-of-bounds index) instead of
    # measuring. Clamping only ever narrows the measured rect - the "no ink
    # inside the preview" assertion keeps its meaning.
    $pl = [Math]::Max(0, ($pv[0] - $sh[0]) + $inset)
    $pt = [Math]::Max(0, ($pv[1] - $sh[1]) + $inset)
    $pw = $pv[2] - (2 * $inset)
    $ph = $pv[3] - (2 * $inset)
    if ($shot) {
        $pw = [Math]::Min($pw, $shot.Width - $pl)
        $ph = [Math]::Min($ph, $shot.Height - $pt)
    }

    if (-not $shot -or $pw -le 4 -or $ph -le 4) {
        Add-Check 'defect:connector-inside-panel' 'FAIL' `
            ("capture/rect failed (shot={0}, inner panel rect {1}x{2})" -f $(if ($shot) { 'ok' } else { 'none' }), $pw, $ph)
    } else {
        # The sheet paints on transparency: PrintWindow hands transparent
        # pixels back as black, so "ink" is anything away from black - the
        # 0.55-opacity stroke and the full-opacity dots both qualify.
        $box = [Shiyu.Probe.Pixels]::InkBox($shot, $pl, $pt, $pw, $ph, @(0, 0, 0), 24)
        $count = $box[5]
        $detail = ("sheet {0}x{1}; panel inner rect {2}x{3} at +{4},+{5} (inset {6} px); ink inside: {7} px" -f `
            $sh[2], $sh[3], $pw, $ph, $pl, $pt, $inset, $count)
        if ($count -le 2) {
            Add-Check 'defect:connector-inside-panel' 'PASS' $detail
        } else {
            Add-Check 'defect:connector-inside-panel' 'FAIL' `
                ($detail + ' - curve or endpoint pixels leaked into the preview rectangle')
        }
    }

    Send-ProbeKey $bar $VK_SPACE -KeyUp
    Start-Sleep -Milliseconds 400
} finally {
    Stop-ProbeApp $p
}

return $script:Checks
