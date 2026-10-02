# probe-library.ps1 - the history management window (ticket 15).
#
# Checks:
#   - the seeded history is listed (realized rows)
#   - defect: the two button groups in the bottom bar collide at the
#     default 1150-DIP width (review 3.8 P0)
#
# ASCII only (see lib.ps1 header).

param([string]$Exe = '')

. (Join-Path $PSScriptRoot 'lib.ps1')

if ($Exe -eq '') { $Exe = Get-DefaultProbeExe }
Reset-Checks

$dataDir = Join-Path $env:TEMP 'shiyu-probe-run\library'
& (Join-Path $PSScriptRoot 'seed.ps1') -DataDir $dataDir | Out-Null

$p = Start-ProbeApp -Exe $Exe -DataDir $dataDir -Cmd 'library'
try {
    $hwnd = Wait-ProbeWindow $p.Id 1150
    if ($hwnd -eq [IntPtr]::Zero) {
        Add-Check 'window:library-found' 'FAIL' 'no 1150-DIP window of the probe pid'
        return $script:Checks
    }
    Add-Check 'window:library-found' 'PASS' ("hwnd={0}" -f $hwnd)
    Move-WindowToProbeSpot $hwnd 3
    Start-Sleep -Milliseconds 1500

    $rect = Get-WindowRectInfo $hwnd 1150
    $shot = Get-WindowShot $hwnd
    if ($shot) { [void](Save-Shot $shot 'library') }

    $win = $null
    foreach ($w in (Get-UiaWindowsOfPid $p.Id)) {
        $r = $w.Current.BoundingRectangle
        if ([Math]::Abs($r.Width - 1150 * $rect.Scale) -le 20) { $win = $w; break }
    }
    $tree = if ($win) { Get-UiaTree $win } else { @() }

    $rows = @($tree | Where-Object { $_.Type -eq 'ListItem' })
    Add-Check 'data:library-rows' `
        $(if ($rows.Count -ge 5) { 'PASS' } else { 'FAIL' }) `
        ("list items realized: {0} (seeded 17)" -f $rows.Count)

    # The bottom bar: every button-sized control in the lowest band. The
    # defect puts two groups in one grid row so their rects intersect.
    $bandTop = $rect.T + $rect.H - [int](130 * $rect.Scale)
    $controls = @($tree | Where-Object {
        ($_.Type -eq 'Button' -or $_.Type -eq 'ComboBox' -or $_.Type -eq 'Edit' -or $_.Type -eq 'Calendar') `
        -and $_.T -ge $bandTop -and $_.W -ge 20 -and $_.W -le 500 -and $_.H -ge 14 -and $_.H -le 60 })

    $clashes = @()
    for ($i = 0; $i -lt $controls.Count; $i++) {
        for ($j = $i + 1; $j -lt $controls.Count; $j++) {
            $a = $controls[$i]; $b = $controls[$j]
            # skip containment (a label inside its control)
            $inside = ($a.L -le $b.L -and $a.L + $a.W -ge $b.L + $b.W -and $a.T -le $b.T -and $a.T + $a.H -ge $b.T + $b.H) `
                   -or ($b.L -le $a.L -and $b.L + $b.W -ge $a.L + $a.W -and $b.T -le $a.T -and $b.T + $b.H -ge $a.T + $a.H)
            if ($inside) { continue }
            $ox = [Math]::Min($a.L + $a.W, $b.L + $b.W) - [Math]::Max($a.L, $b.L)
            $oy = [Math]::Min($a.T + $a.H, $b.T + $b.H) - [Math]::Max($a.T, $b.T)
            if ($ox -gt 3 -and $oy -gt 3) {
                $clashes += ("{0},{1} {2}x{3} vs {4},{5} {6}x{7} (overlap {8}x{9})" -f `
                    $a.L, $a.T, $a.W, $a.H, $b.L, $b.T, $b.W, $b.H, $ox, $oy)
            }
        }
    }

    $detail = ("{0} controls in the bottom band; {1} overlapping pair(s)" -f `
        $controls.Count, $clashes.Count)
    if ($clashes.Count -gt 0) {
        $detail += '; ' + ($clashes[0..([Math]::Min(2, $clashes.Count) - 1)] -join ' | ')
        Add-Check 'defect:library-bottom-overlap' 'FAIL' $detail
    } else {
        Add-Check 'defect:library-bottom-overlap' 'PASS' $detail
    }
} finally {
    Stop-ProbeApp $p
}

return $script:Checks
