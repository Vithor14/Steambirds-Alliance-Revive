# Download old versions.ps1
# Downloads ONLY the configuration files (StreamingAssets) and Assembly-CSharp.dll from old builds of
# Steambirds Alliance, to look for the original tutorial map.
#
# Why: in the current build (201219.1413) the file StreamingAssets\configBinary has
# "Dungeons/dungeon_tutorial" (35,676 bytes in the original), but its content was emptied in this build
# (it used to be part of the server). Older builds may have the whole file, with the map and the script.
#
# Needs DepotDownloader (https://github.com/SteamRE/DepotDownloader/releases):
#   - either installed with winget:  winget install --exact --id SteamRE.DepotDownloader
#   - or extracted to C:\Steambirds\DepotDownloader\DepotDownloader.exe
# The Steam account must own the game. The password and the Steam Guard code are asked by
# DepotDownloader itself (this script does not save them).
#
# Manifest IDs are listed on SteamDB: https://steamdb.info/depot/386011/manifests/
# (sign in to SteamDB with your Steam account to see the full list). Start with the 2019 ones
# (the Open Beta video is from August 2019) and some from 2018.
#
# Usage:
#   .\"Download old versions.ps1"                          (asks for the IDs and the user)
#   .\"Download old versions.ps1" -Manifests 123,456 -User mylogin
#   .\"Download old versions.ps1" -App 1047410 -Depot 1047411 ...   (Open Beta app, if you have it)
#
# Each version goes to C:\Steambirds\VersoesAntigas\<depot>-<manifest>\ (a few MB each).

param(
    [string[]]$Manifests,
    [string]$User,
    [int]$App = 386010,
    [int]$Depot = 386011
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$oldVersionsFolder = Join-Path $root 'VersoesAntigas'

# DepotDownloader: first the one installed by winget (PATH), then the local folder.
$depotDownloader = $null
$command = Get-Command 'DepotDownloader' -ErrorAction SilentlyContinue
if ($command) { $depotDownloader = $command.Source }
if (-not $depotDownloader) {
    $localDownloader = Join-Path $root 'DepotDownloader\DepotDownloader.exe'
    if (Test-Path $localDownloader) { $depotDownloader = $localDownloader }
}
if (-not $depotDownloader) {
    Write-Host 'DepotDownloader not found.' -ForegroundColor Red
    Write-Host 'Install it with:  winget install --exact --id SteamRE.DepotDownloader'
    Write-Host 'or download DepotDownloader-windows-x64.zip from https://github.com/SteamRE/DepotDownloader/releases'
    Write-Host "and extract it to $(Join-Path $root 'DepotDownloader')"
    exit 1
}

if (-not $Manifests -or $Manifests.Count -eq 0) {
    $typed = Read-Host 'Depot manifest IDs (separated by space or comma)'
    $Manifests = $typed -split '[\s,;]+' | Where-Object { $_ -ne '' }
} else {
    $Manifests = $Manifests | ForEach-Object { $_ -split '[\s,;]+' } | Where-Object { $_ -ne '' }
}
if (-not $Manifests -or $Manifests.Count -eq 0) {
    Write-Host 'No manifest informed.' -ForegroundColor Red
    exit 1
}
if (-not $User) { $User = Read-Host 'Steam username (the login, not the display name)' }

New-Item -ItemType Directory -Force -Path $oldVersionsFolder | Out-Null
$fileList = Join-Path $oldVersionsFolder 'filelist.txt'
# Only what is needed: configs (StreamingAssets) and the client code.
@(
    'regex:(?i).*StreamingAssets[/\\].*',
    'regex:(?i).*Managed[/\\]Assembly-CSharp(-firstpass)?\.dll'
) | Set-Content -Encoding ASCII -Path $fileList

$summary = @()
foreach ($manifest in $Manifests) {
    if ($manifest -notmatch '^\d+$') {
        Write-Host "Ignoring '$manifest' (not a manifest number)." -ForegroundColor Yellow
        continue
    }
    $destination = Join-Path $oldVersionsFolder "$Depot-$manifest"
    Write-Host ''
    Write-Host "=== manifest $manifest (app $App, depot $Depot) -> $destination ===" -ForegroundColor Cyan
    & $depotDownloader -app $App -depot $Depot -manifest $manifest -username $User -remember-password -dir $destination -filelist $fileList
    $exitCode = $LASTEXITCODE

    $configFile = Get-ChildItem -Path $destination -Recurse -Filter 'configBinary' -ErrorAction SilentlyContinue | Select-Object -First 1
    $versionFile = Get-ChildItem -Path $destination -Recurse -Filter 'version.bytes' -ErrorAction SilentlyContinue | Select-Object -First 1
    $build = ''
    if ($versionFile) {
        $line = Get-Content $versionFile.FullName | Where-Object { $_ -like 'buildId:*' } | Select-Object -First 1
        if ($line) { $build = $line.Substring(8).Trim() }
    }
    if ($configFile) {
        $summary += "{0}: configBinary {1:N0} bytes, build {2}" -f $manifest, $configFile.Length, $build
    } else {
        $otherCount = (Get-ChildItem -Path $destination -Recurse -File -ErrorAction SilentlyContinue | Measure-Object).Count
        $summary += "{0}: no configBinary ({1} file(s) downloaded, exit code {2})" -f $manifest, $otherCount, $exitCode
    }
}

Write-Host ''
Write-Host '=== summary ===' -ForegroundColor Green
$summary | ForEach-Object { Write-Host $_ }
$summary | Set-Content -Path (Join-Path $oldVersionsFolder 'summary.txt')
Write-Host ''
Write-Host "Done. The versions are in $oldVersionsFolder."
Write-Host 'Tell the chat: the C:\Steambirds folder is linked to the conversation, so the files can be checked from there.'
Read-Host "Press Enter to close"
