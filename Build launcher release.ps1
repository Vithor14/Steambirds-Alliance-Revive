# Build launcher release.ps1
# Builds the zip of the launcher for GitHub Releases: everything a PC that has never run the project needs, and nothing else.
#
#   Release\SteambirdsLauncher-<version>-win-x64.zip
#     SteambirdsLauncher\SteambirdsLauncher.exe     the launcher (self-contained: nobody needs .NET installed)
#     SteambirdsLauncher\SteambirdsGameFiles.exe    takes the game files from the player's own copy of the game
#     SteambirdsLauncher\plugin\QuieterPlugin.dll   the plugin the launcher / SteambirdsGameFiles.exe install in the game
#     SteambirdsLauncher\lang\                      launcher languages (English is built in)
#     SteambirdsLauncher\server\SteambirdsServer.exe  the server the launcher starts, next to appsettings.json, its settings
#                                                    (network.json, progression.json, items.json...), maps\, loot_tables\ and ai\
#     SteambirdsLauncher\README.md, SteambirdsGameFiles-README.md, LICENSE
#
# NO game file goes into the zip: configBinary, the data files of the plugin (units_config.txt...), the item catalog and the
# map editor pictures (editor-assets) belong to the game. Each player makes them from their own copy with
# SteambirdsGameFiles.exe. Player accounts, launcher settings, logs and backups stay out too.
# Before zipping, the script checks the package for those files and stops if it finds one.
#
# Usage:  .\"Build launcher release.ps1"                       (version = the one in Launcher\SteambirdsLauncher.csproj)
#         .\"Build launcher release.ps1" -Version 0.5.0
#         .\"Build launcher release.ps1" -Version 0.5.0 -NoPause
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
$sharedFolder     = Join-Path $root "Shared"
$launcherFolder   = Join-Path $root "Launcher"
$releaseFolder    = Join-Path $root "Release"
$buildFolder      = Join-Path $releaseFolder "build"
$logFile          = Join-Path $buildFolder "release.log"

# Data folders of Server\ that go into the release. A new data folder must be added here on purpose (the script warns
# about folders without code that are not in this list), so nothing private or from the game leaks by accident.
$releaseFolders   = @("maps", "loot_tables", "ai")

# Files in the root of Server\ that never go into the release (development settings and account files).
$excludedRootFiles = @("appsettings.Development.json", "account.json", "inventory.json", "acceptances.json", "flocks.json")

# Folders of Server\ that are known and never released: build output, game data, accounts, logs, backups.
$knownPrivateFolders = @("bin", "obj", "Properties", ".vs", "data", "catalog", "accounts", "client-uploads", "logs", "_backup", "maptest")

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

