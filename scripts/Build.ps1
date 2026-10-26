param([string]$Dotnet = 'dotnet', [string]$Version = '0.3.1')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path $PSScriptRoot -Parent
$taskVendor=Join-Path $taskRoot 'src/Aion-DPS-Meter'
$taskCommit='29ad34ea9babe10ec0f0fbcdd7661b2c2bad0fc6'
if (-not (Test-Path -LiteralPath $taskVendor)) {
    & git clone --depth 1 https://github.com/SkeeveAN/Aion-DPS-Meter.git $taskVendor
    if ($LASTEXITCODE -ne 0) { throw 'Parser download failed' }
    & git -C $taskVendor fetch --depth 1 origin $taskCommit
    if ($LASTEXITCODE -ne 0) { throw 'Pinned parser download failed' }
    & git -C $taskVendor checkout --detach $taskCommit
}
if ((& git -C $taskVendor rev-parse HEAD) -ne $taskCommit) { throw 'Unexpected parser revision. Use a clean checkout.' }
$taskCsproj=Join-Path $taskVendor 'Client/AionDPS.csproj'
$taskText=Get-Content -LiteralPath $taskCsproj -Raw
if (-not $taskText.Contains('Include="SQLitePCLRaw.lib.e_sqlite3"')) {
    $taskText=$taskText.Replace('<PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.9" />','<PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.9" /><PackageReference Include="SQLitePCLRaw.lib.e_sqlite3" Version="3.53.3" />')
    [IO.File]::WriteAllText($taskCsproj,$taskText)
}
$taskArtifacts=Join-Path $taskRoot '.artifacts'
New-Item -ItemType Directory -Force $taskArtifacts | Out-Null
& $Dotnet run --project (Join-Path $taskRoot 'src/BakarmaCoach/Tests/Tests.csproj') -c Release -- (Join-Path $taskArtifacts 'companion-tests.json')
if ($LASTEXITCODE -ne 0) { throw 'Companion tests failed' }
& $Dotnet run --project (Join-Path $taskRoot 'src/BakarmaLauncher/Tests/Tests.csproj') -c Release -- (Join-Path $taskArtifacts 'launcher-tests.json')
if ($LASTEXITCODE -ne 0) { throw 'Launcher tests failed' }
$taskOutput=Join-Path $taskArtifacts 'launcher'
if (Test-Path -LiteralPath $taskOutput) {
    $taskResolvedOutput=(Resolve-Path -LiteralPath $taskOutput).Path
    $taskResolvedArtifacts=(Resolve-Path -LiteralPath $taskArtifacts).Path
    if (-not $taskResolvedOutput.StartsWith($taskResolvedArtifacts+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected generated-output path' }
    Remove-Item -LiteralPath $taskResolvedOutput -Recurse -Force
}
& $Dotnet publish (Join-Path $taskRoot 'src/BakarmaLauncher/BakarmaLauncher.csproj') -c Release -r win-x64 --self-contained true -p:Version=$Version -o (Join-Path $taskArtifacts 'launcher')
if ($LASTEXITCODE -ne 0) { throw 'Launcher build failed' }
Write-Host 'Build and tests completed.'
