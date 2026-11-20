# Build server release.ps1
# Builds the server release zip for GitHub Releases, for people who do not want to compile the project:
#
#   Release\SteambirdsServer-<version>-win-x64.zip
#     SteambirdsServer\SteambirdsServer.exe       the server (self-contained: nobody needs .NET installed)
#     SteambirdsServer\SteambirdsGameFiles.exe    takes the game files from the player's own copy of the game
#     SteambirdsServer\plugin\QuieterPlugin.dll   the plugin SteambirdsGameFiles.exe installs in the game
#     SteambirdsServer\Start server.cmd, README.md, LICENSE   (README and .cmd come from Packaging\server)
#     SteambirdsServer\maps\, loot_tables\ and the settings (network.json, progression.json, items.json...)
#
# NO game file goes into the zip: configBinary, the data files of the plugin (units_config.txt...), the item catalog
# and the map editor pictures belong to the game. Each player gets them with SteambirdsGameFiles.exe. Player accounts,
# logs and backups stay out too. Before zipping, the script checks the package for those files and stops if it finds one.
#
# Usage:  .\"Build server release.ps1"                  (asks the version; Enter = today's date)
#         .\"Build server release.ps1" -Version 0.4.0
#         .\"Build server release.ps1" -Version 0.4.0 -NoPause
# Everything shown on screen is also written to Release\build\release.log.

param(
    [string]$Version = "",
    [switch]$NoPause
)

$root = $PSScriptRoot
if (-not $root) { $root = "C:\Steambirds" }
$serverFolder     = Join-Path $root "Server"
$pluginFolder     = Join-Path $root "QuieterPlugin"
$gameFilesFolder  = Join-Path $root "GameFiles"
$packagingFolder  = Join-Path $root "Packaging\server"
$releaseFolder    = Join-Path $root "Release"
$buildFolder      = Join-Path $releaseFolder "build"
$logFile          = Join-Path $buildFolder "release.log"

# Data folders of Server\ that go into the release. A new data folder must be added here on purpose (the script warns
# about folders without code that are not in this list), so nothing private or from the game leaks by accident.
$releaseFolders   = @("maps", "loot_tables")

# Settings in the root of Server\ that never go into the release (development settings and the old single account).
$excludedRootFiles = @("appsettings.Development.json", "account.json", "inventory.json", "acceptances.json")

# Folders of Server\ that are known and never released: build output, game data, accounts, logs, backups.
$knownPrivateFolders = @("bin", "obj", "Properties", ".vs", "data", "catalog", "accounts", "client-uploads", "logs", "_backup")

New-Item -ItemType Directory -Force -Path $buildFolder | Out-Null
$script:log = New-Object System.IO.StreamWriter($logFile, $false, (New-Object System.Text.UTF8Encoding($false)))
$script:log.AutoFlush = $true

# dotnet 8 writes its messages in UTF-8; without this, PowerShell reads them as code page 850 and the accents break.
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

function Say($text, $color) {
    if ($color) { Write-Host $text -ForegroundColor $color } else { Write-Host $text }
    $script:log.WriteLine([string]$text)
}

function Pause-IfInteractive($text) {
    if (-not $NoPause) { Read-Host $text | Out-Null }
}

function Fail($text) {
    Say ""
    Say "ERROR: $text" Red
    Say "The full log is in: $logFile" Yellow
    $script:log.Dispose()
    Pause-IfInteractive "Press Enter to close"
    exit 1
}

# Runs dotnet showing and recording the output. Returns the exit code.
function RunDotnet($title, [string[]]$arguments) {
    Say ""
    Say "=== $title ===" Cyan
    Say ("dotnet " + ($arguments -join " "))
    & dotnet @arguments 2>&1 | ForEach-Object { Say ("$_") }
    return $LASTEXITCODE
}

# ---------------------------------------------------------------- duplicated .cs files (they break the build)

