# Fails when a hardcoded visual value bypasses the design tokens in src/Shiyu.App.
#
# Ticket 19 / UI report 4.5 / ADR-0012: outside Themes/, a numeric literal in
# one of these three spots is how the token system (and the contrast tests
# behind it) gets quietly bypassed:
#
#   1. A fractional Opacity on a text-bearing element -- opacity dims glyphs
#      on a path the contrast tests never see (the R6 lesson: layer with
#      token colours, never with transparency).
#   2. A numeric literal FontSize -- sizes come from Type.* (and Size.Icon*)
#      resources so the scale has one source and can be audited.
#   3. A numeric literal CornerRadius -- radii come from Radius.* resources.
#
# Scanned: .xaml and .cs under src/Shiyu.App, excluding Themes/ (the token
# consumers themselves), obj/, bin/ and generated .g.cs files.
#
# Exemption: a 'token-ok:' marker on the violating line or within the six
# lines above it -- C# uses an inline "// token-ok: reason"; XAML puts the
# marker inside the comment block that precedes the element (a comment
# cannot live inside a tag, and tags span lines). Every exemption states
# why the literal is not a token choice.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/checks/no-hardcoded-tokens.ps1
# Exit code 0 = clean, 1 = violations found (listed on stdout).

param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot "..\..\src\Shiyu.App")
)

$ErrorActionPreference = 'Stop'

# Text-bearing XAML elements: an Opacity on any of these dims rendered text.
$TextElements = @(
    'TextBlock', 'Run', 'Span', 'Italic', 'Bold', 'Underline', 'Hyperlink',
    'TextBox', 'PasswordBox', 'RichTextBox', 'Label',
    'Button', 'ToggleButton', 'RepeatButton', 'CheckBox', 'RadioButton',
    'ComboBox', 'ComboBoxItem', 'ListBoxItem', 'ListViewItem', 'TabItem',
    'MenuItem', 'ContentControl', 'TextElement'
)

# A fractional Opacity value: "0.5", ".5", "0.55" (but not 1 or 0).
$FractionalOpacity = 'Opacity\s*=\s*"?0*\.\d+'
# FontSize as a numeric literal: FontSize="12" (XAML) / FontSize = 12 (C#).
# Resource references start with { or ( -- no match, by design.
$LiteralFontSize = 'FontSize\s*=\s*"?(\d+\.?\d*)'
# CornerRadius as a numeric literal: CornerRadius="4" (XAML) /
# CornerRadius = 4 or new CornerRadius(4, ...) (C#).
$LiteralCornerRadius = 'CornerRadius\s*=\s*"?(\d+\.?\d*)|new\s+CornerRadius\s*\(\s*\d'

function Test-Exempt {
    param([string[]]$Lines, [int]$Index)

    # The marker may sit on the line itself or in the comment block above it
    # (multi-line XAML elements put the attribute several lines below the
    # comment that explains it).
    $first = [Math]::Max(0, $Index - 6)
    for ($j = $Index; $j -ge $first; $j--) {
        if ($Lines[$j] -match 'token-ok:') { return $true }
    }
    return $false
}

$violations = New-Object System.Collections.Generic.List[string]
$scanned = 0

Get-ChildItem -Path $SourceRoot -Recurse -Include *.xaml,*.cs |
    Where-Object {
        $_.FullName -notmatch '\\(bin|obj|Themes)\\' -and
        $_.Name -notmatch '\.g(\.i)?\.cs$'
    } |
    ForEach-Object {
        $path = $_.FullName
        $isXaml = $_.Extension -eq '.xaml'
        $lines = [System.IO.File]::ReadAllLines($path)
        $relative = $path.Substring((Get-Item $SourceRoot).FullName.Length + 1)
        $scanned++

        # XAML attributes may sit on continuation lines of an element whose
        # opening tag started earlier; remember the pending tag name.
        $pendingTag = $null

        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $reasons = New-Object System.Collections.Generic.List[string]

            if ($isXaml) {
                $tagHere = $null
                if ($line -match '<([A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)*)') {
                    $tagHere = $Matches[1]
                    if ($line -notmatch '>') { $pendingTag = $tagHere }
                }
                $element = if ($tagHere) { $tagHere } else { $pendingTag }
                if ($line -match '>') { $pendingTag = $null }

                if ($element -and ($TextElements -contains $element) -and ($line -match $FractionalOpacity)) {
                    $reasons.Add("text Opacity literal on <$element>")
                }
                if ($line -match $LiteralFontSize) {
                    $reasons.Add('FontSize literal')
                }
                if ($line -match $LiteralCornerRadius) {
                    $reasons.Add('CornerRadius literal')
                }
            }
            else {
                if ($line -match $LiteralFontSize) {
                    $reasons.Add('FontSize literal')
                }
                if ($line -match $LiteralCornerRadius) {
                    $reasons.Add('CornerRadius literal')
                }
            }

            if ($reasons.Count -gt 0 -and -not (Test-Exempt $lines $i)) {
                foreach ($reason in $reasons) {
                    $violations.Add("${relative}:$($i + 1): $reason")
                }
            }
        }
    }

if ($violations.Count -gt 0) {
    Write-Output "no-hardcoded-tokens: $($violations.Count) hardcoded token value(s) in ${SourceRoot}:"
    $violations | ForEach-Object { Write-Output "  $_" }
    Write-Output "Use Type.*/Size.*/Radius.* resources; exempt a line with a 'token-ok:' comment stating why."
    exit 1
}

Write-Output "no-hardcoded-tokens: clean ($scanned files scanned)."
exit 0
