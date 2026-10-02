# probe-panel.ps1 - the translation panel (ticket 15).
#
# Starts the probe instance with the panel opened directly on a placeholder
# sentence. The seeded settings point the OwnKey backend at a scheme-less
# URL, which fails deterministically with a raw English .NET exception and
# no network access - exactly the input the panel-error defect needs.
#
# Checks:
#   - defect: the error text leaks the raw English exception (review 3.6 P0)
#   - defect: double corner radius on the outer shell (review 3.6 P0)
#
# ASCII only (see lib.ps1 header).

param([string]$Exe = '')

. (Join-Path $PSScriptRoot 'lib.ps1')

if ($Exe -eq '') { $Exe = Get-DefaultProbeExe }
Reset-Checks

$dataDir = Join-Path $env:TEMP 'shiyu-probe-run\panel'
& (Join-Path $PSScriptRoot 'seed.ps1') -DataDir $dataDir | Out-Null

$p = Start-ProbeApp -Exe $Exe -DataDir $dataDir -Cmd 'panel' `
    -Text 'Placeholder sentence for probe 15.'
try {
    $hwnd = Wait-ProbeWindow $p.Id 420
    if ($hwnd -eq [IntPtr]::Zero) {
        Add-Check 'window:panel-found' 'FAIL' 'no 420-DIP window of the probe pid'
        return $script:Checks
    }
    Add-Check 'window:panel-found' 'PASS' ("hwnd={0}" -f $hwnd)
    Move-WindowToProbeSpot $hwnd 1

    # The failure is instant (invalid request URI, no network), but the
    # panel needs a beat to render the error into the UIA tree.
    $english = $null
    $deadline = (Get-Date).AddSeconds(12)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 700
        $texts = @()
        foreach ($w in (Get-UiaWindowsOfPid $p.Id)) {
            $r = $w.Current.BoundingRectangle
            if ($r.Width -gt 300 -and $r.Width -lt 700) {
                $texts += Get-UiaTree $w | Where-Object { $_.Type -eq 'Text' }
            }
        }
        foreach ($t in $texts) {
            # Skip the probe's own input sentence (the original text on
            # screen is English by design); the defect leaks a raw .NET
            # exception sentence: an English word followed by two or more
            # lowercase words, or an exception type name.
            if ($t.Name -like 'Placeholder*') { continue }
            $m = [regex]::Match($t.Name, '[A-Za-z]{2,}(?: [a-z]+){2,}|Exception')
            if ($m.Success) { $english = $m.Value; break }
        }
        if ($english) { break }
    }

    Start-Sleep -Milliseconds 400
    $rect = Get-WindowRectInfo $hwnd 420
    $shot = Get-WindowShot $hwnd
    if ($shot) { [void](Save-Shot $shot 'panel-error') }

    if ($english) {
        Add-Check 'defect:panel-raw-english-error' 'FAIL' `
            ("panel text leaks a raw exception: '{0}'" -f $english)
    } else {
        Add-Check 'defect:panel-raw-english-error' 'PASS' `
            'no raw English exception text found in the panel'
    }

    if ($shot) {
        Add-CornerRadiusCheck 'panel' $shot $rect 0.0
    } else {
        Add-Check 'defect:panel-double-radius' 'FAIL' 'screenshot failed'
    }
} finally {
    Stop-ProbeApp $p
}

return $script:Checks
