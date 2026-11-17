# Servidor de dungeon (v8, 15/11/2026)

Testado no jogo em 15/11 (Iron Lair e Bomb Factory): compilou e rodou, o fluxo inteiro funcionou (veja "Teste de 15/11"). A correção do atirador invisível (v8b) ainda não foi testada (o ambiente não tem dotnet; o código foi revisado à mão). Doc para quem hospeda: `Server\maps\README-dungeons.md`. Formato dos campos: `maps\README.md` e `docs/formato-de-mapas.md`.

## Arquitetura

- Terceiro servidor UDP: porta **1113**, ServerRole **Dungeon (2)**, RegisteredHost id 3. Nexus 1111 (0), tutorial 1112 (3).
- `Dungeons\DungeonCatalog.cs`: `dungeons.json` (ao lado do `enemies.json`; relido quando muda). Por dungeon: `tipo` (terreno do cliente), `mapa` (arquivo), `ativa`, `noNexus`, `multiplicadorDeVida`, `abreEm`. Também a fileira de porta-aviões no Nexus, `maxJogadores`, `saidaAutomaticaSegundos`. Sem o arquivo: Iron Lair e Bomb Factory.
- `Dungeons\DungeonMapData.cs`: lê as seções de dungeon do mapa (cache por data do arquivo e multiplicador). Os tipos vão para o `InimigosEsboco` com prefixo `<mapa>/` (`RegistrarTipoPadrao`); `fases[].invocar` ganha o mesmo prefixo. Condições: `limpa:sala`, `morte:tipo`, `quest:id` (por enquanto sempre verdadeira), juntas com `+`.
- `Dungeons\DungeonServer.cs`: uma sessão por conexão (cópia própria da dungeon), tick de 0,1 s sob `JogoEsboco.Trava`.
- `GameData\MonoHash.cs`: hash de string do Mono (algoritmo de 64 bits da referência .NET). `StatefulEffectId(nome) = (short)hash`; `TemplateHash = (short)(h ^ (h >> 16))` (confere com os hashes 1585/1641 do catálogo).

## Fluxo

1. Nexus: no primeiro nascimento da conexão, `DungeonCatalog.SendNexusPortals` cria um Portal (prefab 11) por dungeon aberta: flags 1|2 (dungeon, ligado), `dungeonType` = terreno (o cliente mostra nome e tier de `Texts/dungeons`), fim da energia = agora + 10⁶ (porta-aviões de dungeon some quando a energia acaba).
2. `ServerSendMeThroughPortal` no Nexus → `ClientPortalResult` + `MandarParaDungeon` (`ClientSwitchToServerWithPlane` para 1113, `DungeonServer.SetPending(conta, tipo)`). `/dungeon X` faz o mesmo pelo chat.
3. Dungeon, `ServerSetSessionTicket`: `PrepareSession` cria a sessão antes do welcome; `WriteClientWelcome` leva `terrainName` = tipo (TerrainConfig: luz, música, território) e `scenario` = tipo (o HUD mostra o nome via `GetDungeonNameOrNull`). Depois: chão (`Mapa.EscreverChao`) e ClientMapInfo do terreno com as placas (`InfoDeMapaEsboco.RegistrarMapa` por terreno).
4. Nascimento: no último ponto de controle (ou `nascimento`). O primeiro nascimento cria os portões e começa a dungeon.
5. Salas: montadas (adormecidas, `DefinirAdormecido`) quando o jogador chega a 3 tiles da área; acordam ao entrar (`AcordarGrupo`). Grupo `room:<sala>` = inimigos que contam para limpar; `extra:<sala>` = baús, paredes, invulneráveis, parados, destroços e unidades com `naoContaNaSala`. `fechaAoEntrar`: portões com `sala` = essa sala renascem invulneráveis atrás do jogador e reabrem com a sala limpa.
6. Portões: uma parede por sub-célula livre. `liberado` = barricada (uma parede quebrada derruba o portão todo); senão invulnerável e abre por `abreQuando`.
7. Mortes (polling de `ObterPosicao`): `sequencia`, `aoMorrer` (destroços `*DEATH` somem em 6 s), falas `aoAcordar`/`fase`/`aoMorrer` (`ClientShowUnitChat` formato 1), `invocacaoPeriodica`, `duracaoSegundos`, `bauDeDps`. Chefe ou minichefe morto leva os invocados periódicos.
8. Fim: `saida.quando` (ou todas as salas de chefe limpas) → o resto das salas de chefe explode, porta-aviões de saída (flags 2|4) no ponto `saida.ponto` depois de 2,5 s, volta automática depois de `saidaAutomaticaSegundos`. `SendMarkDungeonFinished` e `ServerSendMeThroughPortal` do porta-aviões marcam o tipo em `finishedDungeonTypes` e levam ao Nexus com o mesmo avião.
9. Comandos: `/dungeons`, `/dungeon X`, `/dungeonajuda`; dentro da dungeon `/sala [N]`, `/chefe`, `/recomecar`, `/sair` (só dentro da dungeon, porque o tutorial usa `/sair` e `/recomecar`). `DungeonServer.Command` roda antes do tutorial e dos inimigos.

## Mudanças em arquivos existentes

