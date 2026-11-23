# Mapas em arquivo (formato `steambirds-map` v1)

Atualizado em 23/11/2026 (servidor 0.5.0). Os mapas ficam em `Server\maps`, um `.json` por mapa; o id do mapa é o nome do arquivo. O mesmo formato é lido pelo servidor e escrito pelo editor de mapas do launcher (`Launcher\Editor`, `Launcher\Editor\README.md`). Todos os campos são em **inglês** (a camada que lia os nomes antigos em português foi removida em 22/11: mapas com `chao`, `enfeites`, `tipos`... não abrem mais).

| Arquivo | Mapa | Quem usa |
|---|---|---|
| `nexus.json` | Rebel City | `Nexus.cs` (chão, enfeites, prédios, colisões, nascimento) |
| `tutorial_walker.json` | Training Grounds | `Tutorial*.cs` (o roteiro continua no código e pega os lugares pelo nome) |
| `dungeon_*.json` (17) | Dungeons da build 190828 recriadas | `Dungeons\DungeonServer*.cs` (porta 1113). Qual arquivo cada dungeon usa: `dungeons.json` (`map`). Ver `docs/dungeons-190828.md`, `docs/dungeon-server.md` |

O terreno `Basic` (mundo aberto) **não** é um arquivo: é gerado por semente (`docs/open-world.md`).

## Coordenadas

- `u`, `v` = unidades ao longo dos eixos a e b da grade do cliente (u = 12·a, v = 12·b, com a, b em tiles de `Ground`). No mundo: x = (u − v)/√2, y = (u + v)/√2.
- 1 tile = 12 u; slot de peça (p, q) = 6 u (centro em 6p + 3); sub-célula de muro (si, sj) = 3 u.
- O cliente guarda 54 tiles para cada lado da origem: mapas compridos precisam ficar centrados (o editor avisa e as dungeons já estão).
- `rot` = graus no mundo (0 = +y, 90 = +x, horário). 315 = de frente para +v, 135 = para −v. Exceção: `floor.pieces[].rot` é relativa ao tile, anti-horária, de 2 em 2 graus; sem `rot` o cliente sorteia giro e espelho.

## Seções (todas lidas por `Server\Maps.cs`)

- Cabeçalho: `format` (`"steambirds-map"`), `version` (1), `id`, `name`, `terrain` (terreno do cliente), `territory` (id do território), `source`/`note` (ignorados).
- `floor`: `wallsOnEdges`, `voidMargin` (anel de tiles de abismo em volta), `voidTerritory` (padrão 74), `drawingPieces` (variante (a+b+k+l) mod n, sem rotação), `drawing` {`siStart`, `sjStart`, `lines`: `.` chão livre, `#` muro, `-` sem chão; **primeira linha = maior sj**, como o ASCIIMap do jogo} e `pieces` [{`p`, `q`, `id`, `name`, `rot`}]. Um tile tem chão se alguma sub-célula dele não é `-`.
- `decor` [{`id`, `name`, `u`, `v`, `z`, `rot`}]: scatter de precisão total, mandado no tile onde cai. z 0,5 = chão; menor = mais alto.
- `buildings` [{`prefab`, `name`, `u`, `v`, `rot`, `store`}] (shortIds 70001+ na ordem do arquivo; `store` liga o prédio a uma loja de `stores.json`); `spawn` {`u`, `v`, `rot`}.
- `collisions` [{`name`, `minU`, `maxU`, `minV`, `maxV`}]: cenário sólido. Toda sub-célula de muro (3 u) cujo centro cai no retângulo é fechada na mensagem do chão (`Map.AddCollision`, `WriteFloor`). Funciona em tiles sem chão desenhado (o Nexus não tem). Os prédios do Nexus ficam fora para dar para atracar.
- Para roteiros: `points` {nome: ponto}, `groups` {nome: [pontos]}, `areas` {nome: {`minU`, `maxU`, `minV`, `maxV`}}, `lines` {nome: v}, `values` {nome: número}, `checkpoints` [{`name`, `activeAtV`, `u`, `v`, `rot`}], `gates` [{`name`, `type`, `sj`, `siFrom`, `siTo`, `damageable`}], `signs` [{`key`, `u`, `v`, `radius`, `rot`}] (texto de chão que o cliente cria sozinho: um evento por placa no ClientMapInfo).
- Campos desconhecidos (`note`, `legend`, `scaled`) são ignorados. O leitor aceita comentários `//` e vírgula sobrando. `"scaled": 0.5` marca os 17 mapas de dungeon já reduzidos por `tools\scale_dungeon_maps.py` (o script se recusa a escalar duas vezes).

