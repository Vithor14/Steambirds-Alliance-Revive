# Dungeons da build 190828 (recriadas)

Escrito em 15/11/2026, revisado em 23/11/2026. Os mapas ficam em `Server\maps\dungeon_*.json`. Foram gerados em 15/11 por scripts Python que **não estão mais no repositório** (`tools\dungeons-190828`, removida); hoje o `.json` é a fonte e se edita no editor de mapas do launcher. Em 22/11 todos foram reduzidos à metade da largura e da altura (`tools\scale_dungeon_maps.py`, `"scaled": 0.5`; `docs/dungeon-tuning.md`). O formato está em `docs/map-format.md`. Eles rodam no servidor de dungeon (`docs/dungeon-server.md`); só Iron Lair e Bomb Factory estão ligadas no `dungeons.json` (testadas em 15/11; as correções posteriores não foram retestadas).

## O que se achou no configBinary

- Os 21 arquivos `Dungeons/dungeon_*` da 190828.2045 estão **vazios** (`{}`), como os da 201219. Conferido também nas builds antigas que o usuário baixou (pasta `VersoesAntigas`, fora do git): 190426.2227, 190609.2106, 190709.0120 e 190911.1941, todas vazias. O desenho das salas ficava só no servidor oficial.
- Só a 190426 ainda traz camadas de IA nas unidades (`additionalStates` de 53 unidades, `includedStates` 74, `namedUnitStates`, `difficultyTable` 114; a exportação `ia_190426.json` saiu do repositório junto com os scripts). Delas saiu o escudo do SN-Z ("toggle_safety": 10 s em 90/70/50/30/10% de vida).
- O que existe e foi usado:
  - Unidades por dungeon (`Units/60D..999D`, 883 unidades em 20 pastas): nome, vida, velocidade, armas e papel (chefe, minichefe, baú, muro, destroços). Todos os templates usados existem no cliente 201219.
  - Nomes e tiers (`Texts/en-US/dungeons`), chaves `key_*` (`Items/Consumables/dungeonkey`) e itens de portal (`Items/Consumables/dungeon`).
  - Território de cada dungeon (`territoryTiles`, mesmos ids na 201219) e peças de chão `<groundTag>_center_*` (ground_assets da 201219). Todos os terrenos (`DungeonBomb`, `DungeonSneezy`...) existem em `territories`.
  - Placas `*dungeonsign1` (nome) e `*dungeonsign2` (texto sorteado), e `thiefdungeonsign3/4/6` das salas de segurança das Vaults.
  - Falas dos chefes (`bark`): Phoenix 1–5, Poison God, Frozen Horror, Husky Gang Leader, Bomb Maker, Lazer, Geckelston, N.A.N.A., Doomsday, Cyclopus, Tiffany, Bast Statue, Security Chiefs, Altar de Horus, broca de The Dig, Viceroy, Meowza 1 e 2, Three Brothers.
  - Quests: `dungeon_thief_01` (chave → altar), `dungeon_rescue_01` (marcos da broca), `dungeon_finalboss_01/02` (Trono de Meowza, liga `meowzalasergateWE`).
- Da wiki (wiki.steambirds.com e fandom) e das notas de patch: minichefes e chefes, salas de altar do Temple of Bast (2, com maldições), escolta da broca em The Dig, chave e emboscadas das Vaults, ondas das arenas (10 e 15), barricadas anti-rush e respiros de calor da Reeducation Camp, salas sorteadas da Bomb Factory, fileiras de esteiras no palco do Lazer.
- A página `web.archive.org/.../wiki/Dungeons` não pôde ser aberta (bloqueada para a ferramenta). A wiki atual não descreve o desenho das salas de nenhuma dungeon.

## As 17 dungeons

