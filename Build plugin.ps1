# Build plugin.ps1
# Builds QuieterPlugin and copies the DLL into the game's BepInEx\plugins folder.
# Game folder: STEAMBIRDS_GAME, else <Steam>\steamapps\content\app_386010\depot_386011 (Steam folder from the
# registry, like the launcher), else the default Steam folder. The project file looks for the game the same way.

function FindGameFolder() {
    $depot = "steamapps\content\app_386010\depot_386011"
    $candidates = @()
    if ($env:STEAMBIRDS_GAME) { $candidates += $env:STEAMBIRDS_GAME }
    $steam = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction SilentlyContinue).SteamPath
    if (-not $steam) { $steam = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -Name InstallPath -ErrorAction SilentlyContinue).InstallPath }
    if ($steam) { $candidates += (Join-Path ($steam -replace '/', '\') $depot) }
    $candidates += "C:\Program Files (x86)\Steam\$depot"
    foreach ($folder in $candidates) {
        if ($folder -and (Test-Path -LiteralPath (Join-Path $folder "BepInEx\plugins"))) { return $folder }
    }
    return $null
}

Set-Location (Join-Path $PSScriptRoot "QuieterPlugin")
$gameFolder = FindGameFolder
if (-not $gameFolder) {
    Write-Host "Game folder with BepInEx not found. Set STEAMBIRDS_GAME to the depot_386011 folder." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}
Write-Host "Game folder: $gameFolder"

dotnet build -c Release "-p:GameDir=$gameFolder"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

Copy-Item .\bin\Release\net46\QuieterPlugin.dll "$gameFolder\BepInEx\plugins\" -Force
Write-Host "Copied QuieterPlugin.dll to $gameFolder\BepInEx\plugins" -ForegroundColor Green
Read-Host "Press Enter to close"
