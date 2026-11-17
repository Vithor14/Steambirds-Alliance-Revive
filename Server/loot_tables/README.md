# Tabelas de loot

Cada arquivo `.json` desta pasta é **uma tabela de loot**. Para criar uma tabela nova basta colocar um arquivo aqui:
o servidor relê a pasta sozinho (a cada 2 segundos) e não precisa mudar nada no código. Arquivos podem ficar em
subpastas e aceitam comentários `//`.

O formato é o mesmo `LootTableDef` que o jogo usa nas configs das unidades, então dá para copiar tabelas do jogo
para cá e vice-versa.

## Campos do arquivo

| Campo | O que faz |
|---|---|
| `id` | Nome da tabela (sem ele, vale o nome do arquivo). É o nome usado em `items.json` e nos `lootTableId`. |
| `description` | Texto livre. |
| `replaces` | `lootTableId`s das unidades do jogo que esta tabela substitui. Aceita `*` (ex.: `"*boss*"`, `"tier7minion"`). |
| `units` | Unidades (`templateName`) que **sempre** usam esta tabela, no lugar do loot do jogo. Aceita `*`. |
| `rolls` | Lista de sorteios. Cada sorteio escolhe **um** grupo de itens pelo peso. |

Cada sorteio (`LootTableDef`):

| Campo | O que faz |
|---|---|
| `lootTable` | Grupos com `weight` (peso) e `items`. Um grupo sem `items` é "nada". |
| `usableChance` | 0 a 100: chance de os itens aleatórios serem de algo que a classe do jogador pode equipar. |
| `ensureUsableSlots` | Ex.: `["MainGun"]`: itens desses slots sempre servem para a classe. |
| `conditions` | Condições do jogo: `pilotLevelAboveOrEq`, `planeClass`, `planeLevelAboveOrEq`, `buff`, `isAdmin`, `isAlways`, `or`, `not`, `and`. Extra do servidor: `difficulty` (`normal`, `difficult`, `lethal`). |

Cada item:

| Forma | Resultado |
|---|---|
| `{ "id": "armor_3" }` | Exatamente esse item (veja `catalog\item_catalog.md`). |
| `{ "slot": "Armor", "tier": 2 }` | Um item aleatório desse slot e tier. |
| `{ "tier": 1 }` | Um item aleatório de arma principal, secundário, motor ou armadura. |
| `{ "slot": "Health", "tier": 1 }` | Vida (ou `Energy`, vapor): coletada na hora. |
| `{ "instant": { "currency_id": "newscrap", "currency_amount": 10 } }` | Entregue na hora, sem saco. Também: `planeXp`, `pilotXp`, `unlocks`, `ingredient_id` + `ingredient_amount`, `buff`. |

O `tier` é **relativo ao inimigo**, como no jogo: `tier 1` = tier do inimigo, `tier 2` = um acima. O resultado
fica entre 1 e 10.

## Qual tabela cada inimigo usa

1. Se alguma tabela tem a unidade em `units`, só ela vale.
2. Senão, o loot que a própria unidade tem no jogo (`loot` e `lootGlobal`):
   - tabelas embutidas (ex.: a chave de dungeon que alguns inimigos soltam) são sorteadas como estão;
   - cada `lootTableId` usa a tabela com esse `id`, ou a de `replaces` mais específico.
   (desligável em `items.json`: `useGameUnitLoot`)
3. Se nada disso resolveu: `normalLootTable` (inimigos comuns) ou `bossLootTable` (chefes) do `items.json`.

Entradas do jogo com nome `..._normal`, `..._difficult` e `..._lethal` só valem na dificuldade de `items.json`
(`dungeonDifficulty`).

As tabelas por nome do servidor original (`tier7minion`, `tiernboss`, `tier10treasure`...) não estão no cliente.
Para recriar uma delas com fidelidade, crie um arquivo com esse `id`: ele passa a valer na hora.

## Testar

No chat do jogo: `/loot` (inimigo comum), `/loot boss 3` (chefe tier 3), `/loot tutorialwalkerdungeontreasure`,
`/bags` (lista as tabelas carregadas) e `/clear_bags`.

## Tabelas das dungeons

`dungeon_chest`, `dungeon_chest_big`, `dungeon_miniboss` e `dungeon_no_loot` listam em `units` os inimigos de cada dungeon (gerado por `tools\dungeons-190828\make_loot_tables.py`; para refazer: `python make_loot_tables.py ..\..\Server\maps ..\..\Server\loot_tables`). Pode editar a mão os pesos e itens; rodar o script de novo sobrescreve. A `dungeon_no_loot` tem um sorteio vazio de propósito (muros, barris, bombas, minas, destroços).
