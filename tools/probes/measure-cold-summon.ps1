# measure-cold-summon.ps1 - cold-summon latency gate for ticket 26 (O-38).
#
# The merge question (UI report 5.4 / ADR-0012 #8): with lightweight mode on,
# is the narrow bar's COLD summon P95 within 100 ms of the quick bar's? A
# "cold summon" is the first summon of a fresh process - with the lightweight
# policy on, every summon after a hide rebuilds from nothing, so the first
# one is exactly that state plus one window construction (the honest worst
# case; both surfaces construct their window once per process here).
#
# Method (kept deliberately structural, same as the visual probes):
#   - one isolated seeded data dir per surface, under TEMP, probe instance
#   - start the process with SHIYU_PROBE_CMD=<surface> (the probe-legal way
#     to summon - probe instances register no hotkeys)
#   - time from just before process start until the surface's window is
#     visible, found by pid + DIP width (bar 384 / quickbar 460), polling
#     tightly (12 ms, not the visual probes' 400 ms - this is a timer)
#   - stop the process; repeat N times per surface
#
# The 100 ms gate applies to the P95 delta (bar minus quickbar). Run the
# whole script twice and record the SECOND run (the first warms the file
# cache); see the ticket for the recorded numbers.
#
# ASCII only (see lib.ps1 header).

param(
    [string]$Exe = '',
    [int]$Runs = 50
)

. (Join-Path $PSScriptRoot 'lib.ps1')

if ($Exe -eq '') { $Exe = Get-DefaultProbeExe }
if (-not (Test-Path $Exe)) { throw "probe exe not found: $Exe (build src\Shiyu.App -c Debug first)" }

# Tight window poll: unlike Wait-ProbeWindow this is a timer, so it polls at
# ~12 ms instead of 400 ms. Returns elapsed milliseconds, or -1 on timeout.
function Measure-SummonOnce {
    param([string]$Surface, [double]$DipWidth, [string]$DataDir, [int]$TimeoutSec = 30)
    $candidates = @(1.0, 1.25, 1.5, 1.75, 2.0 | ForEach-Object { [int][Math]::Round($DipWidth * $_) })

    $p = Start-ProbeApp -Exe $Exe -DataDir $DataDir -Cmd $Surface
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSec)
        while ([DateTime]::UtcNow -lt $deadline) {
            foreach ($h in [Shiyu.Probe.Native]::ListWindows()) {
                if ([Shiyu.Probe.Native]::PidOf($h) -ne $p.Id) { continue }
                $r = [Shiyu.Probe.Native]::RectOf($h)
                foreach ($w in $candidates) {
                    if ([Math]::Abs($r[2] - $w) -le 8) {
                        return [double]$watch.Elapsed.TotalMilliseconds
                    }
                }
            }
            Start-Sleep -Milliseconds 12
        }
        return -1.0
    } finally {
        Stop-ProbeApp $p
    }
}

function Get-Percentile {
    param([double[]]$Sorted, [double]$P)
    $index = [Math]::Ceiling($P * $Sorted.Count) - 1
    if ($index -lt 0) { $index = 0 }
    return $Sorted[[int]$index]
}

# --- run both surfaces --------------------------------------------------------

$results = @{}

foreach ($surface in @('quickbar', 'bar')) {
    $dipWidth = if ($surface -eq 'bar') { 384 } else { 460 }
    $dataDir = Join-Path $env:TEMP ("shiyu-probe-run\qb26-cold-" + $surface)
    & (Join-Path $PSScriptRoot 'seed.ps1') -DataDir $dataDir | Out-Null

    Write-Host ("== {0}: {1} cold summons (lightweight on by default) ==" -f $surface, $Runs)
    $samples = New-Object System.Collections.Generic.List[double]
    for ($i = 0; $i -lt $Runs; $i++) {
        $ms = Measure-SummonOnce -Surface $surface -DipWidth $dipWidth -DataDir $dataDir
        if ($ms -lt 0) {
            Write-Host ("  run {0}: TIMEOUT (window never appeared)" -f ($i + 1))
            continue
        }
        $samples.Add($ms)
        Write-Host ("  run {0,3}: {1,7:N0} ms" -f ($i + 1), $ms) -NoNewline
        if (($i + 1) % 5 -eq 0) { Write-Host '' }
    }
    Write-Host ''

    $sorted = @($samples | Sort-Object)
    $stat = [pscustomobject]@{
        Surface = $surface
        N       = $sorted.Count
        Min     = [Math]::Round((Get-Percentile $sorted 0.0), 0)
        P50     = [Math]::Round((Get-Percentile $sorted 0.50), 0)
        P95     = [Math]::Round((Get-Percentile $sorted 0.95), 0)
        Max     = [Math]::Round((Get-Percentile $sorted 1.0), 0)
    }
    $results[$surface] = $stat
    Write-Host ("  {0}: n={1}  min={2}  p50={3}  p95={4}  max={5} (ms)" -f `
        $stat.Surface, $stat.N, $stat.Min, $stat.P50, $stat.P95, $stat.Max)
}

# --- the gate -----------------------------------------------------------------

if ($results['bar'] -and $results['quickbar']) {
    $delta = $results['bar'].P95 - $results['quickbar'].P95
    ""
    ("COLD-SUMMON P95: bar {0} ms vs quickbar {1} ms -> delta {2} ms (gate: <= 100 ms -> {3})" -f `
        $results['bar'].P95, $results['quickbar'].P95, $delta, $(if ($delta -le 100) { 'PASS, merge' } else { 'FAIL, fallback' }))
}
