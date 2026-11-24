# Progressão do piloto e do avião (servidor v7; revisado em 23/11/2026)

Seção 3 do checklist (tudo aqui ainda precisa de um teste no jogo, menos o XP por abate, o nível e o desbloqueio de classes, testados em 14/11 e 22/11). Código em `Server\Progression` e `Server\GameData`, nomes e comentários em inglês.

## De onde vêm os dados

O servidor lê o **configBinary** do jogo instalado (`Steambirds_Data\StreamingAssets\configBinary`): é o arquivo único de onde o cliente carrega todas as configs (DarkConfig, comprimido em LZMA). Ordem de procura: `Server\data\configBinary`, a pasta `STEAMBIRDS_GAME`, a pasta acima do BepInEx e a pasta padrão do depot 386011. Sem ele valem as tabelas da build 190828, embutidas.

| Dado | Config | Uso |
|---|---|---|
| Níveis do avião | `global.xpTargets` (níveis 2–20; 225000 XP no 20 na build 201219) | XP → nível |
| Patentes do piloto | `global.pilotXpTargets` (1–200) | XP de piloto → patente |
| XP de piloto de um avião | `pilotScore` (por patente; melhor taxa 250 XP de avião : 1 de piloto) | tempo real e queda |
| Parte em tempo real | `global.realTimePlaneXPProportion` (0,9 na build 201219) | `Pilot.computedXP` |
| XP por abate | `tierConfig.xpTiers` + `Units/*` (`maxHitPoints`, `tier`, `xpMultiplier`) | ver fórmula |
| Skills | `skills`, `skillTree` | validação na Pilot School |
| Desbloqueio de classes | `unlocks`, `playerclasses.unlockItemId`, `global.startingClassUnlocks` | classes novas |
| Buffs | `buffs` | buffs da conta |
| Nome da classe | `Texts/<idioma>/planeclasses` | tela de morte |

Confirmado no primeiro teste (build 201219.1413): 1953 configs, 1641 unidades, 200 skills em 100 grupos, 15 regras de desbloqueio, 126 buffs, `pilotScore` com as 200 patentes. `death` vem vazio. `career_rewards` traz 13 ids sem patente: `career_reward_storage_1..12` (baú) e `career_reward_hangar_1` (vaga de hangar).

## Regras (do código do cliente)

- **Abate:** XP = vida base × `xpTiers[tier]` × `xpMultiplier` (reconstruída; a fórmula só existia no servidor original, mas `xpTiers` ≈ 0,19 × `healthTiers`, então dá ~19% da vida real). Limite do original (`GameServer.GetXpActuallyAwarded`, que veio inteiro na descompilação): no máximo metade do nível atual; no nível máximo, `xpLimitForMaxLevel`. Vai `ClientUpdatePlayer` `61589` (barra, subida de nível, efeito de classe nova) e `TriggerPlayerSkill(OnKill)` `65038`, que o cliente nunca dispara sozinho. Chefes com `showPostFightDetails` ganham `ClientPostFightDetails` `61590`, com a parcela de dano de cada jogador (vida realmente tirada do inimigo ÷ total); abaixo do `damageThreshold` da unidade (padrão 0,5%) o boss não solta loot para aquele jogador.
- **XP de piloto em tempo real:** o cliente mostra `baselineXP` + `realTimePlaneXPProportion` × `pilotScore(patente, XP)` de **cada avião do hangar** (voando ou não). A patente só vem do servidor, então o servidor sobe a patente assim que esse total passa da próxima meta, mesmo em voo, e ajusta o `baselineXP` para o total não pular (a tabela de conversão muda com a patente). A calculadora do meowzaDB (meowzadb.com/xp) descreve a mesma regra para uma build mais antiga: 65% em tempo real e os outros 35% só ao **aposentar** o avião. Nesta build o hangar tem o botão "Retire plane" (`ServerRetirePlane` `12153`): `Progression\PlaneRetirement.cs` paga o XP de piloto que ainda não contava e a moeda (`pilotXpPer1Currency`), manda `ClientRemovePlane` `55111` e passa os itens equipados para o armazém (22/11, sem teste no jogo).
- **Queda:** o cliente fica parado no local esperando `ClientDeathSummaryInformation` `43727`. Com permadeath, o avião sai da conta e a parte em tempo real dele (90%) passa para o `baselineXP`. O resto (10%) só entra se o host ligar `deathPilotXpBonus`. A tela mostra classe, nível, "+XP de piloto" (essa parte mais o bônus), "subiu de patente" e a patente.
- **Troca de avião no hangar** (`15839`; falta conferir no jogo): o cliente manda `ServerRequestChangePlayerPlane` `15839` e para a câmera. O servidor tira o avião atual com `KillUnitsBatch` e `KillReason.ChangePlane` (16), que não conta como morte, e cria o escolhido no mesmo lugar com outro shortId (1000 → 1001…): o cliente descarta um spawn cujo id ainda está vivo. O avião novo entra 0,25 s depois, para o velho sair num quadro e o novo entrar em outro. Sem isso o jogador fica preso no hangar.
- **Skills:** pontos = patente − 1. Um grupo tem uma skill só (aprender a outra devolve os pontos da primeira). Requisitos do grupo: patente, nível de classe alcançado (`highestLevels`), or/and/not. Erro → `ClientSkillChangeError` `65040`; certo → `UpdatePlayerSkills` `65037`.
- **Desbloqueio de classes:** regras `unlocks` (always / classLevelGreater) com o maior nível de cada classe. Testado: walker nível 9 → paladin.
- **Recompensas de carreira:** o cliente pede a lista (`12159`), mas nunca pede a entrega (`12161`); o servidor entrega sozinho ao alcançar a patente (`12162`). Tipos entregues: PilotXp, PlaneXp, Unlock e Item (22/11: `Items\ItemDelivery`, com fila `PendingRewardItems` quando não há vaga).
- **Bônus de XP (22/11):** o consumível `planexpboost` liga um bônus de XP de avião por tempo (`PlaneXpBoost`); o cliente o mostra na lista de buffs (`ClientUpdatePlayerDataPlaneXpBoost` `48944` e os dois campos do welcome).
- **Retrato:** esta build não tem tela de escolher retrato; o servidor original sorteava. Nesta build todas as 69 peças são retratos inteiros (`portrait_player_A_01` … `W_03`, slot Base), então o sorteio é de uma base só.
- **Jogadores por perto:** `ClientReceiveReliableStats(0, hora, quantidade)` a cada segundo (raio `reliableStats_nearbyPlayerCountRadius`, o cliente esquece em 3 s).