## Campos das dungeons (lidos por `Dungeons\DungeonMapData.cs`; `Maps.cs` ignora)

- Metadados: `key` (chave `key_*` da dungeon), `portalItem`, `quest`, `exit` {`point`, `when`} (sem `exit`: fim quando as salas de chefe estão limpas), `nextDungeon` {`map`, `point`, `when`} (**ainda não usado**: Meowza → Three Brothers → Inner Sanctum); `values.tier`, `values.bossTerritory`.
- `sideGates` [{`name`, `type`, `si`, `sjFrom`, `sjTo`, `damageable`, `opensWhen`, `room`}]: portões dos corredores laterais. Ficam fora de `gates` porque `Maps.cs` só entende fileiras `sj`. Os `gates` ganham `opensWhen` e `room` aqui também.
- Portões: `damageable` = barricada (leva dano; uma parede quebrada derruba o portão); senão invulnerável e abre por `opensWhen`: `cleared:<room>` (com `+` para juntar), `death:<type>`, `quest:<id>` (por enquanto sempre verdadeira). `room`: para onde o portão leva (fecha atrás do jogador se a sala tem `closesOnEnter` e a opção `lockBossRooms` do `dungeons.json` está ligada para salas de chefe).
- `routes` {nome: [{`u`, `v`}]}: caminho da broca (The Dig) e trilho das Blood Nurses (ainda não usado).
- `types`: inimigos do mapa com os campos do `enemies.json` (entram no `Enemies` como `<mapa>/<tipo>`; lista abaixo) mais os extras da dungeon: `miniboss`, `onDeath`, `sequence`, `barks` (chaves de `Texts/bark` por momento: `onWake`, `phase`, `onDeath`), `durationSeconds`, `excludeFromRoomClear`, `periodicSummon` {`type`, `seconds`, `count`, `max`, `belowHealth`, `countsForRoomClear`}, `dpsChest` {`seconds`, `reward`, `fail`}, `chainReaction` {`radius`, `delaySeconds`}, `route`, `target`, `note`.
- `encounters` (um por sala): `room`, `area`, `line`, `wakes` (`onEnter` / `death:<type>`; `script` e `drill` ainda acordam como `onEnter`), `boss`, `miniboss`, `closesOnEnter`, `altarRoom`, `note`, `controllers` (unidades invisíveis do servidor original, só referência), `enemies` [{`type`, `pos`: [{`u`, `v`, `rot`}], `wakes`, `route`, `behavior`, `miniboss`, `note`}], `waves` [{`name`, `delayBeforeSeconds`, `message`, `groups`}] (a próxima onda vem quando a anterior morre inteira).
- Sala limpa = sem inimigos que contam (baús, paredes, destroços, invulneráveis, parados e `excludeFromRoomClear` não contam) e sem ondas pendentes.

### Campos de um tipo de unidade (`types` de uma dungeon ou `types` do `enemies.json`)