- `InimigosEsboco.cs`: `EfeitoVisual` no tipo; `EscudoSegundos` e `EfeitoVisual` nas fases; escudo bloqueia dano; FieldUpdate EFFECTSTATE1 (máscara 8192, short) no spawn e quando muda; API de uma unidade (`ObterVida`, `RemoverUnidade`, `EstaAdormecido`, `DefinirInvulneravelUnidade`, `DefinirEfeitoVisual`); limite de 140 unidades por instância; instância nova quando o mesmo cliente volta por outra conexão, e `Desconectou` de uma conexão antiga não apaga a instância nova. **v8b:** arma `JustShootDammit` (modo 7) com arco de 360° atira em qualquer direção (antes exigia o jogador a ±60° da frente da unidade).
- `ObjetosEsboco.cs`: `EnviarPortal` genérico (flags, `dungeonType`, início/fim da energia), `EnviarPortalDeDungeon`.
- `PlayerDataEsboco.cs`: `WriteClientWelcome(..., terrainName, scenario)`.
- `JogoEsboco.cs`: chama `DungeonServer.Command`.
- `Program.cs`: porta 1113, `MandarParaDungeon`, welcome/chão/ClientMapInfo da dungeon, nascimento, portais no Nexus e na dungeon, marca de terminada, desconexão, laço e inicialização.
- `DungeonMapData.cs` (v8b): campo `naoContaNaSala` do tipo; `DungeonServer.Spawn` o respeita.

## Iron Lair e Bomb Factory (as ligadas)

- **SN-Z:** a build 190426 tem a camada "toggle_safety": `StartEffect Shielded` por 10 s em 90/70/50/30/10% de vida. "Shielded" é efeito de servidor, não visual; o visual usado é o `statefulEffects` `tankShield`. Os 22 `gunMounts` vêm em grupos por fase: 0–2 metralhadoras, 3/6/13/15 "dud", 4–5 mainphase1, 7–12 mainphase2, 14 mainphase3 (míssil), 16–18 mainphase4, 19–21 finalattack. Cada escudo troca as armas; os esquadrões invocados são reconstrução.
- **Bomb Factory:** The Bomb Maker tem 0 armas → `invocacaoPeriodica` de `bombbossexploder` (2 a cada 5 s, máx. 6). Overseer Charles (sem armas) solta `homingbomb` a cada 7 s. `bombdungeontreasureBIG` (10000 hp no cliente, que era o medidor de DPS do original) virou `bauDeDps` com 1000 de vida e 25 s.

## Teste de 15/11 (Iron Lair e Bomb Factory)

- Funcionou: porta-aviões do Nexus → dungeon, welcome, chão, salas montando e acordando, ponto de controle, portão fechando atrás do jogador (Iron Lair), destruição do portão, morte com perda do avião (permadeath) e volta ao Nexus. Sem erro no servidor nem no log do jogo.
- **Bug achado:** na 1ª sala da Bomb Factory havia um inimigo invisível que atirava um anel de balas em volta de si quando o jogador chegava perto. Causa: `bombbarrelboom` (arte `!EnemyInvisible`, 1 arma de 360°, 12 balas, `burst 1`, `cooldown 40000`) é no original o atirador de UM anel que nasce quando o barril visível (`bombbarrel`) explode; no mapa ele estava solto na sala como `torre` com `raioDeAgro`.
- **Correção (v8b):** o barril leva `aoMorrer: "bombbarrelboom"`; o atirador tem `raioDeAgro 0`, `atrasoDoPrimeiroTiro 0`, `alcanceDeTiro 999`, `naoContaNaSala`, `duracaoSegundos 1.5` (1 anel e some). Mesma coisa para `firebarrel` / `firebarrelboom` (Reeducation Camp, anel dura 3 s). `chainreaction` (invisível, sem arma) saiu do depósito. Gerador: função `barrel_types`.
- A dificuldade (vida e dano por tier: o Factory Worker de 50 hp base tem 300 hp na 190828) não foi mexida de propósito: o dono do projeto vai compensar com itens que melhoram o avião.
- Ainda invisíveis e armadas nas dungeons fechadas (conferir quando abrir): `lavapillar`, `blizzard`, `meowzalaserB/D`, `avatarcannon`, `sunfoxtrap`, `lasergateWE`, `beartrap`, `arena2tripodminionmaster`, `dochatcher`, `trapbutton`, `coilsnake01neck`, `riotguardleader`.

## Builds antigas conferidas (15/11)

190426.2227, 190609.2106, 190709.0120, 190828.2045 e 190911.1941: todos os `Dungeons/dungeon_*` vazios. Só a 190426 tem `additionalStates` (53 unidades), `includedStates` (74), `namedUnitStates` e `difficultyTable` (114, "standardBoss") nas unidades; exportados em `tools\dungeons-190828\ia_190426.json` para um futuro interpretador de máquinas de estado.

## Falta

- Retestar a Bomb Factory depois da v8b (barris) e jogá-la até o fim; testar as outras dungeons uma a uma.
- `proximaDungeon`, roteiros especiais (broca, chave das Vaults, altares, emboscadas, esteiras, trilhos, respiros), loot, chaves `key_*`, modificadores, vários jogadores por instância, IA original pelos estados da 190426.
