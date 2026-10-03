# visual-matrix.ps1 - capture every interface x state x combination (user
# request 2026-10-03). Screenshots land in %TEMP%\shiyu-visual\<name>.png and
# a PASS/FAIL table in results.txt; a human (the developer, with image
# review) then walks the folder. ASCII only.
#
# Plan: docs/visual-test-plan.md. Scenes are named A1..G per that matrix so
# findings can cite the scene id. Wait-ProbeWindow returns an hwnd; the UIA
# element for the same window comes from Get-UiaWin (by DIP width + scale).

param(
    [string]$Exe = '',
    [string]$Only = ''          # comma list of scene ids to run (default all)
)

. (Join-Path $PSScriptRoot 'lib.ps1')

if ($Exe -eq '') { $Exe = Get-DefaultProbeExe }
if (-not (Test-Path $Exe)) { throw "probe exe not found: $Exe" }

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName System.Drawing
$script:OutDir = Join-Path $env:TEMP 'shiyu-visual'
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

function New-DataDir([string]$Name, [hashtable]$Settings = @{}) {
    $dir = Join-Path $env:TEMP ("shiyu-vm-" + $Name)
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    & (Join-Path $PSScriptRoot 'seed.ps1') -DataDir $dir -AppBin (Split-Path $Exe -Parent) | Out-Null
    if ($Settings.Count -gt 0) {
        $json = Join-Path $dir 'settings.json'
        $obj = Get-Content $json -Raw | ConvertFrom-Json
        foreach ($k in $Settings.Keys) { $obj.$k = $Settings[$k] }
        $obj | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $json
    }
    return $dir
}

$script:Matrix = New-Object System.Collections.ArrayList
function Shot([string]$Scene, [scriptblock]$Body) {
    if ($Only -ne '' -and ((',' + $Only + ',') -notlike ('*,' + $Scene + ',*'))) { return }
    try {
        $path = Join-Path $OutDir ($Scene + '.png')
        & $Body $path
        [void]$Matrix.Add([pscustomobject]@{ Scene = $Scene; Status = 'PASS'; Detail = (Split-Path $path -Leaf) })
        Write-Host ("  [OK  ] {0}" -f $Scene)
    } catch {
        [void]$Matrix.Add([pscustomobject]@{ Scene = $Scene; Status = 'FAIL'; Detail = $_.Exception.Message })
        Write-Host ("  [FAIL] {0}: {1}" -f $Scene, $_.Exception.Message)
    }
}

# hwnd -> UIA element of the same window, matched by handle: the bar spawns
# at the cursor, so which monitor (and scale) it lands on changes run to run
# and a width match misses; the handle is exact.
function Get-UiaWin([int]$ProcessId, [double]$DipWidth, [IntPtr]$Hwnd) {
    foreach ($w in (Get-UiaWindowsOfPid $ProcessId)) {
        if ([IntPtr]$w.Current.NativeWindowHandle -eq $Hwnd) { return $w }
    }
    # fallback: nearest width at any common scale (old callers' behavior)
    $best = $null; $bestDiff = [double]::MaxValue
    foreach ($w in (Get-UiaWindowsOfPid $ProcessId)) {
        $r = $w.Current.BoundingRectangle
        if ($r.Width -le 0) { continue }
        foreach ($s in 1.0, 1.25, 1.5, 1.75, 2.0) {
            $diff = [Math]::Abs($r.Width - $DipWidth * $s)
            if ($diff -lt $bestDiff) { $bestDiff = $diff; $best = $w }
        }
    }
    if ($bestDiff -le 80) { return $best }
    return $null
}

function Capture-Hwnd([IntPtr]$Hwnd, [string]$Path) {
    $shot = Get-WindowShot $Hwnd
    if (-not $shot) { throw 'screenshot failed' }
    $shot.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $shot.Dispose()
}