`name`, `template` (UnitConfig do jogo; vários separados por `|`), `behavior` (`chase`, `circle`, `charge`, `turret`, `patrol`, `idle`, `escort`, `fixed`, `kamikaze`), `ai` (id de um arquivo de `Server\ai\brains`; substitui `behavior`, `aggroRadius` e `preferredDistance`; `docs/enemy-ai.md`), `boss`, `health`/`maxHealth`, `healthMultiplier`, `speedMultiplier`, `turnMultiplier`, `fireRateMultiplier`, `damageMultiple`, `preferredDistance`, `aggroRadius`, `contactRadius` (kamikaze), `shotRange`, `aimLead`, `firstShotDelay`, `freeAim`, `homingBullets`, `guns` (índices de `gunMounts`), `team` (2 inimigo, 1 aliado), `invulnerable`, `opensAfterHitSeconds` (baú), `freeRoam`, `visualEffect` (statefulEffects; `tankShield` nos escudos) e `phases` [{`belowHealth`, `behavior`, `ai`, `speedMultiplier`, `fireRateMultiplier`, `guns`, `summon`, `summonCount`, `message`, `bark`, `shieldSeconds`, `visualEffect`}].

Desde a 0.5.0 o editor de mapas não mostra mais `behavior`, `aggroRadius`, `preferredDistance`, `contactRadius`, `shotRange`, `aimLead` e `guns` (isso é do editor de IA); valores que já estão no mapa continuam valendo quando o tipo não tem `ai`.

## Implementação (`Server\Maps.cs`)

- `Maps.Get(id, log)`: procura `<cwd>\maps\<id>.json` e depois `<pasta do exe>\maps`. Relê quando a data do arquivo muda. Se o arquivo novo tiver erro, fica a versão anterior (log `[MAP] ERROR ...`). Sem nenhuma versão boa, lança `InvalidOperationException` (o Nexus e o tutorial não sobem; a dungeon avisa no chat e manda o jogador de volta ao Nexus). Só lê o arquivo do mapa pedido.
- Desserialização com `System.Text.Json` (sem diferença de maiúsculas); `values` e `lines` só aceitam números.
- `Map.CreatePlan()` monta o `Ground.FloorPlan` (tiles do desenho; `#` que não é borda vira muro de sub-célula). `Map.WriteFloor(buf, plan, out summary)` escreve o `ClientReceiveTerrainTiles` de qualquer mapa: tiles do desenho, depois tiles de peças e de enfeites, as colisões e por fim o abismo.
- `Maps.AddSigns(info, map)`: um evento por placa no ClientMapInfo (`repeatEvery_1` + PlayerNearPos → `lore_text_relative_pos`). O servidor de dungeon registra isso para cada terreno do `dungeons.json`.
- Tutorial: `Tutorial.Helpers.cs` (`Validate`) confere todos os nomes que o roteiro usa; mapa com nome faltando → fica o último válido. Cada sessão guarda o seu mapa; `/restart` pega a versão nova do arquivo. Nomes usados:
  - `points`: instructor, ring, crate, boss, portal, dronesGuide, finalCorridorGuide
  - `groups`: towers, drones1, drones2
  - `areas`: hangar, anteroom, drones, ring, arena
  - `lines`: instructor, towers, bulletsTalk, hangarExit, towersQuiet, droneCorridor, finalCorridor
  - `values`: ringCount, ringOpening, ringRadiusU, ringRadiusV, ringMiddleRadius, drones1Radius
  - `checkpoints`: o primeiro é o nascimento; `/step` usa anteroom, ring room, crate corridor, final corridor, arena
  - `gates`: 3, na ordem crate wall, gate, boss gate

## Como os mapas são feitos hoje

- Pelo **editor de mapas** do launcher (aba Map editor): abre, valida (erros e avisos), salva em `server\maps` (com erros só salva como rascunho, `"draft": true`; o servidor não lê essa marca) e mostra as figuras do próprio jogo quando a pasta `editor-assets` existe.
- Os 17 mapas de dungeon foram gerados por scripts Python em 15/11, que **não estão mais no repositório** (a pasta `tools\dungeons-190828` foi removida): hoje o arquivo `.json` é a fonte. `tools\scale_dungeon_maps.py` reduz um mapa (`python tools\scale_dungeon_maps.py Server\maps\meu_mapa.json`).

## Falta

- Mapa global (o mundo aberto vem do gerador, não de arquivo).
- No Nexus e no tutorial, os encontros continuam no `enemies.json` (por servidor, não por mapa); o roteiro do tutorial segue em código.
- `nextDungeon` e os roteiros especiais de dungeon (broca, chave, altares, emboscadas).
