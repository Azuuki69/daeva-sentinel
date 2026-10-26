param(
    [Parameter(Mandatory=$true)][string]$Version,
    [string]$Dotnet='dotnet',
    [string]$SigningKeyPath,
    [switch]$SkipBuild,
    [switch]$NoUpload
)
$ErrorActionPreference='Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part release version such as 0.3.0' }
$taskRoot=Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'Build.ps1') -Dotnet $Dotnet -Version $Version }
$taskArtifacts=Join-Path $taskRoot '.artifacts'
$taskBuilt=Join-Path $taskArtifacts 'launcher'
$taskStage=Join-Path $taskArtifacts ('stage-'+[Guid]::NewGuid().ToString('N'))
$taskApp=Join-Path $taskStage 'app'
$taskLauncher=Join-Path $taskStage 'Daeva-Sentinel-Launcher'
New-Item -ItemType Directory -Force $taskApp,$taskLauncher | Out-Null
Get-ChildItem -LiteralPath $taskBuilt | Where-Object { $_.Name -notin @('data','logs','versions','update-cache','launcher-state.json','launcher-language.txt','startup-error.txt','launcher-error.txt','feed-verification.json') -and $_.Extension -ne '.pdb' -and $_.Name -notin @('Aion DPS.exe','Aion DPS.runtimeconfig.json') } | Copy-Item -Destination $taskLauncher -Recurse
foreach ($taskStageFile in Get-ChildItem -LiteralPath (Join-Path $taskLauncher 'assets') -Recurse -File | Where-Object { $_.Extension -in @('.png','.jpg','.ico','.wav') }) {
    if (-not $taskStageFile.FullName.StartsWith($taskLauncher + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Outside package stage' }
    Remove-Item -LiteralPath $taskStageFile.FullName
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md'),(Join-Path $taskRoot 'THIRD-PARTY-NOTICES.txt'),(Join-Path $taskRoot 'LICENSE-Aion-DPS-Meter.txt') -Destination $taskLauncher
Get-ChildItem -LiteralPath $taskLauncher | Where-Object { $_.Name -notlike 'Daeva Sentinel Launcher*' -and $_.Name -ne 'update-public-key.pem' } | Copy-Item -Destination $taskApp -Recurse
$taskRelease=Join-Path $taskArtifacts ('release-'+$Version)
New-Item -ItemType Directory -Force $taskRelease | Out-Null
$taskAppZip=Join-Path $taskRelease ('Daeva-Sentinel-App-v'+$Version+'.zip')
$taskLauncherZip=Join-Path $taskRelease ('Daeva-Sentinel-Launcher-v'+$Version+'.zip')
foreach ($taskZip in @($taskAppZip,$taskLauncherZip)) { if (Test-Path -LiteralPath $taskZip) { Remove-Item -LiteralPath $taskZip } }
Add-Type -AssemblyName System.IO.Compression.ZipFile
[IO.Compression.ZipFile]::CreateFromDirectory($taskApp,$taskAppZip,[IO.Compression.CompressionLevel]::Optimal,$false)
[IO.Compression.ZipFile]::CreateFromDirectory($taskLauncher,$taskLauncherZip,[IO.Compression.CompressionLevel]::Optimal,$true)
$taskHash=(Get-FileHash -LiteralPath $taskAppZip -Algorithm SHA256).Hash
$taskManifest=@{ Product='daeva-sentinel'; Version=$Version; PackageUrl=('https://github.com/Azuuki69/daeva-sentinel/releases/download/v'+$Version+'/'+[IO.Path]::GetFileName($taskAppZip)); Sha256=$taskHash; Size=(Get-Item -LiteralPath $taskAppZip).Length }
$taskBytes=[Text.Encoding]::UTF8.GetBytes(($taskManifest | ConvertTo-Json -Compress))
$taskRsa=[Security.Cryptography.RSA]::Create()
try {
    if ($SigningKeyPath) { $taskRsa.ImportFromPem([IO.File]::ReadAllText((Resolve-Path -LiteralPath $SigningKeyPath))) }
    elseif ($env:UPDATE_SIGNING_KEY) { $taskRsa.ImportFromPem($env:UPDATE_SIGNING_KEY) }
    else { throw 'A private signing key is required; it must not be committed to Git.' }
    $taskExpected=([IO.File]::ReadAllText((Join-Path $taskRoot 'src/BakarmaLauncher/update-public-key.pem')) -replace '\s','')
    if (($taskRsa.ExportSubjectPublicKeyInfoPem() -replace '\s','') -ne $taskExpected) { throw 'Signing key does not match the shipped launcher public key.' }
    $taskEnvelope=@{ Payload=[Convert]::ToBase64String($taskBytes); Signature=[Convert]::ToBase64String($taskRsa.SignData($taskBytes,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pss)) }
    [IO.File]::WriteAllText((Join-Path $taskRelease 'update-manifest.json'),($taskEnvelope | ConvertTo-Json -Compress))
} finally { $taskRsa.Dispose() }
$taskPublishedFiles=@($taskAppZip,$taskLauncherZip,(Join-Path $taskRelease 'update-manifest.json'))
$taskPublishedFiles | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($_) } | Set-Content -LiteralPath (Join-Path $taskRelease 'SHA256SUMS.txt') -Encoding utf8
if (-not $NoUpload) {
    & gh release create ('v'+$Version) --repo Azuuki69/daeva-sentinel --draft --title ('Daeva Sentinel v'+$Version+' — experimental') --notes-file (Join-Path $taskRoot 'release-notes.md')
    if ($LASTEXITCODE -ne 0) { throw 'Draft release creation failed' }
    $taskAssets=$taskPublishedFiles+@((Join-Path $taskRelease 'SHA256SUMS.txt'))
    & gh release upload ('v'+$Version) @taskAssets --repo Azuuki69/daeva-sentinel
    if ($LASTEXITCODE -ne 0) { throw 'Release upload failed; draft retained' }
    & gh release edit ('v'+$Version) --repo Azuuki69/daeva-sentinel --draft=false --latest
    if ($LASTEXITCODE -ne 0) { throw 'Release publication failed' }
}
Write-Host ('Release files: '+$taskRelease)