function Click-Element([System.Windows.Automation.AutomationElement]$Root, [scriptblock]$Match) {
    $target = Get-UiaTree $Root | Where-Object $Match | Select-Object -First 1
    if (-not $target) { throw 'target element not found' }
    Click-Item $target
}

function Click-Item($Target) {
    # GetCurrentPattern THROWS (not null) for unsupported patterns; the chip
    # peers expose Toggle but not Invoke, menu rows the reverse.
    try {
        $inv = $Target.Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
        if ($inv) { $inv.Invoke(); return }
    } catch { }
    try {
        $tog = $Target.Element.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern) -as [System.Windows.Automation.TogglePattern]
        if ($tog) { $tog.Toggle(); return }
    } catch { }
    try {
        $sel = $Target.Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern) -as [System.Windows.Automation.SelectionItemPattern]
        if ($sel) { $sel.Select(); return }
    } catch { }
    throw 'no invoke/toggle/selection pattern on target'
}

$VK = @{ Ctrl = 0x11; Down = 0x28; Up = 0x26; Enter = 0x0D; Esc = 0x1B; Space = 0x20; Tab = 0x09; F6 = 0x75; F1 = 0x70 }

# =====================================================================
# A - bar family
# =====================================================================
Shot 'A1' {
    param($path)
    $d = New-DataDir 'bar'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'bar'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 384
        if ($hwnd -eq [IntPtr]::Zero) { throw 'bar window not found' }
        Start-Sleep -Seconds 2
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'A2' {
    param($path)
    $d = New-DataDir 'bar2'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'bar'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 384
        if ($hwnd -eq [IntPtr]::Zero) { throw 'bar window not found' }
        Send-ProbeKey $hwnd $VK.Ctrl
        Start-Sleep -Milliseconds 700
        Capture-Hwnd $hwnd $path
        Send-ProbeKey $hwnd $VK.Ctrl -KeyUp
    } finally { Stop-ProbeApp $p }
}