# "Name-1.cs", "Name (1).cs", "Name - Copia.cs", "Name - Copy.cs" next to "Name.cs", and the same file in two
# folders of one project. Only reported here: "Build launcher.ps1" and "Run server.ps1" offer to move them.
function FindDuplicatedCode($project) {
    $found = @()
    if (-not (Test-Path -LiteralPath $project)) { return $found }
    $files = @(Get-ChildItem -LiteralPath $project -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs)\\' })
    foreach ($file in $files) {
        $name = $file.BaseName
        $withoutSuffix = $name -replace '(-\d+| \(\d+\)| - C.pia( \(\d+\))?| - Copy( \(\d+\))?)$', ''
        if ($withoutSuffix -eq $name) { continue }
        if (Test-Path -LiteralPath (Join-Path $file.DirectoryName ($withoutSuffix + ".cs"))) { $found += $file.FullName }
    }
    # Same name and same content in two folders: the class is compiled twice. (Same name with different content may be
    # two different classes, as Build launcher.ps1 also assumes.)
    $groups = @($files | Group-Object Name | Where-Object { $_.Count -gt 1 })
    foreach ($group in $groups) {
        $hashes = @($group.Group | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } | Select-Object -Unique)
        if ($hashes.Count -gt 1) { continue }
        $found += ("{0} (same file in: {1})" -f $group.Name, (($group.Group | ForEach-Object { $_.DirectoryName }) -join ", "))
    }
    return $found
}

# ---------------------------------------------------------------- copying

$script:copiedFiles = 0

function CopyFileTo($source, $target) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    $script:copiedFiles++
}

function CopyFolderTo($source, $target) {
    $files = @(Get-ChildItem -LiteralPath $source -File -Recurse -ErrorAction SilentlyContinue)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($source.Length).TrimStart('\')
        CopyFileTo $file.FullName (Join-Path $target $relative)
    }
    return $files.Count
}

# ---------------------------------------------------------------- game files check

# Files that belong to the game or to the players and must never be in a public zip.
function FindForbiddenFiles($folder) {
    $forbiddenNames = @("configBinary", "ground_assets.txt", "thumbs.json", "item_catalog.json", "item_catalog.md",
        "account.json", "inventory.json", "acceptances.json", "launcher.json", "server-report.txt")
    $forbiddenExtensions = @(".assets", ".ress", ".resource", ".bundle", ".unity3d", ".ogg", ".wav", ".glb", ".fbx", ".png", ".jpg", ".pdb", ".log")
    $forbiddenFolders = @("accounts", "contas", "data", "dados", "catalog", "catalogo", "client-uploads", "_backup", "logs", "editor-assets", "BepInEx")
    $found = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $folder -File -Recurse -Force)) {
        $relative = $file.FullName.Substring($folder.Length).TrimStart('\')
        $parts = $relative.Split('\')
        $badFolder = @($parts | Select-Object -SkipLast 1 | Where-Object { $forbiddenFolders -contains $_ }).Count -gt 0
        $badName = ($forbiddenNames -contains $file.Name) -or ($file.Name -like "*_config.txt")
        $badExtension = $forbiddenExtensions -contains $file.Extension.ToLowerInvariant()
        $badDll = ($file.Extension -ieq ".dll") -and ($relative -ine "plugin\QuieterPlugin.dll")
        if ($badFolder -or $badName -or $badExtension -or $badDll) { $found += $relative }
    }
    return $found
}

# ================================================================ start

Say "=== BUILD STEAMBIRDS SERVER RELEASE ===" Cyan
Say "Date: $(Get-Date -Format 'dd/MM/yyyy HH:mm:ss')"
Say "Project folder: $root"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "the dotnet command was not found. Install the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)."
}
Say "dotnet: $((& dotnet --version 2>&1) -join ' ')"

