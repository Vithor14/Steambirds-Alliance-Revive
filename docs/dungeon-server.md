# Servidor de dungeon (porta 1113)

Escrito em 15/11/2026 (v8) e atualizado em 23/11/2026 (0.5.0). Testado no jogo em 15/11 com Iron Lair e Bomb Factory (fluxo inteiro funcionou, ver "Teste de 15/11") e com dois PCs em 22/11 (dungeons compartilhadas). As correções posteriores (barris, tamanho dos mapas, limite de tiros, Mission Transport) estão em `docs/dungeon-tuning.md` e `docs/dungeon-enemies-and-loot.md`; **não foram retestadas no jogo**. Formato dos mapas: `docs/map-format.md`. Opções do host: `Server\dungeons.json` (também na tela "Maps and dungeons" do launcher).

## Arquitetura

- Servidor UDP de dungeon: porta **1113** (`dungeonPort` do `network.json`), ServerRole **Dungeon (2)**, RegisteredHost id 3. Nexus 1111 (role 0), tutorial 1112 (role 3), mundo aberto 1114 (role 1, id 4).
- `Dungeons\DungeonCatalog.cs`: lê o `dungeons.json` (relido quando muda). Por dungeon: `type` (terreno do cliente), `map` (arquivo), `name`, `enabled`, `inNexus`, `healthMultiplier`, `opensAt`, `note`. Globais: `nexusPortals` (onde ficam os porta-aviões no Nexus), `maxPlayers`, `autoExitSeconds`, `entryMode`, `lockBossRooms`, `fireRateScale`, `maxBulletsPerSecond`. Sem o arquivo valem só Iron Lair e Bomb Factory (o arquivo que vai com o servidor traz as 17, com as duas primeiras ligadas).
- `Dungeons\DungeonMapData.cs`: lê as seções de dungeon do mapa (cache por data do arquivo e multiplicador de vida). Os tipos vão para o `Enemies` com o prefixo `<mapa>/` e `phases[].summon` ganha o mesmo prefixo. Condições: `cleared:<sala>`, `death:<tipo>`, `quest:<id>` (por enquanto sempre verdadeira), juntas com `+`.
- `Dungeons\DungeonServer*.cs` (`.cs`, `.Commands`, `.Helpers`, `.Session`, `.Tick`, `.Units`): uma **instância por tipo de dungeon**, com vários jogadores (`Session.Members`); tick de 0,1 s sob o lock do jogo. Um jogador que entra numa dungeon do mesmo tipo já em andamento (não terminada) entra na mesma instância (ver `docs/multiplayer-and-accounts.md`).
- `Dungeons\DungeonKeys.cs` (chaves `key_*` / "summon a drop ship", só no Nexus) e `Dungeons\MissionTransport.cs` (lista de missões do porta-aviões gigante; `docs/dungeon-tuning.md`).
- `GameData\MonoHash.cs`: hash de string do Mono. `StatefulEffectId(nome) = (short)hash`; `TemplateHash = (short)(h ^ (h >> 16))`.

## Fluxo

1. Nexus: no primeiro nascimento da conexão, `DungeonCatalog.SendNexusPortals` cria um Portal (prefab 11) por dungeon aberta com `inNexus`: flags 1|2 (dungeon, ligado), `dungeonType` = terreno (o cliente mostra nome e tier de `Texts/dungeons`), fim da energia = agora + 10⁶. O Mission Transport (`entryMode`) é a outra entrada.
2. `ServerSendMeThroughPortal` no Nexus (ou `ServerMissionJoin`, ou uma chave `key_*`) → `ClientPortalResult` + troca de servidor para 1113 com o avião (`ClientSwitchToServerWithPlane`; `DungeonServer.SetPending(conta, tipo)`). `/dungeon X` faz o mesmo pelo chat.
3. Dungeon, `ServerSetSessionTicket`: `PrepareSession` cria ou escolhe a instância antes do welcome; `WriteClientWelcome` leva `terrainName` = tipo (TerrainConfig: luz, música, território) e `scenario` = tipo (o HUD mostra o nome). Depois: chão (`Map.WriteFloor`) e ClientMapInfo do terreno com as placas.
4. Nascimento no último ponto de controle (ou `spawn`). O primeiro nascimento cria os portões e começa a dungeon.
5. Salas: montadas adormecidas quando o jogador chega a 3 tiles da área; acordam ao entrar. Grupo `room:<sala>` = inimigos que contam para limpar; `extra:<sala>` = baús, paredes, invulneráveis, parados, destroços e `excludeFromRoomClear`. `closesOnEnter`: portões com `room` = essa sala renascem atrás do jogador e reabrem com a sala limpa, **se** `lockBossRooms` estiver ligado (padrão desligado).
6. Portões: uma parede por sub-célula livre. `damageable` = barricada (uma parede quebrada derruba o portão todo); senão invulnerável e abre por `opensWhen`.
7. Mortes: `sequence`, `onDeath` (destroços `*DEATH` somem em 6 s), falas `barks` (`ClientShowUnitChat`), `periodicSummon`, `durationSeconds`, `dpsChest`, `chainReaction`. Chefe ou minichefe morto leva os invocados periódicos.
8. Fim: `exit.when` (ou todas as salas de chefe limpas) → o resto das salas de chefe explode, porta-aviões de saída (flags 2|4) em `exit.point` depois de 2,5 s, volta automática depois de `autoExitSeconds`. `SendMarkDungeonFinished` e a entrada no porta-aviões marcam o tipo em `finishedDungeonTypes` e levam ao Nexus com o mesmo avião. Quando o último jogador sai, a instância é descartada.
9. Comandos (todos em inglês): `/dungeons`, `/dungeon X`, `/dungeonhelp` (em qualquer servidor); dentro da dungeon `/room [N]`, `/boss`, `/restart`, `/leave` (`/leave` e `/restart` também existem no tutorial). `DungeonServer.Command` roda antes do tutorial e dos inimigos. Lista completa: `Chat commands.md`.

