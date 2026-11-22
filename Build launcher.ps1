# Build launcher.ps1
# Builds the full launcher into C:\Steambirds\Launcher\dist, ready to run without dotnet and without typing commands:
#   dist\SteambirdsLauncher.exe        launcher (Client, Server and Settings tabs)
#   dist\lang\                         launcher languages (copied from Launcher\lang; English is built into the .exe)
#   dist\server\SteambirdsServer.exe   server (the same code that Run server.ps1 runs), with maps, data and configs
#   dist\plugin\QuieterPlugin.dll      Steambirds Quieter plugin, which the launcher copies into the game's BepInEx folder
#   dist\editor-assets\                game pictures for the Map editor tab (copied from Launcher\editor-assets)
#   dist\SteambirdsGameFiles.exe       takes configBinary, the plugin's data files and the map editor pictures from the
#                                      player's own copy of the game (GameFiles\README.md); writes server\data and editor-assets
# The .exe files are self-contained: whoever receives the dist folder does not need to install .NET.
#
# Run it again whenever the server, plugin or launcher code changes. Data copied into dist\server:
#   - maps, data, loot_tables, ai (enemy AI files, which the Enemy AI tab edits) and configs (progression.json,
#     items.json, stores.json, dungeons.json, enemies.json): copied when missing in dist or when the Server file is
#     newer. Any other folder of Server\ without code is picked up automatically. Before a file is replaced, the
#     version the launcher had goes to dist\server\_backup\<date>.
#   - accounts: the server keeps one per player in server\accounts\<key> (account.json, inventory.json, acceptances.json).
#     The accounts folder is never copied (dist keeps its own; Run server.ps1 uses the ones in Server).
#   - network.json (name, ports and who can join): copied like the other configs.
#   - game data: configBinary (from the installed game, Steambirds_Data\StreamingAssets) and portraits_config.txt
#     (from the game's BepInEx folder) go to dist\server\data, so a host PC WITHOUT the game still has items, loot,
#     XP tables, skills and unlocks. dist is in .gitignore: these game files never go to GitHub.
#
# Before compiling, it looks for duplicated .cs files ("Name-1.cs" copies, and the same file in two folders, such as
# LootDrops.cs in the root and in Items\), which make the build fail with CS0101/CS0111. Shared\EnemyAi (the enemy AI
# core compiled into BOTH the server and the launcher) is checked together with each of them.
# Everything shown on screen is also written to Launcher\build\build.log.

$root = $PSScriptRoot
if (-not $root) { $root = "C:\Steambirds" }
$serverFolder      = Join-Path $root "Server"
$pluginFolder      = Join-Path $root "QuieterPlugin"
$gameFilesFolder   = Join-Path $root "GameFiles"
$sharedFolder      = Join-Path $root "Shared"
$launcherFolder    = Join-Path $root "Launcher"
$buildFolder       = Join-Path $launcherFolder "build"
$distFolder        = Join-Path $launcherFolder "dist"
$duplicatesFolder  = Join-Path $root "_duplicates"
$buildLog          = Join-Path $buildFolder "build.log"

New-Item -ItemType Directory -Force -Path $buildFolder | Out-Null
$script:log = New-Object System.IO.StreamWriter($buildLog, $false, (New-Object System.Text.UTF8Encoding($false)))
$script:log.AutoFlush = $true

# dotnet 8 writes its messages in UTF-8; without this, PowerShell reads them as code page 850 and the accents break.
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

function Say($text, $color) {
    if ($color) { Write-Host $text -ForegroundColor $color } else { Write-Host $text }
    $script:log.WriteLine([string]$text)
}