Shot 'A5' {
    param($path)
    $d = New-DataDir 'bar5'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'bar'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 384
        if ($hwnd -eq [IntPtr]::Zero) { throw 'bar window not found' }
        foreach ($c in 0x5A, 0x5A) { Send-ProbeKey $hwnd $c; Start-Sleep -Milliseconds 150 }
        Start-Sleep -Milliseconds 600
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'A6' {
    param($path)
    $d = New-DataDir 'bar6'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'bar'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 384
        if ($hwnd -eq [IntPtr]::Zero) { throw 'bar window not found' }
        $win = Get-UiaWin $p.Id 384 $hwnd
        if (-not $win) { throw 'bar UIA element not found' }
        # Names come from AutomationProperties.Name on the chips (ASCII script:
        # Chinese matched via codepoints, not literals).
        $favName = New-Zh 0x53EA,0x770B,0x6536,0x85CF          # zhi kan shoucang
        $chipName = New-Zh 0x7C7B,0x578B,0xFF1A,0x56FE,0x7247  # leixing: tupian
        $fav = Get-UiaTree $win | Where-Object { $_.Name -eq $favName } | Select-Object -First 1
        if (-not $fav) { throw 'favorite filter chip not found' }
        $chip = Get-UiaTree $win | Where-Object { $_.Name -eq $chipName } | Select-Object -First 1
        if (-not $chip) { throw 'kind chip not found' }
        Click-Item $chip
        Start-Sleep -Milliseconds 500
        Click-Item $fav
        Start-Sleep -Milliseconds 600
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'A7' {
    param($path)
    $d = New-DataDir 'qb'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'quickbar'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 384
        if ($hwnd -eq [IntPtr]::Zero) { throw 'paste-mode bar not found' }
        Start-Sleep -Seconds 2
        Send-ProbeKey $hwnd $VK.Down -Extended
        Start-Sleep -Milliseconds 400
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'A8' {
    param($path)
    $d = New-DataDir 'bar8'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'bar'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 384
        if ($hwnd -eq [IntPtr]::Zero) { throw 'bar window not found' }
        # PostMessage'd letters only land when the bar holds keyboard focus,
        # which the unactivated probe window does not; set the search text
        # through the value pattern instead (same TextChanged path).
        $win = Get-UiaWin $p.Id 384 $hwnd
        if (-not $win) { throw 'bar UIA element not found' }
        $edit = Get-UiaTree $win | Where-Object { $_.Type -eq 'Edit' } | Select-Object -First 1
        if (-not $edit) { throw 'search box not found' }
        $val = $edit.Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern) -as [System.Windows.Automation.ValuePattern]
        if (-not $val) { throw 'search box has no value pattern' }
        $val.SetValue('qqq')
        Start-Sleep -Milliseconds 800
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'A10' {
    param($path)
    $d = New-DataDir 'bar10'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'bar'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 384
        if ($hwnd -eq [IntPtr]::Zero) { throw 'bar window not found' }
        $win = Get-UiaWin $p.Id 384 $hwnd
        if (-not $win) { throw 'bar UIA element not found' }
        # The group drawer button is named via AutomationProperties.Name.
        $grpName = New-Zh 0x5206,0x7EC4,0x83DC,0x5355          # fenzu caidan
        Click-Element $win { $_.Type -eq 'Button' -and $_.Name -eq $grpName }
        Start-Sleep -Milliseconds 800
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

# =====================================================================
# B - preview (space held)
# =====================================================================
Shot 'B1' {
    param($path)
    $d = New-DataDir 'preview'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'bar'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 384
        if ($hwnd -eq [IntPtr]::Zero) { throw 'bar window not found' }
        # The preview follows the pointer, not a key: park the mouse on a
        # card at an unoccluded spot and capture whatever window appears.
        Move-WindowToProbeSpot $hwnd 4
        Start-Sleep -Seconds 2
        $win = Get-UiaWin $p.Id 384 $hwnd
        if (-not $win) { throw 'bar UIA element not found' }
        $card = Get-UiaTree $win | Where-Object { $_.Type -eq 'ListItem' -and $_.T -gt 0 -and $_.H -gt 60 -and $_.H -lt 120 } |
            Sort-Object T | Select-Object -Skip 1 -First 1
        if (-not $card) { throw 'no card to hover' }
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public class B1Mouse {
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
}
'@ -ErrorAction SilentlyContinue
        [void][B1Mouse]::SetCursorPos(($card.L + 60), ($card.T + [int]($card.H / 2)))
        Start-Sleep -Milliseconds 1200
        # the preview is its own top-level hwnd (~360 DIP) but not a UIA
        # top-level element; find it through Win32 by walking owned windows.
        Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class B1Enum {
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 public static List<IntPtr> VisibleOf(uint pid) {
   var list = new List<IntPtr>();
   EnumWindows((h, l) => {
     uint wpid; GetWindowThreadProcessId(h, out wpid);
     if (wpid == pid && IsWindowVisible(h)) { list.Add(h); }
     return true;
   }, IntPtr.Zero);
   return list;
 }
}
'@ -ErrorAction SilentlyContinue
        $prevHwnd = [IntPtr]::Zero
        foreach ($h in [B1Enum]::VisibleOf([uint32]$p.Id)) {
            if ($h -ne $hwnd) { $prevHwnd = $h; break }
        }
        if ($prevHwnd -eq [IntPtr]::Zero) { throw 'preview window did not open' }
        Capture-Hwnd $prevHwnd $path
        [void][B1Mouse]::SetCursorPos(60, 60)
    } finally { Stop-ProbeApp $p }
}

# =====================================================================
# C - panel
# =====================================================================
$fakeBackendJob = $null
function Start-FakeBackend {
    $script:fakeBackendJob = Start-Job -ScriptBlock {
        param($Port)
        $listener = New-Object System.Net.HttpListener
        $listener.Prefixes.Add("http://127.0.0.1:$Port/")
        $listener.Start()
        foreach ($i in 1..80) {
            $ctx = $listener.GetContext()
            # The panel translates through the SSE stream; a bare JSON body
            # parses as nothing on that path (visual walk 2026-10-03: C1
            # captured an empty result area). Answer with proper SSE chunks.
            $q = [char]34
            $sse = "data: {$($q)choices$($q):[{$($q)delta$($q):{$($q)content$($q):$($q)PLACEHOLDER TRANSLATED OUTPUT.$($q)}}]}`n`n" +
                   "data: {$($q)choices$($q):[{$($q)delta$($q):{}}]}`n`n" +
                   "data: [DONE]`n`n"
            $bytes = [Text.Encoding]::UTF8.GetBytes($sse)
            $ctx.Response.ContentType = 'text/event-stream'
            $ctx.Response.ContentLength64 = $bytes.Length
            $ctx.Response.OutputStream.Write($bytes, 0, $bytes.Length)
            $ctx.Response.OutputStream.Flush()
            $ctx.Response.Close()
        }
        $listener.Close()
    } -ArgumentList 18731
    Start-Sleep -Seconds 2
}

function Set-LocalBackend([string]$Dir) {
    $json = Join-Path $Dir 'settings.json'
    $obj = Get-Content $json -Raw | ConvertFrom-Json
    $obj.BackendBaseUrl = 'http://127.0.0.1:18731/v1'
    $obj.BackendModel = 'fake'
    $obj.BackendApiKey = 'k'
    $obj | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $json
}

Shot 'C5' {
    param($path)
    $d = New-DataDir 'panel5'   # seed writes a broken backend -> error state
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'panel' -Text 'Placeholder sentence for probe.'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 420
        if ($hwnd -eq [IntPtr]::Zero) { throw 'panel window not found' }
        Start-Sleep -Seconds 7
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'C1' {
    param($path)
    Start-FakeBackend
    $d = New-DataDir 'panel1'
    Set-LocalBackend $d
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'panel' -Text 'Placeholder sentence.'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 420
        if ($hwnd -eq [IntPtr]::Zero) { throw 'panel window not found' }
        Start-Sleep -Seconds 5
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'C3' {
    param($path)
    if (-not $script:fakeBackendJob) { Start-FakeBackend }
    $d = New-DataDir 'panel3'
    Set-LocalBackend $d
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'panel' -Text 'Placeholder'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 420
        if ($hwnd -eq [IntPtr]::Zero) { throw 'panel window not found' }
        Start-Sleep -Seconds 5
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

# =====================================================================
# D - library family
# =====================================================================
Shot 'D1' {
    param($path)
    $d = New-DataDir 'lib'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'library'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 1100 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'library window not found' }
        Start-Sleep -Seconds 2
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'D2' {
    param($path)
    $d = New-DataDir 'lib2'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'library'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 1100 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'library window not found' }
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public class VMResize {
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
}
'@
        [VMResize]::SetWindowPos($hwnd, [IntPtr]::Zero, 60, 60, [int](720 * 1.5), [int](480 * 1.5), 0x0040) | Out-Null
        Start-Sleep -Seconds 2
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'D4' {
    param($path)
    $d = Join-Path $env:TEMP 'shiyu-vm-empty'
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'library'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 1100 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'library window not found' }
        Start-Sleep -Seconds 2
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'D8' {
    param($path)
    $d = New-DataDir 'lib8'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'library'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 1100 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'library window not found' }
        $win = Get-UiaWin $p.Id 1100 $hwnd
        $rows = @(Get-UiaTree $win | Where-Object { $_.Type -eq 'ListItem' } | Select-Object -First 3)
        foreach ($r in $rows) {
            $sel = $r.Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern) -as [System.Windows.Automation.SelectionItemPattern]
            if ($sel) { $sel.AddToSelection() }
        }
        Start-Sleep -Milliseconds 800
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'D9' {
    param($path)
    $d = New-DataDir 'lib9'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'library'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 1100 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'library window not found' }
        Send-ProbeKey $hwnd $VK.Ctrl
        Start-Sleep -Milliseconds 700
        Capture-Hwnd $hwnd $path
        Send-ProbeKey $hwnd $VK.Ctrl -KeyUp
    } finally { Stop-ProbeApp $p }
}

Shot 'D12' {
    param($path)
    $d = New-DataDir 'lib12'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'library'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 1100 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'library window not found' }
        $win = Get-UiaWin $p.Id 1100 $hwnd
        $moreName = New-Zh 0x66F4,0x591A,0x64CD,0x4F5C          # gengduo caozuo
        Click-Element $win { $_.Type -eq 'Button' -and $_.Name -eq $moreName }
        Start-Sleep -Milliseconds 800
        # The menu is a WPF Popup: its content lives in an owned hwnd that
        # UIA does not list as a top-level window; walk Win32 for it.
        Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
public class D12Enum {
 [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
 public delegate bool EnumProc(IntPtr h, IntPtr l);
 [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
 [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
 public static List<IntPtr> VisibleOf(uint pid) {
   var list = new List<IntPtr>();
   EnumWindows((h, l) => {
     uint wpid; GetWindowThreadProcessId(h, out wpid);
     if (wpid == pid && IsWindowVisible(h)) { list.Add(h); }
     return true;
   }, IntPtr.Zero);
   return list;
 }
}
'@ -ErrorAction SilentlyContinue
        $popupHwnd = [IntPtr]::Zero
        foreach ($h in [D12Enum]::VisibleOf([uint32]$p.Id)) {
            if ($h -ne $hwnd) { $popupHwnd = $h; break }
        }
        if ($popupHwnd -eq [IntPtr]::Zero) { throw 'menu popup window not found' }
        Capture-Hwnd $popupHwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'D14' {
    param($path)
    $d = New-DataDir 'lib14'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'library'
    try {
        $libHwnd = Wait-ProbeWindow $p.Id 1100 30
        if ($libHwnd -eq [IntPtr]::Zero) { throw 'library window not found' }
        Send-ProbeKey $libHwnd $VK.F1 -Extended
        Start-Sleep -Milliseconds 1000
        # the cheatsheet is its own top-level dialog; find a new 360-DIP window
        $dlg = [IntPtr]::Zero
        $deadline = (Get-Date).AddSeconds(5)
        while ($dlg -eq [IntPtr]::Zero -and (Get-Date) -lt $deadline) {
            $dlg = Wait-ProbeWindow $p.Id 360 2
            if ($dlg -eq [IntPtr]::Zero) { Start-Sleep -Milliseconds 300 }
        }
        if ($dlg -ne [IntPtr]::Zero) { Capture-Hwnd $dlg $path } else { Capture-Hwnd $libHwnd $path }
    } finally { Stop-ProbeApp $p }
}

Shot 'D17' {
    param($path)
    $d = New-DataDir 'lib17'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'library'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 1100 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'library window not found' }
        # Key-driven deletes proved flaky here (protected rows swallow D and
        # the toast never shows); select an unprotected row and press the
        # toolbar delete button through UIA, then shoot fast - the undo toast
        # carries a 5s progress line.
        $win = Get-UiaWin $p.Id 1100 $hwnd
        if (-not $win) { throw 'library UIA element not found' }
        $plain = Get-UiaTree $win | Where-Object {
            $_.Type -eq 'ListItem' -and $_.Name -match 'Id = 1,'   # short plain text row
        } | Select-Object -First 1
        if (-not $plain) { throw 'plain row not found' }
        $sel = $plain.Element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern) -as [System.Windows.Automation.SelectionItemPattern]
        if (-not $sel) { throw 'row has no selection pattern' }
        $sel.Select()
        Start-Sleep -Milliseconds 400
        $delName = New-Zh 0x5220,0x9664,0xFF08,0x44,0xFF09    # shanchu (D)
        $del = Get-UiaTree $win | Where-Object { $_.Type -eq 'Button' -and $_.Name -eq $delName } | Select-Object -First 1
        if (-not $del) { throw 'delete button not found' }
        Click-Item $del
        Start-Sleep -Milliseconds 400
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

# =====================================================================
# E - settings
# =====================================================================
Shot 'E1' {
    param($path)
    $d = New-DataDir 'set'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'settings'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 880 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'settings window not found' }
        Start-Sleep -Seconds 2
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'E7' {
    param($path)
    $d = New-DataDir 'set7'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'settings'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 880 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'settings window not found' }
        Add-Type @'
using System;
using System.Runtime.InteropServices;
public class VMResize2 {
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
}
'@
        [VMResize2]::SetWindowPos($hwnd, [IntPtr]::Zero, 40, 40, [int](640 * 1.5), [int](560 * 1.5), 0x0040) | Out-Null
        Start-Sleep -Seconds 2
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'E8' {
    param($path)
    $d = New-DataDir 'set8'
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'settings'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 880 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'settings window not found' }
        # Typing needs the search box focused (Ctrl+F does that); the value
        # pattern reaches the same TextChanged path without keyboard focus.
        $win = Get-UiaWin $p.Id 880 $hwnd
        if (-not $win) { throw 'settings UIA element not found' }
        $edit = Get-UiaTree $win | Where-Object { $_.Type -eq 'Edit' } | Select-Object -First 1
        if (-not $edit) { throw 'settings search box not found' }
        $val = $edit.Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern) -as [System.Windows.Automation.ValuePattern]
        if (-not $val) { throw 'settings search box has no value pattern' }
        $val.SetValue((New-Zh 0x5F00,0x673A))   # kai ji (startup) -> matches the autostart row
        Start-Sleep -Milliseconds 900
        Capture-Hwnd $hwnd $path
    } finally { Stop-ProbeApp $p }
}

# =====================================================================
# F - onboarding five screens
# =====================================================================
Shot 'F1' {
    param($path)
    $d = Join-Path $env:TEMP 'shiyu-vm-onb'
    if (Test-Path $d) { Remove-Item -Recurse -Force $d }
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'onboarding'
    try {
        # The wizard is the process's single visible top-level Window element;
        # width matching is flaky here because where it spawns varies. The
        # Next button is the first CJK-named button; its name spells the
        # action (xia yibu). Move to the probe spot AFTER the entrance has
        # settled, and retry the capture: PrintWindow has been observed to
        # fail on a cold first call for this window.
        $win = $null
        $deadline = (Get-Date).AddSeconds(20)
        while (-not $win -and (Get-Date) -lt $deadline) {
            foreach ($w in (Get-UiaWindowsOfPid $p.Id)) {
                if ($w.Current.ControlType.ProgrammaticName -eq 'ControlType.Window') { $win = $w; break }
            }
            if (-not $win) { Start-Sleep -Milliseconds 400 }
        }
        if (-not $win) { throw 'onboarding window not found' }
        $hwnd = [IntPtr]$win.Current.NativeWindowHandle
        Start-Sleep -Seconds 2
        Move-WindowToProbeSpot $hwnd 6
        Start-Sleep -Milliseconds 600
        $shot = $null
        for ($try = 1; $try -le 3 -and -not $shot; $try++) {
            $shot = Get-WindowShot $hwnd
            if (-not $shot) { Start-Sleep -Milliseconds 700 }
        }
        if (-not $shot) { throw 'screenshot failed after retries' }
        $shot.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
        $shot.Dispose()

        $xiaYiBu = New-Zh 0x4E0B,0x4E00,0x6B65   # next step
        foreach ($name in 'F2', 'F3', 'F4', 'F5') {
            if (-not $win) { break }
            $next = Get-UiaTree $win | Where-Object { $_.Type -eq 'Button' -and $_.Name -eq $xiaYiBu } | Select-Object -First 1
            if (-not $next) { break }
            Click-Item $next
            Start-Sleep -Milliseconds 1000
            $s2 = $null
            for ($try = 1; $try -le 3 -and -not $s2; $try++) {
                $s2 = Get-WindowShot $hwnd
                if (-not $s2) { Start-Sleep -Milliseconds 700 }
            }
            if (-not $s2) { throw ('screenshot failed for ' + $name) }
            $s2.Save((Join-Path $OutDir ($name + '.png')), [System.Drawing.Imaging.ImageFormat]::Png)
            $s2.Dispose()
            [void]$Matrix.Add([pscustomobject]@{ Scene = $name; Status = 'PASS'; Detail = $name + '.png' })
            Write-Host ("  [OK  ] {0}" -f $name)
        }
    } finally { Stop-ProbeApp $p }
}

# =====================================================================
# G - dark theme key surfaces
#
# These capture through the SCREEN DC (BitBlt), not PrintWindow: the
# material windows' bare areas are transparent on the redirect surface, so
# PrintWindow paints them white and misleads review -- what the compositor
# actually shows (SurfaceMaterial tint over DWM Mica) only exists on screen.
# The window is parked topmost at an unoccluded spot for the duration.
# =====================================================================
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class GScreenCap {
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
 [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f);
 [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
 [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
 [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
 [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
}
'@ -ErrorAction SilentlyContinue

function Capture-Screen([IntPtr]$Hwnd, [string]$Path) {
    # topmost at a known-empty spot, then blt the composited window rect
    [void][GScreenCap]::SetWindowPos($Hwnd, [IntPtr](-1), 1900, 500, 0, 0, 0x0013)
    Start-Sleep -Milliseconds 900
    $r = New-Object GScreenCap+RECT
    [void][GScreenCap]::GetWindowRect($Hwnd, [ref]$r)
    $w = $r.R - $r.L; $h = $r.B - $r.T
    if ($w -le 0 -or $h -le 0) { throw 'window rect empty' }
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    $src = [GScreenCap]::GetDC([IntPtr]::Zero)
    [void][GScreenCap]::BitBlt($dc, 0, 0, $w, $h, $src, $r.L, $r.T, 0x00CC0020)
    [void][GScreenCap]::ReleaseDC([IntPtr]::Zero, $src)
    $g.ReleaseHdc($dc)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

Shot 'G-DARK-BAR' {
    param($path)
    $d = New-DataDir 'darkbar' @{ Theme = 'Dark' }
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'bar'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 384
        if ($hwnd -eq [IntPtr]::Zero) { throw 'bar window not found' }
        Start-Sleep -Seconds 2
        Capture-Screen $hwnd $path
    } finally { Stop-ProbeApp $p }
}

Shot 'G-DARK-LIB' {
    param($path)
    $d = New-DataDir 'darklib' @{ Theme = 'Dark' }
    $p = Start-ProbeApp -Exe $Exe -DataDir $d -Cmd 'library'
    try {
        $hwnd = Wait-ProbeWindow $p.Id 1100 30
        if ($hwnd -eq [IntPtr]::Zero) { throw 'library window not found' }
        Start-Sleep -Seconds 2
        Capture-Screen $hwnd $path
    } finally { Stop-ProbeApp $p }
}

# =====================================================================
# wrap up
# =====================================================================
if ($script:fakeBackendJob) {
    Stop-Job $script:fakeBackendJob -ErrorAction SilentlyContinue
    Remove-Job $script:fakeBackendJob -Force -ErrorAction SilentlyContinue
}

$results = Join-Path $OutDir 'results.txt'
$Matrix | Format-Table -AutoSize | Out-String -Width 220 | Set-Content -Encoding UTF8 $results
$total = $Matrix.Count
$pass = @($Matrix | Where-Object Status -eq 'PASS').Count
$fail = @($Matrix | Where-Object Status -eq 'FAIL').Count
"visual matrix: $total scenes -> PASS $pass / FAIL $fail"
"screenshots: $OutDir"