# Self-contained single-file publish of a project into a folder. Returns the exe path (stops the script on failure).
function PublishExe($title, $project, $exeName, $output) {
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    $exitCode = RunDotnet $title @(
        "publish", $project,
        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=none", "-o", $output)
    if ($exitCode -ne 0) { Fail "the build of $exeName failed (look for the lines with 'error' above)." }
    $exe = Join-Path $output $exeName
    if (-not (Test-Path -LiteralPath $exe)) { Fail "the publish did not create $exe" }
    return $exe
}

# ---------------------------------------------------------------- duplicated .cs files (they break the build)

# "Name-1.cs", "Name (1).cs", "Name - Copia.cs", "Name - Copy.cs" next to "Name.cs", and the same file in two
# folders of one project. Only reported here: "Build launcher.ps1" offers to move them.
function FindDuplicatedCode([string[]]$projects) {
    $found = @()
    $files = @()
    foreach ($project in $projects) {
        if (-not (Test-Path -LiteralPath $project)) { continue }
        $files += @(Get-ChildItem -LiteralPath $project -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs|build|dist)\\' })
    }
    foreach ($file in $files) {
        $name = $file.BaseName
        $withoutSuffix = $name -replace '(-\d+| \(\d+\)| - C.pia( \(\d+\))?| - Copy( \(\d+\))?)$', ''
        if ($withoutSuffix -eq $name) { continue }
        if (Test-Path -LiteralPath (Join-Path $file.DirectoryName ($withoutSuffix + ".cs"))) { $found += $file.FullName }
    }
    # Same name and same content in two folders: the class is compiled twice.
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
    $forbiddenFolders = @("accounts", "maptest", "data", "catalog", "client-uploads", "_backup", "logs", "editor-assets", "BepInEx")
    $found = @()
    foreach ($file in @(Get-ChildItem -LiteralPath $folder -File -Recurse -Force)) {
        $relative = $file.FullName.Substring($folder.Length).TrimStart('\')
        $parts = $relative.Split('\')
        $badFolder = @($parts | Select-Object -SkipLast 1 | Where-Object { $forbiddenFolders -contains $_ }).Count -gt 0
        $badName = ($forbiddenNames -contains $file.Name) -or ($file.Name -like "*_config.txt") -or ($file.Name -like "*.missing.json")
        $badExtension = $forbiddenExtensions -contains $file.Extension.ToLowerInvariant()
        $badDll = ($file.Extension -ieq ".dll") -and ($relative -ine "plugin\QuieterPlugin.dll")
        if ($badFolder -or $badName -or $badExtension -or $badDll) { $found += $relative }
    }
    return $found
}

# ================================================================ start

Say "=== BUILD STEAMBIRDS LAUNCHER RELEASE ===" Cyan
Say "Date: $(Get-Date -Format 'dd/MM/yyyy HH:mm:ss')"
Say "Project folder: $root"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "the dotnet command was not found. Install the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)."
}
Say "dotnet: $((& dotnet --version 2>&1) -join ' ')"

$serverProject     = Join-Path $serverFolder "SteambirdsServer.csproj"
$launcherProject   = Join-Path $launcherFolder "SteambirdsLauncher.csproj"
$gameFilesProject  = Join-Path $gameFilesFolder "SteambirdsGameFiles.csproj"
$pluginProject     = Join-Path $pluginFolder "QuieterPlugin.csproj"
foreach ($project in @($serverProject, $launcherProject, $gameFilesProject, $pluginProject)) {
    if (-not (Test-Path -LiteralPath $project)) { Fail "$project not found" }
}
# The server and the launcher both compile ..\Shared\EnemyAi\*.cs: without the folder the build fails.
if (-not (Test-Path -LiteralPath (Join-Path $sharedFolder "EnemyAi"))) { Fail "Shared\EnemyAi not found in $sharedFolder (the server and the launcher compile it)." }

# Version of the release (goes in the zip name): the launcher's own version unless one is given.
if (-not $Version) {
    $match = [regex]::Match((Get-Content -LiteralPath $launcherProject -Raw), '<Version>([^<]+)</Version>')
    $defaultVersion = if ($match.Success) { $match.Groups[1].Value.Trim() } else { Get-Date -Format "yyyy.MM.dd" }
    if ($NoPause) { $Version = $defaultVersion }
    else {
        $answer = Read-Host "Release version (Enter = $defaultVersion)"
        if ($answer -and $answer.Trim()) { $Version = $answer.Trim() } else { $Version = $defaultVersion }
    }
}
if ($Version -notmatch '^[0-9A-Za-z._-]+$') { Fail "invalid version '$Version' (use letters, digits, '.', '-' or '_')" }
$packageName = "SteambirdsLauncher-$Version-win-x64"
Say "Version: $Version"

$duplicates = @()
$duplicates += @(FindDuplicatedCode @($serverFolder, $sharedFolder))
$duplicates += @(FindDuplicatedCode @($launcherFolder, $sharedFolder))
$duplicates += @(FindDuplicatedCode @($gameFilesFolder))
$duplicates += @(FindDuplicatedCode @($pluginFolder))
if ($duplicates.Count -gt 0) {
    Say ""
    Say "Duplicated .cs files (the build fails with CS0101/CS0111):" Yellow
    foreach ($item in $duplicates) { Say "  $item" }
    Fail "delete the extra copies (or run Build launcher.ps1, which offers to move them) and run this script again."
}

# ---------------------------------------------------------------- builds
$serverExe     = PublishExe "Server (SteambirdsServer)" $serverProject "SteambirdsServer.exe" (Join-Path $buildFolder "server")
$launcherExe   = PublishExe "Launcher (SteambirdsLauncher)" $launcherProject "SteambirdsLauncher.exe" (Join-Path $buildFolder "launcher")
$gameFilesExe  = PublishExe "Game files tool (SteambirdsGameFiles)" $gameFilesProject "SteambirdsGameFiles.exe" (Join-Path $buildFolder "gamefiles")

# The plugin must be built now: the game files tool depends on its export mode (GameFilesExport.cs).
$pluginDll = Join-Path $pluginFolder "bin\Release\net46\QuieterPlugin.dll"
$exitCode = RunDotnet "Plugin (QuieterPlugin)" @("build", $pluginProject, "-c", "Release")
if ($exitCode -ne 0) { Fail "the plugin build failed (it needs the game's DLLs in the game folder; see QuieterPlugin.csproj)." }
if (-not (Test-Path -LiteralPath $pluginDll)) { Fail "the plugin build did not create $pluginDll" }

# ---------------------------------------------------------------- package folder
Say ""
Say "=== Package folder ===" Cyan
$stageRoot = Join-Path $buildFolder "stage"
$stage = Join-Path $stageRoot "SteambirdsLauncher"
if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

CopyFileTo $launcherExe (Join-Path $stage "SteambirdsLauncher.exe")
CopyFileTo $gameFilesExe (Join-Path $stage "SteambirdsGameFiles.exe")
CopyFileTo $pluginDll (Join-Path $stage "plugin\QuieterPlugin.dll")
CopyFileTo $serverExe (Join-Path $stage "server\SteambirdsServer.exe")
Say "  SteambirdsLauncher.exe, SteambirdsGameFiles.exe, plugin\QuieterPlugin.dll and server\SteambirdsServer.exe"

# Languages (the *.missing.json files are generated by the launcher and never leave the project).
$langSource = Join-Path $launcherFolder "lang"
$langCount = 0
foreach ($file in @(Get-ChildItem -LiteralPath $langSource -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike "*.missing.json" })) {
    CopyFileTo $file.FullName (Join-Path $stage ("lang\" + $file.Name))
    $langCount++
}
Say "  lang\: $langCount file(s)"

# Server data: the folders and settings the server reads from its own folder.
$stageServer = Join-Path $stage "server"
foreach ($name in $releaseFolders) {
    $source = Join-Path $serverFolder $name
    if (-not (Test-Path -LiteralPath $source)) { Say "  WARNING: Server\$name does not exist" Yellow; continue }
    $count = CopyFolderTo $source (Join-Path $stageServer $name)
    Say ("  server\{0}\: {1} file(s)" -f $name, $count)
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
foreach ($file in $rootFiles) { CopyFileTo $file.FullName (Join-Path $stageServer $file.Name) }
Say ("  server settings: {0}" -f (($rootFiles | ForEach-Object { $_.Name }) -join ", "))

foreach ($doc in @(@((Join-Path $launcherFolder "README.md"), "README.md"), @((Join-Path $gameFilesFolder "README.md"), "SteambirdsGameFiles-README.md"), @((Join-Path $root "LICENSE"), "LICENSE"))) {
    if (Test-Path -LiteralPath $doc[0]) { CopyFileTo $doc[0] (Join-Path $stage $doc[1]) }
}
Say "  README.md, SteambirdsGameFiles-README.md and LICENSE"

# ---------------------------------------------------------------- no game files, no accounts
$forbidden = @(FindForbiddenFiles $stage)
if ($forbidden.Count -gt 0) {
    Say ""
    Say "Files that must not be published (game data, accounts, settings or logs):" Red
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
    # includeBaseDirectory: the zip opens into a "SteambirdsLauncher" folder.
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
Say "  Publish it in GitHub: Releases > Draft a new release > attach the zip."
Say "  On a PC without the project: unzip, run SteambirdsGameFiles.exe once (it needs the game installed from Steam), then open SteambirdsLauncher.exe."
Say "  The zip is in .gitignore (*.zip and /Release/): it never goes in a commit."
$script:log.Dispose()

if (-not $NoPause) {
    $openNow = Read-Host "Open the Release folder? (Y/N)"
    if ($openNow -match '^[yYsS]') { Start-Process -FilePath "explorer.exe" -ArgumentList "`"$releaseFolder`"" }
}