## Iron Lair e Bomb Factory (as primeiras ligadas)

- **SN-Z:** a build 190426 tem a camada "toggle_safety": `StartEffect Shielded` por 10 s em 90/70/50/30/10% de vida. O visual usado é o `statefulEffects` `tankShield`. Os 22 `gunMounts` vêm em grupos por fase: 0–2 metralhadoras, 3/6/13/15 "dud", 4–5 mainphase1, 7–12 mainphase2, 14 mainphase3 (míssil), 16–18 mainphase4, 19–21 finalattack. Cada escudo troca as armas; os esquadrões invocados são reconstrução.
- **Bomb Factory:** The Bomb Maker tem 0 armas → `periodicSummon` de `bombbossexploder` (2 a cada 5 s, máx. 6). Overseer Charles (sem armas) solta `homingbomb` a cada 7 s. `bombdungeontreasureBIG` (10000 hp no cliente, o medidor de DPS do original) virou `dpsChest` com 1000 de vida e 25 s.

## Teste de 15/11 (Iron Lair e Bomb Factory)

- Funcionou: porta-aviões do Nexus → dungeon, welcome, chão, salas montando e acordando, ponto de controle, portão fechando atrás do jogador (Iron Lair), destruição do portão, morte com perda do avião (permadeath) e volta ao Nexus. Sem erro no servidor nem no log do jogo.
- **Bug achado e corrigido:** na 1ª sala da Bomb Factory havia um inimigo invisível que atirava um anel de balas em volta de si. Causa: `bombbarrelboom` (arte `!EnemyInvisible`, 1 arma de 360°, 12 balas) é no original o atirador de UM anel que nasce quando o barril visível (`bombbarrel`) explode. Correção: o barril leva `onDeath: "bombbarrelboom"`; o atirador tem `aggroRadius 0`, `firstShotDelay 0`, `shotRange 999`, `excludeFromRoomClear` e `durationSeconds 1.5`. Mesma coisa para `firebarrel` / `firebarrelboom` (Reeducation Camp, anel dura 3 s). Falta retestar.
- A dificuldade (vida e dano por tier) não foi mexida de propósito: o dono do projeto vai compensar com itens que melhoram o avião.
- Ainda invisíveis e armadas nas dungeons fechadas (conferir quando abrir): `lavapillar`, `blizzard`, `meowzalaserB/D`, `avatarcannon`, `sunfoxtrap`, `lasergateWE`, `beartrap`, `arena2tripodminionmaster`, `dochatcher`, `trapbutton`, `coilsnake01neck`, `riotguardleader`. Desde 22/11, unidade invisível sem arma (`trapbutton`) nem é criada.

## Builds antigas conferidas (15/11)

190426.2227, 190609.2106, 190709.0120, 190828.2045 e 190911.1941: todos os `Dungeons/dungeon_*` vazios. Só a 190426 tem `additionalStates` (53 unidades), `includedStates` (74), `namedUnitStates` e `difficultyTable` (114, "standardBoss") nas unidades. Eles tinham sido exportados para `ia_190426.json` pelos scripts de `tools\dungeons-190828`, que **saíram do repositório**; para um futuro interpretador de máquinas de estado é preciso exportar de novo a partir da 190426 (`VersoesAntigas\`, fora do git).

## Falta

- Retestar a Bomb Factory depois das correções de 22/11 e jogá-la até o fim; testar as outras dungeons uma a uma e ligá-las no `dungeons.json`.
- `nextDungeon`, roteiros especiais (broca, chave das Vaults, altares, emboscadas, esteiras, trilhos, respiros), modificadores de dungeon, limite de jogadores por instância e fila, IA original pelos estados da 190426.