foreach ($project in @((Join-Path $serverFolder "SteambirdsServer.csproj"), (Join-Path $gameFilesFolder "SteambirdsGameFiles.csproj"), (Join-Path $pluginFolder "QuieterPlugin.csproj"))) {
    if (-not (Test-Path -LiteralPath $project)) { Fail "$project not found" }
}
foreach ($file in @("README.md", "Start server.cmd")) {
    if (-not (Test-Path -LiteralPath (Join-Path $packagingFolder $file))) { Fail "$(Join-Path $packagingFolder $file) not found" }
}

# Version of the release (goes in the zip name).
if (-not $Version) {
    $defaultVersion = Get-Date -Format "yyyy.MM.dd"
    if ($NoPause) { $Version = $defaultVersion }
    else {
        $answer = Read-Host "Release version (Enter = $defaultVersion)"
        if ($answer -and $answer.Trim()) { $Version = $answer.Trim() } else { $Version = $defaultVersion }
    }
}
if ($Version -notmatch '^[0-9A-Za-z._-]+$') { Fail "invalid version '$Version' (use letters, digits, '.', '-' or '_')" }
$packageName = "SteambirdsServer-$Version-win-x64"
Say "Version: $Version"

$duplicates = @()
foreach ($project in @($serverFolder, $gameFilesFolder, $pluginFolder)) { $duplicates += @(FindDuplicatedCode $project) }
if ($duplicates.Count -gt 0) {
    Say ""
    Say "Duplicated .cs files (the build fails with CS0101/CS0111):" Yellow
    foreach ($item in $duplicates) { Say "  $item" }
    Fail "delete the extra copies (or run Build launcher.ps1, which offers to move them) and run this script again."
}

# ---------------------------------------------------------------- builds
$serverOutput = Join-Path $buildFolder "server"
if (Test-Path -LiteralPath $serverOutput) { Remove-Item -LiteralPath $serverOutput -Recurse -Force }
$exitCode = RunDotnet "Server (SteambirdsServer)" @(
    "publish", (Join-Path $serverFolder "SteambirdsServer.csproj"),
    "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none", "-o", $serverOutput)
if ($exitCode -ne 0) { Fail "the server build failed (look for the lines with 'error' above)." }
$serverExe = Join-Path $serverOutput "SteambirdsServer.exe"
if (-not (Test-Path -LiteralPath $serverExe)) { Fail "the server publish did not create $serverExe" }

$gameFilesOutput = Join-Path $buildFolder "gamefiles"
if (Test-Path -LiteralPath $gameFilesOutput) { Remove-Item -LiteralPath $gameFilesOutput -Recurse -Force }
$exitCode = RunDotnet "Game files tool (SteambirdsGameFiles)" @(
    "publish", (Join-Path $gameFilesFolder "SteambirdsGameFiles.csproj"),
    "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none", "-o", $gameFilesOutput)
if ($exitCode -ne 0) { Fail "the game files tool build failed (look for the lines with 'error' above)." }
$gameFilesExe = Join-Path $gameFilesOutput "SteambirdsGameFiles.exe"
if (-not (Test-Path -LiteralPath $gameFilesExe)) { Fail "the publish did not create $gameFilesExe" }

# The plugin must be built now: the game files tool depends on its export mode (GameFilesExport.cs).
$pluginDll = Join-Path $pluginFolder "bin\Release\net46\QuieterPlugin.dll"
$pluginStart = Get-Date
$exitCode = RunDotnet "Plugin (QuieterPlugin)" @("build", (Join-Path $pluginFolder "QuieterPlugin.csproj"), "-c", "Release")
if ($exitCode -ne 0) { Fail "the plugin build failed (it needs the game's DLLs in the game folder; see QuieterPlugin.csproj)." }
if (-not (Test-Path -LiteralPath $pluginDll)) { Fail "the plugin build did not create $pluginDll" }
if ((Get-Item -LiteralPath $pluginDll).LastWriteTime -lt $pluginStart.AddMinutes(-1)) {
    Say "  (the plugin was already up to date: $pluginDll)"
}

# ---------------------------------------------------------------- package folder
Say ""
Say "=== Package folder ===" Cyan
$stageRoot = Join-Path $buildFolder "stage"
$stage = Join-Path $stageRoot "SteambirdsServer"
if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

