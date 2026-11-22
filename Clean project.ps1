# Clean project.ps1
# Removes what the project does not need: the old compatibility layer and its files, files with Portuguese names,
# build output, old reports and logs, duplicate copies of .cs files and other garbage. It also moves old data to
# the new English names so nothing the host owns is lost.
#
#   .\Clean project.ps1              lists everything, asks for confirmation (type YES), then cleans
#   .\Clean project.ps1 -DryRun      only lists, changes nothing
#   .\Clean project.ps1 -Yes         does not ask (for scripts)
#
# Extra switches (off by default, because they remove things you may want to keep):
#   -IncludeRelease   also removes the Release\ folder (zip and build of "Build server release.ps1")
#   -IncludeLogs      also removes the log files (Launcher\logs, Launcher\dist\logs, Server\logs)
#   -RenameFolders    renames VersoesAntigas -> OldVersions and decompilado -> decompiled (big folders, only renamed)
#   -NoPause          does not wait for Enter at the end
#
# It never touches: .git, Assets, Asset ripper, decompiled, OldVersions, accounts, Server\data, Launcher\dist\server\accounts.
# Data of the host is MOVED, not deleted: the old single account goes to _backup\old-single-account-<date>.
# After cleaning, run "Build launcher.ps1" (it builds everything again).

param(
    [switch]$DryRun,
    [switch]$Yes,
    [switch]$IncludeRelease,
    [switch]$IncludeLogs,
    [switch]$RenameFolders,
    [switch]$NoPause
)

Set-Location -Path $PSScriptRoot
$root = $PSScriptRoot
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

# Folders that are never scanned (huge, not ours, or the host's data).
$skipNames = @(".git", ".claude", "Assets", "Asset ripper", "decompiled", "decompilado", "OldVersions", "VersoesAntigas",
    "accounts", "data", "dist", "Release", "editor-assets", "_backup", "client-uploads", "catalog", "node_modules")
$projects = @("Server", "Launcher", "QuieterPlugin", "GameFiles", "Shared")

$actions = New-Object System.Collections.ArrayList   # each: Kind (Delete, Move, Run), Path, Target, Why, Script

