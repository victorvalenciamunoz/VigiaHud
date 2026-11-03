# Copies the bot token and the owner's chat id from the PC's user secrets (id "vigiahud", DECISIONS 017) into the
# app's private storage on the phone, as files/telegram.json (DECISIONS 021). Nothing is written to a temporary file
# and nothing is printed: the JSON goes through adb's standard input, and the check compares sizes only.
#
#   powershell -File scripts\push-telegram-secrets.ps1 [-Serial 9d82dcda]
#
# Needs the app installed from the PC (a debuggable build: "run-as" does not work on a published one). The watch
# reads the file when it starts: restart it afterwards (PARAR and INICIAR, or reinstall).

param(
    [string]$Serial = '9d82dcda',
    [string]$Adb = 'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe'
)

$ErrorActionPreference = 'Stop'
$package = 'com.vigiahud.app'
$target = 'files/telegram.json'

$secretsPath = Join-Path $env:APPDATA 'Microsoft\UserSecrets\vigiahud\secrets.json'
if (-not (Test-Path $secretsPath)) {
    throw "No user secrets with id 'vigiahud': dotnet user-secrets set ""Telegram:Token"" (Read-Host 'Token') --id vigiahud"
}

$secrets = Get-Content $secretsPath -Raw | ConvertFrom-Json
$token = $secrets.'Telegram:Token'
$chat = $secrets.'Telegram:ChatId'
if (-not $token -or -not $chat) {
    throw 'The user secrets need Telegram:Token and Telegram:ChatId (the chat id is saved by scripts/record-telegram-fixtures.cs).'
}

# Built by hand: ConvertTo-Json in Windows PowerShell would turn the chat id into a string.
$json = '{"token":"' + $token + '","chatId":' + [long]$chat + '}'
$bytes = New-Object System.Text.UTF8Encoding($false)
$payload = $bytes.GetBytes($json)

$info = New-Object System.Diagnostics.ProcessStartInfo
$info.FileName = $Adb
$info.Arguments = "-s $Serial shell ""run-as $package sh -c 'umask 077; cat > $target'"""
$info.UseShellExecute = $false
$info.RedirectStandardInput = $true
$info.RedirectStandardOutput = $true
$info.RedirectStandardError = $true

# Windows PowerShell opens the child's standard input with the console encoding and writes its byte-order mark
# first (seen on 2026-11-03: three bytes before the JSON, which then does not parse). Without a BOM for the send.
$previousEncoding = [Console]::InputEncoding
[Console]::InputEncoding = $bytes
try {
    $process = [System.Diagnostics.Process]::Start($info)
    $process.StandardInput.BaseStream.Write($payload, 0, $payload.Length)
    $process.StandardInput.Close()
    $process.WaitForExit()
}
finally {
    [Console]::InputEncoding = $previousEncoding
}
if ($process.ExitCode -ne 0) {
    throw "adb failed ($($process.ExitCode)): $($process.StandardError.ReadToEnd())"
}

$size = (& $Adb -s $Serial shell "run-as $package sh -c 'wc -c < $target'").Trim()
if ([int]$size -ne $payload.Length) {
    throw "The file on the phone has $size bytes, expected $($payload.Length)."
}

Write-Output "Secrets copied to the phone ($size bytes in $package/$target). Restart the watch to start the bot."