| Mapa | Dungeon | Tier | Território | Salas | Destaques |
|---|---|---|---|---|---|
| dungeon_bomb | Bomb Factory | 2 | 41 | 10 | barris de bomba (anel de balas ao explodir), Overseer Charles, The Bomb Maker (ataca com minas-robô), Big Treasure de DPS |
| dungeon_obstacle | Laser Playground | 3 | 44 | 9 | Mincer, Graters, Dicer, Slicer, esteiras de balas, The Grinder, Lazer |
| dungeon_arena | The Arena | 5 | 64 | 3 | 10 ondas (2 Bruisers na 10), Commodus |
| dungeon_grind | Home for Special Children | 6 | 43 | 9 | Orphans, Blood Nurses em trilho, N.A.N.A., Dr. Geckelston |
| dungeon_eye | Crypt of Cathulhu | 9 | 42 | 9 | olhos de laser, 2 Psychopus em câmaras laterais, Cyclopus com lasers giratórios |
| dungeon_fire | Reeducation Camp | 10 | 36 (chefe 71) | 10 | MaxSec Guards, celas, fornalha, barris de fogo, heat vents, Converted Prisoner, Phoenix que renasce |
| dungeon_ice | Frozen Owl Monastery | 10 | 31 (chefe 72) | 9 | huskies congelados, ninhos, nevasca, Husky Gang Leader, Frozen Horror com 6 nós |
| dungeon_snake | Venom Compound | 10 | 34 | 8 | esteiras, barreiras, Poison Convoy, Giant Egg → Poison God |
| dungeon_thief | The Vaults | 10 | 59 | 10 | altar de Horus, 3 Security Chiefs, chave dourada, emboscadas de dropships |
| dungeon_space | Super Secret Base | 10 | 58 | 8 | sentinelas, sala de emboscada, esquadrilha Omega, Mammoth-1, Doomsday Mechanism |
| dungeon_temple | Temple of Bast | 10 | 61 | 8 | 2 salas de altar, Bast Statue, High Priestess Tiffany |
| dungeon_sneezy | Iron Lair | 10 | 67 | 1 | arena em donut (refeita em 23/11, `docs/dungeon-server.md`): SN-Z girando pelo anel, bombas de explosão grande e fase de escudo com rajadas na metade da vida |
| dungeon_arena2 | The Elder Arena | 10 | 65 | 3 | 15 ondas, minichefes na 8 e na 15, Dark Sphinx |
| dungeon_rescue | The Dig | 10 | 45 | 11 | escolta da broca, Deep Leviathan, Stone Worm, Buried Guardian, Legendary Supplies |
| dungeon_finalboss | Command Center | 10 | 35 | 3 | Viceroy com contagem de incineração, Meowza fase 1, portal para os Three Brothers |
| dungeon_3brothers | The Three Brothers | 10 | 73 | 3 | corredor de torretas, Golden Kai → Jinn → Rei, portal para o Inner Sanctum |
| dungeon_finalboss2 | Inner Sanctum | 10 | 73 | 2 | Meowza fase 2 parada no centro, Defense Grid, almas dos generais |

Fora: `dungeon_tutorial` (olfaithful, item próprio), `dungeon_0test`, `dungeon_gate` (sem nome, chave nem unidades na 190828; a pasta "Pacifist Dungeon" talvez seja dela). `DungeonOrbitroid` só existe na 201219.

## O que é original e o que é recriado

- Original: quem aparece em cada dungeon, nomes, vida, armas, papéis, chefes e minichefes, falas, placas, territórios, chaves, quests e mecânicas descritas na wiki; os limiares de escudo do SN-Z (190426) e os grupos de armas por fase (nomes dos `gunMounts`).
- Recriado: formato e tamanho das salas (36 a 108 u; o walker anda 3 u/s), posição e quantidade dos inimigos, ordem das salas comuns, fases dos chefes (limiares de vida e quem é invocado), ondas das arenas, cadência das invocações periódicas, vida e tempo dos baús de DPS.
- As unidades invisíveis que dirigiam as salas (`*master`, `*spawner`, `trapmaster`, `arenawave_*`) ficam listadas em `controllers`, só como referência.

## Unidades invisíveis armadas (lição do teste de 15/11)

Algumas unidades do cliente têm arma e a arte `!EnemyInvisible`. No original elas faziam parte de outra coisa (um barril, um cenário, um roteiro); **colocadas soltas numa sala, atiram no jogador sem serem vistas**. Já corrigido: `bombbarrelboom` e `firebarrelboom` (anel de balas que nasce quando o barril visível explode: `onDeath` do barril). Desde 22/11 a regra geral é: unidade invisível sem arma (`trapbutton`) não nasce. Ainda para conferir quando cada dungeon abrir: `lavapillar`, `blizzard`, `meowzalaserB/D`, `avatarcannon`, `sunfoxtrap`, `lasergateWE`, `beartrap`, `arena2tripodminionmaster`, `dochatcher`, `trapbutton`, `coilsnake01neck`, `riotguardleader`.

Dificuldade: vida e dano seguem os tiers originais da 190828 (healthTiers 1…90, damageTiers 1…9,7); não foi escalada. Quando houver itens que melhorem o avião isso se equilibra.

## Como os mapas foram feitos (v8, 15/11)

- Mapas centrados na grade do cliente (o cliente guarda 54 tiles para cada lado da origem; The Dig tem 56 tiles de ponta a ponta).
- Portões ganham `room` (para fechar atrás do jogador em salas com `closesOnEnter`).
- Todo `*treasureBIG` com `_reward` vira `dpsChest` (vida 500 + 250 × tier, 25 s).
- Sem pontos de controle nas câmaras laterais do Crypt of Cathulhu (o ponto de controle vale pela linha `activeAtV`, então salas laterais confundiam a ordem).
- O validador do editor de mapas (aba Map editor) confere posições no chão livre, portões, templates do cliente, falas, armas por índice, invocações, baús de DPS, condições e o tamanho da grade (substitui o antigo `check_maps.py`).

## Próximos passos

1. Retestar a Bomb Factory (barris, reação em cadeia) e ir até o fim; depois ligar as outras no `dungeons.json` uma por uma, conferindo o tamanho das salas (agora 4× menores) e as unidades invisíveis acima.
2. `nextDungeon` (Meowza → Three Brothers → Inner Sanctum); as chaves `key_*` já levam à dungeon (`Dungeons\DungeonKeys.cs`), falta testar.
3. Mecânicas especiais (broca, chave das Vaults, altares, emboscada, esteiras, trilhos, respiros).
4. IA original a partir dos estados da 190426.
