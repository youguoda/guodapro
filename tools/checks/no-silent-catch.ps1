# Fails when a "catch (Exception ...)" block in src/ is silent.
#
# O-24: every broad catch must either log (a "Log." call) or declare itself
# expected with an "// expected:" comment explaining in one line why silence
# is correct. Anything else hides a failure nobody will ever see.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools/checks/no-silent-catch.ps1
# Exit code 0 = clean, 1 = violations found (listed on stdout).

param(
    [string]$SourceRoot = (Join-Path $PSScriptRoot "..\..\src")
)

$ErrorActionPreference = 'Stop'

# Finds the matching closing brace for the brace at $StartIndex by walking
# the source with a small state machine that skips string literals, char
# literals and comments, so braces inside text cannot fool the pairing.
function Find-BlockEnd {
    param([string]$Text, [int]$StartIndex)

    $depth = 0
    $i = $StartIndex
    $length = $Text.Length

    while ($i -lt $length) {
        $ch = $Text[$i]
        $next = if ($i + 1 -lt $length) { $Text[$i + 1] } else { [char]0 }

        switch ($ch) {
            '/' {
                if ($next -eq '/') {
                    # Line comment: skip to end of line.
                    while ($i -lt $length -and $Text[$i] -ne "`n") { $i++ }
                    continue
                }
                if ($next -eq '*') {
                    # Block comment: skip to its close.
                    $i += 2
                    while ($i + 1 -lt $length -and -not ($Text[$i] -eq '*' -and $Text[$i + 1] -eq '/')) { $i++ }
                    $i++
                    $i++
                    continue
                }
                break
            }
            '"' {
                # String literal (regular or verbatim): both use "" escaping.
                $i++
                while ($i -lt $length) {
                    if ($Text[$i] -eq '"') {
                        if ($i + 1 -lt $length -and $Text[$i + 1] -eq '"') { $i += 2; continue }
                        break
                    }
                    if ($Text[$i] -eq '`') { $i++ }  # escaped char in a regular string
                    $i++
                }
                break
            }
            "'" {
                # Char literal.
                $i++
                while ($i -lt $length) {
                    if ($Text[$i] -eq "'") {
                        if ($i + 1 -lt $length -and $Text[$i + 1] -eq "'") { $i += 2; continue }
                        break
                    }
                    if ($Text[$i] -eq '\') { $i++ }
                    $i++
                }
                break
            }
            '{' { $depth++; break }
            '}' {
                $depth--
                if ($depth -eq 0) { return $i }
                break
            }
        }
        $i++
    }
    return -1
}

$violations = New-Object System.Collections.Generic.List[string]
$scanned = 0

Get-ChildItem -Path $SourceRoot -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object {
        $path = $_.FullName
        $text = [System.IO.File]::ReadAllText($path)
        $scanned++

        $searchFrom = 0
        while ($true) {
            $hit = $text.IndexOf('catch (Exception', $searchFrom, [System.StringComparison]::Ordinal)
            if ($hit -lt 0) { break }

            $brace = $text.IndexOf('{', $hit)
            if ($brace -lt 0) { break }

            $end = Find-BlockEnd -Text $text -StartIndex $brace
            if ($end -lt 0) { break }

            $block = $text.Substring($brace, $end - $brace + 1)
            $line = ($text.Substring(0, $hit) -split "`n").Count

            # -cmatch, not -match: the default is case-insensitive, and a
            # comment like "not worth a dialog." would count as a Log. call.
            if (-not ($block -cmatch 'Log\.') -and -not ($block -cmatch '//\s*expected:')) {
                $relative = $path.Substring((Get-Item $SourceRoot).FullName.Length + 1)
                $violations.Add("${relative}:$line")
            }

            $searchFrom = $end + 1
        }
    }

if ($violations.Count -gt 0) {
    Write-Output "no-silent-catch: $($violations.Count) silent catch (Exception) block(s) in src/:"
    $violations | ForEach-Object { Write-Output "  $_" }
    Write-Output "Each must log (a 'Log.' call) or carry an '// expected:' comment."
    exit 1
}

Write-Output "no-silent-catch: clean ($scanned files scanned)."
exit 0
