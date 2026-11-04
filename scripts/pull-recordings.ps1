# Brings the test recordings (/grabar, DECISIONS 024) from the phone to the PC, outside the repo: they show the inside
# of a home and the repo is public. Each file is copied byte for byte (adb exec-out, read as a binary stream: piping
# it through PowerShell would turn it into text), checked against the phone by size and SHA-256, and only then
# deleted from the phone. A recording still being written is left alone.
#
#   powershell -File scripts\pull-recordings.ps1 [-Serial 9d82dcda] [-To <folder>] [-Keep]
#
# Default folder: %USERPROFILE%\.vigiahud\secuencias. -Keep leaves the copies on the phone. Needs the app installed
# from the PC (a debuggable build: "run-as" does not work on a published one).

param(
    [string]$Serial = '9d82dcda',
    [string]$Adb = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe',
    [string]$To = (Join-Path $env:USERPROFILE '.vigiahud\secuencias'),
    [switch]$Keep
)

$ErrorActionPreference = 'Stop'
$package = 'com.vigiahud.app'
$source = 'files/recordings'

# Never into the repo, whatever -To says.
$repo = (Resolve-Path (Split-Path -Parent $PSScriptRoot)).Path.TrimEnd('\') + '\'
$destination = [IO.Path]::GetFullPath($To).TrimEnd('\') + '\'
if ($destination.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The recordings show the inside of a home and the repo is public: -To must be outside $repo"
}

function PhoneShell([string]$command) {
    $output = & $Adb -s $Serial shell "run-as $package sh -c '$command'"
    if ($LASTEXITCODE -ne 0) { throw "adb failed on: $command" }
    $output
}

function PhoneSize([string]$name) { [long]((PhoneShell "wc -c < $source/$name") | Out-String).Trim() }

$names = @(PhoneShell "ls $source 2>/dev/null || true" | Where-Object { $_ -like '*.vseq' } | ForEach-Object { $_.Trim() })
if ($names.Count -eq 0) {
    Write-Output 'No recordings on the phone.'
    return
}

New-Item -ItemType Directory -Force $To | Out-Null
foreach ($name in $names) {
    # A file that is still growing is the recording in progress.
    $before = PhoneSize $name
    Start-Sleep -Seconds 3
    $size = PhoneSize $name
    if ($size -ne $before) {
        Write-Output "$name is still recording; left on the phone."
        continue
    }

    $target = Join-Path $To $name
    $partial = "$target.part"
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = $Adb
    $info.Arguments = "-s $Serial exec-out run-as $package cat $source/$name"
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($info)
    $file = [IO.File]::Create($partial)
    try { $process.StandardOutput.BaseStream.CopyTo($file) } finally { $file.Dispose() }
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "adb failed copying $name ($($process.ExitCode)): $($process.StandardError.ReadToEnd())" }

    $copied = (Get-Item $partial).Length
    if ($copied -ne $size) { throw "$name has $size bytes on the phone and $copied on the PC; nothing deleted." }
    $hash = (Get-FileHash $partial -Algorithm SHA256).Hash.ToLowerInvariant()
    $phoneHash = ((PhoneShell "sha256sum $source/$name") | Out-String).Trim().Split(' ')[0]
    if ($hash -ne $phoneHash) { throw "$name differs between the phone and the PC; nothing deleted." }

    Move-Item -Force $partial $target
    if (-not $Keep) { PhoneShell "rm $source/$name" | Out-Null }
    $mb = [math]::Round($size / 1MB, 1)
    $where = if ($Keep) { 'kept on the phone' } else { 'deleted from the phone' }
    Write-Output "$name  $mb MB  sha256 $hash  ($where)"
}

$free = ((& $Adb -s $Serial shell "df -h /data | tail -1") | Out-String).Trim()
Write-Output "Recordings in $To. Phone storage: $free"
