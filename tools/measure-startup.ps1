# Reports GUI cold start (launch -> first rendered document) and warm handoff.
# Not a CI gate: WebView2 first-navigation cost is highly environment-sensitive.
param([string]$Exe = "$PSScriptRoot\..\publish\app\mdreader.exe")

$fixture = "$PSScriptRoot\..\fixtures\basic.md"
$log = Join-Path $env:TEMP "mdreader-startup-$([guid]::NewGuid().ToString('N')).log"
$env:MDREADER_LOG = $log
$env:MDREADER_INSTANCE_ID = "startup-measure"

$t0 = Get-Date
$p = Start-Process $Exe -ArgumentList "`"$fixture`"" -PassThru
while (-not (Test-Path $log) -or -not (Select-String -Path $log -Pattern "render complete" -Quiet -ErrorAction SilentlyContinue)) {
    Start-Sleep -Milliseconds 25
    if (((Get-Date) - $t0).TotalSeconds -gt 60) { Write-Error "timed out"; break }
}
"cold start to rendered: $([int]((Get-Date) - $t0).TotalMilliseconds)ms"

Start-Sleep 5
$t1 = Get-Date
Start-Process $Exe -ArgumentList "`"$PSScriptRoot\..\fixtures\tables.md`"" | Out-Null
while ((Select-String -Path $log -Pattern "render complete" -AllMatches -ErrorAction SilentlyContinue | Measure-Object).Count -lt 2) {
    Start-Sleep -Milliseconds 20
    if (((Get-Date) - $t1).TotalSeconds -gt 30) { Write-Error "handoff timed out"; break }
}
"warm handoff to rendered tab: $([int]((Get-Date) - $t1).TotalMilliseconds)ms"

Stop-Process $p -Force -ErrorAction SilentlyContinue
Get-Process mdreader -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-Item $log -ErrorAction SilentlyContinue
