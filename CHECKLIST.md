# Steambirds Alliance Revive — Checklist

Situação em **17/11/2026**: nomes de arquivos, funções e variáveis revisados para inglês (seção 14; ainda não compilados, rodar `Build launcher.ps1` antes de testar). Servidor Server v7 (troca de avião no hangar e XP de piloto em tempo real; a v6 rodou no jogo em 14/11: XP por abate, nível do avião e desbloqueio do paladin funcionaram), plugin Steambirds Quieter 1.20.0, cliente **201219.1413** (depot 386011, manifest 58813776496064724). As 17 dungeons da build 190828 foram recriadas em `maps\dungeon_*.json`. **v8 (15/11, ainda não testada no jogo):** servidor de dungeon na porta 1113 (`Dungeons\`), com Iron Lair e Bomb Factory ligadas no `dungeons.json`. **v9 (15/11, ainda não testada no jogo):** itens, inventário autoritativo e loot (`Items\`, `loot_tables\`, `itens.json`, `inventario.json`) e o catálogo de itens (`itens\catalogo_de_itens.md`). **Launcher 0.1.0 (15/11, compilado e testado no PC: subiu o servidor e abriu o jogo):** `Launcher\` (WinForms, .NET 8) com abas Client e Server, montado pelo `Build launcher.ps1` em `Launcher\dist` (servidor e launcher como .exe self-contained, sem `dotnet run`); o que ainda não existe no projeto aparece acinzentado. **Launcher 0.2.0 (15/11, ainda não testado):** aba Settings (tema escuro, pastas e logs), inglês como padrão com português e pasta `lang` para a comunidade, idioma do jogo (`-locale`), desbloqueios iniciais em checklist, um campo por moeda e janela para editar e remover a conta. **v11 + Launcher 0.3.0 (16/11, ainda não testados):** contas por jogador (`contas\<steam_ID>\`, Steam ID tirado do ticket de login; a conta antiga vai para o primeiro jogador do PC do host), servidor aberto para outros PCs (`rede.json` com nome, portas, máximo de jogadores e comandos de chat; HTTP em todas as redes; o endereço de cada troca de servidor e portal é o que o jogo de cada jogador usou), busca de servidores (UDP 6444) e `/revive/info`; no launcher, lista de servidores da rede local e das VPNs, Conectar por IP, nome do jogador, download do depot pela Steam, aba Rede e multiplayer (endereços, firewall, portas) e lista de contas. Cada jogador ainda joga no próprio mundo (não se veem). **Relay de multiplayer no Nexus (17/11, escrito sem compilar, ainda não testado):** `Server\Network\PlayerRelay.cs` repassa avião, movimento, tiros, mortes e chat entre os jogadores do Nexus; nomes antigos em português corrigidos no `Compat\LegacyFiles.cs` (servidor e launcher). **Dungeons com vários jogadores (17/11, escrito sem compilar, ainda não testado):** quem entra numa dungeon já em andamento do mesmo tipo entra na mesma instância (mesmas salas, inimigos e portões; se vêem pelo `PlayerRelay`); quando o último jogador sai a instância é descartada e a próxima entrada começa do zero. **Launcher + aba Map editor (16/11, escrita sem compilar, ainda não testada):** editor visual de mapas dentro do launcher (`Launcher\Editor`, imagens em `Launcher\editor-assets`).

**Legenda**
- [x] feito
- [ ] 🟡 parcial: existe no servidor, mas responde vazio, só grava no log ou ainda falta conferir no jogo
- [ ] falta

**Protocolo:** o cliente conhece 232 mensagens de rede. 65 delas já aparecem no servidor, mas parte só é registrada no log ou respondida vazia. Os números entre crases são os IDs das mensagens (`CatalogoDeMensagens.cs`).

## Resumo

| # | Área | Feito | Parcial | Falta |
|---|---|---|---|---|
| 1 | Base: conexão, login e conta | 12 | 3 | 3 |
| 2 | Avião, classes e hangar | 6 | 5 | 4 |
| 3 | Piloto, permadeath e progressão | 0 | 13 | 0 |
| 4 | Combate e inimigos | 7 | 3 | 4 |
| 5 | Itens, inventário e loot | 2 | 11 | 3 |
| 6 | Rebel City (Nexus) e prédios | 3 | 4 | 6 |
| 7 | Mundo aberto (ilhas) | 0 | 0 | 9 |
| 8 | Dungeons e tutoriais | 5 | 8 | 5 |
| 9 | Quests, missões e conversas | 0 | 3 | 6 |
| 10 | Social: chat, amigos, flocks e troca | 2 | 1 | 7 |
| 11 | Combat Sim | 0 | 0 | 5 |
| 12 | Troca de servidores | 1 | 1 | 3 |
| 13 | Áudio, interface e extras | 0 | 1 | 5 |
| 14 | Código e repositório | 4 | 2 | 0 |
| 15 | Launcher base — aba Client (objetivo intermediário) | 2 | 6 | 1 |
| 16 | Launcher — aba Server | 0 | 8 | 3 |
| 17 | Launcher standalone (objetivo final) | 0 | 5 | 0 |
| 18 | Editor de mapas | 2 | 4 | 4 |
| | **Total** | **46** | **78** | **68** |

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
- [ ] 🟡 Config `death` vem vazia (`death = {}`): o que se salva na queda virou opção do host no `itens.json` (`deathSalvageRandomEquipment`, padrão 0 como o original; `keepCargoOnDeath`, `keepEquippedOnDeath`)
- [x] Tabelas de loot por nome: `named_loot_tables` vem vazio; as unidades só citam o nome (`tier7minion`, `tiernboss`, `tier10treasure`...) e algumas trazem tabelas embutidas. Recriadas em `loot_tables\` (um arquivo por tabela)
- [ ] 🟡 Recompensas de carreira: o cliente só tem ids e ícones (`career_reward_storage_*`, `career_reward_hangar_1`); o que cada uma dá foi definido no `progressao.json`
- [x] Sorteio do retrato do piloto: feito no servidor com as peças exportadas pelo plugin

**Regras que só existiam no servidor (precisam ser recriadas)**
- [ ] Mundo aberto: rodar a geração, decidir onde e quando cada inimigo nasce, densidade, respawn, hora dos eventos e chefes de setpiece, quando um porta-aviões de dungeon aparece
- [ ] 🟡 Roteiro das dungeons: ordem das salas, portões, quem acorda quando, fases dos chefes. Feito no tutorial do walker; nas 17 dungeons da 190828 está descrito nos mapas (`tipos` e `encontros`) e o servidor de dungeon (v8) roda essa parte genérica. Os roteiros especiais (broca, chave, altares, emboscadas) ainda não
- [ ] 🟡 Loot: sortear o drop (algoritmo do cliente, `SteambirdsLootTable.DrawLoot`), criar o saco, raridade. Feito na v9; falta testar e "quem pode pegar" com vários jogadores
- [ ] 🟡 Inventário autoritativo: validar trocas de vaga, criar ids de item, desgaste, reforja (com sorteio) e reciclagem. Feito na v9; falta testar
- [ ] Lojas: conteúdo das prateleiras e preços chegam por mensagem do servidor (`ClientReceiveStoreContent`)
- [ ] Quests: quais quests cada prédio oferece, contagem do progresso dos objetivos, entrega das recompensas
- [ ] Missões e squads: rotação das missões do porta-aviões e formação dos grupos
- [ ] Social: flocks (os dados das guildas ficavam no backend Ozy), amigos, chat entre jogadores, troca segura, rankings e pontuação do Combat Sim
- [ ] 🟡 Dungeon compartilhada (17/11): jogadores do mesmo tipo de dungeon dividem a instância (inimigos, portões, salas, ondas; cada um recebe XP e o próprio loot; inimigos perseguem o jogador mais próximo) e a instância some quando fica vazia. Falta testar com dois PCs; ainda sem limite de jogadores por dungeon e sem fila
- [ ] Orquestração: listar servidores, subir uma instância de dungeon por grupo, fila e troca entre servidores (no projeto vira o launcher e a aba Server)
- [ ] Multiplayer: repassar a cada jogador o que os outros fazem por perto e decidir quem tem autoridade sobre o dano nos inimigos
- [ ] Economia: as moedas vinham de microtransações da Steam; precisam de outra regra (configurável pelo host)

**A conferir:** o `configBinary` pode trazer tabelas de loot por inimigo, o conteúdo das lojas (StoreConfig), as listas de quests e a agenda de missões. Antes de recriar, o servidor deve gravar no log quais dos 1953 arquivos de config vêm vazios ou quase vazios, e comparar com o configBinary da build 190828. Cada item acima muda para "pegar do cliente" ou "recriar" conforme o resultado.
- [ ] Gravar no log os arquivos de config vazios (201219 e 190828) e atualizar esta lista

---

## 1. Base: conexão, login e conta

- [x] Cliente da build 201219.1413 baixado com `download_depot 386010 386011 58813776496064724`
- [x] Jogo conecta no servidor local só com `OZY_URL` + `-connecttolocal` (`Run game.ps1`), sem precisar do plugin
- [x] HTTP Ozy: `/region`, `/user/login` (ticket da Steam), `/user/self`, `/user/queue_check`, `/maintenance_info`, `/alive`
- [x] Envio de logs de erro do cliente (`/user_uploads/*`), gravados em `envios-do-cliente`
- [x] UDP Copper/Lidgren: aprovação, hail, sincronia de relógio (`RequestDelta`/`ReplyDelta`, `ResyncSender`)
- [x] `ClientWelcomeMessage` com o PlayerData completo (piloto, retrato, inventários, buffs, aceites)
- [x] EULA e avisos legais (`ServerAcceptLegalNotice`, aceites no `aceites.json` de cada conta)
- [x] Todas as 232 mensagens decodificadas campo a campo no log (as que não têm tratamento também)
- [x] Dados do jogo extraídos para `Server\data` (1641 unidades, 92 nuvens, 25 manobras, ClientMapInfo de 10 terrenos)
- [x] Servidor lê o `configBinary` do jogo direto da pasta instalada (DarkConfig + LZMA, `GameData\ConfigBinary.cs`): tabelas de XP, `pilotScore`, skills, desbloqueios, buffs, unidades e textos
- [x] Relatório automático de cada teste (`Run server.ps1`) e plugin de diagnóstico opcional
- [x] Conta salva em disco (`conta.json` com piloto, aviões e dungeons; `inventario.json`; `aceites.json`)
- [ ] 🟡 Uma conta por jogador (v11): `contas\steam_<SteamID>\` no PC do host. O Steam ID sai do ticket de sessão da Steam que o jogo manda no login (bytes 12 a 19; sem ticket, a conta é do PC); o `sessid` de cada login liga as conexões UDP à conta. A conta antiga (`conta.json` da raiz) vai para o primeiro jogador que entrar pelo PC do host. O resto do servidor continua chamando `ContaEsboco` sem dizer a conta: cada mensagem abre o "escopo" da conta do jogador da conexão. Falta testar
- [ ] 🟡 Servidor aberto para outros PCs (v11): HTTP em todas as redes (`allowOtherPlayers` no `rede.json`), o login devolve o Nexus no endereço que o jogo usou (LAN, Radmin, Hamachi ou IP público) e as trocas de servidor e os portais usam o endereço de cada jogador. Portas, nome, máximo de jogadores e comandos de chat (`everyone`, `host`, `none`) no `rede.json`; `/jogadores` no chat; aviso de quem entra e sai. Falta testar com dois PCs
- [ ] 🟡 Todo jogador entra como admin (`isAdmin`), o que evita a desconexão automática após 120 s parado; quem usa os comandos de chat do servidor agora é escolhido no `rede.json` (`chatCommands`: todos, só o host ou ninguém)
- [ ] 🟡 **Multiplayer (Nexus):** `PlayerRelay` repassa o avião (SpawnUnitsBatch com o PeerId do dono e shortId de relay 7.000.000+), `UpdateUnitsBatch`, `GunFire`, `KillUnitsBatch` (morte, troca de avião, saída) e `ClientReceiveRemotePlayerData` de quem está online. Falta testar com dois PCs; ainda sem nuvens, skills dos outros (`TriggerOtherPlayerSkill`), `GunFireBatch` `62327`, inimigos e loot compartilhados. Tutorial e dungeons seguem um mundo por jogador
- [ ] Login por usuário e senha (`/user/create`, `/user/change`, `/user/reset_request`, `/activation/*`); opcional, já que o login pela Steam funciona
- [ ] `/msg_interface_config` (mensagens/banners da interface)

## 2. Avião, classes e hangar

- [x] Criar avião (`ServerRequestCreateCharacter` → `ClientAddPlane`)
- [x] Spawn do avião com classe, variante, conta e characterId nas opções da unidade
- [x] Itens iniciais equipados (tabela das 15 classes no `PlayerDataEsboco.cs`)
- [x] Seleção de avião (save slot) e menu de opções funcionando
- [ ] 🟡 Classes: só o **walker** foi testado (lista das 15 no Apêndice A)
- [ ] 🟡 Modelo de voo por classe (v10, `Planes\PlayerPlaneTemplates.cs`): o spawn usa o `planeName` da classe (configBinary `playerclasses`) em vez de `playerPlane` para todos. walker, paladin, support, pirate, siegetank e daggerknight → `walkerClassPlane` (8 direções, paira parado, armas seguem o mouse); olfaithful, duster e wizard → `playerPlane` (sempre para frente, tiro para frente). Conferido nas builds 190426–190911; falta conferir no jogo com a 201219
- [x] Unlock de classes pelo nível dos aviões: regras `unlocks` do configBinary (always / classLevelGreater) com `highestLevels`; o item novo vai no `ClientUpdatePlayer`. Testado: walker nível 9 desbloqueou e criou um paladin
- [ ] 🟡 Trocar de avião no Hangar (`ServerRequestChangePlayerPlane` `15839`): na v6 o jogador ficava preso no hangar. A v7 tira o avião atual (`KillUnitsBatch` com `ChangePlane`) e cria o escolhido no mesmo lugar com outro shortId. Falta conferir no jogo; `ClientShowPlaneSelection` `24120` sem uso
- [ ] Remover avião da lista (`ClientRemovePlane` `55111`)
- [ ] Variantes e skins (`ServerRequestChangePlaneVariant` `15910` / `ClientUpdatePlaneVariant` `15909`)
- [ ] 🟡 Vagas do hangar configuráveis (`hangarSlots` no `progressao.json`); falta comprar vagas e `ClientUpdatePlayerDataSlots` `48914`
- [ ] Aposentar avião (`ServerRetirePlane` `12153`; o cliente desta build não manda)
- [x] XP e nível do avião: XP por abate (fórmula reconstruída: vida base × `xpTiers` do tier × `xpMultiplier` da unidade), limite por abate do original, `ClientUpdatePlayer` `61589`, `ClientUpdateHighestLevels` `21500`, `ServerReportPlaneLevel` `13948`. Testado no tutorial (drone 4 XP, chefe 40 com o limite, nível 2 no fim). O ritmo ainda pode ser ajustado em `planeXpMultiplier`
- [ ] Bônus de XP (`ClientUpdatePlayerDataPlaneXpBoost` `48944`)
- [ ] 🟡 Upgrades permanentes do avião, que se perdem na morte: `ServerInvAccumulateItem` guarda em `_accumulatedItems` do avião (v9); falta conferir `upgradeCaps` e o `Permanents_`

## 3. Piloto, permadeath e progressão

Código em `Server\Progression` e doc em `docs/progressao.md`. Tudo abaixo ainda precisa de um teste no jogo.

- [ ] 🟡 Permadeath: o avião sai da conta e o piloto fica com a parte em tempo real do valor dele (90% nesta build); o resto só com `deathPilotXpBonus`. Com `"permadeath": false` o avião volta ao hangar
- [ ] 🟡 Tela de morte (`ClientDeathSummaryInformation` `43727`; sem ela o cliente ficava parado no local da queda) e texto de pós-luta dos chefes (`ClientPostFightDetails` `61590`). O cliente repete os números da morte anterior a partir da 2ª morte: o plugin 1.20.0 corrige
- [ ] 🟡 Equipamento salvo na morte (DeathLootConfig `salvageRandomEquipment`): `deathSalvageRandomEquipment` no `itens.json` manda itens equipados sorteados para o armazém, gastos (condição cai, supercarga some). Com morte permanente o cargo e os equipados se perdem. "Ejeção" não aparece no código do cliente
- [ ] 🟡 XP e patente do piloto: `pilotXpTargets` e `pilotScore` (melhor taxa 250:1). Na v7 a patente sobe em tempo real, seguindo o XP que o cliente mostra (`Pilot.computedXP`: 90% do valor de cada avião do hangar), como descreve a calculadora do meowzaDB; `ServerRecordLevelupUIShown` `12158` guardado
- [ ] 🟡 Árvore de skills (`UpdatePlayerSkills` `65037`, `ServerRequestSkillChange` `65039`, `ClientSkillChangeError` `65040`), conferida no servidor como em `Pilot.GetSkillChangeError` (pontos, requisitos do grupo, nível máximo, uma skill por grupo)
- [ ] 🟡 Loadouts de skills (`65042` trocar, `65044` limpar, `12152` renomear); quantidade no `progressao.json`. A compra na loja `store_skill_loadouts` ainda não
- [ ] 🟡 Bônus de dano das nuvens e blasts (`Skills\DamageBoost.cs`, v10): o cliente nunca aplicava o Attack Area do walker (sem `ServerReportDamageBoost` no log, dano 1.74 antes e depois). O servidor guarda a zona, dá x`effect` por `bufftime` s (Attack Area: x1.3 por 4 s) aos aliados dentro e multiplica os acertos de tiro; se o cliente reportar o bônus, o servidor não multiplica de novo. Manda `ClientCloudEffect` `32571` para o efeito visual
- [ ] 🟡 Gatilhos de skills: o servidor manda `TriggerPlayerSkill` `65038` (OnKill) a cada abate; `ServerReportPlayerSkillTrigger`, `ServerReportDamageBoost` e `ServerReportCloudHeal` registrados; repasse para outros jogadores depende do multiplayer
- [ ] 🟡 Recompensas de carreira (`12159`–`12162`): lista no `progressao.json`, entregues sozinhas ao alcançar a patente (o cliente nunca pede o `12161`). Faltam as recompensas originais (o log mostra o `career_rewards` do configBinary)
- [ ] 🟡 Retrato do piloto sorteado pelo servidor com as peças exportadas pelo plugin 1.20.0 (`retratos_config.txt`); `ServerReceivePortrait` `12150` / `ClientUpdatePlayerPortrait` `12151`
- [ ] 🟡 Buffs da conta (`ClientUpdatePlayerBuffs` `48945`, `ClientAddPlayerBuff` `48946`, `ServerAuditBuffAction` `48947`): guardados, vencidos saem, `/buff`; falta quem dá buffs (consumíveis, modificadores de dungeon)
- [ ] 🟡 Jogadores por perto (`ClientReceiveReliableStats` `35051`, a cada segundo, para skills com condição de jogadores próximos); `IncrementPlayerStats` `879` (cura entre jogadores) depende do multiplayer
- [ ] 🟡 Ferramentas de admin no chat: `/piloto`, `/xp`, `/nivel`, `/patente`, `/zerar_piloto` (`ClientWipePilot` `4870`), `/buff`, `/retrato`; `ClientWipePlaneProgress` `8951` sem uso

## 4. Combate e inimigos

- [x] Entrada do jogador, manobras e supervelocidade recebidas
- [x] Tiros do jogador tiram vida de inimigos do servidor (`ServerReportBulletHit`, `ServerReportUnitDamage`)
- [x] Nuvens/blasts das skills criadas pelo servidor e encerradas no fim da duração
- [x] IA no servidor (a do cliente veio vazia): voo, mira, cadência e armas de cada UnitConfig
- [x] Chefes com fases, encontros e ondas (`inimigos.json`), inimigos adormecidos que acordam por proximidade
- [x] Morte de inimigo (`KillUnitsBatch`) e HUD de chefes restantes
- [x] Comandos de teste no chat (`/inimigo`, `/spawn`, `/chefe`, `/onda`, `/catalogo`…)
- [ ] 🟡 Inimigos atravessam muros (só ficam presos à área da sala)
- [ ] Rotação visual das torretas (`FIELDMASK_GUNS`)
- [ ] Comportamento original de cada inimigo: hoje são comportamentos genéricos para 856 unidades armadas; a IA de cada chefe precisa ser recriada a partir de vídeos (Apêndice C)
- [ ] 🟡 Aliados: `ServerSpawnGreenTeamAlly` `23584` chega e só é registrado
- [ ] 🟡 Efeitos de status: `ServerReportPlayerEffect` e `ServerShowInstantEffect` chegam e só são registrados; faltam `ClientCloudEffect` `32571`, `ServerReportCloudEffect` `27911`, `ClientInstantEffect` `58535`
- [ ] Números de dano e efeitos de tela (`ClientShowDamage` `29463`, `ClientShowUIEffect` `46628`, `ClientCameraShake` `502`, `ClientKillBullet` `8267`)
- [ ] Skill de um jogador em outro (`TriggerOtherPlayerSkill` `65043`; depende do multiplayer)

## 5. Itens, inventário e loot

Código em `Server\Items` (v9, ainda não testada no jogo). Tabelas de loot em `loot_tables\` (um arquivo por tabela; `LEIA-ME.md`), opções do host em `itens.json`, inventários da conta em `inventario.json`. Comandos de teste: `/itens`.

- [x] Cargo do avião (12 vagas, **compartilhado entre os aviões** como no original: `isUsingSharedCargoForAll`) e armazém da conta (16 vagas), com os itens salvos; vagas extras no `itens.json`
- [x] Catálogo de todos os itens: `itens\catalogo_de_itens.md` e `.json` (id, nome, tier, classes que podem usar, stats, texto de lore, valor de reciclagem). O desta entrega vem da build 190911 (1242 itens); o servidor regrava com a 201219 ao subir (`/exportar_itens`)
- [ ] 🟡 Loot ao matar inimigos: sacos de loot (prefab `lootbag`, um item por saco, some em 30 s), `ServerRequestAddInventoryItem` `21818` → `ClientInvAdd` `34491`, `ServerReportItemPickup` `4422`, `ServerRequestKill` do saco. Vale no tutorial e nas dungeons
- [ ] 🟡 Tabelas de loot em arquivo: `normal` (inimigos comuns), `boss` (chefes e baús), `tutorial_walker_chest`. Ordem: tabela da unidade (`units`), loot do jogo da unidade (embutido + `lootTableId` via `replaces`), senão normal/boss. Valores reconstruídos
- [ ] 🟡 Avisos de coleta: `ClientNoRoomForPickup` `22110` (o item volta ao mundo), `ClientConsumedItemOnPickup` `22111` (ingredientes e itens `devourOnPickup`), `ClientCargoFull` `14528` (desequipar com cargo cheio). `ClientReplaceInventoryAndPickup` `16148` escrito, sem uso
- [ ] 🟡 Mover, equipar, descartar e favoritar itens: `ServerInvSwapItems` `17407` (confere o slot do item), `ServerInvRemoveItem` `60901` (soltar = saco no mundo; lixo = reciclagem), `ServerInvAccumulateItem` `60904`, `ServerInvFavoriteItem` `60905`
- [ ] 🟡 Consumir e ordenar itens: `ServerInvConsumeItem` `60902` (o cliente aplica vida/vapor/nuvem; o servidor entrega desbloqueio, variante, moeda, buff de conta e ingrediente, e gasta os usos), `ServerInvApplySort` `60903` (tier ou tipo, ordem estável e `ClientReplaceInventory` depois)
- [ ] 🟡 Respostas de inventário: `ClientUpdateInventoryItem` `37467`, `ClientRemoveInventoryItem` `37468`, `ClientReplaceInventory` `16147` e as respostas de RPC (id de auditoria; `ServerAuditInvAction` `61999` só registrado)
- [ ] 🟡 Mover itens entre o avião e o armazém (mesmo `ServerInvSwapItems`, com o characterId do armazém)
- [ ] 🟡 Ingredientes e reforja na Oficina: `ServerReforgeItemFromIngredients` `12157` com o custo da config `reforging` (itens e moedas), `ClientReforgeSucceeded` `12155`/`ClientReforgeFailed` `12156`, `ClientUpdateIngredients` `61591` (carteira no login e na coleta)
- [ ] 🟡 Reciclagem de itens: lixo dá a moeda da config `mulching` (`newscrap`) pelo tier e pelas tags do item; moedas no PlayerData e no `ClientUpdatePlayer`
- [ ] 🟡 Condição, desgaste, soulbound e supercharged: guardados por item; desgaste na queda (itens salvos), reforja volta a 0, supercarga (tier 9+) sorteia stats de `SuperchargeNewStatProbabilities`; itens com prazo (`decaysInSeconds`) somem no login
- [ ] 🟡 Loot da caixa de suprimentos do tutorial: os quatro motores de tier 1 (a placa manda escolher o motor antes do chefe)
- [ ] Consumíveis sem lado de servidor ainda: pinturas (`deco`), chaves de dungeon (`dungeon`, `key_*`) e bônus de XP (`planexpboost`); o servidor recusa e o item fica
- [ ] Recompensas de carreira do tipo item entregues no inventário
- [ ] Loot com vários jogadores (cada um com o próprio saco, como no original; depende do multiplayer)

## 6. Rebel City (Nexus) e prédios

- [x] Mapa do Rebel City em `maps\nexus.json`: chão, pistas, cidade em volta e 9 prédios
- [x] Atracar em todos os prédios sem o avião ficar preso
- [x] Flock Hall visível (precisa do plugin 1.18.0+, porque a arte vem desligada no cliente)
- [ ] 🟡 Conferir no jogo: pontas da pista (4.3), cidade em volta do Flock Hall (4.4), Flock Hall sem os letreiros "HANGAR" (plugin 1.19.0); a v5 já carregou no teste de 14/11 03:18
- [ ] Talvez: heliponto com "H" no prédio elevado perto da Store (vídeo, 14:40)
- [ ] 🟡 Lojas (`store_general`, `store_hangar`, `store_skill_loadouts`): abrem com prateleiras vazias; compra (`ServerPurchaseInStore` `54296`) responde erro
- [ ] 🟡 Pilot's Pub e Oficina: abrem com lista de quests vazia (ver seção 9)
- [ ] Hangar: troca de avião (seção 2)
- [ ] Pilot School: tutoriais das outras classes (seção 8)
- [ ] Flock Hall: flocks (seção 10); `ServerFindFlockSuggestion` responde `not_implemented`
- [ ] Combat Sim (seção 11)
- [ ] 🟡 Porta-aviões das missões: abre com lista vazia (seção 9)
- [ ] Levar o jogador do Nexus para os mundos/ilhas (seção 7)

## 7. Mundo aberto (ilhas)

- [ ] Servidor de mundo (terreno `Basic`, papel World)
- [ ] Gerar o mapa do mundo: o `WorldLayout` do cliente traz o algoritmo (semente, raio 650, mapas ASCII, assets de chão, cânions); quem gerava era o servidor
- [ ] Regiões do mundo (o cliente cita midlands e highlands; o anúncio do lançamento cita a região Palace) e nome da sub-região na tela (`ReceiveCurrentSubterritoryName` `62683`)
- [ ] Spawn de inimigos por região e por nível de ilha (spawn rules; cada unidade tem faixa `ilhaMin`/`ilhaMax`)
- [ ] Chefes de setpiece, chefes de evento e setpieces que aparecem (IslandConfig, EventBossesConfig, PopupSetpieceConfig)
- [ ] Quests de ilha (`ClientReceiveIslandQuestNotification` `54199`)
- [ ] Porta-aviões de dungeon que aparecem no mundo (`ClientReceivePortalUpdate` `35053`, `ServerSendMeThroughPortal`)
- [ ] Teleporte para casa/local (`ServerNotifyCompleteTeleport` `12006`), nascimento com zona segura
- [ ] Eventos agendados (UGUIUpcomingEventsList)

## 8. Dungeons e tutoriais

Doc das dungeons recriadas: `docs/dungeons-190828.md`. Servidor de dungeon: `docs/servidor-de-dungeon.md` e `Server\mapas\LEIA-ME-dungeons.md`.

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
- [ ] Conferir as outras unidades invisíveis armadas das dungeons ainda fechadas (lista em `mapas\LEIA-ME-dungeons.md`)
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
- [ ] 🟡 Chat entre jogadores: proximidade (`11732` → `1612`, repassado no Nexus pelo `PlayerRelay`, falta testar); sussurro (`11731`/`11730`), chat da flock (`11733`/`11734`)
- [ ] 🟡 Amigos: a lista é respondida vazia; faltam adicionar e remover (`ServerAddFriend` `12201`, `ServerDeleteFriend` `12202`)
- [ ] Flocks (guildas): criar, entrar, sair, convites, cargos, expulsar, banir, silenciar, buscar, sugestão (`30000`–`30016`), flock padrão (`48916`)
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
- [ ] Ir para o servidor de outro jogador (`ServerSendMeToPlayerServer` `43983` / `ClientSendMeToPlayerServerFailed` `46627`)
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
- [ ] 🟡 Nomes de arquivos, classes, variáveis e comentários em inglês. Feito: arquivos e classes do servidor (`Accounts.cs`, `Maps.cs`, `Enemies*.cs`…), identificadores e a maior parte dos comentários e mensagens do servidor, pastas (`Server`, `maps`, `data`, `accounts`, `tools`), scripts (`Run server.ps1`…), plugin e launcher. Os scripts `.ps1` foram traduzidos em 17/11 (ainda não rodados). Falta conferir os textos de log e comentários de `Game.cs`, `Program.cs`, `DungeonServer.cs` e `Tutorial.cs`, e os textos restantes do plugin, que não foram revisados nessa passagem
- [x] Campos do formato de mapa em inglês (`floor`, `decor`, `buildings`…), com o formato `steambirds-map`. Os mapas antigos continuam abrindo: `Server\Compat\LegacyNames.cs` e o editor os convertem ao ler (tabela `legacy-names.json`). Os 17 mapas de dungeon foram regerados pelo gerador com os nomes novos e conferidos. Em 17/11, `fala` (falas de fase dos chefes, agora `bark`) e `ambush` do `dungeon_thief.json` (agora `group` e `intervalSeconds`) também foram trocados, nos mapas e no gerador `dungeons_190828.py`
- [ ] 🟡 Dividir arquivos grandes. Feito: `Enemies.cs` (era `InimigosEsboco.cs`, 118 KB) virou 7 arquivos `partial` por tema (`Enemies.Scripts`, `Encounters`, `Ai`, `Messages`, `Files`, `Util`); `Game.cs` (60 KB) virou 6 (`Actions`, `Players`, `Rpc`, `Shots`, `Units`); `Tutorial.cs` (61 KB) virou 6 (`Commands`, `Helpers`, `Instructor`, `Script`, `Session`); `DungeonServer.cs` (57 KB) virou 6 (`Commands`, `Helpers`, `Session`, `Tick`, `Units`); `Items\InventoryHandler.cs` (45 KB) virou 5 (`.Moves`, `.Use`, `.Commands`, `.Send`). Falta: `Program.cs` (54 KB; antes de dividir, precisa virar uma classe `Program` com `Main`, pois hoje é código de topo de arquivo)
- [x] README de instalação e de contribuição para o GitHub (`README.md`, `CONTRIBUTING.md`)
- [x] Servidor como executável publicado (sem `dotnet run`): o `Build launcher.ps1` publica o `SteambirdsServer.exe` self-contained em `Launcher\dist\server`, com mapas, dados e configs. Rodou pelo launcher em 15/11

## 15. Launcher base — aba Client (objetivo intermediário)

- [x] Scripts PowerShell que sobem o servidor e abrem o jogo (`Run server.ps1`, `Run game.ps1`)
- [x] Programa de launcher com abas Client, Server e Settings: `Launcher\` (WinForms, .NET 8, `Core\` e `UI\`), montado pelo `Build launcher.ps1` em `Launcher\dist`. Tudo fica na pasta do exe (`launcher.json`, `lang\`, `logs\`, `server\`, `plugin\`). Console do cliente com o `LogOutput.log` e o `Player.log`. Doc em `Launcher\LEIA-ME.md`. A 0.1.0 compilou e rodou no PC em 15/11
- [ ] 🟡 Aba Settings (0.2.0): tema claro/escuro, pasta do jogo e os botões de abrir pastas, logs e arquivos (jogo, BepInEx, servidor, mapas, backups, configs). Falta testar
- [ ] 🟡 Idiomas (0.2.0): launcher em inglês por padrão, português em `lang\pt-BR.json`, pasta `lang` com `_template.json` e `README.md` para a comunidade adicionar idiomas (`<código>.missing.json` lista o que falta traduzir); idioma do jogo escolhido no launcher (lista do `Texts/locales` do jogo, passado com `-locale`). Falta testar
- [ ] 🟡 Escolher o nome do jogador (0.3.0): padrão = nome da conta Steam (`loginusers.vdf`); vai no caminho do `OZY_URL` (`/n/<hex>/ozy`) e o servidor usa no login. A conta continua sendo a da Steam. Falta testar
- [ ] 🟡 Lista de servidores na rede local (0.3.0): pergunta UDP na porta 6444 por broadcast, para a tabela ARP e para cada endereço de redes pequenas; o servidor responde com nome, jogadores e porta (`Network\LanDiscovery.cs`). Busca sozinha a cada minuto. Falta testar
- [ ] 🟡 Lista de servidores em VPN (Radmin, Hamachi, ZeroTier...) e Conectar por IP (0.3.0): broadcast e tabela ARP de cada adaptador de VPN; IP manual conferido em `/revive/info` e guardado na lista. Falta testar
- [ ] 🟡 Botão de abrir o jogo já apontando para o servidor escolhido: este PC com `-connecttolocal` (e `-connecttolocalport` se o Nexus mudar de porta), sobe o servidor antes se precisar; outro PC sem `-connecttolocal` (o login devolve o Nexus dele) e com "Iniciar o servidor deste PC" desmarcado e acinzentado. Falta testar outro PC
- [ ] Lista de servidores dentro do jogo igual à do launcher, com troca entre eles (seção 12)

## 16. Launcher — aba Server

- [ ] 🟡 Hospedar o servidor pelo launcher: aba Servidor com iniciar, parar e reiniciar, console colorido com filtro, log por sessão (`logs\servidor-<data>.log`) e o `relatorio-esboco.txt` ao parar (como o `Run server.ps1`). Jogadores de outros PCs entram desde a 0.3.0 (falta testar)
- [ ] 🟡 Contas salvas no PC do host, uma por jogador (`server\contas`, v11). Falta testar
- [ ] 🟡 Configurações: telas Progressão, Itens e loot e Inimigos e dificuldade, montadas a partir do `progressao.json`, `itens.json` e `inimigos.json` (cada chave vira um campo e o comentário vira a explicação; salvar mantém os comentários; o servidor relê sozinho). Dificuldade: `multiplicadorGlobalDeVida`, `dungeonDifficulty` e o multiplicador de vida de cada dungeon
- [ ] 🟡 Rede e multiplayer (0.3.0): endereços para passar aos outros jogadores (um por rede, com botão de copiar), regra do Firewall do Windows (cria com permissão de administrador) e o `rede.json` (nome, outros PCs, busca, máximo de jogadores, comandos de chat, mensagem de boas-vindas e as portas HTTP, Nexus, tutorial, dungeon e busca). Falta testar
- [ ] 🟡 Aba de contas: estado inicial de contas novas editável (patente; desbloqueios em checklist com as classes do jogo; um campo por moeda do jogo; ingredientes e itens no armazém)
- [ ] 🟡 Ver, editar e apagar contas: lista com uma linha por jogador (nome, Steam ID, último jogo, patente, aviões...) e os aviões da selecionada (0.3.0); janela de edição (piloto e patente, aviões com variante/nível/XP, desbloqueios, dungeons terminadas, moedas e ingredientes) e remover a conta, com cópia em `server\_backup`. Só com o servidor do launcher parado. Falta testar
- [ ] Escolher o arquivo de mapa do Nexus e do tutorial (no launcher, acinzentado: o servidor usa `nexus` e `tutorial_walker` fixos)
- [ ] 🟡 Escolher o arquivo de mapa de cada dungeon: tela "Mapas e dungeons" do launcher (grava no `dungeons.json`). Falta testar
- [ ] Ver cada mapa na tela do servidor (no launcher, acinzentado)
- [ ] 🟡 Definir quando cada dungeon fica disponível: "Ativa", "No Nexus", "Vida ×" e "Abre em" na tela "Mapas e dungeons". Falta testar
- [ ] Mapa do mundo com a posição de cada jogador (no launcher, aba acinzentada)

## 17. Launcher standalone (objetivo final)

- [ ] 🟡 Detectar a Steam e o login: o launcher acha a pasta da Steam pelo registro, avisa se ela está fechada e mostra a conta logada (`ActiveProcess\ActiveUser` e `loginusers.vdf`: nome e Steam ID)
- [ ] 🟡 Abrir o console da Steam com o comando pronto (0.3.0): botão "Baixar o jogo pela Steam" copia `download_depot 386010 386011 58813776496064724` e abre `steam://open/console`; o jogador cola e aperta Enter (a Steam não deixa mandar o comando de fora). Falta testar
- [ ] 🟡 Detectar quando o download termina (0.3.0): linhas `Depot download complete` / `failed` do `logs\console_log.txt` da Steam e o tamanho da pasta `depot_386011`; no fim ela vira a pasta do jogo. Falta testar
- [ ] 🟡 Colocar os arquivos necessários para o jogo logar via Steam: o launcher cria o `steam_appid.txt` (386010) na pasta do jogo (0.3.0), mostra a build e instala o plugin no `BepInEx\plugins`. O BepInEx ainda não vem com o launcher
- [ ] 🟡 Executar o jogo (botão "Jogar" do launcher)

## 18. Editor de mapas

- [x] Formato de mapa em arquivo (JSON v1): chão desenhado, peças, enfeites, prédios, pontos, áreas, portões, placas (`mapas\LEIA-ME-mapas.md`)
- [x] Servidor relê o mapa ao mudar, valida os nomes que o roteiro usa e mantém a última versão boa se o arquivo tiver erro
- [ ] 🟡 Editor visual com os assets do jogo (aba Map editor do launcher, `Launcher\Editor`): chão, muros, entrada, saída, pontos de controle, portões, salas com inimigos e ondas, tipos de unidade, pontos, áreas, linhas, rotas, placas, enfeites e prédios, com desfazer/refazer e tema claro/escuro. Prévias em geometria e cor dos vértices (sem as texturas do jogo); a arte de várias unidades ainda cai em letra. Escrito em 16/11 **sem compilar**: falta compilar e testar
- [ ] 🟡 Exportar arquivos que o servidor usa: salvar confere o mapa (entrada, saída, chão, referências) e grava em `server\mapas`; com erros só salva como rascunho (`"rascunho": true`). Falta testar
- [ ] 🟡 Dungeons: o editor abre uma cópia de cada uma das 17 da 190828 (também geradas por script em `ferramentas\dungeons-190828`, com prévia em imagem). Falta testar
- [ ] Mapa global (mundo aberto)
- [ ] 🟡 Inimigos e encontros por mapa: o servidor de dungeon lê `tipos` e `encontros` de cada mapa (os tipos entram com o prefixo do mapa); no Nexus e no tutorial ainda vale o `inimigos.json`
- [ ] Roteiros de dungeon em arquivo (hoje o roteiro do tutorial está em código)
- [ ] Coleção de versões de dungeons da comunidade para o host escolher
- [ ] Editor de IA dos inimigos: a aba e o botão existem acinzentados; cada tipo de unidade já guarda um objeto opcional `"ia"` que o editor preserva e o servidor ignora (`Launcher\Editor\EnemyAiHook.cs`)

---

## Sugestão de ordem para o MVP

0. Compilar o launcher 0.3.0 e o servidor v11 (`Build launcher.ps1`) e testar sozinho: a conta antiga vai para a sua conta Steam (`server\contas\steam_...`), o nome do jogador aparece no log (`[LOGIN]`), o tutorial e o Nexus continuam iguais.
1. Testar com um segundo PC (Radmin ou rede local): busca de servidores, Conectar por IP, firewall, duas contas separadas, trocas Nexus ↔ tutorial ↔ dungeon no PC de fora.
2. Multiplayer de verdade: testar o `PlayerRelay` (avião, tiros, chat) com dois PCs; depois skills dos outros, inimigos e loot compartilhados. Quase todo o resto depende disso.
3. Testar a v9: loot no tutorial (drones, caixa com os motores, chefe), equipar o motor, mover para o armazém, reciclar, `/item` e `/loot`.
4. Testar a v7 no jogo: troca de avião no hangar, patente em tempo real, morte, skills e loadouts.
5. Testar o servidor de dungeon (v8) com Iron Lair e Bomb Factory, depois ligar as outras no `dungeons.json` uma por uma.
6. Mundo aberto (`Basic`) com spawns por região e porta-aviões de dungeon.
7. Lojas, quests e missões, conferindo antes se o `configBinary` traz os dados (os mapas de dungeon e a IA vieram vazios em todas as builds).

## Apêndice A — Classes (15 na tabela do servidor)

Testada no jogo:
- [x] walker

Falta testar criação, spawn e itens iniciais:
- [ ] assault
- [ ] medic
- [ ] rapier
- [ ] tank
- [ ] olfaithful
- [ ] duster
- [ ] support
- [ ] paladin
- [ ] wizard
- [ ] engineer
- [ ] pirate
- [ ] siegetank
- [ ] daggerknight
- [ ] vampire

## Apêndice B — Terrenos que o cliente conhece (mapinfo)

- [x] Nexus (Rebel City)
- [x] DungeonTutorialWalker (Training Grounds)
- [ ] Basic (mundo aberto)
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
