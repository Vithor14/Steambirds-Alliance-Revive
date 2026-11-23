# Steambirds Alliance Revive — Checklist

 **Mundo aberto (22/11/2026, testado no jogo: funcionou):** `Server\World\` (porta UDP 1114, `worldPort` no `network.json`, `world.json` do host) gera o mapa do terreno `Basic` por semente (regiões por elevação, ilhas em discos, cânions, muros, peças de chão), manda o chão em pedaços, o nome da região, inimigos por região perto dos jogadores e porta-aviões de dungeon que aparecem e somem; o Nexus ganhou um portal para o mundo e há `/world`, `/worldinfo` e `/carrier`. Chefes de setpiece, quests de ilha, eventos e IA própria dos inimigos ficaram de fora (seção 7 e `docs\open-world.md`). **Lojas, compras e vagas (22/11/2026, compiladas; só testadas com um cliente de teste que fala as mesmas mensagens, falta o jogo):** `Server\Stores\` monta as prateleiras das 4 lojas a partir do `store` do `configBinary` (294 produtos, que o cliente já trazia), trata `ServerPurchaseInStore` `54296`, desconta ouro/ferro da conta, entrega itens, desbloqueios e vagas (hangar, loadouts, armazém, cargo), guarda as compras em `accounts\<chave>\store.json` e lê as regras do host em `stores.json` (aba Stores do launcher). Sem pacotes de ouro da Steam. Detalhes e bytes em `docs\stores.md`.Situação em **19/11/2026**: nomes, comentários, logs e READMEs em inglês e `Program.cs` dividido em partes (seção 14; os quatro projetos compilam e o servidor sobe e responde ao login, mas o jogo ainda não foi aberto com essa versão: rodar `Build launcher.ps1` e `Build plugin.ps1` antes de testar). Servidor Server v7 (troca de avião no hangar e XP de piloto em tempo real; a v6 rodou no jogo em 14/11: XP por abate, nível do avião e desbloqueio do paladin funcionaram), plugin Steambirds Quieter 1.20.0, cliente **201219.1413** (depot 386011, manifest 58813776496064724). As 17 dungeons da build 190828 foram recriadas em `maps\dungeon_*.json`. **v8 (15/11, ainda não testada no jogo):** servidor de dungeon na porta 1113 (`Dungeons\`), com Iron Lair e Bomb Factory ligadas no `dungeons.json`. **v9 (15/11, ainda não testada no jogo):** itens, inventário autoritativo e loot (`Items\`, `loot_tables\`, `itens.json`, `inventario.json`) e o catálogo de itens (`itens\catalogo_de_itens.md`). **Launcher 0.1.0 (15/11, compilado e testado no PC: subiu o servidor e abriu o jogo):** `Launcher\` (WinForms, .NET 8) com abas Client e Server, montado pelo `Build launcher.ps1` em `Launcher\dist` (servidor e launcher como .exe self-contained, sem `dotnet run`); o que ainda não existe no projeto aparece acinzentado. **Launcher 0.2.0 (15/11, testado em 22/11):** aba Settings (tema escuro, pastas e logs), inglês como padrão com português e pasta `lang` para a comunidade, idioma do jogo (`-locale`), desbloqueios iniciais em checklist, um campo por moeda e janela para editar e remover a conta. **v11 + Launcher 0.3.0 (16/11, testados em 22/11, inclusive com dois PCs):** contas por jogador (`contas\<steam_ID>\`, Steam ID tirado do ticket de login; a conta antiga vai para o primeiro jogador do PC do host), servidor aberto para outros PCs (`rede.json` com nome, portas, máximo de jogadores e comandos de chat; HTTP em todas as redes; o endereço de cada troca de servidor e portal é o que o jogo de cada jogador usou), busca de servidores (UDP 6444) e `/revive/info`; no launcher, lista de servidores da rede local e das VPNs, Conectar por IP, nome do jogador, download do depot pela Steam, aba Rede e multiplayer (endereços, firewall, portas) e lista de contas. Cada jogador ainda joga no próprio mundo (não se veem). **Relay de multiplayer no Nexus (17/11, testado com dois PCs em 19/11 e 22/11):** `Server\Network\PlayerRelay.cs` repassa avião, movimento, tiros, mortes e chat entre os jogadores do Nexus; nomes antigos em português corrigidos no `Compat\LegacyFiles.cs` (servidor e launcher). **Dungeons com vários jogadores (17/11, testado com dois PCs em 22/11):** quem entra numa dungeon já em andamento do mesmo tipo entra na mesma instância (mesmas salas, inimigos e portões; se vêem pelo `PlayerRelay`); quando o último jogador sai a instância é descartada e a próxima entrada começa do zero. **Launcher + aba Map editor (16/11, compilada e testada em 22/11):** editor visual de mapas dentro do launcher (`Launcher\Editor`, imagens em `Launcher\editor-assets`). **Release do servidor e arquivos do jogo (20/11, compilados e testados em 22/11):** `Build server release.ps1` monta `Release\SteambirdsServer-<versão>-win-x64.zip` para o GitHub Releases (servidor, `SteambirdsGameFiles.exe`, plugin, configs e mapas, sem nenhum arquivo do jogo); `GameFiles\` (`SteambirdsGameFiles.exe`) pega da cópia do jogo de cada jogador o `configBinary`, instala o BepInEx e o plugin, abre o jogo uma vez em modo de exportação (plugin 1.21.0, `GameFilesExport.cs`) e copia os dados do plugin para `data\` e as imagens do editor para `editor-assets\`.

**Legenda**
- [x] feito
- [ ] 🟡 parcial: existe no servidor, mas responde vazio, só grava no log ou ainda falta conferir no jogo
- [ ] falta

**Protocolo:** o cliente conhece 232 mensagens de rede. 65 delas já aparecem no servidor, mas parte só é registrada no log ou respondida vazia. Os números entre crases são os IDs das mensagens (`MessageCatalog.cs`).

## Resumo

| # | Área | Feito | Parcial | Falta |
|---|---|---|---|---|
| 1 | Base: conexão, login e conta | 14 | 2 | 2 |
| 2 | Avião, classes e hangar | 8 | 7 | 0 |
| 3 | Piloto, permadeath e progressão | 0 | 13 | 0 |
| 4 | Combate e inimigos | 8 | 9 | 1 |
| 5 | Itens, inventário e loot | 2 | 14 | 0 |
| 6 | Rebel City (Nexus) e prédios | 4 | 4 | 5 |
| 7 | Mundo aberto (ilhas) | 5 | 2 | 3 |
| 8 | Dungeons e tutoriais | 5 | 13 | 5 |
| 9 | Quests, missões e conversas | 0 | 3 | 6 |
| 10 | Social: chat, amigos, flocks e troca | 2 | 2 | 6 |
| 11 | Combat Sim | 0 | 0 | 5 |
| 12 | Troca de servidores | 1 | 1 | 3 |
| 13 | Áudio, interface e extras | 0 | 1 | 5 |
| 14 | Código e repositório | 8 | 1 | 0 |
| 15 | Launcher base — aba Client (objetivo intermediário) | 8 | 0 | 1 |
| 16 | Launcher — aba Server | 5 | 8 | 2 |
| 17 | Launcher standalone (objetivo final) | 3 | 4 | 0 |
| 18 | Editor de mapas | 6 | 3 | 3 |
| | **Total** | **79** | **87** | **47** |

Os apêndices (classes, terrenos e chefes) e as observações abaixo ficam fora da conta.

## Observações: o que vem do cliente e o que precisa ser recriado

O cliente traz os **dados**, mas as **regras** ficavam no servidor original. O servidor já lê os 1953 arquivos de config do `configBinary`, então o que está lá não precisa ser refeito. O resto precisa ser recriado: pelo código do cliente (o que ele espera receber), por vídeos e por builds antigas.

**Dá para pegar do cliente (`configBinary` e código)**
- Unidades, armas, nuvens e manobras (já em uso)
- Classes, itens iniciais, skills, árvore de skills e desbloqueios (já em uso)
- Tabelas de XP do avião e do piloto e `pilotScore` (já em uso; o `pilotScore` existe na build 201219)
- Buffs, inclusive os `dungeonmod_*` (já lidos)
- Textos, conversas, placas de lore e ClientMapInfo de 10 terrenos (já em uso)
- Algoritmo de geração do mundo (`WorldLayout`), que o servidor original rodava com uma semente
- Unidades de cada dungeon, placas `*dungeonsign1/2`, falas dos chefes e quests das dungeons (usadas nos mapas de dungeon da 190828)

**Confirmado que não está no cliente atual**
- [x] IA dos inimigos (`AI.cs` e `ModalAI.cs` vazios): recriada com comportamentos genéricos; falta a IA original de cada chefe (Apêndice C)
- [x] Mapa do Rebel City (era só do servidor): refeito pelos vídeos
- [x] Mapa e roteiro do tutorial do walker (`Dungeons/dungeon_tutorial` vazio): refeitos pelo vídeo do Open Beta
- [x] Mapas das outras dungeons: os arquivos `Dungeons/dungeon_*` estão vazios (`{}`) em todas as builds conferidas (201219, 190828 e 190426). Recriados a partir das unidades, placas, falas e quests da 190828 (seção 8)
- [x] Fórmula de XP por abate: reconstruída a partir de `xpTiers`
- [ ] 🟡 Config `death` vem vazia (`death = {}`): o que se salva na queda virou opção do host no `items.json` (`deathSalvageRandomEquipment`, padrão 0 como o original; `keepCargoOnDeath`, `keepEquippedOnDeath`)
- [x] Tabelas de loot por nome: `named_loot_tables` vem vazio; as unidades só citam o nome (`tier7minion`, `tiernboss`, `tier10treasure`...) e algumas trazem tabelas embutidas. Recriadas em `loot_tables\` (um arquivo por tabela)
- [ ] 🟡 Recompensas de carreira: o cliente só tem ids e ícones (`career_reward_storage_*`, `career_reward_hangar_1`); o que cada uma dá foi definido no `progression.json`
- [x] Sorteio do retrato do piloto: feito no servidor com as peças exportadas pelo plugin

**Regras que só existiam no servidor (precisam ser recriadas)**
- [ ] 🟡 Mundo aberto (22/11, `Server\World\`, `docs\open-world.md`): a geração, onde e quando cada inimigo nasce, densidade, respawn e quando um porta-aviões de dungeon aparece foram recriados (sítios de spawn por região, `world.json`). Cada cliente só recebe os inimigos dentro de `enemies.viewRadius` (23/11, `Enemies.Interest.cs`), então o radar não mostra setas de inimigos distantes; falta testar no jogo. Falta a hora dos eventos e os chefes de setpiece
- [ ] 🟡 Roteiro das dungeons: ordem das salas, portões, quem acorda quando, fases dos chefes. Feito no tutorial do walker; nas 17 dungeons da 190828 está descrito nos mapas (`tipos` e `encontros`) e o servidor de dungeon (v8) roda essa parte genérica. Os roteiros especiais (broca, chave, altares, emboscadas) ainda não
- [ ] 🟡 Loot: sortear o drop (algoritmo do cliente, `SteambirdsLootTable.DrawLoot`), criar o saco, raridade. Feito na v9; falta testar e "quem pode pegar" com vários jogadores
- [ ] 🟡 Inventário autoritativo: validar trocas de vaga, criar ids de item, desgaste, reforja (com sorteio) e reciclagem. Feito na v9; falta testar
- [ ] 🟡 Lojas: o `configBinary` traz o `store` inteiro (294 produtos, 4 lojas, moedas `gold`, `iron`, `newscrap`), então só as prateleiras (`ClientReceiveStoreContent` `54290`) e a compra foram recriadas, em `Server\Stores\`. Falta testar no jogo (lista em "Lojas e vagas: testes no jogo", seção 4). Sem as promoções (`fillers` não vêm em nenhuma build; a prateleira `general_sales` fica vazia até o host pôr produtos no `stores.json`)
- [ ] Quests: quais quests cada prédio oferece, contagem do progresso dos objetivos, entrega das recompensas
- [ ] Missões e squads: rotação das missões do porta-aviões e formação dos grupos
- [ ] Social: flocks (os dados das guildas ficavam no backend Ozy), amigos, chat entre jogadores, troca segura, rankings e pontuação do Combat Sim
- [ ] 🟡 Dungeon compartilhada (17/11): jogadores do mesmo tipo de dungeon dividem a instância (inimigos, portões, salas, ondas; cada um recebe XP e o próprio loot; inimigos perseguem o jogador mais próximo) e a instância some quando fica vazia. Testado com dois PCs em 22/11; ainda sem limite de jogadores por dungeon e sem fila
- [ ] Orquestração: listar servidores, subir uma instância de dungeon por grupo, fila e troca entre servidores (no projeto vira o launcher e a aba Server)
- [ ] Multiplayer: repassar a cada jogador o que os outros fazem por perto e decidir quem tem autoridade sobre o dano nos inimigos
- [ ] 🟡 Economia: o ouro vinha de microtransações da Steam e não volta. Regra nova, configurável no `stores.json`: `priceMode` `currency` (a conta paga com o próprio ouro e ferro; o host dá ouro com `/currency gold N`, `startingCurrencies` do `items.json` ou o editor de contas) ou `free` (tudo custa 0), mais `discountPercent` e `discount` por produto. Falta: ferro por XP de piloto (`pilotXpPer1Currency` 10 / `ClientNotificationGiveCurrencyForPilotXp` `54391`), que era a forma de ganhar ferro jogando

**A conferir:** o `configBinary` pode trazer tabelas de loot por inimigo, as listas de quests e a agenda de missões. (O conteúdo das lojas, `StoreConfig`, foi conferido em 22/11: vem completo em todas as builds, só as prateleiras e a compra eram do servidor.) Antes de recriar, o servidor deve gravar no log quais dos 1953 arquivos de config vêm vazios ou quase vazios, e comparar com o configBinary da build 190828. Cada item acima muda para "pegar do cliente" ou "recriar" conforme o resultado.
- [ ] Gravar no log os arquivos de config vazios (201219 e 190828) e atualizar esta lista

---

## 1. Base: conexão, login e conta

- [x] Cliente da build 201219.1413 baixado com `download_depot 386010 386011 58813776496064724`
- [x] Jogo conecta no servidor local só com `OZY_URL` + `-connecttolocal` (`Run game.ps1`), sem precisar do plugin
- [x] HTTP Ozy: `/region`, `/user/login` (ticket da Steam), `/user/self`, `/user/queue_check`, `/maintenance_info`, `/alive`
- [x] Envio de logs de erro do cliente (`/user_uploads/*`), gravados em `envios-do-cliente`
- [x] UDP Copper/Lidgren: aprovação, hail, sincronia de relógio (`RequestDelta`/`ReplyDelta`, `ResyncSender`)
- [x] `ClientWelcomeMessage` com o PlayerData completo (piloto, retrato, inventários, buffs, aceites)
- [x] EULA e avisos legais (`ServerAcceptLegalNotice`, aceites no `acceptances.json` de cada conta)
- [x] Todas as 232 mensagens decodificadas campo a campo no log (as que não têm tratamento também)
- [x] Dados do jogo extraídos para `Server\data` (1641 unidades, 92 nuvens, 25 manobras, ClientMapInfo de 10 terrenos)
- [x] Servidor lê o `configBinary` do jogo direto da pasta instalada (DarkConfig + LZMA, `GameData\ConfigBinary.cs`): tabelas de XP, `pilotScore`, skills, desbloqueios, buffs, unidades e textos
- [x] Relatório automático de cada teste (`Run server.ps1`) e plugin de diagnóstico opcional
- [x] Conta salva em disco (`account.json` com piloto, aviões e dungeons; `inventory.json`; `acceptances.json`)
- [x] Uma conta por jogador (v11, testado em 22/11): `accounts\steam_<SteamID>\` no PC do host. O Steam ID sai do ticket de sessão da Steam que o jogo manda no login (bytes 12 a 19; sem ticket, a conta é do PC); o `sessid` de cada login liga as conexões UDP à conta. A conta única antiga (`account.json` da raiz) não é mais migrada. O resto do servidor continua chamando `Accounts` sem dizer a conta: cada mensagem abre o "escopo" da conta do jogador da conexão
- [x] Servidor aberto para outros PCs (v11, testado em 22/11): HTTP em todas as redes (`allowOtherPlayers` no `network.json`), o login devolve o Nexus no endereço que o jogo usou (LAN, Radmin, Hamachi ou IP público) e as trocas de servidor e os portais usam o endereço de cada jogador. Portas, nome, máximo de jogadores e comandos de chat (`everyone`, `host`, `none`) no `network.json`; `/jogadores` no chat; aviso de quem entra e sai
- [ ] 🟡 Todo jogador entra como admin (`isAdmin`), o que evita a desconexão automática após 120 s parado; quem usa os comandos de chat do servidor agora é escolhido no `network.json` (`chatCommands`: todos, só o host ou ninguém)
- [ ] 🟡 **Multiplayer (Nexus):** `PlayerRelay` repassa o avião (SpawnUnitsBatch com o PeerId do dono e shortId de relay 7.000.000+), `UpdateUnitsBatch` (posição, mira, estado, vida, nome, sinal das armas e efeitos), `GunFire` (com o relógio do atirador convertido para o do servidor), `KillUnitsBatch` (morte, troca de avião, saída), nuvens das skills e `ClientReceiveRemotePlayerData` de quem está online. Teste de 19/11 com dois PCs: aviões, animação de tiro, inimigos, portões e reset da dungeon funcionaram; nome, balas e skills em área dos outros foram corrigidos no mesmo dia (testado em 22/11). `TriggerOtherPlayerSkill` repassado (22/11), balas dos outros terminam onde acertam (`ClientKillBullet`); ainda sem `GunFireBatch` `62327`, nuvens para quem entra depois que elas foram criadas. Tutorial segue um mundo por jogador
- [ ] Login por usuário e senha (`/user/create`, `/user/change`, `/user/reset_request`, `/activation/*`); opcional, já que o login pela Steam funciona
- [ ] `/msg_interface_config` (mensagens/banners da interface)

## 2. Avião, classes e hangar

- [x] Criar avião (`ServerRequestCreateCharacter` → `ClientAddPlane`)
- [x] Spawn do avião com classe, variante, conta e characterId nas opções da unidade
- [x] Itens iniciais equipados (tabela das 15 classes no `PlayerData.cs`)
- [x] Seleção de avião (save slot) e menu de opções funcionando
- [x] Classes: as 15 foram testadas no jogo e funcionam (22/11; lista no Apêndice A)
- [ ] 🟡 Modelo de voo por classe (v10, `Planes\PlayerPlaneTemplates.cs`): o spawn usa o `planeName` da classe (configBinary `playerclasses`) em vez de `playerPlane` para todos. walker, paladin, support, pirate, siegetank e daggerknight → `walkerClassPlane` (8 direções, paira parado, armas seguem o mouse); olfaithful, duster e wizard → `playerPlane` (sempre para frente, tiro para frente). Conferido nas builds 190426–190911; falta conferir no jogo com a 201219
- [x] Unlock de classes pelo nível dos aviões: regras `unlocks` do configBinary (always / classLevelGreater) com `highestLevels`; o item novo vai no `ClientUpdatePlayer`. Testado: walker nível 9 desbloqueou e criou um paladin
- [ ] 🟡 Trocar de avião no Hangar (`ServerRequestChangePlayerPlane` `15839`): na v6 o jogador ficava preso no hangar. A v7 tira o avião atual (`KillUnitsBatch` com `ChangePlane`) e cria o escolhido no mesmo lugar com outro shortId. Falta conferir no jogo; `ClientShowPlaneSelection` `24120` sem uso
- [x] Remover avião da lista (`ClientRemovePlane` `55111`): já era mandado ao aposentar o avião (`PlaneRetirement`, antes de apagar o inventário dele); com permadeath o próprio cliente tira o avião ao cair (`GameClient` 755), então não há outro caminho
- [ ] 🟡 Variantes e skins (`ServerRequestChangePlaneVariant` `15910` → `ClientUpdatePlaneVariant` `15909`, `Planes\PlaneVariants.cs`, 22/11, sem teste no jogo): o menu "Variant" troca a variante do avião vivo se a classe a tem (`playerclasses` `variants`) e a conta tem o `unlockItemId` dela (o consumível `variant` e as lojas entregam); o servidor salva na conta, responde com o `15909` (também quando recusa, com a variante em vigor) e passa a variante nova para quem vê o avião depois. A pintura salva do avião volta ao padrão da variante. Só variantes com id de rede em `PlayerData.VariantNetIdShort` (as do cliente 201219)
- [ ] 🟡 Vagas do hangar: base em `hangarSlots` do `progression.json` somada às compradas na loja `store_hangar` (`hangar_slot`, preço 750, 1750, 2750, 3750 de ouro); `ClientUpdatePlayerDataSlots` `48914` e `ClientNotificationGiveProduct` `54390` mandados depois de cada compra, `accumulatedItems` no welcome. Teto de 20 aviões. Falta testar no jogo
- [ ] 🟡 Aposentar avião (`ServerRetirePlane` `12153`, botão "Retire plane" do hangar): `Progression\PlaneRetirement.cs` tira o avião da conta, paga o XP de piloto que ainda não contava e a moeda (`pilotXpPer1Currency`), manda `ClientRemovePlane` `55111`, passa os itens equipados para o armazém (itens tier 0 nunca vão para o armazém: são descartados com o avião) e encerra os buffs do avião. Falta testar no jogo
- [x] XP e nível do avião: XP por abate (fórmula reconstruída: vida base × `xpTiers` do tier × `xpMultiplier` da unidade), limite por abate do original, `ClientUpdatePlayer` `61589`, `ClientUpdateHighestLevels` `21500`, `ServerReportPlaneLevel` `13948`. Testado no tutorial (drone 4 XP, chefe 40 com o limite, nível 2 no fim). O ritmo ainda pode ser ajustado em `planeXpMultiplier`
- [ ] 🟡 Bônus de XP (`ClientUpdatePlayerDataPlaneXpBoost` `48944`, `Progression\PlaneXpBoost.cs`, 22/11, sem teste no jogo): o consumível `planexpboost` (`additionalPercentage` por `duration` segundos) guarda no piloto `PlaneXpBoostPercent` / `PlaneXpBoostUntil` (unix), manda o `48944` e vai no welcome (`planeXpAdditionalPercentage` + `until`). O XP de cada abate sobe esse % antes do limite por abate, como em `GameServer.GetXpActuallyAwarded`. Mesmo % soma o tempo; % maior substitui; menor é recusado pelo cliente
- [ ] 🟡 Upgrades permanentes do avião, que se perdem na morte: `ServerInvAccumulateItem` guarda em `_accumulatedItems` do avião (v9); falta conferir `upgradeCaps` e o `Permanents_`

## 3. Piloto, permadeath e progressão

Código em `Server\Progression` e doc em `docs/progression.md`. Tudo abaixo ainda precisa de um teste no jogo.

- [ ] 🟡 Permadeath: o avião sai da conta e o piloto fica com a parte em tempo real do valor dele (90% nesta build); o resto só com `deathPilotXpBonus`. Com `"permadeath": false` o avião volta ao hangar
- [ ] 🟡 Tela de morte (`ClientDeathSummaryInformation` `43727`; sem ela o cliente ficava parado no local da queda) e texto de pós-luta dos chefes (`ClientPostFightDetails` `61590`). O cliente repete os números da morte anterior a partir da 2ª morte: o plugin 1.20.0 corrige
- [ ] 🟡 Equipamento salvo na morte (DeathLootConfig `salvageRandomEquipment`): `deathSalvageRandomEquipment` no `items.json` manda itens equipados sorteados para o armazém, gastos (condição cai, supercarga some); itens tier 0 nunca entram no sorteio nem vão para o armazém. Com morte permanente o cargo e os equipados se perdem. "Ejeção" não aparece no código do cliente
- [ ] 🟡 XP e patente do piloto: `pilotXpTargets` e `pilotScore` (melhor taxa 250:1). Na v7 a patente sobe em tempo real, seguindo o XP que o cliente mostra (`Pilot.computedXP`: 90% do valor de cada avião do hangar), como descreve a calculadora do meowzaDB; `ServerRecordLevelupUIShown` `12158` guardado
- [ ] 🟡 Árvore de skills (`UpdatePlayerSkills` `65037`, `ServerRequestSkillChange` `65039`, `ClientSkillChangeError` `65040`), conferida no servidor como em `Pilot.GetSkillChangeError` (pontos, requisitos do grupo, nível máximo, uma skill por grupo)
- [ ] 🟡 Loadouts de skills (`65042` trocar, `65044` limpar, `12152` renomear); base em `skillLoadoutSlots` do `progression.json` somada às compradas na loja `store_skill_loadouts` (500, 1500, 2500 de ouro; teto de 10). A Pilot School compra e pede o loadout novo logo depois. Falta testar no jogo
- [ ] 🟡 Bônus de dano das nuvens e blasts (`Skills\DamageBoost.cs`, v10): o cliente nunca aplicava o Attack Area do walker (sem `ServerReportDamageBoost` no log, dano 1.74 antes e depois). O servidor guarda a zona, dá x`effect` por `bufftime` s (Attack Area: x1.3 por 4 s) aos aliados dentro e multiplica os acertos de tiro; se o cliente reportar o bônus, o servidor não multiplica de novo. Manda `ClientCloudEffect` `32571` para o efeito visual
- [ ] 🟡 Gatilhos de skills: o servidor manda `TriggerPlayerSkill` `65038` (OnKill) a cada abate; `ServerReportPlayerSkillTrigger`, `ServerReportDamageBoost` e `ServerReportCloudHeal` registrados; o repasse para outros jogadores é o `TriggerOtherPlayerSkill` (`Game.SkillRelay.cs`)
- [ ] 🟡 Recompensas de carreira (`12159`–`12162`): lista no `progression.json`, entregues sozinhas ao alcançar a patente (tipos PilotXp, PlaneXp, Unlock e Item) (o cliente nunca pede o `12161`). Faltam as recompensas originais (o log mostra o `career_rewards` do configBinary)
- [ ] 🟡 Retrato do piloto sorteado pelo servidor com as peças exportadas pelo plugin 1.20.0 (`retratos_config.txt`); `ServerReceivePortrait` `12150` / `ClientUpdatePlayerPortrait` `12151`
- [ ] 🟡 Buffs da conta (`ClientUpdatePlayerBuffs` `48945`, `ClientAddPlayerBuff` `48946`, `ServerAuditBuffAction` `48947`): guardados, vencidos saem, `/buff`, os buffs de um avião acabam quando ele cai ou é aposentado; falta quem dá buffs (consumíveis, modificadores de dungeon)
- [ ] 🟡 Jogadores por perto (`ClientReceiveReliableStats` `35051`, a cada segundo, para skills com condição de jogadores próximos); `IncrementPlayerStats` `879` (cura entre jogadores) depende do multiplayer
- [ ] 🟡 Ferramentas de admin no chat: `/piloto`, `/xp`, `/nivel`, `/patente`, `/zerar_piloto` (`ClientWipePilot` `4870`), `/buff`, `/retrato`; `ClientWipePlaneProgress` `8951` sem uso

## 4. Combate e inimigos

- [x] Entrada do jogador, manobras e supervelocidade recebidas
- [x] Tiros do jogador tiram vida de inimigos do servidor (`ServerReportBulletHit`, `ServerReportUnitDamage`)
- [x] Nuvens/blasts das skills criadas pelo servidor e encerradas no fim da duração
- [ ] 🟡 Dano das nuvens de veneno nos inimigos (`Skills\CloudDamage.cs`, 19/11): o ataque em área do Old Faithful (`poison_0`) criava a nuvem, mas o dano ficava no cliente, que nunca atinge os inimigos do servidor. Agora o servidor tira `effect` de vida por segundo (configBinary `clouds`) dos inimigos dentro do raio; `DamageMultiplier` ainda é 1 (multiplicador de dano do avião do original desconhecido). Falta testar
- [ ] 🟡 Habilidades de classe aplicadas pelo servidor (22/11, sem teste no jogo): cura do medic e aura de cura (`Game.CloudEffects.cs`, `IncrementPlayerStats`), escudo do dagger knight / resist aura (o servidor devolve a parte do dano que o escudo bloquearia; vida máxima estimada), aura de dano do assault (`Skills\DamageBoost.cs`, zona que segue o dono), torres do engineer e minas do raptor (`ServerSpawnGreenTeamAlly` -> `Enemies.Allies.cs`, limites e raio da explosão são reconstrução) e stun do canhão EMP do bandit (efeito Confuse/Jam/Bash em `ServerReportBulletHit`). Ataque em área do hog: o caminho (`CloudDamage`) já existia; sem log do teste não achei defeito, falta testar com o log Teste 22/11: stun do bandit, minas do raptor, turret criada, cura do medic e dano do hog funcionaram; teleporte para a turret corrigido (parentShortId = avião dono), a testar.
- [ ] 🟡 As nuvens das skills (poison, damage blast, heal, aura...) são criadas também para os outros jogadores da sala (o dono da nuvem vai com o shortId de relay). Falta testar
- [x] IA no servidor (a do cliente veio vazia): voo, mira, cadência e armas de cada UnitConfig
- [x] Chefes com fases, encontros e ondas (`enemies.json`), inimigos adormecidos que acordam por proximidade
- [x] Morte de inimigo (`KillUnitsBatch`) e HUD de chefes restantes
- [x] Comandos de teste no chat (`/inimigo`, `/spawn`, `/chefe`, `/onda`, `/catalogo`…)
- [ ] 🟡 Inimigos atravessam muros (só ficam presos à área da sala)
- [ ] 🟡 Rotação visual das torretas (`FIELDMASK_GUNS` `4096`, `Enemies.Guns.cs`, 22/11, sem teste no jogo): o `UpdateUnitsBatch` dos inimigos leva o ângulo alvo de cada arma (índice, relógio de recarga x64, ângulo x32, termina em -1, como o `UnitScript.SyncGuns` lê). Armas "Aimed" seguem o jogador dentro do arco; as de padrão (arco, mangueira, sprinkler) mostram o ângulo do último tiro por 1 s e voltam ao repouso. Só manda quando o ângulo muda mais de 2°
- [x] IA em arquivos (22/11, compilada e testada no launcher e no jogo): `Server\ai\behaviors\*.json` e `Server\ai\brains\*.json` (máquina de estados: cada estado age como um comportamento e tem transições com condições), lidos pelo servidor (`Enemies.Brains.cs`) e relidos ao mudar; o tipo de unidade usa `"ai": "<id>"` (também em `phases`). Vem com 6 IAs e 10 comportamentos de exemplo e tipos `ai_*` no `enemies.json` (`/enemy ai_chaser`, `/brains`). Formato em `Server\ai\README.md`, código em `docs/enemy-ai.md`
- [ ] Comportamento original de cada inimigo: hoje são comportamentos genéricos para 856 unidades armadas; a IA de cada chefe precisa ser recriada a partir de vídeos (Apêndice C)
- [ ] 🟡 Aliados: `ServerSpawnGreenTeamAlly` `23584` chega e só é registrado
- [ ] 🟡 Efeitos de status: `ServerReportPlayerEffect` e `ServerShowInstantEffect` chegam e só são registrados; faltam `ClientCloudEffect` `32571`, `ServerReportCloudEffect` `27911`, `ClientInstantEffect` `58535`
- [ ] 🟡 Efeitos de tela (`ScreenEffects.cs`, opções em `enemies.json` `effects`, 22/11, sem teste no jogo): o `ClientShowDamage` `29463` deste cliente só toca a faísca (`spark`) no ponto, não há número de dano na interface. Mandado ao acertar um inimigo com torreta de jogador (balas só desenhadas); `ClientCameraShake` `502` na morte de chefe e ao limpar a dungeon; `ClientShowUIEffect` `46628` "mission_accomplished" ao limpar a dungeon; `ClientKillBullet` `8267` termina a bala de um jogador na tela dos outros quando ela acerta (`ServerReportBulletHit` com `shouldKillBullet`, id de bala trocado pelo id de relay do dono)
- [ ] 🟡 Skill de um jogador em outro (`TriggerOtherPlayerSkill` `65043`, `Game.SkillRelay.cs`, 22/11, sem teste): a nuvem do jogador A tocou algo na tela do jogador B; B manda o gatilho com o id de relay de A e o servidor repassa a A como `TriggerPlayerSkill` `65038`, que dispara as skills de piloto de A. Precisa de dois PCs para testar

## 5. Itens, inventário e loot

Código em `Server\Items` (v9, ainda não testada no jogo). Tabelas de loot em `loot_tables\` (um arquivo por tabela; `README.md`), opções do host em `items.json`, inventários da conta em `inventory.json`. Comandos de teste: `/itens`.

- [x] Cargo do avião (12 vagas, **compartilhado entre os aviões** como no original: `isUsingSharedCargoForAll`) e armazém da conta (16 vagas), com os itens salvos; vagas extras no `items.json`
- [x] Catálogo de todos os itens: `catalog\item_catalog.md` e `.json` (id, nome, tier, classes que podem usar, stats, texto de lore, valor de reciclagem). O desta entrega vem da build 190911 (1242 itens); o servidor regrava com a 201219 ao subir (`/exportar_itens`)
- [ ] 🟡 Loot ao matar inimigos: sacos de loot (prefab `lootbag`, um item por saco, some em 30 s), `ServerRequestAddInventoryItem` `21818` → `ClientInvAdd` `34491`, `ServerReportItemPickup` `4422`, `ServerRequestKill` do saco. Vale no tutorial e nas dungeons
- [ ] 🟡 Tabelas de loot em arquivo: `normal` (inimigos comuns), `boss` (chefes e baús), `tutorial_walker_chest`. Ordem: tabela da unidade (`units`), loot do jogo da unidade (embutido + `lootTableId` via `replaces`), senão normal/boss. Valores reconstruídos
- [ ] 🟡 Avisos de coleta: `ClientNoRoomForPickup` `22110` (o item volta ao mundo), `ClientConsumedItemOnPickup` `22111` (ingredientes e itens `devourOnPickup`), `ClientCargoFull` `14528` (desequipar com cargo cheio). `ClientReplaceInventoryAndPickup` `16148` escrito, sem uso
- [ ] 🟡 Mover, equipar, descartar e favoritar itens: `ServerInvSwapItems` `17407` (confere o slot do item), `ServerInvRemoveItem` `60901` (soltar = arrastar para o mundo = saco no mundo, sem avião vivo o item fica no cargo; reciclar = o menu do cliente manda Lixo: o item é sempre destruído e paga a moeda de reciclagem se `mulchOnTrash` em `items.json` estiver ligado), `ServerInvAccumulateItem` `60904`, `ServerInvFavoriteItem` `60905`
- [ ] 🟡 Consumir e ordenar itens: `ServerInvConsumeItem` `60902` (o cliente aplica vida/vapor/nuvem; o servidor entrega desbloqueio, variante, moeda, buff de conta e ingrediente, e gasta os usos), `ServerInvApplySort` `60903` (tier ou tipo, ordem estável e `ClientReplaceInventory` depois)
- [ ] 🟡 Respostas de inventário: `ClientUpdateInventoryItem` `37467`, `ClientRemoveInventoryItem` `37468`, `ClientReplaceInventory` `16147` e as respostas de RPC (id de auditoria; `ServerAuditInvAction` `61999` só registrado)
- [ ] 🟡 Mover itens entre o avião e o armazém (mesmo `ServerInvSwapItems`, com o characterId do armazém)
- [ ] 🟡 Ingredientes e reforja na Oficina: `ServerReforgeItemFromIngredients` `12157` com o custo da config `reforging` (itens e moedas), `ClientReforgeSucceeded` `12155`/`ClientReforgeFailed` `12156`, `ClientUpdateIngredients` `61591` (carteira no login e na coleta)
- [ ] 🟡 Reciclagem de itens: lixo dá a moeda da config `mulching` (`newscrap`) pelo tier e pelas tags do item; moedas no PlayerData e no `ClientUpdatePlayer`
- [ ] 🟡 Condição, desgaste, soulbound e supercharged: guardados por item; desgaste na queda (itens salvos), reforja volta a 0, supercarga (tier 9+) sorteia stats de `SuperchargeNewStatProbabilities`; itens com prazo (`decaysInSeconds`) somem no login
- [ ] 🟡 Loot da caixa de suprimentos do tutorial: os quatro motores de tier 1 (a placa manda escolher o motor antes do chefe)
- [ ] 🟡 Consumíveis antes sem lado de servidor (22/11, sem teste no jogo): pinturas e rastros (`deco`, `Planes\PlaneDecoration.cs`: o cliente repinta o avião e o servidor guarda cores, livery e rastros no avião da conta, os escreve no `PlaneRecord`, nas opções do spawn e nas do relay; cores base = `unitColors` + decoração da variante), chaves de dungeon (`dungeon`, `key_*`, `Dungeons\DungeonKeys.cs`: só no Nexus, para dungeon aberta em `dungeons.json`; gasta a chave e manda o jogador para a dungeon, "summon a drop ship") e bônus de XP (`planexpboost`). Se não der para usar (fora do Nexus, dungeon fechada, sem avião) o servidor recusa e o item fica. Na loja, "usar na hora" de tinta/chave deixa o item no inventário para o jogador usar
- [ ] 🟡 Recompensas de carreira do tipo item entregues no inventário (`Items\ItemDelivery.cs`, 22/11, sem teste): vão para o cargo do avião vivo ou o armazém; o que não couber fica em `PendingRewardItems` do piloto e é tentado no próximo login, patente ou pedido de entrega
- [ ] 🟡 Loot com vários jogadores: já é por jogador no código (cada membro da instância sorteia a própria tabela com a própria conta e os sacos só vão para a conexão dele, `Enemies.TakeDamage` → `LootDrops.EnemyKilled`); falta testar com dois PCs

## 6. Rebel City (Nexus) e prédios

- [x] Mapa do Rebel City em `maps\nexus.json`: chão, pistas, cidade em volta e 9 prédios
- [x] Atracar em todos os prédios sem o avião ficar preso
- [x] Flock Hall visível (precisa do plugin 1.18.0+, porque a arte vem desligada no cliente)
- [ ] 🟡 Conferir no jogo: pontas da pista (4.3), cidade em volta do Flock Hall (4.4), Flock Hall sem os letreiros "HANGAR" (plugin 1.19.0); a v5 já carregou no teste de 14/11 03:18
- [ ] Talvez: heliponto com "H" no prédio elevado perto da Store (vídeo, 14:40)
- [ ] 🟡 Lojas e vagas (`store_general`, `store_hangar`, `store_storage`, `store_skill_loadouts`): prateleiras e compra feitas (`docs\stores.md`), servidor conferido com um cliente de teste. **Testes no jogo, nesta ordem:**
  - [ ] 1. Subir o servidor (`Run server.ps1`) e ver no log `[STORE] store config of the game: 294 product(s), 4 store(s)` e a criação do `stores.json`
  - [ ] 2. Entrar no jogo, no chat `/currency gold 20000` e `/store info` (hangar 3, loadouts 3)
  - [ ] 3. Atracar na Store: a Loja Geral abre com banner (Beginner Bundle), seções (Upgrades and Boosts, chaves, emotes, tintas, variantes) e preços em ouro e ferro
  - [ ] 4. Comprar uma chave: ouro cai, item aparece no cargo (ou armazém), mensagem "You purchased" no chat
  - [ ] 5. Sem ouro (`/store reset` não devolve ouro; use `/currency gold -20000`): comprar dá "You can not afford it" e abre a tela vazia "Buy currency" (esperado)
  - [ ] 6. Hangar: abrir a lista de aviões, botão de comprar vaga com o preço certo (750); comprar, a lista ganha uma vaga e o preço seguinte sobe (1750)
  - [ ] 7. Criar aviões até encher o hangar e confirmar que a vaga comprada permite mais um
  - [ ] 8. Pilot School: comprar loadout (500), o novo loadout fica ativo e o preço seguinte sobe
  - [ ] 9. Armazém: botão de comprar espaço; o armazém ganha 6 vagas. Cargo: comprar `store_inventory_slot` na Loja Geral (4 vagas, até 3 vezes)
  - [ ] 10. Beginner Bundle: compra uma vez; a segunda dá "You own it already"
  - [ ] 11. Emote (desbloqueio) e variante de avião com "Use instantly": o desbloqueio aparece sem reiniciar
  - [ ] 12. Tinta (dye) com "Just put into storage" e "Use and put into inventory": o item vai para onde foi escolhido e, com "Use and ...", o avião é pintado na hora (o servidor manda o cliente aplicar a tinta)
  - [ ] 13. Sair e entrar de novo (e trocar Nexus, tutorial, dungeon): vagas e preços seguem iguais (vêm do `accumulatedItems` do welcome)
  - [ ] 14. `stores.json`: `priceMode` `free`, `discountPercent`, `stock`, `maxPerAccount`, `general_sales` com `include`; salvar com o servidor rodando e reabrir a loja. Também pela aba Stores do launcher (compilar com `Build launcher.ps1`)
  - [ ] 14b. Launcher, aba Server > **Store** (compilar com `Build launcher.ps1`): Geral (compras, modo de preço, desconto), Preços e produtos (tabela dos 294 produtos com preço, desconto, estoque, limite e o preço "agora"; edição em lote dos produtos filtrados), Prateleiras (lojas e seções, incluir/excluir com seletor de produtos). Salvar e ver o jogo reagir sem reiniciar o servidor; os comentários do `stores.json` ficam
  - [ ] 14c. Launcher, aba Server > **Accounts**: "Starting money of new accounts" (ouro, ferro...) grava `startingCurrencies` do `items.json`; conta nova recebe o valor. o dinheiro de uma conta que já existe é editado na janela "Edit account..." (aba Currencies and ingredients; servidor parado; cópia em `server\_backup`); o painel "Money of the selected account" foi removido (22/11)
  - [ ] 15. Dois jogadores: cada conta guarda as próprias compras em `accounts\<chave>\store.json`; o estoque (`stock`) é do servidor todo
- [ ] 🟡 Pilot's Pub e Oficina: abrem com lista de quests vazia (ver seção 9)
- [ ] Hangar: troca de avião (seção 2)
- [ ] Pilot School: tutoriais das outras classes (seção 8)
- [ ] 🟡 Flock Hall: flocks feitas no servidor (seção 10); falta abrir o Flock Hall no jogo
- [ ] Combat Sim (seção 11)
- [ ] 🟡 Porta-aviões das missões: abre com lista vazia (seção 9)
- [x] Levar o jogador do Nexus para o mundo aberto (22/11): portal no Nexus (`world.json` > `nexusPortal`, agora depois da nave das missões, u 72 v 9, tipo `Basic`) que troca o servidor com o avião; testado só com um cliente de teste, falta o jogo. Só um mundo (sem lista de mundos/ilhas) Testado no jogo em 22/11.

## 7. Mundo aberto (ilhas)

- [x] Servidor de mundo (terreno `Basic`, papel World; `Server\World\WorldServer*.cs`, porta UDP 1114): welcome, chão, spawn, jogadores no mesmo mundo (se veem pelo `PlayerRelay`), mundo descartado quando o último sai. Testado só com um cliente de teste; falta o jogo Testado no jogo em 22/11.
- [x] Mapa do mundo (`WorldGenerator`, `docs\open-world.md`): o cliente traz a estrutura e as tabelas (elevação, discos, grupos de cânion, peças de chão), mas não o programa que enchia o mapa. Recriado por semente: círculo de raio 650, praia no centro, regiões por elevação, ilhas em discos, cânions (`MakeWideCanyons`), muros em faixas e as peças de chão como o cliente escolhe. Em 22/11 o layout padrão passou a ser as 12 zonas do mapa do próprio jogo (`layout: original` no `world.json`, `WorldZones.cs`) e o tamanho ficou em `sizeScale` 1,3333 (raio 867, 16 mil tiles) e muro invisível na borda feito pelo servidor (`WorldServer.Edge.cs`: o avião que passa de `edgeMargin` é parado e devolvido; não é um muro do cliente). Falta ver no jogo se o desenho parece o original e se o muro da borda incomoda Testado no jogo em 22/11.
- [x] Regiões do mundo (beach, lowlands, midlands, highlands, mountains, highmountains, cada uma com variantes 1 a 3) e nome da sub-região na tela (`ReceiveCurrentSubterritoryName` `62683`, enviado ao mudar de região). Falta a região Palace do anúncio do lançamento e conferir o que o cliente faz com o nome Testado no jogo em 22/11.
- [x] Spawn de inimigos por região (`WorldEnemyPools`, `WorldServer.Sites.cs`): sítios de spawn na terra, grupos (líder e seguidores) ou solitários da pasta de unidades da região, só perto dos jogadores, dormindo até alguém chegar perto, respawn pelo `secondsPerStandardEnemyIn10x10` da região. IA: a padrão (`chase`); nada novo. Falta o nível de ilha (`islandMin`/`islandMax` só existe nos chefes), densidade conferida no jogo e a composição original dos grupos Testado no jogo em 22/11.
- [ ] Chefes de setpiece, chefes de evento e setpieces que aparecem (IslandConfig, EventBossesConfig, PopupSetpieceConfig; os dados de `islandQuestEnemies` e `events` vêm no `configBinary`, a configuração `island` vem vazia)
- [ ] Quests de ilha (`ClientReceiveIslandQuestNotification` `54199`)
- [x] Porta-aviões de dungeon que aparecem no mundo (`WorldServer.Carriers.cs`): uma dungeon aberta do `dungeons.json` por vez, até `maxAtOnce`, a cada `everySeconds`, por `lifetimeSeconds`, com contagem regressiva; entrar leva à dungeon (`ServerSendMeThroughPortal`) e o aviso no chat diz a direção. Falta `ClientReceivePortalUpdate` `35053` (jogadores em cada porta-aviões) e a regra original de quando e onde aparecem Testado no jogo em 22/11.
- [ ] 🟡 Nascimento com zona segura (`spawn.safeZoneRadius`: sem inimigos nem muros em volta). Teleporte para casa/local: `ServerNotifyCompleteTeleport` `12006` não tem tratamento (o cliente se teleporta sozinho); o tempo do "Voltar para Rebel City" do menu é do cliente (`teleportNexusDelay`): o plugin (1.23.0) sobe para no mínimo 3 s (`NexusTeleportDelaySeconds` no BepInEx cfg)
- [ ] Eventos agendados (UGUIUpcomingEventsList)
- [ ] 🟡 Aba Server > World map do launcher (22/11): mapa por zona com jogadores e porta-aviões e dados ao passar o mouse, testado no jogo. Novo em 22/11 (compilado e testado só com um cliente de teste e uma tela renderizada por programa; falta o jogo): abas Map e World settings (edita o `world.json`: ligar, nome, máximo de jogadores, forma de geração, tamanho, semente, montar antes, muro da borda), botão Reroll world (grava uma semente nova) e mundo montado antes do primeiro jogador (`pregenerate`). Falta editar no launcher as chaves aninhadas (inimigos, porta-aviões, ponto de nascimento) e o mundo na lista de servidores/portas do launcher

## 8. Dungeons e tutoriais

Doc das dungeons recriadas: `docs/dungeons-190828.md`. Servidor de dungeon: `docs/dungeon-server.md` e `docs/dungeons-190828.md`.

- [x] Tutorial do walker (Training Grounds) refeito pelo vídeo do Open Beta, com roteiro, inimigos, chefe, placas e portal de saída para o Nexus
- [x] Mapa do tutorial em `maps\tutorial_walker.json`, relido quando o arquivo muda
- [x] Dungeon concluída marcada na conta (`SendMarkDungeonFinished`; `/reset_tutorial` desfaz)
- [x] **Achado conferido (15/11):** os 21 arquivos `Dungeons/dungeon_*` da 190828 (e os da 190426) estão vazios (`{}`). O desenho das salas não está em nenhuma build; o resto (unidades, placas, falas, quests, território) está
- [ ] 🟡 Conferir no jogo que o tutorial v5 (mapa em arquivo) ficou igual ao 4.4
- [ ] 🟡 17 dungeons da 190828 recriadas em `maps\dungeon_*.json` (Bomb Factory, Laser Playground, The Arena, Home for Special Children, Crypt of Cathulhu, Reeducation Camp, Frozen Owl Monastery, Venom Compound, The Vaults, Super Secret Base, Temple of Bast, Iron Lair, The Elder Arena, The Dig, Command Center, Three Brothers, Inner Sanctum), com salas, 433 tipos de inimigo, encontros, minichefes, chefes com fases e falas, placas e portões. Gerador em `ferramentas\dungeons-190828`. O formato das salas é recriado; falta rodar no jogo
- [ ] 🟡 Servidor de dungeon (v8, porta 1113, `Dungeons\DungeonServer.cs`): welcome com o terreno da dungeon, chão do mapa, salas que acordam quando o jogador entra, portões (barricadas, portas por `abreQuando`, portas que fecham atrás na sala do chefe), ondas, `aoMorrer`/`sequencia`, falas, fases com escudo e efeito visual (EFFECTSTATE1), `invocacaoPeriodica`, pontos de controle, porta-aviões de saída e marca de dungeon terminada; comandos `/dungeons`, `/dungeon`, `/sala`, `/chefe`, `/recomecar`, `/sair`. Falta testar no jogo (Iron Lair e Bomb Factory primeiro) e `proximaDungeon` (Meowza → Three Brothers → Inner Sanctum)
- [x] Teste de 15/11 (Iron Lair e Bomb Factory): entrar pelo porta-aviões, welcome, chão, salas acordando, ponto de controle, portão fechando atrás do jogador na Iron Lair, morte e volta ao Nexus funcionaram, sem erro no servidor nem no jogo. Achado: na primeira sala da Bomb Factory um atirador invisível (`bombbarrelboom`) atirava um anel no jogador de longe. Corrigido: o barril visível leva o atirador no `aoMorrer` (atira uma vez, 1,5 s, `naoContaNaSala`); a mesma correção vale para o `firebarrel` do Reeducation Camp. Falta retestar no jogo. A dificuldade (vida e dano dos tiers) fica como está, até existirem itens que melhorem o avião
- [ ] 🟡 Falas de fase dos chefes (v11): a chave de cada fase vinha em `fala`, e o servidor ignorava em silêncio. Agora é `bark` (`dungeon_finalboss.json`, quatro fases do meowza) e sai como balão de fala pelo mesmo caminho das falas das dungeons (`WorldObjects.SendSpeech`, em `Enemies.Messages.cs`). Mudança de comportamento: o chefe passa a falar. Falta testar no jogo
- [ ] 🟡 Emboscada do ladrão (`ambush` do `dungeon_thief.json`, chave renomeada para `group` e `intervalSeconds`): o servidor ainda não lê essa chave
- [ ] 🟡 Loot das dungeons: tabelas `loot_tables\dungeon_*.json` (baús, Big Treasure, minichefes; muros/barris/bombas/minas sem loot). Falta conferir no jogo
- [ ] 🟡 Inimigos sem arma no cliente (bombas teleguiadas, jatos bombardeiros): `comportamento: "kamikaze"` explode em anel ao encostar; carregadores/dirigíveis largam bombas (`contaNaSala: false`); minas-robô e outros que orbitavam além do alcance da arma agora se aproximam. Falta conferir no jogo (Bomb Factory) e as demais dungeons
- [ ] Conferir as outras unidades invisíveis armadas das dungeons ainda fechadas (lista em `docs/dungeons-190828.md`)
- [ ] 🟡 **Correções do teste de 22/11 (compiladas, falta testar no jogo; detalhes em `docs/dungeon-tuning.md`):**
  - Todas as 17 dungeons ficaram 4× menores (metade da largura e da altura), por `tools\scale_dungeon_maps.py`; os mapas ganharam `"scaled": 0.5`. Posições, salas, portões, pontos de controle e placas foram junto; conferido por script (tudo ligado, nada fora do chão)
  - Sala do chefe não tranca mais o jogador: `lockBossRooms` (padrão `false`) no `dungeons.json`
  - Tiros muito rápidos (Iron Lair, Slicer, Zombie...): a unidade soltava todas as armas do modelo ao mesmo tempo. Agora há `maxBulletsPerSecond` (padrão 10) e `fireRateScale` no `dungeons.json` (também na tela "Maps and dungeons" do launcher); Slicer ficou com 2 das 4 armas e o SN-Z com `fireRateMultiplier` 0,7
  - Bomb Factory: reação em cadeia (`chainReaction` nos barris e bombas: uma explode e as vizinhas, num raio de 13 u, explodem em seguida)
  - Laser Playground: `trapbutton` (controlador invisível) não é mais criado (regra geral: unidade invisível sem arma não nasce); Bullet Belt com alcance de tiro maior. A arte minúscula do Bullet Belt e o fato de as balas dele viajarem só ~3 u são do cliente e não dá para mudar daqui
  - The Arena: inimigos de onda e invocados vão atrás do jogador desde o nascimento (antes só dentro de 28 u); Malicious Acolyte mira à frente (`aimLead` 1); Commodus com alcance de tiro 45 e agro 120
  - Home for Special Children: Electric Belt (1 de vida, sem raio, não dá para acertar) virou perigo invulnerável que não conta para limpar a sala; Edmund No. 6 com metade da cadência
  - Sala que "não abre o portão": o servidor avisa no chat quais inimigos ainda seguram a sala quando sobram até 3
  - Falta resolver: nomes ausentes de Slicer e Bouncer no jogo (o texto existe na tabela do cliente; causa não achada no servidor, precisa de um teste com o log do jogo) e os discos pequenos sem nome do Laser Playground
- [ ] 🟡 Mission Transport (22/11, compilado, falta testar): o porta-aviões gigante no fim da rampa lista as dungeons abertas como missões (`ServerListMissions` `12000`, `ServerMissionJoin` `12001`, `Dungeons\MissionTransport.cs`); escolher uma leva à dungeon. `entryMode` no `dungeons.json` (`both` padrão, `portals`, `missionTransport`), na tela "Maps and dungeons". O cliente esconde missões de tier muito acima do avião quando há missões do nível dele (regra do jogo)
- [ ] 🟡 Colisão no Nexus (22/11, compilado, falta testar): seção `collisions` do mapa (retângulos em u/v viram sub-células de muro de 3 u no `ClientReceiveTerrainTiles`); o `nexus.json` tem os fundos da cidade, morros, torres, bunker e adereços entre os prédios. Os prédios continuam sem colisão para dar para atracar. Risco: a arte do muro vem do território do Nexus (se não tiver arte aparece só o erro "No walls found" no log do jogo)
- [ ] 🟡 ClientMapInfo dos terrenos de dungeon: o servidor junta as placas do mapa ao ClientMapInfo do cliente (ou monta um novo quando o cliente não tem). Falta conferir no jogo
- [ ] Mecânicas especiais: escolta da broca (The Dig), chave e altar de Horus (The Vaults), salas de altar com maldições (Temple of Bast), emboscada (Super Secret Base), esteiras (Venom Compound), trilhos das enfermeiras (Home for Special Children), respiros de calor (`trapmaster`)
- [ ] Outros terrenos de tutorial: `DungeonTutorial` (olfaithful), `DungeonTutorial2`, `DungeonTutorialWalker2`
- [ ] `DungeonOrbitroid` (só existe na 201219; não está na 190828)
- [ ] 🟡 Recompensas no fim da dungeon: o Big Treasure já funciona como baú de DPS (`bauDeDps`: quebrado a tempo vira o baú de recompensa); o loot dos baús abertos sai pela tabela `boss` (v9, `replaces` `*treasure*`). Falta conferir no jogo
- [ ] Modificadores de dungeon (dungeon mods do lançamento; os buffs `dungeonmod_*` estão no configBinary)
- [ ] 🟡 Entrada em dungeon pelo servidor: porta-aviões de dungeon no Nexus (um por dungeon aberta no `dungeons.json`) e `/dungeon NOME`. Falta usar as chaves `key_*` (o mapa diz `chave` e `itemDoPortal`)

## 9. Quests, missões e conversas

- [ ] 🟡 Quest givers (Pub `tavern`, Oficina `crafter`): respondem lista vazia
- [ ] Aceitar, conferir e entregar quests (`54188`–`54199`)
- [ ] Quest ativa e lista de quests (`ServerSetPlayerActiveQuest` `54190`, `ClientReceivePlayerQuests` `54090`, `ClientReceivePlayerActiveQuest` `54091`)
- [ ] Recompensas de quest (`ServerRequestQuestRewards` `54197` / `ClientReceiveQuestRewards` `54198`)
- [ ] Diálogos de quest e seta de guia (`ClientShowQuestDialogueOnEvent` `7334`, `ClientChooseGuidanceTarget` `54088`, `ServerRequestQuestGoalSourceInfo` `12007`)
- [ ] 🟡 Missões do porta-aviões: `ServerListMissions` responde vazio; faltam `ServerMissionJoin` `12001`, `ClientMissionsChanged` `12002`, `ClientMissionsPickFollowUpMission` `12003` e a agenda (MissionScheduleConfig)
- [ ] Squads de missão (`13500`–`13505`)
- [ ] 🟡 Conversas: `ServerReportConversationStart`/`End` chegam (ex.: `tut_00`); faltam `ClientStartConversation` `27100` e `ServerRequestConversation` `27101`
- [ ] Falas em lote (`ClientShowBatchSpeech` `7333`)

## 10. Social: chat, amigos, flocks e troca

- [x] Mensagens do servidor no chat (`ClientReceiveText`) e falas de NPC (`ClientShowUnitChat`)
- [x] Comandos `/` desconhecidos do cliente chegam ao servidor (`SlashCommand`)
- [ ] 🟡 Chat entre jogadores: proximidade (`11732` → `1612`, repassado pelo `PlayerRelay`, agora com a flock no nome; falta testar); sussurro (`11731`/`11730`) e chat da flock (`11733`/`11734`) feitos em `ServerSocialSocialHandler.Chat.cs`, testados com cliente de teste, falta testar no jogo
- [ ] 🟡 Amigos: lista salva por conta (`accounts<chave>social.json`), adicionar/remover/bloquear/silenciar (`12201`/`12202`), dados de amigos em outro servidor ou offline, `/friends` e `/addfriend NOME`; testado com cliente de teste, falta testar no jogo
- [ ] 🟡 Flocks (guildas): criar (gasta o token de fundador, configurável), entrar, sair, convites, cargos, expulsar, banir, silenciar, buscar, sugestão (`30000`–`30016`), flock padrão (`48916`), salvas em `flocks.json`; testado com cliente de teste, falta testar no Flock Hall do jogo
- [ ] Troca segura entre jogadores (`12500`–`12508`)
- [ ] Barracas de venda (`RequestOpenStall` `60871` / `RequestCloseStall` `22615`)
- [ ] Espectar e seguir jogadores (`ServerSpectate` `17500`, viewpoints `3276`, `3761`, `14265`, `34455`)
- [ ] Códigos de resgate (`ServerRedeemCode` `30006`); dá para virar códigos criados pelo host
- [ ] Denúncia de abuso (AbuseReporter; pode gravar num arquivo do host)

## 11. Combat Sim

- [ ] Servidor de Combat Sim (papel CombatSim) com modos, variantes, locais e builds (CombatSimConfig e afins)
- [ ] Entrar e começar (`ServerCombatSimStartAndJoin` `602`, `ClientReceiveCombatSimJoinFail` `603`)
- [ ] Recompensas imediatas, por período e "vagas" (`601`, `606`–`609`); o `607` já chega no login
- [ ] Ranking (`604`/`605` e `/leaderboards/*` no HTTP)
- [ ] Aviso de Combat Sim da flock (`ClientCombatSimGuildStartMessaage` `11736`)

## 12. Troca de servidores

- [x] Nexus ↔ tutorial levando o mesmo avião (`ClientSwitchToServer`, `ClientSwitchToServerWithPlane`, `ClientPortalResult`), agora com XP e nível
- [ ] 🟡 Nexus → dungeon e volta com o mesmo avião (porta-aviões, `/dungeon`, porta-aviões de saída, `/sair`); falta testar no jogo. Nexus → mundo e Combat Sim ainda não
- [ ] Vários Nexus e troca entre eles no jogo (`ServerSendMeToOtherNexus` `28738`). A lista que o jogo mostra vem de `procs` no `/user/login` e `/user/self`; hoje vai o Nexus do próprio servidor (no endereço que o jogo usou). É por aqui que a lista do jogo pode espelhar a do launcher
- [ ] 🟡 Ir para o servidor de outro jogador (`ServerSendMeToPlayerServer` `43983` / `ClientSendMeToPlayerServerFailed` `46627`): Nexus ↔ mundo aberto e no mesmo servidor, o avião nasce ao lado do amigo; testado com cliente de teste, falta testar no jogo (`docs/social.md`)
- [ ] Contas entre hosts diferentes: cada host guarda as próprias contas, então trocar de PC precisa de uma regra (conta por host ou transferência)

## 13. Áudio, interface e extras

- [ ] Sons mandados pelo servidor (`ClientPlayAudio` `7335`)
- [ ] 🟡 Música: o plugin de diagnóstico silencia o PlaylistController; para jogar de verdade a música deve tocar
- [ ] Moedas e compras: os pacotes de moeda eram microtransações da Steam (`54254`, `54297`–`54300`, `54390`, `54391`); trocar por moeda do jogo ou por algo configurável no host
- [ ] Ajustes de debug e admin (`3732`, `6848`, `15630`, `27395`, `30792`, `49275`, `58246`, `33683`, `19540`, `38607`)
- [ ] Captura de tela e auditoria (`ClientCaptureScreen` `23010`, `ClientOnAuditChange` `23009`, `ServerAuditInvAction` `61999`)
- [ ] Estatísticas do servidor (`ClientReceiveServerStats` `59268`, `ServerQuery` `45861`)

## 14. Código e repositório

- [x] Repositório privado no GitHub (`Vithor14/Steambirds-Alliance-Revive`) com script de envio
- [x] Servidor dividido por partes (conta, mapa, tutorial, inimigos, Nexus, chão, mensagens…)
- [x] Nomes de arquivos, classes, variáveis, comentários, logs e README em inglês (22/11). Servidor, launcher, `GameFiles`, scripts `.ps1`, READMEs (`Chat commands.md`, `Server\maps\README*.md`, `Server\loot_tables\README.md`) e comentários dos `.json` de configuração estão em inglês e compilam. O plugin (`QuieterPlugin` 1.22.0) também: seções e chaves do `.cfg` do BepInEx (`Diagnostics`, `Fixes`, `ExportUnits`…), palavras dos arquivos que ele grava (`unit`, `gun`, `cloud`, `maneuver`, `part`; o servidor e o launcher só leem as palavras em inglês, então os `.txt` de plugins antigos precisam ser regravados) e todas as linhas de log (`buildings_log.txt`, `terrain_dump.txt` e `ascii_maps.txt` no lugar dos nomes em português). O `.cfg` do plugin antigo fica sem uso e o novo nasce com os padrões; rodar `Build plugin.ps1` e `SteambirdsGameFiles.exe` de novo para regravar os `.txt`. Continuam em português de propósito: `docs\` (snapshots), `Launcher\LEIA-ME.md` e `Launcher\Editor\LEIA-ME.md` (versão em português dos READMEs), `Launcher\lang\pt-BR.json`
- [x] Campos do formato de mapa em inglês (`floor`, `decor`, `buildings`…), com o formato `steambirds-map`. A camada de compatibilidade com os nomes em português foi removida em 22/11 (`Server\Compat\`, `legacy-names.json`, a migração de pastas antigas, a conta única antiga e as palavras `unidade`/`arma`/`nuvem`/`manobra`/`parte`): mapas, configs e contas antigos não abrem mais. Os 17 mapas de dungeon foram regerados pelo gerador com os nomes novos e conferidos. Em 17/11, `fala` (falas de fase dos chefes, agora `bark`) e `ambush` do `dungeon_thief.json` (agora `group` e `intervalSeconds`) também foram trocados, nos mapas e no gerador `dungeons_190828.py`
- [x] Dividir arquivos grandes. `Enemies.cs` (era `InimigosEsboco.cs`, 118 KB) virou 7 arquivos `partial` por tema (`Enemies.Scripts`, `Encounters`, `Ai`, `Messages`, `Files`, `Util`); `Game.cs` (60 KB) virou 6 (`Actions`, `Players`, `Rpc`, `Shots`, `Units`); `Tutorial.cs` (61 KB) virou 6 (`Commands`, `Helpers`, `Instructor`, `Script`, `Session`); `DungeonServer.cs` (57 KB) virou 6 (`Commands`, `Helpers`, `Session`, `Tick`, `Units`); `Items\InventoryHandler.cs` (45 KB) virou 5 (`.Moves`, `.Use`, `.Commands`, `.Send`); e, em 22/11, `Program.cs` (54 KB, código de topo de arquivo) virou a classe `Program` com `Main` em 5 arquivos `partial` (`Program.cs`: constantes, estado e `Main`; `Program.Transfers.cs`: troca de servidor e nascimento do avião; `Program.Messages.cs`: mensagens do cliente; `Program.Udp.cs`: laço UDP; `Program.Http.cs`: login e info do servidor). Nenhum arquivo do servidor passa de 40 KB; os maiores agora são `Progression\PilotProgression.cs` (39 KB), `Enemies.cs` (36 KB) e `Items\LootTables.cs` (33 KB)
- [x] README de instalação e de contribuição para o GitHub (`README.md`, `CONTRIBUTING.md`)
- [x] Servidor como executável publicado (sem `dotnet run`): o `Build launcher.ps1` publica o `SteambirdsServer.exe` self-contained em `Launcher\dist\server`, com mapas, dados e configs. Rodou pelo launcher em 15/11
- [x] Release do servidor para o GitHub (20/11, testado em 22/11): `Build server release.ps1` publica servidor, `SteambirdsGameFiles.exe` e plugin e monta `Release\SteambirdsServer-<versão>-win-x64.zip` com `Start server.cmd`, README (`Packaging\server`), configs, `maps\` e `loot_tables\`; confere que não entrou arquivo do jogo, conta ou log antes de zipar. Pastas de dados novas do servidor só entram se forem adicionadas em `$releaseFolders` no script
- [ ] 🟡 Arquivos do jogo fora do GitHub (20/11): `GameFiles\` (`SteambirdsGameFiles.exe`, também no `Launcher\dist`) pega da cópia do jogo de cada jogador o `configBinary`, os `.txt` do plugin e as imagens do editor (ver seção 18). Compilado e testado em 22/11. Atenção: o `Launcher\dist` montado pelo `Build launcher.ps1` ainda copia o `configBinary` e o `server\data` do PC de desenvolvimento, então o zip do launcher não deve ir para o GitHub assim

## 15. Launcher base — aba Client (objetivo intermediário)

- [x] Scripts PowerShell que sobem o servidor e abrem o jogo (`Run server.ps1`, `Run game.ps1`)
- [x] Programa de launcher com abas Client, Server e Settings: `Launcher\` (WinForms, .NET 8, `Core\` e `UI\`), montado pelo `Build launcher.ps1` em `Launcher\dist`. Tudo fica na pasta do exe (`launcher.json`, `lang\`, `logs\`, `server\`, `plugin\`). Console do cliente com o `LogOutput.log` e o `Player.log`. Doc em `Launcher\README.md`. A 0.1.0 compilou e rodou no PC em 15/11
- [x] Aba Settings (0.2.0, testada em 22/11): tema claro/escuro, pasta do jogo e os botões de abrir pastas, logs e arquivos (jogo, BepInEx, servidor, mapas, backups, configs)
- [x] Idiomas (0.2.0, testado em 22/11): launcher em inglês por padrão, português em `lang\pt-BR.json`, pasta `lang` com `_template.json` e `README.md` para a comunidade adicionar idiomas (`<código>.missing.json` lista o que falta traduzir); idioma do jogo escolhido no launcher (lista do `Texts/locales` do jogo, passado com `-locale`)
- [x] Escolher o nome do jogador (0.3.0, testado em 22/11): padrão = nome da conta Steam (`loginusers.vdf`); vai no caminho do `OZY_URL` (`/n/<hex>/ozy`) e o servidor usa no login. A conta continua sendo a da Steam
- [x] Lista de servidores na rede local (0.3.0, testada em 22/11): pergunta UDP na porta 6444 por broadcast, para a tabela ARP e para cada endereço de redes pequenas; o servidor responde com nome, jogadores e porta (`Network\LanDiscovery.cs`). Busca sozinha a cada minuto
- [x] Lista de servidores em VPN (Radmin, Hamachi, ZeroTier...) e Conectar por IP (0.3.0, testados em 22/11): broadcast e tabela ARP de cada adaptador de VPN; IP manual conferido em `/revive/info` e guardado na lista
- [x] Botão de abrir o jogo já apontando para o servidor escolhido: este PC com `-connecttolocal` (e `-connecttolocalport` se o Nexus mudar de porta), sobe o servidor antes se precisar; outro PC sem `-connecttolocal` (o login devolve o Nexus dele) e com "Iniciar o servidor deste PC" desmarcado e acinzentado. Testado neste PC e em outro PC (22/11)
- [ ] Lista de servidores dentro do jogo igual à do launcher, com troca entre eles (seção 12)

## 16. Launcher — aba Server

- [x] Hospedar o servidor pelo launcher: aba Servidor com iniciar, parar e reiniciar, console colorido com filtro, log por sessão (`logs\server-<date>.log`) e o `server-report.txt` ao parar (como o `Run server.ps1`). Jogadores de outros PCs entram desde a 0.3.0 (testado em 22/11)
- [x] Contas salvas no PC do host, uma por jogador (`server\accounts`, v11, testado em 22/11)
- [ ] 🟡 Configurações: telas Progressão, Itens e loot e Inimigos e dificuldade, montadas a partir do `progression.json`, `items.json` e `enemies.json` (cada chave vira um campo e o comentário vira a explicação; salvar mantém os comentários; o servidor relê sozinho). Dificuldade: `multiplicadorGlobalDeVida`, `dungeonDifficulty` e o multiplicador de vida de cada dungeon
- [x] Rede e multiplayer (0.3.0, testada em 22/11): endereços para passar aos outros jogadores (um por rede, com botão de copiar), regra do Firewall do Windows (cria com permissão de administrador) e o `network.json` (nome, outros PCs, busca, máximo de jogadores, comandos de chat, mensagem de boas-vindas e as portas HTTP, Nexus, tutorial, dungeon e busca)
- [ ] 🟡 Aba de contas: estado inicial de contas novas editável (patente; desbloqueios em checklist com as classes do jogo; um campo por moeda do jogo; ingredientes e itens no armazém)
- [x] Ver, editar e apagar contas (testado em 22/11): lista com uma linha por jogador (nome, Steam ID, último jogo, patente, aviões...) e os aviões da selecionada (0.3.0); janela de edição (piloto e patente, aviões com variante/nível/XP, desbloqueios, dungeons terminadas, moedas e ingredientes) e remover a conta, com cópia em `server\_backup`. Só com o servidor do launcher parado
- [ ] 🟡 Aba Store do launcher (22/11): configura tudo do `stores.json` por tela (compras, modo de preço grátis/moeda, desconto geral, desconto/estoque/limite por produto com os preços do jogo, lojas e seções, incluir/excluir). Os preços só podem baixar (o cliente desenha o preço): o desconto de 0 a 100% é o ajuste. Falta testar
- [ ] 🟡 Dinheiro nas contas (22/11): na aba Accounts, dinheiro inicial das contas novas (`startingCurrencies`) e dinheiro de uma conta que já existe, que fica só na janela "Edit account..." (`inventory.json`, só com o servidor parado; o painel do canto da lista foi removido por ser redundante). Falta testar
- [ ] 🟡 Retrato por conta (22/11, compilado, falta testar): coluna Portrait na lista de contas, aba Portrait na janela de edição e botão "Reroll portrait" (sorteia com as regras do jogo a partir de `server\data\portraits_config.txt`; só com o servidor parado, cópia em `server\_backup`). O launcher mostra as peças por nome e id, não o desenho: as peças são prefabs da Unity, e desenhá-las exigiria o plugin exportar as imagens (a fazer)
- [ ] 🟡 Console do servidor por nível (22/11, compilado, falta testar): o que os jogadores fazem (`[SKILL]`, `[SHOT]`, `[MSG]`, `[XP]`, `[SPAWN]`... em `Core\ServerLogLevel.cs`) é "info" e fica escondido; o padrão mostra avisos, erros e eventos do servidor (quem conecta e sai, dungeons, portais, mortes). Caixa "Show player actions (info)" liga tudo; o `logs\server-<data>.log` e o `server-report.txt` continuam completos
- [ ] Escolher o arquivo de mapa do Nexus e do tutorial (no launcher, acinzentado: o servidor usa `nexus` e `tutorial_walker` fixos)
- [ ] 🟡 Escolher o arquivo de mapa de cada dungeon: tela "Mapas e dungeons" do launcher (grava no `dungeons.json`). Falta testar
- [ ] Ver cada mapa na tela do servidor (no launcher, acinzentado)
- [ ] 🟡 Definir quando cada dungeon fica disponível: "Ativa", "No Nexus", "Vida ×" e "Abre em" na tela "Mapas e dungeons". Falta testar
- [x] Mapa do mundo com a posição de cada jogador (22/11, aba World map do launcher; dados ao passar o mouse; falta testar com o servidor e o jogo rodando) Testado no jogo em 22/11.

## 17. Launcher standalone (objetivo final)

- [ ] 🟡 Detectar a Steam e o login: o launcher acha a pasta da Steam pelo registro, avisa se ela está fechada e mostra a conta logada (`ActiveProcess\ActiveUser` e `loginusers.vdf`: nome e Steam ID)
- [x] Abrir o console da Steam com o comando pronto (0.3.0, testado em 22/11): botão "Baixar o jogo pela Steam" copia `download_depot 386010 386011 58813776496064724` e abre `steam://open/console`; o jogador cola e aperta Enter (a Steam não deixa mandar o comando de fora)
- [ ] 🟡 Janela de primeira execução (22/11, compilada e aberta no PC, falta o fluxo completo): se o `Steambirds.exe` não está na pasta do jogo, o launcher abre antes da janela principal uma janela com "Download the game with Steam" (o mesmo fluxo da aba Client) ou "Choose the folder where the game is", avisando que a build certa é a 201219.1413 do console da Steam e que a última versão do jogo tem todo o conteúdo removido; "Continue without the game" abre o launcher assim mesmo. `UI\GameSetupForm.cs`
- [x] Aba Client explica o plugin Quieter (o que corrige e o que se perde sem ele) em texto fixo e em dica (22/11)
- [x] Detectar quando o download termina (0.3.0, testado em 22/11): linhas `Depot download complete` / `failed` do `logs\console_log.txt` da Steam e o tamanho da pasta `depot_386011`; no fim ela vira a pasta do jogo
- [ ] 🟡 Colocar os arquivos necessários para o jogo logar via Steam: o launcher cria o `steam_appid.txt` (386010) na pasta do jogo (0.3.0), mostra a build e instala o plugin no `BepInEx\plugins`. O BepInEx não vem com o launcher, mas o `SteambirdsGameFiles.exe` (20/11) baixa o BepInEx 5.4.23.5 oficial do GitHub (64 ou 32 bits pelo `Steambirds.exe`) e instala junto com o plugin (testado em 22/11). Falta chamar pelo launcher
- [ ] 🟡 Executar o jogo (botão "Jogar" do launcher)

## 18. Editor de mapas

- [x] Formato de mapa em arquivo (JSON v1): chão desenhado, peças, enfeites, prédios, pontos, áreas, portões, placas (`docs/map-format.md`)
- [x] Servidor relê o mapa ao mudar, valida os nomes que o roteiro usa e mantém a última versão boa se o arquivo tiver erro
- [x] Editor visual com os assets do jogo (aba Map editor do launcher, `Launcher\Editor`): chão, muros, entrada, saída, pontos de controle, portões, salas com inimigos e ondas, tipos de unidade, pontos, áreas, linhas, rotas, placas, enfeites e prédios, com desfazer/refazer e tema claro/escuro. Prévias em geometria e cor dos vértices (sem as texturas do jogo); a arte de várias unidades ainda cai em letra. Escrito em 16/11, compilado e testado em 22/11
- [x] Imagens do editor tiradas do próprio jogo (20/11, testado em 22/11): no modo de exportação (`STEAMBIRDS_EXPORT`, aberto pelo `SteambirdsGameFiles.exe`) o plugin desenha de cima cada peça do `GroundAssetContainer` e cada arte de unidade (`UnitConfig.art`, pool `Planes`) com as malhas, materiais e luz do jogo, grava `ground\*.png`, `units\*.png` e `thumbs.json` (mesmo formato do editor) e fecha o jogo. Substitui o `tools\editor` (que dependia do AssetRipper). Texturas, tamanho (`half`) e orientação conferidos com o jogo
- [ ] 🟡 Editor (22/11, testado com um programa de teste que abre as telas reais e confere abas, opções, rotação e desenho; usado com o mouse em 22/11): decoração e prédios giram pelo `rot`, as peças de `floor.pieces` passam a ser desenhadas com a rotação do arquivo (anti-horária, relativa ao tile) e os inimigos já giravam; abas da direita mostram só as do objeto selecionado e se selecionam sozinhas (clicar num ponto vazio traz todas); opções da esquerda só as da ferramenta ativa; dicas em todas as opções e campos. A rotação da decoração (`rot` + 45° no canvas, como as unidades) não foi conferida contra o jogo
- [x] Exportar arquivos que o servidor usa (testado em 22/11): salvar confere o mapa (entrada, saída, chão, referências) e grava em `server\maps`; com erros só salva como rascunho (`"rascunho": true`)
- [x] Dungeons: o editor abre uma cópia de cada uma das 17 da 190828 (também geradas por script em `ferramentas\dungeons-190828`, com prévia em imagem). Testado em 22/11
- [ ] Mapa global (mundo aberto)
- [ ] 🟡 Inimigos e encontros por mapa: o servidor de dungeon lê `tipos` e `encontros` de cada mapa (os tipos entram com o prefixo do mapa); no Nexus e no tutorial ainda vale o `enemies.json`
- [ ] Roteiros de dungeon em arquivo (hoje o roteiro do tutorial está em código)
- [ ] Coleção de versões de dungeons da comunidade para o host escolher
- [ ] 🟡 Editor de IA e comportamentos dos inimigos (22/11): aba **Enemy AI** do launcher (`Launcher\EnemyAi`) com lista de arquivos, editor de estados, transições e condições, comportamentos reutilizáveis, validação, novo/duplicar/renomear/apagar (vai para `_backup`), importar e exportar (um arquivo com a IA e os comportamentos que ela usa) e **simulador** (arte, velocidade, giro e armas de qualquer unidade, jogador que segue cliques, gira ou fica parado, multiplicadores do tipo, perda de vida). O simulador roda o mesmo código do servidor (`Shared\EnemyAi`). Ligado ao editor de mapas (campo `ai` em Units > Enemy AI). Testado no launcher real em 22/11. Falta: desfazer/refazer, invocar e escudo dentro da IA, muros e salas no simulador

---

## Sugestão de ordem para o MVP

0. (Feito em 22/11) Compilar o launcher 0.3.0 e o servidor v11 (`Build launcher.ps1`) e testar sozinho: a sua conta Steam é criada em `server\accounts\steam_...` (a conta única antiga não é mais migrada), o nome do jogador aparece no log (`[LOGIN]`), o tutorial e o Nexus continuam iguais.
1. (Feito em 22/11) Testar com um segundo PC (Radmin ou rede local): busca de servidores, Conectar por IP, firewall, duas contas separadas, trocas Nexus ↔ tutorial ↔ dungeon no PC de fora.
2. Multiplayer de verdade: `PlayerRelay` (avião, tiros, chat) testado com dois PCs em 22/11; falta dos outros, inimigos e loot compartilhados. Quase todo o resto depende disso.
3. Testar a v9: loot no tutorial (drones, caixa com os motores, chefe), equipar o motor, mover para o armazém, reciclar, `/item` e `/loot`.
4. Testar a v7 no jogo: troca de avião no hangar, patente em tempo real, morte, skills e loadouts.
5. Testar o servidor de dungeon (v8) com Iron Lair e Bomb Factory, depois ligar as outras no `dungeons.json` uma por uma.
6. (Feito e testado no jogo em 22/11) Mundo aberto (`Basic`) com spawns por região e porta-aviões de dungeon.
7. Lojas: feitas, falta testar no jogo (seção 4, "Lojas e vagas"). Depois quests e missões, conferindo antes se o `configBinary` traz os dados (os mapas de dungeon e a IA vieram vazios em todas as builds; o das lojas veio completo).

## Apêndice A — Classes (15 na tabela do servidor)

Testadas no jogo (criação, spawn e itens iniciais; 22/11):
- [x] walker
- [x] assault
- [x] medic
- [x] rapier
- [x] tank
- [x] olfaithful
- [x] duster
- [x] support
- [x] paladin
- [x] wizard
- [x] engineer
- [x] pirate
- [x] siegetank
- [x] daggerknight
- [x] vampire

## Apêndice B — Terrenos que o cliente conhece (mapinfo)

- [x] Nexus (Rebel City)
- [x] DungeonTutorialWalker (Training Grounds)
- [x] Basic (mundo aberto): servido e testado no jogo em 22/11 (`ServerWorld`)
- [ ] DungeonTutorial
- [ ] DungeonTutorial2
- [ ] DungeonTutorialWalker2
- [ ] 🟡 Dungeon3Brothers (mapa recriado: `dungeon_3brothers.json`)
- [ ] DungeonOrbitroid
- [ ] 🟡 DungeonFinalBoss (mapa recriado: `dungeon_finalboss.json`)
- [ ] 🟡 DungeonFinalBoss2 (mapa recriado: `dungeon_finalboss2.json`)

## Apêndice C — Chefes no `unidades_config.txt` (44)

IA original de cada um a recriar (hoje o `clockboss` só é usado como chefe genérico). Os chefes de dungeon já têm fases e falas descritas nos mapas `dungeon_*.json`:
- [ ] arenacube
- [ ] avatar
- [ ] berserker
- [ ] bombboss
- [ ] bosstrain01
- [ ] clockboss
- [ ] crawler
- [ ] DQ_KS_23MID_whalesharkDEATH
- [ ] dragonsnake01
- [ ] escortevilorb
- [ ] gargantua
- [ ] hypnotist
- [ ] iceboss
- [ ] jester
- [ ] kaiju
- [ ] kaiju3B
- [ ] kraken
- [ ] meowza
- [ ] meowza2
- [ ] meowza2DEATH
- [ ] notra
- [ ] octopus
- [ ] orbitroid
- [ ] orbitroid_lasergrid
- [ ] orbitroid_minion
- [ ] orbitroidDEATH
- [ ] pharaoh
- [ ] phoenix5
- [ ] raceboss
- [ ] raijin
- [ ] raijin3B
- [ ] smokeboss
- [ ] sneezy
- [ ] solarissun
- [ ] spiderboss
- [ ] sprinkles
- [ ] summonermaster
- [ ] swarmboss
- [ ] tank
- [ ] teslahq
- [ ] thunderboss
- [ ] thunderboss3B
- [ ] turbineking
- [ ] ufo
