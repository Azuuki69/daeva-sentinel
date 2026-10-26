param(
    [Parameter(Mandatory=$true)][string]$Piper,
    [Parameter(Mandatory=$true)][string]$Model
)
$ErrorActionPreference = 'Stop'
$taskVoiceDir = Join-Path $PSScriptRoot 'voicepack'
New-Item -ItemType Directory -Force $taskVoiceDir | Out-Null
$taskLines = @(
    'Waves incoming. Move toward Bakarma and jump.',
    'Frontal attack. Watch the boss.',
    'Frontal sweep. Get behind Bakarma.',
    'Frontal charge. Move out of the line.',
    'Projectiles incoming. Watch the ground.',
    'Water bomb on you. Keep moving.',
    'Area attack. Watch the ground.',
    'Water bombs incoming. Watch the ground.',
    'Wave phase soon. Save your stagger skills.',
    'Stagger now. Use your stagger skills.',
    'Stagger broken. Attack Bakarma.',
    'Voice alerts are ready. Watch the ground and save your stagger skills.',
    '1', '2', '3'
)
$taskInfo = New-Object System.Diagnostics.ProcessStartInfo
$taskInfo.FileName = (Resolve-Path -LiteralPath $Piper).Path
$taskInfo.ArgumentList.Add('--model')
$taskInfo.ArgumentList.Add((Resolve-Path -LiteralPath $Model).Path)
$taskInfo.ArgumentList.Add('--json-input')
$taskInfo.ArgumentList.Add('--length_scale')
$taskInfo.ArgumentList.Add('0.87')
$taskInfo.UseShellExecute = $false
$taskInfo.RedirectStandardInput = $true
$taskInfo.CreateNoWindow = $true
$taskProcess = [System.Diagnostics.Process]::Start($taskInfo)
$taskManifest = @()
foreach ($taskLine in $taskLines) {
    $taskHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('piper-en-US-ljspeech|' + $taskLine))).Substring(0,24)
    $taskFile = Join-Path $taskVoiceDir ($taskHash + '.wav')
    $taskProcess.StandardInput.WriteLine((@{ text=$taskLine; output_file=$taskFile } | ConvertTo-Json -Compress))
    $taskManifest += @{ text=$taskLine; file=($taskHash + '.wav') }
}
$taskProcess.StandardInput.Close()
$taskProcess.WaitForExit()
if ($taskProcess.ExitCode -ne 0) { throw "Piper exited with code $($taskProcess.ExitCode)" }
$taskManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskVoiceDir 'manifest.json') -Encoding utf8
foreach ($taskClip in $taskManifest) {
    if ((Get-Item -LiteralPath (Join-Path $taskVoiceDir $taskClip.file)).Length -lt 1000) { throw 'Missing or empty generated audio' }
}
Write-Host "Generated $($taskManifest.Count) English clips."
