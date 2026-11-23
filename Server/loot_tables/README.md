# Loot tables

Each `.json` file in this folder is **one loot table**. To create a new table, just drop a file here:
the server rereads the folder by itself (every 2 seconds) and nothing in the code has to change. Files may sit in
subfolders and accept `//` comments.

The format is the same `LootTableDef` that the game uses in the unit configs, so tables can be copied from the game
to here and back.

## File fields

| Field | What it does |
|---|---|
| `id` | Name of the table (without it, the file name is used). It is the name used in `items.json` and in `lootTableId`s. |
| `description` | Free text. |
| `replaces` | `lootTableId`s of the game's units that this table replaces. Accepts `*` (e.g. `"*boss*"`, `"tier7minion"`). |
| `units` | Units (`templateName`) that **always** use this table, in place of the game's loot. Accepts `*`. |
| `rolls` | List of rolls. Each roll picks **one** group of items by weight. |

Each roll (`LootTableDef`):

| Field | What it does |
|---|---|
| `lootTable` | Groups with `weight` and `items`. A group without `items` means "nothing". |
| `usableChance` | 0 to 100: chance that the random items are something the player's class can equip. |
| `ensureUsableSlots` | E.g. `["MainGun"]`: items of those slots always fit the class. |
| `conditions` | The game's conditions: `pilotLevelAboveOrEq`, `planeClass`, `planeLevelAboveOrEq`, `buff`, `isAdmin`, `isAlways`, `or`, `not`, `and`. Server extra: `difficulty` (`normal`, `difficult`, `lethal`). |

Each item:

| Form | Result |
|---|---|
| `{ "id": "armor_3" }` | Exactly that item (see `catalog\item_catalog.md`). |
| `{ "slot": "Armor", "tier": 2 }` | A random item of that slot and tier. |
| `{ "tier": 1 }` | A random main weapon, secondary, engine or armor. |
| `{ "slot": "Health", "tier": 1 }` | Health (or `Energy`, steam): collected at once. |
| `{ "instant": { "currency_id": "newscrap", "currency_amount": 10 } }` | Delivered at once, without a bag. Also: `planeXp`, `pilotXp`, `unlocks`, `ingredient_id` + `ingredient_amount`, `buff`. |

The `tier` is **relative to the enemy**, as in the game: `tier 1` = the enemy's tier, `tier 2` = one above. The result
stays between 1 and 10.

## Which table each enemy uses

1. If some table lists the unit in `units`, only that table counts.
2. Otherwise, the loot the unit itself has in the game (`loot` and `lootGlobal`):
   - embedded tables (e.g. the dungeon key that some enemies drop) are rolled as they are;
   - each `lootTableId` uses the table with that `id`, or the one with the most specific `replaces`.
   (can be turned off in `items.json`: `useGameUnitLoot`)
3. If none of that resolved: `normalLootTable` (regular enemies) or `bossLootTable` (bosses) of `items.json`.

Game entries named `..._normal`, `..._difficult` and `..._lethal` only count at the difficulty set in `items.json`
(`dungeonDifficulty`).

The original server's named tables (`tier7minion`, `tiernboss`, `tier10treasure`...) are not in the client.
To recreate one of them faithfully, create a file with that `id`: it takes effect at once.

## Testing

In the game chat: `/loot` (regular enemy), `/loot boss 3` (tier 3 boss), `/loot tutorialwalkerdungeontreasure`,
`/bags` (lists the loaded tables) and `/clear_bags`.

## Dungeon tables

`dungeon_chest`, `dungeon_chest_big`, `dungeon_miniboss` and `dungeon_no_loot` list in `units` the enemies of each dungeon (they were first generated from the dungeon maps by a script that is no longer in the repository, so the files are now the source: edit the weights, items and `units` by hand). `dungeon_no_loot` has an empty roll on purpose (walls, barrels, bombs, mines, debris).