## progression.json (host)

Criado com os padrões na pasta do servidor e relido ao salvar: `planeXpMultiplier`, `pilotXpMultiplier` (multiplica o XP de piloto em tempo real e o bônus), `capXpPerKill`, `permadeath`, `deathPilotXpBonus` (0 = original; 1 = o piloto ganha 100% do valor do avião na queda), `skillLoadoutSlots` (padrão 3; o original era 1 + loja), `hangarSlots` (padrão 3; o original era 2 + loja), `startingPilotLevel`, `startingUnlocks`, `unlockAllClasses`, `unlockClassesByLevel`, `postFightDetails`, `killSkillTrigger`, `randomPortrait`, `validateSkills`, `careerRewards`. Chaves que a versão não conhece aparecem no log (a antiga `deathPilotXpShare` saiu).

## Comandos no chat

`/progression` (ajuda), `/pilot`, `/xp N`, `/level N`, `/rank N`, `/reset_pilot`, `/buff ID`, `/buffs`, `/no_buffs`, `/portrait` (`Chat commands.md`).

## Conta

`account.json` ganhou `Pilot` (patente, `BaselineXp`, loadouts, retrato, buffs, `HighestLevels`, `Unlocks`, recompensas entregues, abates) e, em cada avião, `Xp`, `Level` e `BankedPilotXp` (bônus de queda já pago, só usado com permadeath desligado).

## Plugin (1.20.0 em diante; o atual é o 1.22.0)

- `portraits_config.txt` com as peças de retrato (o `SteambirdsGameFiles.exe` copia para `Server\data`).
- Correção: a partir da 2ª morte o cliente reabre a janela de morte com os números da morte anterior (`DeathWrapupStateMachine.hasDeathStats` nunca volta para false). O patch esquece os números antigos ao começar a esperar os novos.

## Em aberto

- Conferir no jogo a fórmula de XP por abate e a velocidade de subida.
- Patente de cada recompensa de carreira (os 13 ids existem, as patentes não estão no configBinary).
- Equipamento salvo na morte (`DeathLootConfig.salvageRandomEquipment`): feito como opção do host (`deathSalvageRandomEquipment` no `items.json`); falta testar.
- Multiplayer: `IncrementPlayerStats` `879` para cura em outros jogadores; o repasse de gatilhos (`TriggerOtherPlayerSkill`) existe desde 22/11, sem teste com dois PCs.
- Ferro por XP de piloto (`pilotXpPer1Currency` / `ClientNotificationGiveCurrencyForPilotXp` `54391`) ainda não existe (a aposentadoria já usa a mesma taxa).
