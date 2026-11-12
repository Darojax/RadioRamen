[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameRoot,
    [string]$Ffmpeg = 'ffmpeg',
    [switch]$LiveStreams,
    [switch]$ManagedHttp
)
$ErrorActionPreference = 'Stop'
$taskModule = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskGame = (Resolve-Path -LiteralPath $GameRoot).Path
$taskGameHash = (Get-FileHash -LiteralPath (Join-Path $taskGame 'Ramen Shop Simulator_Data\Managed\Assembly-CSharp.dll') -Algorithm SHA256).Hash
& (Join-Path $PSScriptRoot 'Setup-Tools.ps1')
New-Item -ItemType Directory -Path (Join-Path $taskModule 'artifacts\native') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskModule '.tools\bass24\x64\bass.dll'),(Join-Path $taskModule '.tools\basshls24\x64\basshls.dll') -Destination (Join-Path $taskModule 'artifacts\native') -Force
& $Ffmpeg -hide_banner -loglevel error -f lavfi -i 'sine=frequency=440:duration=3' -ar 44100 -ac 2 -codec:a libmp3lame -q:a 4 -y (Join-Path $taskModule 'artifacts\fixture.mp3')
if ($LASTEXITCODE -ne 0) { throw 'MP3 check fixture generation failed.' }
& $Ffmpeg -hide_banner -loglevel error -f lavfi -i 'sine=frequency=660:duration=3' -ar 44100 -ac 2 -codec:a libmp3lame -b:a 128k -f hls -hls_time 1 -hls_list_size 0 -hls_segment_filename (Join-Path $taskModule 'artifacts\hls-%03d.ts') -y (Join-Path $taskModule 'artifacts\fixture.m3u8')
if ($LASTEXITCODE -ne 0) { throw 'HLS check fixture generation failed.' }
& dotnet build (Join-Path $taskModule 'src\RadioRamen.csproj') -c Release --nologo "-p:GameRoot=$taskGame"
if ($LASTEXITCODE -ne 0) { throw 'RadioRamen build failed.' }
$taskArguments = @($taskModule,$taskGame)
if ($LiveStreams) { $taskArguments += '--live' }
if ($ManagedHttp) { $taskArguments += '--managed-http' }
$taskChecks = @(& dotnet run --project (Join-Path $taskModule 'tests\RadioRamen.Checks.csproj') -c Release -- @taskArguments)
$taskChecks | Write-Output
if ($LASTEXITCODE -ne 0) { throw 'RadioRamen checks failed.' }
$taskMatch = [regex]::Match(($taskChecks -join "`n"),'(\d+) RadioRamen checks passed')
if (-not $taskMatch.Success) { throw 'Check completion summary is missing.' }
$taskVersion = ([xml](Get-Content -LiteralPath (Join-Path $taskModule 'src\RadioRamen.csproj') -Raw)).Project.PropertyGroup.Version
$taskStage = Join-Path $taskModule ('dist\RadioRamen-' + $taskVersion)
$taskPlugin = Join-Path $taskStage 'BepInEx\plugins\RadioRamen'
$taskNative = Join-Path $taskStage 'BepInEx\RadioRamen\native'
$taskNotices = Join-Path $taskStage 'BepInEx\RadioRamen\licenses'
New-Item -ItemType Directory -Path (Join-Path $taskPlugin 'Music'),$taskNative,$taskNotices -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $taskModule 'src\bin\Release\netstandard2.1\RadioRamen.dll') -Destination $taskPlugin -Force
Copy-Item -LiteralPath (Join-Path $taskModule 'config\RadioRamen.ini'),(Join-Path $taskModule 'config\Stations.ini') -Destination $taskPlugin -Force
Copy-Item -LiteralPath (Join-Path $taskModule 'Music\README.txt') -Destination (Join-Path $taskPlugin 'Music') -Force
Copy-Item -LiteralPath (Join-Path $taskModule '.tools\bass24\x64\bass.dll'),(Join-Path $taskModule '.tools\basshls24\x64\basshls.dll') -Destination $taskNative -Force
Copy-Item -LiteralPath (Join-Path $taskModule '.tools\bass24\bass.txt') -Destination (Join-Path $taskNotices 'BASS.txt') -Force
Copy-Item -LiteralPath (Join-Path $taskModule '.tools\basshls24\basshls.txt') -Destination (Join-Path $taskNotices 'BASSHLS.txt') -Force
Copy-Item -LiteralPath (Join-Path $taskModule 'README.md') -Destination $taskStage -Force
$taskFiles = @(Get-ChildItem -LiteralPath $taskStage -Recurse -File)
$taskExpected = @('BepInEx\plugins\RadioRamen\RadioRamen.dll','BepInEx\plugins\RadioRamen\RadioRamen.ini','BepInEx\plugins\RadioRamen\Stations.ini','BepInEx\plugins\RadioRamen\Music\README.txt','BepInEx\RadioRamen\native\bass.dll','BepInEx\RadioRamen\native\basshls.dll','BepInEx\RadioRamen\licenses\BASS.txt','BepInEx\RadioRamen\licenses\BASSHLS.txt','README.md')
if ($taskFiles.Count -ne $taskExpected.Count) { throw 'Unexpected files in package stage; review before packaging.' }
$taskManifest = foreach ($taskFile in $taskFiles) {
    $taskRelative = $taskFile.FullName.Substring($taskStage.Length+1)
    if ($taskRelative -notin $taskExpected) { throw ('Unexpected package file: ' + $taskRelative) }
    [ordered]@{Relative=$taskRelative;Bytes=$taskFile.Length;SHA256=(Get-FileHash -LiteralPath $taskFile.FullName -Algorithm SHA256).Hash}
}
$taskZip = Join-Path $taskModule ('dist\RadioRamen-' + $taskVersion + '.zip')
Compress-Archive -Path (Join-Path $taskStage '*') -DestinationPath $taskZip -Force
[ordered]@{BuiltUtc=[DateTime]::UtcNow.ToString('O');Version=$taskVersion;GameSHA256=$taskGameHash;CompatibilityMode='Game API, radio control and hook capability checks; no version allowlist';ChecksPassed=[int]$taskMatch.Groups[1].Value;LiveStreamDecodeChecks=[bool]$LiveStreams;ManagedHttpForcedDuringChecks=[bool]$ManagedHttp;Stage=$taskStage;Archive=$taskZip;ArchiveSHA256=(Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash;Files=$taskManifest;InGameMenuVerified=$false;InGameSpatialAudioVerified=$false} | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $taskModule 'artifacts\build-verification.json') -Encoding UTF8
Write-Output ('Package: ' + $taskZip)