function RelativePath($path) {
    $full = [System.IO.Path]::GetFullPath($path)
    if ($full.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) { return $full.Substring($root.Length).TrimStart('\') }
    return $full
}

function SizeOf($path) {
    try {
        if (Test-Path -LiteralPath $path -PathType Container) {
            $sum = (Get-ChildItem -LiteralPath $path -Recurse -Force -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum
            if ($null -eq $sum) { return 0 }
            return [long]$sum
        }
        return [long](Get-Item -LiteralPath $path -Force).Length
    }
    catch { return 0 }
}

function AddDelete($path, $why) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    foreach ($a in $actions) { if ($a.Kind -eq "Delete" -and $a.Path -eq $path) { return } }
    [void]$actions.Add([pscustomobject]@{ Kind = "Delete"; Path = $path; Target = ""; Why = $why; Script = $null })
}

function AddMove($path, $target, $why) {
    if (-not (Test-Path -LiteralPath $path)) { return }
    [void]$actions.Add([pscustomobject]@{ Kind = "Move"; Path = $path; Target = $target; Why = $why; Script = $null })
}

function AddRun($label, $why, $script) {
    [void]$actions.Add([pscustomobject]@{ Kind = "Run"; Path = $label; Target = ""; Why = $why; Script = $script })
}

function Warn($text) { Write-Host ("  WARNING: " + $text) -ForegroundColor Yellow }

# Walks a folder without entering the skipped folders (Get-ChildItem -Recurse would scan decompiled and Assets too).
function WalkFiles($folder) {
    if (-not (Test-Path -LiteralPath $folder)) { return }
    foreach ($item in @(Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue)) {
        if ($item.PSIsContainer) {
            if ($skipNames -contains $item.Name -or $item.Name -eq "bin" -or $item.Name -eq "obj" -or $item.Name -eq "build" -or $item.Name -eq ".vs") { continue }
            WalkFiles $item.FullName
        }
        else { $item }
    }
}

function CodeFilesThatMention($pattern, $exceptFiles) {
    $hits = @()
    foreach ($project in $projects) {
        foreach ($file in @(WalkFiles (Join-Path $root $project))) {
            if ($file.Extension -ne ".cs") { continue }
            if ($exceptFiles -contains $file.FullName) { continue }
            if (Select-String -LiteralPath $file.FullName -Pattern $pattern -Quiet) { $hits += $file.FullName }
        }
    }
    return $hits
}

Write-Host ""
Write-Host "=== Clean project ===" -ForegroundColor Cyan
Write-Host $root
if ($DryRun) { Write-Host "(dry run: nothing will be changed)" -ForegroundColor Yellow }
Write-Host ""
Write-Host "Looking for things to clean..."

# ------------------------------------------------------------------ 1. compatibility layer
$legacyFiles = @(
    (Join-Path $root "Server\Compat\LegacyNames.cs"),
    (Join-Path $root "Server\Compat\LegacyFiles.cs"),
    (Join-Path $root "Launcher\Core\LegacyFiles.cs"),
    (Join-Path $root "Launcher\Editor\LegacyNames.cs"))
$stillUsed = @(CodeFilesThatMention "Legacy(Names|Files)\." $legacyFiles)
if ($stillUsed.Count -gt 0) {
    Warn "some code still calls LegacyNames/LegacyFiles, so the compatibility files were NOT removed:"
    foreach ($file in $stillUsed) { Write-Host ("    " + (RelativePath $file)) -ForegroundColor Yellow }
}
else {
    foreach ($file in $legacyFiles) { AddDelete $file "compatibility layer (old Portuguese names)" }
    AddDelete (Join-Path $root "Server\Compat") "compatibility layer (empty folder)"
    AddDelete (Join-Path $root "Server\legacy-names.json") "table of old Portuguese names"
}

# ------------------------------------------------------------------ 2. files with Portuguese names
foreach ($file in @(WalkFiles $root)) {
    if ($file.Name -like "LEIA-ME*.md") { AddDelete $file.FullName "Portuguese README (the English README.md stays)" }
}
$oldDocs = @{
    "multiplayer-e-contas.md" = "multiplayer-and-accounts.md"; "servidor-de-dungeon.md" = "dungeon-server.md"
    "dungeon-inimigos-e-loot-v8c.md" = "dungeon-enemies-and-loot.md"; "itens-e-loot.md" = "items-and-loot.md"
    "progressao.md" = "progression.md"; "formato-de-mapas.md" = "map-format.md"; "inimigos-esboco.md" = "enemies.md"
    "tutorial-esboco.md" = "tutorial.md" }
foreach ($old in $oldDocs.Keys) {
    $oldPath = Join-Path $root ("docs\" + $old)
    if (Test-Path -LiteralPath $oldPath) {
        if (Test-Path -LiteralPath (Join-Path $root ("docs\" + $oldDocs[$old]))) { AddDelete $oldPath ("old name of docs\" + $oldDocs[$old]) }
        else { Warn ("docs\" + $old + " was kept: docs\" + $oldDocs[$old] + " does not exist yet") }
    }
}

# ------------------------------------------------------------------ 3. old single account of the server
$serverFolders = @((Join-Path $root "Server"), (Join-Path $root "Launcher\dist\server"))
foreach ($serverFolder in $serverFolders) {
    foreach ($name in @("account.json", "inventory.json", "acceptances.json", "conta.json", "inventario.json", "aceites.json")) {
        $file = Join-Path $serverFolder $name
        $backup = Join-Path $serverFolder ("_backup\old-single-account-" + $stamp)
        AddMove $file (Join-Path $backup $name) "old single account (not used anymore; moved, not deleted)"
    }
}

# ------------------------------------------------------------------ 4. old Portuguese folders and files of the server
$oldFolders = @{ "mapas" = "maps"; "dados" = "data"; "contas" = "accounts"; "itens" = "catalog"; "catalogo" = "catalog"; "envios-do-cliente" = "client-uploads" }
$oldSettings = @{ "progressao.json" = "progression.json"; "itens.json" = "items.json"; "inimigos.json" = "enemies.json"; "rede.json" = "network.json" }
foreach ($serverFolder in $serverFolders) {
    if (-not (Test-Path -LiteralPath $serverFolder)) { continue }
    foreach ($old in $oldFolders.Keys) {
        $oldPath = Join-Path $serverFolder $old
        $newPath = Join-Path $serverFolder $oldFolders[$old]
        if (-not (Test-Path -LiteralPath $oldPath)) { continue }
        if (Test-Path -LiteralPath $newPath) { Warn ((RelativePath $oldPath) + " and " + (RelativePath $newPath) + " both exist: merge them by hand, nothing was changed") }
        else { AddMove $oldPath $newPath "old Portuguese folder name (renamed, the contents are kept)" }
    }
    foreach ($old in $oldSettings.Keys) {
        $oldPath = Join-Path $serverFolder $old
        $newPath = Join-Path $serverFolder $oldSettings[$old]
        if (-not (Test-Path -LiteralPath $oldPath)) { continue }
        if (Test-Path -LiteralPath $newPath) { AddDelete $oldPath ("old name of " + $oldSettings[$old]) }
        else { AddMove $oldPath $newPath "old Portuguese settings file name (renamed)" }
    }
}
# account files inside accounts\<key>\ (also inside the folder that is renamed above)
foreach ($serverFolder in $serverFolders) {
    foreach ($accountsName in @("accounts", "contas")) {
        $accountsFolder = Join-Path $serverFolder $accountsName
        if (-not (Test-Path -LiteralPath $accountsFolder)) { continue }
        $hasOld = $false
        foreach ($dir in @(Get-ChildItem -LiteralPath $accountsFolder -Directory -ErrorAction SilentlyContinue)) {
            foreach ($old in @("conta.json", "inventario.json", "aceites.json")) {
                if (Test-Path -LiteralPath (Join-Path $dir.FullName $old)) { $hasOld = $true }
            }
        }
        if (-not $hasOld -and $accountsName -eq "accounts") { continue }
        $folderCapture = $accountsFolder
        $renameScript = {
            $base = $folderCapture
            if (-not (Test-Path -LiteralPath $base)) { $base = Join-Path (Split-Path $folderCapture -Parent) "accounts" }
            foreach ($dir in @(Get-ChildItem -LiteralPath $base -Directory -ErrorAction SilentlyContinue)) {
                foreach ($old in @("conta.json", "inventario.json", "aceites.json")) {
                    $new = @{ "conta.json" = "account.json"; "inventario.json" = "inventory.json"; "aceites.json" = "acceptances.json" }[$old]
                    $oldFile = Join-Path $dir.FullName $old
                    if ((Test-Path -LiteralPath $oldFile) -and -not (Test-Path -LiteralPath (Join-Path $dir.FullName $new))) {
                        Rename-Item -LiteralPath $oldFile -NewName $new
                    }
                }
            }
        }.GetNewClosure()
        AddRun ((RelativePath $accountsFolder) + "\<key>\conta.json, inventario.json, aceites.json") "old Portuguese account file names (renamed)" $renameScript
    }
}

# ------------------------------------------------------------------ 5. data exported by the plugin (old Portuguese rows)
$dataFolders = @((Join-Path $root "Server\data"), (Join-Path $root "Launcher\dist\server\data"))
$gameFolder = $env:STEAMBIRDS_GAME
if (-not $gameFolder) { $gameFolder = "C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011" }
$bepinex = Join-Path $gameFolder "BepInEx"
$dataFolders += $bepinex
$rowRenames = @(
    @{ File = "units_config.txt"; Rows = @(@("unidade", "unit"), @("arma", "gun")) },
    @{ File = "actions_config.txt"; Rows = @(@("nuvem", "cloud"), @("manobra", "maneuver")) },
    @{ File = "portraits_config.txt"; Rows = @(, @("parte", "part")) })
foreach ($folder in $dataFolders) {
    foreach ($entry in $rowRenames) {
        $file = Join-Path $folder $entry.File
        if (-not (Test-Path -LiteralPath $file)) { continue }
        $needsConversion = $false
        foreach ($row in $entry.Rows) {
            if (Select-String -LiteralPath $file -Pattern ("^" + $row[0] + "`t") -Quiet) { $needsConversion = $true }
        }
        if (-not $needsConversion) { continue }
        $fileCapture = $file; $rowsCapture = $entry.Rows
        $convertScript = {
            $text = [System.IO.File]::ReadAllText($fileCapture)
            foreach ($row in $rowsCapture) { $text = [regex]::Replace($text, ("(?m)^" + $row[0] + "`t"), ($row[1] + "`t")) }
            [System.IO.File]::WriteAllText($fileCapture, $text, (New-Object System.Text.UTF8Encoding($false)))
        }.GetNewClosure()
        AddRun (RelativePath $file) "rows with Portuguese names (unidade/arma/nuvem/manobra/parte): converted to English in place" $convertScript
    }
}
foreach ($folder in $dataFolders) {
    foreach ($name in @("unidades_config.txt", "acoes_config.txt", "retratos_config.txt")) {
        AddDelete (Join-Path $folder $name) "old Portuguese name of a plugin export (the English file is used)"
    }
}
if (Test-Path -LiteralPath $bepinex) {
    foreach ($name in @("mapas_ascii.txt", "predios_log.txt", "placas_log.txt", "dump_terreno.txt")) {
        AddDelete (Join-Path $bepinex $name) "old Portuguese name of a plugin log"
    }
    $cfg = Join-Path $bepinex "config\local.steambirds.quieter.cfg"
    if ((Test-Path -LiteralPath $cfg) -and (Select-String -LiteralPath $cfg -Pattern "Diagnostico|Correcoes|EsconderDebug" -Quiet)) {
        AddDelete $cfg "plugin settings with Portuguese keys (a new file with English keys is written when the game starts)"
    }
}

# maps in the launcher's dist folder that still use the old Portuguese format cannot be converted here
$distMaps = Join-Path $root "Launcher\dist\server\maps"
if (Test-Path -LiteralPath $distMaps) {
    foreach ($map in @(Get-ChildItem -LiteralPath $distMaps -Filter "*.json" -File -ErrorAction SilentlyContinue)) {
        if (Select-String -LiteralPath $map.FullName -Pattern "steambirds-esboco-mapa" -Quiet) {
            Warn ((RelativePath $map.FullName) + " is still in the old Portuguese map format: run Build launcher.ps1 (it copies the converted map from Server\maps)")
        }
    }
}

# ------------------------------------------------------------------ 6. garbage
foreach ($name in @("FileList.txt", "ListaDeArquivos.txt", "server-report.txt", "relatorio-esboco.txt", "Claude outputs", "_duplicados", "_duplicates")) {
    AddDelete (Join-Path $root $name) "garbage at the project root"
}
AddDelete (Join-Path $root "Server\server-report.txt") "old report"
AddDelete (Join-Path $root "Server\Lidgren.Network.dll") "stray DLL (the project uses the NuGet package)"
foreach ($project in $projects) {
    foreach ($name in @("bin", "obj", ".vs")) { AddDelete (Join-Path $root ($project + "\" + $name)) "build output (rebuilt by the build scripts)" }
}
AddDelete (Join-Path $root "Launcher\build") "output and logs of Build launcher.ps1"
AddDelete (Join-Path $root ".vs") "Visual Studio cache"
if ($IncludeRelease) { AddDelete (Join-Path $root "Release") "Release folder (-IncludeRelease)" }
if ($IncludeLogs) {
    foreach ($folder in @("Launcher\logs", "Launcher\dist\logs", "Server\logs")) { AddDelete (Join-Path $root $folder) "logs (-IncludeLogs)" }
}

$garbagePatterns = @("*.tmp", "*.bak", "*.orig", "*.rej", "*.user", "*.suo", "*~", "*.broken-*")
$allCode = @()
foreach ($project in $projects) { $allCode += @(WalkFiles (Join-Path $root $project)) }
$allCode += @(WalkFiles (Join-Path $root "docs")) + @(WalkFiles (Join-Path $root "Packaging"))
foreach ($file in $allCode) {
    foreach ($pattern in $garbagePatterns) {
        if ($file.Name -like $pattern) { AddDelete $file.FullName "temporary or backup file"; break }
    }
}

# duplicate copies of .cs files: "Name-1.cs", "Name (1).cs", "Name - Copia.cs", "Name - Copy.cs" next to "Name.cs"
foreach ($file in $allCode) {
    if ($file.Extension -ne ".cs") { continue }
    $base = $file.BaseName
    $without = $base -replace '(-\d+| \(\d+\)| - C.pia( \(\d+\))?| - Copy( \(\d+\))?)$', ''
    if ($without -eq $base) { continue }
    $original = Join-Path $file.DirectoryName ($without + ".cs")
    if (-not (Test-Path -LiteralPath $original)) { continue }
    if ($file.LastWriteTime -gt (Get-Item -LiteralPath $original).LastWriteTime) {
        Warn ((RelativePath $file.FullName) + " is NEWER than " + $without + ".cs: compare them by hand, nothing was removed")
    }
    else { AddDelete $file.FullName ("duplicate copy of " + $without + ".cs") }
}
# the same .cs file name twice inside one project (the compiler reads both): the deepest one stays, the other is moved
foreach ($project in $projects) {
    $byName = @{}
    foreach ($file in @(WalkFiles (Join-Path $root $project))) {
        if ($file.Extension -ne ".cs") { continue }
        if (-not $byName.ContainsKey($file.Name)) { $byName[$file.Name] = @() }
        $byName[$file.Name] += $file
    }
    foreach ($name in $byName.Keys) {
        $copies = @($byName[$name])
        if ($copies.Count -lt 2) { continue }
        $sorted = @($copies | Sort-Object { $_.FullName.Split('\').Count } -Descending)
        for ($i = 1; $i -lt $sorted.Count; $i++) {
            AddMove $sorted[$i].FullName (Join-Path $root ("_duplicates\" + $stamp + "\" + $project + "\" + $sorted[$i].Name)) ("same file name twice in " + $project + " (the copy in " + (RelativePath $sorted[0].FullName) + " stays)")
        }
    }
}

# ------------------------------------------------------------------ 7. big folders with Portuguese names
if ($RenameFolders) {
    foreach ($pair in @(@("VersoesAntigas", "OldVersions"), @("decompilado", "decompiled"))) {
        $oldPath = Join-Path $root $pair[0]; $newPath = Join-Path $root $pair[1]
        if (-not (Test-Path -LiteralPath $oldPath)) { continue }
        if (Test-Path -LiteralPath $newPath) { Warn ($pair[0] + " and " + $pair[1] + " both exist: nothing was renamed") }
        else { AddMove $oldPath $newPath "folder with a Portuguese name (renamed, the contents are kept)" }
    }
}
else {
    foreach ($name in @("VersoesAntigas", "decompilado")) {
        if (Test-Path -LiteralPath (Join-Path $root $name)) { Write-Host ("  note: " + $name + " has a Portuguese name; run with -RenameFolders to rename it") -ForegroundColor DarkGray }
    }
}

# ------------------------------------------------------------------ show the plan
$worktrees = Join-Path $root ".claude\worktrees"
if (Test-Path -LiteralPath $worktrees) {
    $count = @(Get-ChildItem -LiteralPath $worktrees -Directory -ErrorAction SilentlyContinue).Count
    Write-Host ("  note: .claude\worktrees holds " + $count + " full copy(ies) of the project made by Claude Code; they are not touched. " +
        "Remove them with 'git worktree remove <name>' when you do not need them.") -ForegroundColor DarkGray
}
Write-Host ""
if ($actions.Count -eq 0) {
    Write-Host "Nothing to clean. The project is tidy." -ForegroundColor Green
    if (-not $NoPause) { Read-Host -Prompt "Press Enter to close" }
    exit 0
}

$totalBytes = 0
foreach ($group in @($actions | Group-Object Why | Sort-Object Name)) {
    Write-Host $group.Name -ForegroundColor Cyan
    foreach ($a in $group.Group) {
        switch ($a.Kind) {
            "Delete" {
                $size = SizeOf $a.Path
                $totalBytes += $size
                Write-Host ("  delete  {0}  ({1:N0} KB)" -f (RelativePath $a.Path), ($size / 1KB))
            }
            "Move" { Write-Host ("  move    {0}  ->  {1}" -f (RelativePath $a.Path), (RelativePath $a.Target)) }
            "Run" { Write-Host ("  update  {0}" -f $a.Path) }
        }
    }
}
Write-Host ""
Write-Host ("{0} action(s); about {1:N1} MB will be deleted." -f $actions.Count, ($totalBytes / 1MB)) -ForegroundColor Cyan

if ($DryRun) {
    Write-Host "Dry run: nothing was changed." -ForegroundColor Yellow
    if (-not $NoPause) { Read-Host -Prompt "Press Enter to close" }
    exit 0
}

if (-not $Yes) {
    $answer = Read-Host "Type YES to do all of the above"
    if ($answer -ne "YES") {
        Write-Host "Cancelled. Nothing was changed."
        if (-not $NoPause) { Read-Host -Prompt "Press Enter to close" }
        exit 0
    }
}

# ------------------------------------------------------------------ do it (renames and conversions first, deletions last)
$failed = 0
$order = @("Run", "Move", "Delete")
foreach ($kind in $order) {
    foreach ($a in @($actions | Where-Object { $_.Kind -eq $kind })) {
        try {
            switch ($a.Kind) {
                "Run" { & $a.Script }
                "Move" {
                    if (-not (Test-Path -LiteralPath $a.Path)) { continue }
                    $parent = Split-Path $a.Target -Parent
                    if ($parent -and -not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
                    Move-Item -LiteralPath $a.Path -Destination $a.Target -Force
                }
                "Delete" {
                    if (Test-Path -LiteralPath $a.Path) { Remove-Item -LiteralPath $a.Path -Recurse -Force -ErrorAction Stop }
                }
            }
        }
        catch {
            $failed++
            Write-Host ("  FAILED: " + $a.Kind + " " + $a.Path + " (" + $_.Exception.Message + ")") -ForegroundColor Red
        }
    }
}

Write-Host ""
if ($failed -eq 0) { Write-Host "Done. Run Build launcher.ps1 to build everything again." -ForegroundColor Green }
else { Write-Host ("Done with {0} failure(s) (files in use? close the server, the game and Visual Studio and run again)." -f $failed) -ForegroundColor Yellow }
if (-not $NoPause) { Read-Host -Prompt "Press Enter to close" }
