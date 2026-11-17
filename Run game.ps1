# Run game.ps1
# Opens the game against the local server (OZY_URL points to the server started by Run server.ps1).
# Steam must be open first.

$steam = Get-Process -Name steam -ErrorAction SilentlyContinue

if (-not $steam) {
    Write-Host "ERROR: Steam is not running. Open Steam, sign in to your account and run this again." -ForegroundColor Red
    Read-Host "Press Enter to close"
    exit 1
}

$env:OZY_URL = "http://localhost:6443/ozy"
& "C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011\Steambirds.exe" -connecttolocal