function Fail($text) {
    Say ""
    Say "ERROR: $text" Red
    Say "The full build log is in: $buildLog" Yellow
    $script:log.Dispose()
    Read-Host "Press Enter to close"
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

# ---------------------------------------------------------------- duplicated .cs files

# Copies "Name-1.cs", "Name (1).cs", "Name - Copia.cs", "Name - Copy.cs" next to "Name.cs" (same rule as Run server.ps1).
function FindCopies($folder) {
    $found = @()
    $files = @(Get-ChildItem -LiteralPath $folder -Filter "*.cs" -File -ErrorAction SilentlyContinue)
    foreach ($file in $files) {
        $name = $file.BaseName
        $withoutSuffix = $name -replace '(-\d+| \(\d+\)| - C.pia( \(\d+\))?| - Copy( \(\d+\))?)$', ''
        if ($withoutSuffix -eq $name) { continue }
        $original = Join-Path $folder ($withoutSuffix + ".cs")
        if (Test-Path -LiteralPath $original) {
            $found += New-Object PSObject -Property @{ Copy = $file.FullName; Original = $original }
        }
    }
    return $found
}

function CodeFolders($project) {
    $folders = @((Get-Item -LiteralPath $project))
    $folders += @(Get-ChildItem -LiteralPath $project -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs)(\\|$)' })
    return $folders
}

function ResolveCopies($project) {
    if (-not (Test-Path -LiteralPath $project)) { return }
    foreach ($folder in (CodeFolders $project)) {
        $duplicates = @(FindCopies $folder.FullName)
        if ($duplicates.Count -eq 0) { continue }

        Say ""
        Say "WARNING: duplicated .cs files in $($folder.FullName)" Yellow
        foreach ($pair in $duplicates) {
            $original = Get-Item -LiteralPath $pair.Original
            $copy = Get-Item -LiteralPath $pair.Copy
            $newer = $original.Name
            if ($copy.LastWriteTime -gt $original.LastWriteTime) { $newer = $copy.Name }
            Say ("  {0}  ({1:dd/MM/yyyy HH:mm:ss}, {2} bytes)" -f $original.Name, $original.LastWriteTime, $original.Length)
            Say ("  {0}  ({1:dd/MM/yyyy HH:mm:ss}, {2} bytes)  -> newer: {3}" -f $copy.Name, $copy.LastWriteTime, $copy.Length, $newer)
        }
        $answer = Read-Host "Keep only the newest of each pair? The other one goes to $duplicatesFolder (Y/N)"
        if ($answer -notmatch '^[yYsS]') {
            Say "Nothing was moved. If the build fails, delete the extra copy."
            continue
        }
        New-Item -ItemType Directory -Force -Path $duplicatesFolder | Out-Null
        foreach ($pair in $duplicates) {
            if (-not (Test-Path -LiteralPath $pair.Copy)) { continue }
            $original = Get-Item -LiteralPath $pair.Original
            $copy = Get-Item -LiteralPath $pair.Copy
            $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
            if ($copy.LastWriteTime -gt $original.LastWriteTime) {
                Move-Item -LiteralPath $original.FullName -Destination (Join-Path $duplicatesFolder ($stamp + "-" + $original.Name)) -Force
                Move-Item -LiteralPath $copy.FullName -Destination $pair.Original -Force
                Say "  $($original.Name): kept the version that was in $($copy.Name)"
            }
            else {
                Move-Item -LiteralPath $copy.FullName -Destination (Join-Path $duplicatesFolder ($stamp + "-" + $copy.Name)) -Force
                Say "  $($copy.Name): moved to $duplicatesFolder"
            }
        }
    }
}

# The same file (identical content) in two folders of the project: the compiler reads both and the class is duplicated.
# The copy deepest in the folders stays (e.g. Items\LootDrops.cs); the others go to _duplicates.
# Files with the same name and different content only give a warning (they may be different classes).
# Takes several project folders and checks them together: the server and the launcher both compile Shared\EnemyAi.
function ResolveSameFileInTwoFolders([string[]]$projects) {
    $files = @()
    foreach ($project in $projects) {
        if (-not (Test-Path -LiteralPath $project)) { continue }
        $files += @(Get-ChildItem -LiteralPath $project -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\(bin|obj|\.vs)\\' })
    }
    $groups = @($files | Group-Object Name | Where-Object { $_.Count -gt 1 })
    foreach ($group in $groups) {
        $hashes = @($group.Group | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } | Select-Object -Unique)
        Say ""
        if ($hashes.Count -gt 1) {
            Say "WARNING: $($group.Name) exists in more than one folder, with different content:" Yellow
            foreach ($file in $group.Group) {
                Say ("  {0}  ({1:dd/MM/yyyy HH:mm:ss}, {2} bytes)" -f $file.FullName, $file.LastWriteTime, $file.Length)
            }
            Say "  If they are the same class, the build fails with CS0101: delete the one in the wrong place."
            continue
        }

        $sorted = @($group.Group | Sort-Object @{ Expression = { $_.FullName.Split('\').Count }; Descending = $true }, @{ Expression = { $_.LastWriteTime }; Descending = $true })
        $keep = $sorted[0]
        $extra = @($sorted | Select-Object -Skip 1)
        Say "WARNING: $($group.Name) is in more than one folder, with the same content (the class is duplicated):" Yellow
        Say "  keep:   $($keep.FullName)"
        foreach ($file in $extra) { Say "  extra:  $($file.FullName)" }
        $answer = Read-Host "Move the extra copies to $duplicatesFolder ? (Y/N)"
        if ($answer -notmatch '^[yYsS]') {
            Say "Nothing was moved. The build will fail while both copies exist."
            continue
        }
        New-Item -ItemType Directory -Force -Path $duplicatesFolder | Out-Null
        foreach ($file in $extra) {
            $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
            Move-Item -LiteralPath $file.FullName -Destination (Join-Path $duplicatesFolder ($stamp + "-" + $file.Name)) -Force
            Say "  moved: $($file.FullName)"
        }
    }
}

# ---------------------------------------------------------------- programs open

# The launcher and the dist server must be closed so the .exe files can be replaced.
function CloseDistPrograms() {
    $openProcesses = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $path = $null
        try { $path = $_.Path } catch { }
        $path -and $path.StartsWith($distFolder, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($openProcesses.Count -eq 0) { return }

    Say ""
    Say "Programs in the dist folder are open (they must be closed so the files can be updated):" Yellow
    foreach ($p in $openProcesses) { Say ("  {0} (process {1})" -f $p.ProcessName, $p.Id) }
    $answer = Read-Host "Close them now? (Y/N)"
    if ($answer -notmatch '^[yYsS]') { Fail "close the launcher (and its server) and run the build again." }
    foreach ($p in $openProcesses) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    Start-Sleep -Seconds 2
}

# ---------------------------------------------------------------- copying the server data

$script:newFiles = 0
$script:updated = 0
$script:unchanged = 0
$copyStamp = Get-Date -Format "yyyyMMdd-HHmmss"
$distServerFolder = Join-Path $distFolder "server"
$backupFolder = Join-Path $distServerFolder ("_backup\" + $copyStamp)

function SameContent($a, $b) {
    $itemA = Get-Item -LiteralPath $a
    $itemB = Get-Item -LiteralPath $b
    if ($itemA.Length -ne $itemB.Length) { return $false }
    return (Get-FileHash -LiteralPath $a -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $b -Algorithm SHA256).Hash
}

# Copies when the target does not exist or is older (keeping the target's version in _backup first).
function CopyIfNewer($source, $target, $relative) {
    if (Test-Path -LiteralPath $target) {
        $o = Get-Item -LiteralPath $source
        $d = Get-Item -LiteralPath $target
        if ($o.LastWriteTimeUtc -le $d.LastWriteTimeUtc -or (SameContent $source $target)) {
            $script:unchanged++
            return
        }
        $backup = Join-Path $backupFolder $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
        Copy-Item -LiteralPath $target -Destination $backup -Force
        Copy-Item -LiteralPath $source -Destination $target -Force
        $script:updated++
        Say "  updated: $relative (the previous version is in server\_backup\$copyStamp)"
        return
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
    Copy-Item -LiteralPath $source -Destination $target -Force
    $script:newFiles++
}

function CopyServerData() {
    Say ""
    Say "=== Server data -> dist\server ===" Cyan

    # Data folders: the ones without code (maps, data, loot_tables, items...). A new data folder is picked up automatically.
    $ignore = @("bin", "obj", "Properties", "client-uploads", ".vs", "_backup", "accounts")
    $folders = @(Get-ChildItem -LiteralPath $serverFolder -Directory | Where-Object {
        ($ignore -notcontains $_.Name) -and
        -not (Get-ChildItem -LiteralPath $_.FullName -Filter "*.cs" -File -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1)
    })
    foreach ($folder in $folders) {
        $files = @(Get-ChildItem -LiteralPath $folder.FullName -File -Recurse -ErrorAction SilentlyContinue)
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($serverFolder.Length).TrimStart('\')
            CopyIfNewer $file.FullName (Join-Path $distServerFolder $relative) $relative
        }
        Say ("  folder {0}: {1} file(s)" -f $folder.Name, $files.Count)
    }

    # Files in the root: configs (.json) and texts (.md). appsettings.json always comes from the project.
    $rootFiles = @(Get-ChildItem -LiteralPath $serverFolder -File | Where-Object {
        (@(".json", ".md") -contains $_.Extension.ToLowerInvariant()) -and
        ($_.Name -ne "appsettings.Development.json") -and ($_.Name -ne "appsettings.json")
    })
    foreach ($file in $rootFiles) {
        CopyIfNewer $file.FullName (Join-Path $distServerFolder $file.Name) $file.Name
    }
    $appsettings = Join-Path $serverFolder "appsettings.json"
    if (Test-Path -LiteralPath $appsettings) { Copy-Item -LiteralPath $appsettings -Destination $distServerFolder -Force }

    # Accounts: one per player in server\accounts (never copied).
    $distAccountsFolder = Join-Path $distServerFolder "accounts"
    if (Test-Path -LiteralPath $distAccountsFolder) {
        $accountCount = @(Get-ChildItem -LiteralPath $distAccountsFolder -Directory -ErrorAction SilentlyContinue).Count
        Say "  accounts: $accountCount player account(s) in server\accounts (kept)"
    }

    Say ("  {0} new file(s), {1} updated, {2} unchanged" -f $script:newFiles, $script:updated, $script:unchanged)
}

# ---------------------------------------------------------------- game data from the installed game

# Installed game folder: STEAMBIRDS_GAME first, then the default Steam depot folder. $null when not found.
function FindGameFolder() {
    $candidates = @()
    if ($env:STEAMBIRDS_GAME) { $candidates += $env:STEAMBIRDS_GAME }
    $candidates += "C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011"
    foreach ($folder in $candidates) {
        if ($folder -and (Test-Path -LiteralPath (Join-Path $folder "Steambirds_Data\StreamingAssets\configBinary"))) {
            return $folder
        }
    }
    return $null
}

# Copies a game file into dist\server\data when it is missing there or when its content changed.
function CopyGameFile($source, $fileName) {
    $targetFolder = Join-Path $distServerFolder "data"
    $target = Join-Path $targetFolder $fileName
    New-Item -ItemType Directory -Force -Path $targetFolder | Out-Null
    if ((Test-Path -LiteralPath $target) -and (SameContent $source $target)) {
        Say "  data\$fileName : unchanged"
        return
    }
    Copy-Item -LiteralPath $source -Destination $target -Force
    $sizeKb = [math]::Round((Get-Item -LiteralPath $target).Length / 1KB)
    Say "  data\$fileName : copied from the game ($sizeKb KB)"
}

# The server reads configBinary straight from the installed game. On a host PC without the game it had no
# configBinary at all: no item catalog (no loot, no reforge, no recycling), built-in XP tables, no skill
# validation and only the walker unlocked. Copying it into the package fixes that for any host.
function CopyGameDataToServer() {
    Say ""
    Say "=== Game data -> dist\server\data ===" Cyan
    $gameFolder = FindGameFolder
    if (-not $gameFolder) {
        Say "  WARNING: game not found (set STEAMBIRDS_GAME). The package has no configBinary: a host without the game gets no items, loot or XP tables." Yellow
        return
    }
    Say "  game: $gameFolder"
    CopyGameFile (Join-Path $gameFolder "Steambirds_Data\StreamingAssets\configBinary") "configBinary"

    # Pilot portrait pieces, written by the Quieter plugin 1.20.0+ in the game's BepInEx folder.
    $portraits = Join-Path $gameFolder "BepInEx\portraits_config.txt"
    $projectPortraits = Join-Path $serverFolder "data\portraits_config.txt"
    if (Test-Path -LiteralPath $projectPortraits) {
        Say "  data\portraits_config.txt : comes from Server\data"
    }
    elseif (Test-Path -LiteralPath $portraits) {
        CopyGameFile $portraits "portraits_config.txt"
    }
    else {
        Say "  WARNING: portraits_config.txt is not in the game's BepInEx folder (open the game once with the plugin): the pilot has no portrait." Yellow
    }
}

# ---------------------------------------------------------------- launcher languages

# Launcher\lang -> dist\lang. Copies when the file is missing in dist or when the project's file is newer.
# A file in dist that is newer than the project's one (translation edited directly in dist) is kept, with a warning.
# The *.missing.json files are generated by the launcher in dist and never leave the project.
function CopyLanguages() {
    $source = Join-Path $launcherFolder "lang"
    $target = Join-Path $distFolder "lang"
    if (-not (Test-Path -LiteralPath $source)) {
        Say "  WARNING: $source does not exist; the launcher will run in English only." Yellow
        return
    }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $copied = 0
    $unchangedCount = 0
    $files = @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -notlike "*.missing.json" })
    foreach ($file in $files) {
        $dest = Join-Path $target $file.Name
        if (Test-Path -LiteralPath $dest) {
            $current = Get-Item -LiteralPath $dest
            if (SameContent $file.FullName $dest) { $unchangedCount++; continue }
            if ($current.LastWriteTimeUtc -gt $file.LastWriteTimeUtc) {
                Say "  WARNING: dist\lang\$($file.Name) is newer than the project's copy and was kept (copy it to Launcher\lang if it is the right version)." Yellow
                continue
            }
        }
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
        $copied++
    }
    Say ("  lang: {0} file(s) copied, {1} unchanged" -f $copied, $unchangedCount)
}

# Launcher\editor-assets -> dist\editor-assets: the game's pictures (floor pieces and units, small PNGs), the list of
# floor piece names (ground_assets.txt) and thumbs.json, which the Map editor tab draws. They are generated from the
# game by SteambirdsGameFiles.exe. Without them the editor still works (shapes and flat colors).
function CopyEditorAssets() {
    $source = Join-Path $launcherFolder "editor-assets"
    $target = Join-Path $distFolder "editor-assets"
    if (-not (Test-Path -LiteralPath $source)) {
        Say "  WARNING: $source does not exist; the map editor will draw shapes and flat colors instead of the game pictures." Yellow
        return
    }
    $copied = 0
    $unchangedCount = 0
    $files = @(Get-ChildItem -LiteralPath $source -File -Recurse)
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($source.Length).TrimStart('\')
        $dest = Join-Path $target $relative
        if ((Test-Path -LiteralPath $dest) -and (SameContent $file.FullName $dest)) { $unchangedCount++; continue }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $dest -Force
        $copied++
    }
    Say ("  editor-assets: {0} file(s) copied, {1} unchanged" -f $copied, $unchangedCount)
}

# ================================================================ start

Say "=== BUILD STEAMBIRDS LAUNCHER ===" Cyan
Say "Date: $(Get-Date -Format 'dd/MM/yyyy HH:mm:ss')"
Say "Project folder: $root"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Fail "the dotnet command was not found. Install the .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)."
}
Say "dotnet: $((& dotnet --version 2>&1) -join ' ')"

if (-not (Test-Path -LiteralPath (Join-Path $serverFolder "SteambirdsServer.csproj"))) { Fail "SteambirdsServer.csproj not found in $serverFolder" }
if (-not (Test-Path -LiteralPath (Join-Path $launcherFolder "SteambirdsLauncher.csproj"))) { Fail "SteambirdsLauncher.csproj not found in $launcherFolder" }
# Both csproj files compile ..\Shared\EnemyAi\*.cs: without the folder the wildcard matches nothing and the build
# fails with a flood of "type not found" errors.
if (-not (Test-Path -LiteralPath (Join-Path $sharedFolder "EnemyAi"))) { Fail "Shared\EnemyAi not found in $sharedFolder (the server and the launcher both compile it)." }

ResolveCopies $serverFolder
ResolveCopies $pluginFolder
ResolveCopies $launcherFolder
ResolveCopies $gameFilesFolder
ResolveCopies $sharedFolder
ResolveSameFileInTwoFolders @($serverFolder, $sharedFolder)
ResolveSameFileInTwoFolders @($launcherFolder, $sharedFolder)

CloseDistPrograms

# ---------------------------------------------------------------- server
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

# ---------------------------------------------------------------- plugin (optional: the server and the game run without it)
$pluginDll = Join-Path $pluginFolder "bin\Release\net46\QuieterPlugin.dll"
if (Test-Path -LiteralPath (Join-Path $pluginFolder "QuieterPlugin.csproj")) {
    $exitCode = RunDotnet "Plugin (QuieterPlugin)" @("build", (Join-Path $pluginFolder "QuieterPlugin.csproj"), "-c", "Release")
    if ($exitCode -ne 0) { Say "WARNING: the plugin did not build; the launcher uses the last compiled dll, if there is one." Yellow }
}

# ---------------------------------------------------------------- launcher
$launcherOutput = Join-Path $buildFolder "launcher"
if (Test-Path -LiteralPath $launcherOutput) { Remove-Item -LiteralPath $launcherOutput -Recurse -Force }
$exitCode = RunDotnet "Launcher (SteambirdsLauncher)" @(
    "publish", (Join-Path $launcherFolder "SteambirdsLauncher.csproj"),
    "-c", "Release", "-r", "win-x64", "--self-contained", "true",
    "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
    "-p:DebugType=none", "-o", $launcherOutput)
if ($exitCode -ne 0) { Fail "the launcher build failed (look for the lines with 'error' above)." }
$launcherExe = Join-Path $launcherOutput "SteambirdsLauncher.exe"
if (-not (Test-Path -LiteralPath $launcherExe)) { Fail "the launcher publish did not create $launcherExe" }

# ---------------------------------------------------------------- game files tool (optional: the launcher runs without it)
$gameFilesExe = $null
if (Test-Path -LiteralPath (Join-Path $gameFilesFolder "SteambirdsGameFiles.csproj")) {
    $gameFilesOutput = Join-Path $buildFolder "gamefiles"
    if (Test-Path -LiteralPath $gameFilesOutput) { Remove-Item -LiteralPath $gameFilesOutput -Recurse -Force }
    $exitCode = RunDotnet "Game files tool (SteambirdsGameFiles)" @(
        "publish", (Join-Path $gameFilesFolder "SteambirdsGameFiles.csproj"),
        "-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=none", "-o", $gameFilesOutput)
    if ($exitCode -ne 0) { Say "WARNING: the game files tool did not build; dist keeps the previous SteambirdsGameFiles.exe, if there is one." Yellow }
    else { $gameFilesExe = Join-Path $gameFilesOutput "SteambirdsGameFiles.exe" }
}

# ---------------------------------------------------------------- assemble the dist folder
Say ""
Say "=== Assembling $distFolder ===" Cyan
New-Item -ItemType Directory -Force -Path $distFolder | Out-Null
New-Item -ItemType Directory -Force -Path $distServerFolder | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $distFolder "plugin") | Out-Null

Copy-Item -LiteralPath $launcherExe -Destination $distFolder -Force
Copy-Item -LiteralPath $serverExe -Destination $distServerFolder -Force
Say "  SteambirdsLauncher.exe and server\SteambirdsServer.exe copied"
if ($gameFilesExe -and (Test-Path -LiteralPath $gameFilesExe)) {
    Copy-Item -LiteralPath $gameFilesExe -Destination $distFolder -Force
    Say "  SteambirdsGameFiles.exe copied"
}

if (Test-Path -LiteralPath $pluginDll) {
    Copy-Item -LiteralPath $pluginDll -Destination (Join-Path $distFolder "plugin") -Force
    Say "  plugin\QuieterPlugin.dll copied"
}
else {
    Say "  WARNING: $pluginDll does not exist; the launcher will run without the plugin." Yellow
}

$docPath = Join-Path $launcherFolder "README.md"
if (Test-Path -LiteralPath $docPath) { Copy-Item -LiteralPath $docPath -Destination $distFolder -Force }

CopyLanguages

CopyEditorAssets

CopyServerData

CopyGameDataToServer

$launcherSizeMb = [math]::Round((Get-Item -LiteralPath (Join-Path $distFolder "SteambirdsLauncher.exe")).Length / 1MB, 1)
$serverSizeMb = [math]::Round((Get-Item -LiteralPath (Join-Path $distServerFolder "SteambirdsServer.exe")).Length / 1MB, 1)
Say ""
Say "Launcher ready in: $distFolder" Green
Say "  SteambirdsLauncher.exe ($launcherSizeMb MB) and server\SteambirdsServer.exe ($serverSizeMb MB)"
Say "  Launcher settings, logs and reports stay inside that folder."
$script:log.Dispose()

$openNow = Read-Host "Open the launcher now? (Y/N)"
if ($openNow -match '^[yYsS]') {
    Start-Process -FilePath (Join-Path $distFolder "SteambirdsLauncher.exe") -WorkingDirectory $distFolder
}
