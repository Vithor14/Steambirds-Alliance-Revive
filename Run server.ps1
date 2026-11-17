# Run server.ps1
# Starts the project's server (development mode, dotnet run). When you press Ctrl+C, everything is written to one txt
# (server output + game logs) in C:\Steambirds\server-report.txt
#
# Before starting, it checks for duplicated .cs files (e.g. SpawnEsboco-1.cs), which make the build fail
# with CS0101/CS0111.
# In the report, the IL dumped by HarmonyX and the Unity asset warnings are summarized,
# and each game log gets a list of its most frequent errors.

$serverFolder      = "C:\Steambirds\Server"
$pluginFolder      = "C:\Steambirds\QuieterPlugin"
$duplicatesFolder  = "C:\Steambirds\_duplicados"
$bepInExFolder     = "C:\Program Files (x86)\Steam\steamapps\content\app_386010\depot_386011\BepInEx"
$playerLog         = "C:\Users\Vithor Castelo\AppData\LocalLow\SpryFox\Steambirds Alliance\Player.log"
$reportFile        = "C:\Steambirds\server-report.txt"

# dotnet 8 writes its messages in UTF-8; without this, PowerShell reads them as code page 850 and the accents break.
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

# Looks for copies of .cs files in the folder: "Name-1.cs", "Name (1).cs", "Name - Copia.cs", "Name - Copy.cs"
# next to "Name.cs". The compiler reads every .cs in the folder, so two copies of the same class cause an error.
function FindDuplicates($folder) {
    $found = @()
    if (-not (Test-Path -LiteralPath $folder)) { return $found }

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

# Shows the duplicates and, if the user agrees, keeps only the newest of each pair.
# Nothing is deleted: the file that goes out is moved to the _duplicados folder, outside the projects.
function ResolveDuplicates($folder) {
    $duplicates = @(FindDuplicates $folder)
    if ($duplicates.Count -eq 0) { return }

    Write-Host ""
    Write-Host "WARNING: duplicated .cs files in $folder" -ForegroundColor Yellow
    Write-Host "The compiler reads ALL .cs files in the folder; two copies of the same class cause error CS0101/CS0111."
    foreach ($pair in $duplicates) {
        $original = Get-Item -LiteralPath $pair.Original
        $copy = Get-Item -LiteralPath $pair.Copy
        $newer = $original.Name
        if ($copy.LastWriteTime -gt $original.LastWriteTime) { $newer = $copy.Name }
        Write-Host ("  {0}  ({1:dd/MM/yyyy HH:mm:ss}, {2} bytes)" -f $original.Name, $original.LastWriteTime, $original.Length)
        Write-Host ("  {0}  ({1:dd/MM/yyyy HH:mm:ss}, {2} bytes)  -> newer: {3}" -f $copy.Name, $copy.LastWriteTime, $copy.Length, $newer)
    }

    $answer = Read-Host "Keep only the newest of each pair? The other one goes to $duplicatesFolder (Y/N)"
    if ($answer -notmatch '^[yYsS]') {
        Write-Host "Nothing was moved. If the build fails, delete the extra copy."
        return
    }

    New-Item -ItemType Directory -Force -Path $duplicatesFolder | Out-Null
    foreach ($pair in $duplicates) {
        if (-not (Test-Path -LiteralPath $pair.Copy)) { continue }
        $original = Get-Item -LiteralPath $pair.Original
        $copy = Get-Item -LiteralPath $pair.Copy
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'

        if ($copy.LastWriteTime -gt $original.LastWriteTime) {
            # The copy is the new version: the old file is kept aside and the copy takes the right name.
            Move-Item -LiteralPath $original.FullName -Destination (Join-Path $duplicatesFolder ($stamp + "-" + $original.Name)) -Force
            Move-Item -LiteralPath $copy.FullName -Destination $pair.Original -Force
            Write-Host "  $($original.Name): kept the version that was in $($copy.Name)"
        }
        else {
            Move-Item -LiteralPath $copy.FullName -Destination (Join-Path $duplicatesFolder ($stamp + "-" + $copy.Name)) -Force
            Write-Host "  $($copy.Name): moved to $duplicatesFolder"
        }
    }
}

ResolveDuplicates $serverFolder
ResolveDuplicates $pluginFolder

$reportWriter = New-Object System.IO.StreamWriter($reportFile, $false, (New-Object System.Text.UTF8Encoding($false)))
$reportWriter.AutoFlush = $true

function Write-Report($text) {
    $reportWriter.WriteLine([string]$text)
}

# Removes what only gets in the way of reading the log:
#  - the IL that HarmonyX dumps on every patch ("Generated patch ..." up to the next blank line);
#  - the "The referenced script on this Behaviour ... is missing!" warnings, which come from the game's assets.
# Returns the lines that remain and how many were removed of each kind.
function RemoveNoise($lines) {
    $output = New-Object 'System.Collections.Generic.List[string]'
    $ilSkipped = 0
    $missingScripts = 0
    $insideIL = $false
    $afterWarning = $false

    foreach ($line in $lines) {
        $text = [string]$line

        if ($insideIL) {
            if ($text -match '^\[(Info|Debug|Warning|Error|Message|Fatal)\s*:') {
                $insideIL = $false
            }
            elseif ($text.Trim().Length -eq 0) {
                $insideIL = $false
                continue
            }
            else {
                $ilSkipped++
                continue
            }
        }

        if ($text -match 'HarmonyX\] Generated patch') {
            $insideIL = $true
            $ilSkipped++
            continue
        }

        if ($text -match '^The referenced script on this Behaviour .* is missing!') {
            $missingScripts++
            $afterWarning = $true
            continue
        }

        if ($afterWarning) {
            if ($text.Trim().Length -eq 0) { continue }
            if ($text -match '^\(Filename: .*ManagedMonoBehaviourRef') { continue }
            $afterWarning = $false
        }

        $output.Add($text)
    }

    return @{ Lines = $output; IL = $ilSkipped; Scripts = $missingScripts }
}

# Lists the error lines that repeat the most in a log.
function SummarizeErrors($lines) {
    $pattern = 'Exception|\[Error|\[Fatal|Caught exception|Curl error'
    $groups = @($lines |
        Where-Object { $_ -match $pattern } |
        ForEach-Object {
            $t = ([string]$_).Trim()
            if ($t.Length -gt 180) { $t.Substring(0, 180) } else { $t }
        } |
        Group-Object |
        Sort-Object Count -Descending |
        Select-Object -First 12)

    Write-Report "--- most frequent errors in this log ---"
    if ($groups.Count -eq 0) {
        Write-Report "(no error or exception lines)"
        return
    }
    foreach ($group in $groups) {
        Write-Report ("{0,5}x  {1}" -f $group.Count, $group.Name)
    }
}

function AppendFile($title, $path, $lastLines, $clean) {
    Write-Report ""
    Write-Report ("=" * 70)
    Write-Report "FILE: $title"

    if (-not (Test-Path -LiteralPath $path)) {
        Write-Report "(not found: $path)"
        return
    }

    $file = Get-Item -LiteralPath $path
    Write-Report "PATH: $path"
    Write-Report "MODIFIED: $($file.LastWriteTime.ToString('dd/MM/yyyy HH:mm:ss'))"
    Write-Report ("=" * 70)

    if ($file.Length -eq 0) {
        Write-Report "(empty file: nothing was written to it in this run)"
        return
    }

    $lines = @(Get-Content -LiteralPath $path -Encoding UTF8 -ErrorAction SilentlyContinue)
    if ($lines.Count -eq 0) {
        Write-Report "(could not read the file; it may be open in another program)"
        return
    }

    if ($clean) {
        $cleaned = RemoveNoise $lines
        $lines = @($cleaned.Lines)
        if ($cleaned.IL -gt 0) {
            Write-Report "($($cleaned.IL) HarmonyX IL lines skipped; plugin 1.13.0 turns this dump off at the source)"
        }
        if ($cleaned.Scripts -gt 0) {
            Write-Report "($($cleaned.Scripts) 'The referenced script on this Behaviour ... is missing!' warnings skipped: they come from the game's assets, not from the server)"
        }
        SummarizeErrors $lines
        Write-Report "--- content ---"
    }

    if ($lastLines -gt 0 -and $lines.Count -gt $lastLines) {
        $start = $lines.Count - $lastLines
        Write-Report "(showing the last $lastLines of $($lines.Count) lines)"
        $lines = $lines[$start..($lines.Count - 1)]
    }

    foreach ($line in $lines) { Write-Report $line }
}

Write-Host "Project server starting. Test the game, then press Ctrl+C ONCE."

try {
    Write-Report "=== STEAMBIRDS PROJECT REPORT ==="
    Write-Report "Generated at: $(Get-Date -Format 'dd/MM/yyyy HH:mm:ss')"
    Write-Report "Windows: $([Environment]::OSVersion.VersionString)"
    Write-Report "dotnet: $((dotnet --version 2>&1) -join ' ')"
    Write-Report ""
    Write-Report "=== SERVER OUTPUT (dotnet run) ==="

    # The server reads BepInEx\acoes_config.txt (written by the plugin) to create the skill clouds.
    $env:STEAMBIRDS_BEPINEX = $bepInExFolder

    Set-Location $serverFolder
    dotnet run 2>&1 | ForEach-Object {
        $line = "$_"
        Write-Host $line
        Write-Report $line
    }
}
finally {
    Write-Host ""
    Write-Host "Collecting the game logs..."

    Write-Report ""
    Write-Report "=== GAME LOGS ==="
    AppendFile "BepInEx\LogOutput.log (last 4000 lines)" (Join-Path $bepInExFolder "LogOutput.log") 4000 $true

    AppendFile "Unity Player.log (last 4000 lines)" $playerLog 4000 $true

    # Server account (planes and finished dungeons): shows whether the tutorial was marked as completed.
    AppendFile "Server\account.json" (Join-Path $serverFolder "account.json") 0 $false

    $txts = @(Get-ChildItem -LiteralPath $bepInExFolder -Filter "*.txt" -File -ErrorAction SilentlyContinue |
        Sort-Object FullName)
    Write-Report ""
    Write-Report ".txt files found in the BepInEx folder: $($txts.Count)"
    foreach ($txt in $txts) {
        $relative = $txt.FullName.Substring($bepInExFolder.Length).TrimStart('\')
        AppendFile "BepInEx\$relative" $txt.FullName 0 $false
    }

    $reportWriter.Dispose()
    Write-Host ""
    Write-Host "Report saved in: $reportFile"
}

Read-Host "Press Enter to close"
