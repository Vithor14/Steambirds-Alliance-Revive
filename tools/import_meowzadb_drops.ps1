<#
Imports enemy drop rates from meowzaDB unit pages saved by hand (browser "Save Page As", HTML only) and writes one
loot table per unit into Server\loot_tables\meowzadb\ (format: Server\loot_tables\README.md).

  .\tools\import_meowzadb_drops.ps1 -Pages "D:\SBA Repo\Enemy Droprates Pages"

- Each saved page (https://meowzadb.com/unit/<templateName>) carries the unit and its drops as Astro props.
- Each drop becomes its own roll: chance p drops the item with probability p; p >= 1 is that many guaranteed copies
  (2 = two copies, 1.25 = one copy plus 25% of a second). Drops with separate Normal / Difficult / Lethal rates get
  one roll per difficulty (the "difficulty" condition); drops with one rate count on every difficulty.
- Item ids the client marks obsolete are swapped for the item they migrate to (the old crafting gems become their
  ingredient pickups). Known typos of the site are fixed. Unknown ids and 0% drops are skipped and listed in the
  header of the file.
- The files are regenerated from the pages on every run: edit the pages folder and rerun, or stop using the script for
  a unit before editing its file by hand.
Pages are not copied into the repository (they are meowzaDB's), only the rates turned into loot tables.
#>
param(
    [Parameter(Mandatory = $true)][string]$Pages,
    [string]$Out = "",
    [string]$Catalog = ""
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Web
$repo = Split-Path -Parent $PSScriptRoot
if (-not $Out) { $Out = Join-Path $repo 'Server\loot_tables\meowzadb' }
if (-not $Catalog) { $Catalog = Join-Path $repo 'Server\catalog\item_catalog.json' }
$inv = [System.Globalization.CultureInfo]::InvariantCulture

# Site ids that do not exist in the game: the id the game uses.
$typos = @{ 'upgrade_water_defense' = 'upgrade_waterdefense' }

# ---------------------------------------------------------------- catalog
$items = @{}
foreach ($i in (Get-Content $Catalog -Raw -Encoding utf8 | ConvertFrom-Json).items) { $items[$i.id] = $i }
function Get-MigrateTarget($id) {
    foreach ($f in @($items[$id].flags)) {
        $m = [regex]::Match([string]$f, '^obsolet\w*\s*->\s*(\S+)')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    return $null
}

# ---------------------------------------------------------------- Astro props: [0, value] plain, [1, [...]] array
function ConvertFrom-Astro($v) {
    if ($v -is [System.Array] -and $v.Count -eq 2 -and ($v[0] -is [int] -or $v[0] -is [long])) {
        if ([int]$v[0] -eq 1) { return , @($v[1] | ForEach-Object { ConvertFrom-Astro $_ }) }
        return (ConvertFrom-AstroPlain $v[1])
    }
    return (ConvertFrom-AstroPlain $v)
}
function ConvertFrom-AstroPlain($x) {
    if ($x -is [System.Management.Automation.PSCustomObject]) {
        $h = [ordered]@{}
        foreach ($p in $x.PSObject.Properties) { $h[$p.Name] = ConvertFrom-Astro $p.Value }
        return [pscustomobject]$h
    }
    return $x
}

function Format-Num([double]$d) { return $d.ToString('0.##########', $inv) }
function Format-Pct([double]$d) {
    if ($d -ge 1) { return (Format-Num $d) + 'x' }
    return ($d * 100).ToString('0.####', $inv) + '%'
}
function Format-Str([string]$s) { return ($s | ConvertTo-Json) }

# One roll for chance p of an item: the guaranteed copies and the chance of one more.
function New-Roll([string]$id, [double]$p, [string]$difficulty, [string]$label) {
    $whole = [math]::Floor($p + 1e-9)
    $frac = $p - $whole
    if ($frac -lt 1e-9) { $frac = 0 }
    $copies = { param($n) (@(1..$n | ForEach-Object { '{ "id": ' + (Format-Str $id) + ' }' }) -join ', ') }
    $sets = @()
    if ($frac -gt 0) {
        $sets += '{ "weight": ' + (Format-Num $frac) + ', "items": [ ' + (& $copies ($whole + 1)) + ' ] }'
        if ($whole -gt 0) { $sets += '{ "weight": ' + (Format-Num (1 - $frac)) + ', "items": [ ' + (& $copies $whole) + ' ] }' }
        else { $sets += '{ "weight": ' + (Format-Num (1 - $frac)) + ' }' }
    }
    else { $sets += '{ "weight": 1, "items": [ ' + (& $copies $whole) + ' ] }' }
    $cond = if ($difficulty) { '"conditions": [ { "difficulty": "' + $difficulty + '" } ], ' } else { '' }
    return "    // $label`r`n    { $cond""lootTable"": [ " + ($sets -join ', ') + " ] }"
}

# ---------------------------------------------------------------- pages
$units = [ordered]@{}
foreach ($f in Get-ChildItem $Pages -Filter *.htm* | Sort-Object Name) {
    $html = Get-Content $f.FullName -Raw -Encoding utf8
    $m = [regex]::Match($html, '<astro-island\b(?=[^>]*component-url="[^"]*ItemTabs)[^>]*\bprops="([^"]*)"')
    if (-not $m.Success) { Write-Warning "$($f.Name): no unit data (not a meowzaDB unit page?)"; continue }
    $props = ConvertFrom-Astro ([System.Web.HttpUtility]::HtmlDecode($m.Groups[1].Value) | ConvertFrom-Json)
    if ($props.source -ne 'unit' -or -not $props.item.templateName) { Write-Warning "$($f.Name): not a unit page"; continue }
    $u = $props.item
    if ($units.Contains($u.templateName)) { Write-Warning "$($f.Name): $($u.templateName) saved twice (the first one stays)"; continue }
    $units[$u.templateName] = $u
}
if ($units.Count -eq 0) { throw "No meowzaDB unit pages in $Pages" }

New-Item -ItemType Directory -Force $Out | Out-Null
$written = 0; $skippedTotal = 0
foreach ($name in $units.Keys) {
    $u = $units[$name]
    $rolls = @(); $skipped = @()
    foreach ($drop in @($u._meta.loot)) {
        if ($null -eq $drop) { continue }
        $id = [string]$drop.id; $label = [string]$drop.name
        if (-not $label) { $label = $id }
        $rarity = [string]$drop.tier.rarity
        if ($typos.ContainsKey($id)) { $id = $typos[$id] }
        if (-not $items.ContainsKey($id)) { $skipped += "$id ($label): not an item of this client build"; continue }
        $to = Get-MigrateTarget $id
        if ($to) {
            if (-not $items.ContainsKey($to)) { $skipped += "$id ($label): obsolete, and its new item $to is missing"; continue }
            $label = "$label, obsolete: drops $to"; $id = $to
        }
        $c = $drop.chance
        $byDiff = [ordered]@{}
        foreach ($d in 'normal', 'difficult', 'lethal') {
            $v = $c.PSObject.Properties[$d]
            if ($v -and $null -ne $v.Value -and "$($v.Value)" -ne '') { $byDiff[$d] = [double]$v.Value }
        }
        if ($byDiff.Count -eq 0) { $skipped += "$id ($label): no rate"; continue }
        $distinct = @($byDiff.Values | Select-Object -Unique)
        if ($byDiff.Count -eq 1 -or $distinct.Count -eq 1) {
            $p = $distinct[0]
            if ($p -le 0) { $skipped += "$id ($label): listed at 0%"; continue }
            $rolls += New-Roll $id $p '' "$label ($rarity): $(Format-Pct $p)"
        }
        else {
            foreach ($d in $byDiff.Keys) {
                $p = $byDiff[$d]
                if ($p -le 0) { continue }
                $rolls += New-Roll $id $p $d "$label ($rarity), $d`: $(Format-Pct $p)"
            }
        }
    }
    $skippedTotal += $skipped.Count

    $where = if ($u.location -and $u.location.name) { ", $($u.location.name)" } else { '' }
    $title = "$($u.name) (T$($u.tier)$where)"
    $lines = @(
        "// Generated by tools\import_meowzadb_drops.ps1 from the meowzaDB page of '$name' (https://meowzadb.com/unit/$name).",
        "// meowzaDB: rates are datamined, based on a pattern, or estimated from observation (none could be datamined after",
        "// 2019/11). Each drop is rolled on its own; 1x, 2x... are guaranteed copies. Rerunning the script overwrites this file."
    )
    foreach ($s in $skipped) { $lines += "// Skipped: $s" }
    $lines += '{'
    $lines += '  "id": ' + (Format-Str "drops_$name") + ','
    $lines += '  "description": ' + (Format-Str "$title`: drop rates from meowzaDB.") + ','
    $lines += '  "units": [ ' + (Format-Str $name) + ' ],'
    if ($rolls.Count -eq 0) {
        # The loader rejects an empty "rolls": one roll that drops nothing.
        $lines += '  "rolls": [ { "lootTable": [ { "weight": 1 } ] } ]'
    }
    else {
        $lines += '  "rolls": ['
        $lines += ($rolls -join ",`r`n")
        $lines += '  ]'
    }
    $lines += '}'
    $path = Join-Path $Out ("$name.json")
    [System.IO.File]::WriteAllText($path, ($lines -join "`r`n") + "`r`n", (New-Object System.Text.UTF8Encoding $false))
    $written++
}

# Other tables that also list these units in "units": this folder's table wins only by alphabetical order.
$others = Get-ChildItem (Split-Path -Parent $Out) -Filter *.json -Recurse | Where-Object { $_.DirectoryName -ne (Resolve-Path $Out).Path }
foreach ($o in $others) {
    $text = Get-Content $o.FullName -Raw -Encoding utf8
    $um = [regex]::Match($text, '"units"\s*:\s*\[([^\]]*)\]')
    if (-not $um.Success) { continue }
    $listed = [regex]::Matches($um.Groups[1].Value, '"([^"]+)"') | ForEach-Object { $_.Groups[1].Value }
    foreach ($n in $listed) { if ($units.Contains($n)) { Write-Warning "$($o.Name) also lists '$n' in units: remove it there" } }
}
"Wrote $written table(s) to $Out ($skippedTotal drop(s) skipped, see the file headers)."
