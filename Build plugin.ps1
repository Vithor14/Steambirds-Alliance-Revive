# Build plugin.ps1
# Builds QuieterPlugin and copies the DLL into the game's BepInEx\plugins folder.

Set-Location C:\Steambirds\QuieterPlugin
dotnet build -c Release

$gameFolder = "C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011"
Copy-Item .\bin\Release\net46\QuieterPlugin.dll "$gameFolder\BepInEx\plugins\" -Force
Read-Host "Press Enter to close"
