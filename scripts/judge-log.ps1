# The judge of phase 1 (docs/PLAN.md): reads the watch log and says whether the service stayed alive.
# Pulls the log from the phone with adb (Debug build, run-as) unless -LogPath is given, and prints a Markdown
# report: the window, the capture lines, the largest gap between them, the lines with missing or failed frames,
# starts, stops, restarts and errors, and battery and temperature from the heartbeats.
#
# Usage:  powershell -File scripts\judge-log.ps1 [-LogPath file] [-From 2026-10-23T05:42Z] [-To ...] [-Out report.md]
# A gap between capture lines longer than -MaxGapSeconds (default 120 s, two missed minutes) is a hole.

param(
    [string]$LogPath,
    [string]$From,
    [string]$To,
    [string]$Out,
    [int]$MaxGapSeconds = 120,
    [int]$MinFrames = 25
)

$ErrorActionPreference = 'Stop'
$adb = "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe"
$root = Split-Path -Parent $PSScriptRoot

if (-not $LogPath) {
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd-HHmm')
    $LogPath = Join-Path $root "docs\verificacion\$stamp-vigia.log"
    # Rotated files first (oldest), then the current one, into a single file.
    $lines = @()
    foreach ($name in @('vigia.4.log', 'vigia.3.log', 'vigia.2.log', 'vigia.1.log', 'vigia.log')) {
        $content = & $adb shell "run-as com.vigiahud.app cat files/logs/$name 2>/dev/null"
        if ($LASTEXITCODE -eq 0 -and $content) { $lines += $content }
    }
    if (-not $lines) { throw "No log could be read from the phone. Is it on adb in file-transfer mode?" }
    [IO.File]::WriteAllLines($LogPath, $lines, [Text.UTF8Encoding]::new($false))
    Write-Host "Log pulled to $LogPath ($($lines.Count) lines)"
}

$culture = [Globalization.CultureInfo]::InvariantCulture
$entries = foreach ($line in Get-Content $LogPath -Encoding UTF8) {
    $parts = $line -split "`t", 3
    if ($parts.Count -ne 3) { continue }
    $at = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParseExact($parts[0], "yyyy-MM-dd'T'HH:mm:ss.fff'Z'", $culture, 'AssumeUniversal', [ref]$at)) { continue }
    [pscustomobject]@{ At = $at.ToUniversalTime(); Kind = $parts[1]; Message = $parts[2] }
}

if ($From) { $fromAt = [DateTimeOffset]::Parse($From, $culture).ToUniversalTime(); $entries = $entries | Where-Object { $_.At -ge $fromAt } }
if ($To) { $toAt = [DateTimeOffset]::Parse($To, $culture).ToUniversalTime(); $entries = $entries | Where-Object { $_.At -le $toAt } }
$entries = @($entries)
if ($entries.Count -eq 0) { throw "No entries in the window." }

function Field($message, $key) {
    $m = [regex]::Match($message, "(?:^|\s)$key=([^\s]+)")
    if ($m.Success) { $m.Groups[1].Value } else { $null }
}
function Fmt($t) { $t.ToString("yyyy-MM-dd HH:mm:ss'Z'", $culture) }
# [int] rounds in PowerShell (1.58 h -> 2); Floor keeps 1 h 34 m as 1 h 34 m.
function Span($s) { if ($s.TotalDays -ge 1) { "{0}d {1:00}h {2:00}m" -f [math]::Floor($s.TotalDays), $s.Hours, $s.Minutes } elseif ($s.TotalHours -ge 1) { "{0}h {1:00}m" -f [math]::Floor($s.TotalHours), $s.Minutes } else { "{0}m {1:00}s" -f [math]::Floor($s.TotalMinutes), $s.Seconds } }

$first = $entries[0].At; $last = $entries[-1].At
$captures = @($entries | Where-Object Kind -eq 'capture')
$heartbeats = @($entries | Where-Object Kind -eq 'heartbeat')
$starts = @($entries | Where-Object { $_.Kind -eq 'start' -and $_.Message -like 'service*' })
$restarts = @($starts | Where-Object Message -like '*restarted*')
$stops = @($entries | Where-Object Kind -eq 'stop')
$errors = @($entries | Where-Object Kind -eq 'error')