CopyFileTo $serverExe (Join-Path $stage "SteambirdsServer.exe")
CopyFileTo $gameFilesExe (Join-Path $stage "SteambirdsGameFiles.exe")
CopyFileTo $pluginDll (Join-Path $stage "plugin\QuieterPlugin.dll")
Say "  SteambirdsServer.exe, SteambirdsGameFiles.exe and plugin\QuieterPlugin.dll"

foreach ($name in $releaseFolders) {
    $source = Join-Path $serverFolder $name
    if (-not (Test-Path -LiteralPath $source)) { Say "  WARNING: Server\$name does not exist" Yellow; continue }
    $count = CopyFolderTo $source (Join-Path $stage $name)
    Say ("  {0}\: {1} file(s)" -f $name, $count)
}

# Data folders without code that are neither released nor known as private: maybe a new folder the server needs.
$unknownFolders = @(Get-ChildItem -LiteralPath $serverFolder -Directory | Where-Object {
    ($releaseFolders -notcontains $_.Name) -and ($knownPrivateFolders -notcontains $_.Name) -and (-not $_.Name.StartsWith("_")) -and
    -not (Get-ChildItem -LiteralPath $_.FullName -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1)
})
foreach ($folder in $unknownFolders) {
    Say "  WARNING: Server\$($folder.Name) has no code and is not in the release. If the server needs it, add it to `$releaseFolders in this script." Yellow
}

$rootFiles = @(Get-ChildItem -LiteralPath $serverFolder -File -Filter "*.json" | Where-Object { $excludedRootFiles -notcontains $_.Name })
foreach ($file in $rootFiles) { CopyFileTo $file.FullName (Join-Path $stage $file.Name) }
Say ("  settings: {0}" -f (($rootFiles | ForEach-Object { $_.Name }) -join ", "))

foreach ($file in @(Get-ChildItem -LiteralPath $packagingFolder -File)) { CopyFileTo $file.FullName (Join-Path $stage $file.Name) }
$license = Join-Path $root "LICENSE"
if (Test-Path -LiteralPath $license) { CopyFileTo $license (Join-Path $stage "LICENSE") }
Say "  README.md, Start server.cmd and LICENSE"

# ---------------------------------------------------------------- no game files, no accounts
$forbidden = @(FindForbiddenFiles $stage)
if ($forbidden.Count -gt 0) {
    Say ""
    Say "Files that must not be published (game data, accounts or logs):" Red
    foreach ($item in $forbidden) { Say "  $item" }
    Fail "the package has files of the game or of players. Remove them from the source folders and run again."
}
Say "  checked: no game files, accounts or logs in the package"

# ---------------------------------------------------------------- zip
Say ""
Say "=== Zip ===" Cyan
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipPath = Join-Path $releaseFolder "$packageName.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
try {
    # includeBaseDirectory: the zip opens into a "SteambirdsServer" folder.
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $true)
}
catch {
    Fail "could not create the zip: $($_.Exception.Message)"
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$hashFile = "$zipPath.sha256.txt"
Set-Content -LiteralPath $hashFile -Value "$hash  $packageName.zip" -Encoding Ascii
$sizeMb = [math]::Round((Get-Item -LiteralPath $zipPath).Length / 1MB, 1)

Say ""
Say "Release ready: $zipPath ($sizeMb MB, $script:copiedFiles files)" Green
Say "  SHA-256: $hash (also in $(Split-Path -Leaf $hashFile))"
Say "  Publish it in GitHub: Releases > Draft a new release > attach the zip (the README inside explains how to use it)."
Say "  The zip is in .gitignore (*.zip and /Release/): it never goes in a commit."
$script:log.Dispose()

if (-not $NoPause) {
    $openNow = Read-Host "Open the Release folder? (Y/N)"
    if ($openNow -match '^[yYsS]') { Start-Process -FilePath "explorer.exe" -ArgumentList "`"$releaseFolder`"" }
}