# Gaps between consecutive capture lines.
$maxGap = [TimeSpan]::Zero; $maxGapAt = $null; $holes = @()
for ($i = 1; $i -lt $captures.Count; $i++) {
    $gap = $captures[$i].At - $captures[$i - 1].At
    if ($gap -gt $maxGap) { $maxGap = $gap; $maxGapAt = $captures[$i - 1].At }
    if ($gap.TotalSeconds -gt $MaxGapSeconds) { $holes += [pscustomobject]@{ From = $captures[$i - 1].At; To = $captures[$i].At; Gap = $gap } }
}
$weak = @($captures | Where-Object { [int](Field $_.Message 'frames') -lt $MinFrames -or [int](Field $_.Message 'failed') -gt 0 })
$totalFrames = ($captures | ForEach-Object { [int](Field $_.Message 'frames') } | Measure-Object -Sum).Sum
$totalFailed = ($captures | ForEach-Object { [int](Field $_.Message 'failed') } | Measure-Object -Sum).Sum

$battery = @($heartbeats | ForEach-Object { $v = Field $_.Message 'battery'; if ($v -and $v -ne '?') { [int]($v.TrimEnd('%')) } })
$temps = @($heartbeats | ForEach-Object { $v = Field $_.Message 'temp'; if ($v -and $v -ne '?') { [double]($v.TrimEnd('C')) } })
$unplugged = @($heartbeats | Where-Object { (Field $_.Message 'plugged') -eq 'no' })
$lastUptime = if ($heartbeats.Count -gt 0) { Field $heartbeats[-1].Message 'uptime' } else { '-' }

$verdict = if ($holes.Count -eq 0 -and $restarts.Count -eq 0 -and $errors.Count -eq 0 -and $weak.Count -eq 0) { 'PASA' } elseif ($holes.Count -eq 0 -and $restarts.Count -eq 0) { 'PASA con avisos' } else { 'FALLA' }

$report = @()
$report += "# Juez del registro: $verdict"
$report += ""
$report += "| Medida | Valor |"
$report += "|---|---|"
$report += "| Ventana | $(Fmt $first) a $(Fmt $last) ($(Span ($last - $first))) |"
$report += "| Lineas capture | $($captures.Count) (frames $totalFrames, fallos $totalFailed) |"
$report += "| Hueco maximo entre capture | $(Span $maxGap)$(if ($maxGapAt) { ' desde ' + (Fmt $maxGapAt) }) |"
$report += "| Huecos > $MaxGapSeconds s | $($holes.Count) |"
$report += "| Capture con frames < $MinFrames o fallos | $($weak.Count) |"
$report += "| Arranques del servicio / reinicios por Android | $($starts.Count) / $($restarts.Count) |"
$report += "| Paradas | $($stops.Count) |"
$report += "| Errores | $($errors.Count) |"
$report += "| Latidos | $($heartbeats.Count), ultimo uptime $lastUptime, $($unplugged.Count) sin cargador |"
if ($battery.Count -gt 0) { $report += "| Bateria | $(($battery | Measure-Object -Minimum).Minimum)% a $(($battery | Measure-Object -Maximum).Maximum)% |" }
if ($temps.Count -gt 0) { $report += "| Temperatura | $(($temps | Measure-Object -Minimum).Minimum.ToString('0.0', $culture))C a $(($temps | Measure-Object -Maximum).Maximum.ToString('0.0', $culture))C |" }
foreach ($h in $holes) { $report += ""; $report += "- Hueco: $(Fmt $h.From) a $(Fmt $h.To) ($(Span $h.Gap))" }
foreach ($e in $errors) { $report += "- Error: $(Fmt $e.At) $($e.Message)" }
foreach ($w in $weak) { $report += "- Capture floja: $(Fmt $w.At) $($w.Message)" }
foreach ($s in $starts) { $report += "- Arranque: $(Fmt $s.At) $($s.Message)" }
foreach ($s in $stops) { $report += "- Parada: $(Fmt $s.At) $($s.Message)" }

$text = $report -join "`n"
Write-Output $text
if ($Out) { [IO.File]::WriteAllText($Out, $text + "`n", [Text.UTF8Encoding]::new($false)); Write-Host "Report written to $Out" }
