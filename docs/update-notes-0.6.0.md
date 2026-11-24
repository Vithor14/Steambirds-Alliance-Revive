# Steambirds Alliance Revive 0.6.0

**Released on 24/11/2026.** This file is closed: changes after 0.6.0 go in `docs/update-notes-0.7.0.md`. The short version for the GitHub release is `docs/release-notes-0.6.0.md`. / **Lançada em 24/11/2026.** Este arquivo está fechado: as alterações depois da 0.6.0 vão em `docs/update-notes-0.7.0.md`.

Base: `fe9c331` (the last commit of 0.5.0). / Base: `fe9c331` (último commit da 0.5.0).

## English

Big test of 23/11 with 3 PCs (Brazil, Argentina and Thailand): the tutorial, 4 dungeons, the open world, loot, stores and friends worked; the
problems it found are fixed below. Everything marked *not played* was built and checked by compiling and by reading the client's code,
but not yet played with two or more people. Please report anything odd.

### New: Safe trade between players *(not played)*
- Player menu > Trade, or `/trade NAME`; the other player answers `/trade` or `/trade_reject`, or clicks the bubble over the plane.
- Up to 4 cargo items and some gold per side; the fee (gold or iron, paid by one side or both) is recalculated on every change. The server
  checks both inventories again before the swap and puts the first account back if the second one fails.
- The fee is a reconstruction of the game's own `trade` config. `social.json`: `tradeEnabled` and `tradeTaxes` (false = free).
- Code in `Server\Social\SecureTrade*.cs`; details in `docs/social.md`.

### New: map scripts
- A map file can have a `script` section (spawns, rules with conditions and actions, `/step` jumps) run by the new engine in `Server\Scripting\`.
  The walker tutorial now runs from `maps\tutorial_walker.json` instead of C# code; the dungeon server can run scripts too.
  Played in the 23/11 test from the hangar to the exit portal. Format in `docs/map-script.md`.

### New: Map Maker catalog maps *(not played)*
- Three small maps for people who make maps, each a compact grid with the name of every item written on the floor in front of it:
  `maps\map_maker_floors.json` (**every floor piece**, 349, one tile each), `map_maker_decor.json` (**every decoration**, 292) and
  `map_maker_enemies.json` (**every enemy unit**, 822, the 38 bosses in the last rows). Enemies wait asleep and **only wake up when they take
  damage** (and only the one that was hit). `/room N` jumps to a group of rows. Each one has a plain floor with walls all around, and you start in the middle (in the enemies map, between the normal units and the bosses; there the floor narrows where the bosses are so nobody gets lost, and the units are 10 u apart). Open one in the map editor and click **Play test**.
  Generated from the game data by `tools\make_map_maker_helper.py`; details in `docs/map-format.md`. (First version: one long map; it took
  about 5 minutes to fly to the enemies, so it became three maps with the items twice as close.)

### New: admin, debug and login tools *(not played)*
- **Accounts with a password (optional).** `network.json` `passwordLogin: true`: a new Steam player gets the game's own register screen (name, e-mail,
  password); the game's login screen (no Steam) takes username and password. Accounts that already exist keep logging in through Steam.
  `/passwd NAME PASSWORD` (host) resets one. Passwords are stored as a salted hash in `accounts\<key>\login.json`. Also answered now: `/user/create`,
  `/user/change`, `/user/reset_request`, `/activation/*` and `/msg_interface_config`.
- **Iron for pilot XP.** When a plane is destroyed (and, as before, when it is retired) the pilot XP it pays also becomes currency at the game's rate
  (`pilotXpPer1Currency`, from the threshold on), and the game shows "you got N for X pilot XP" (`ClientNotificationGiveCurrencyForPilotXp`).
- **Sounds sent by the server.** `/sound NAME`, `/soundall NAME` and the new map script action `sound`.
- **Debug and admin.** `/debug`, `/god` (debug settings of the plane: damage, speed, health...), `/watchai`, `/screenshot [PLAYER]`, `/stats`, `/serverstats`,
  `/audit`, `/admin`. The server also answers the client's own debug messages and `ServerQuery` (units the client never saw spawn).
- **Server statistics and audit.** The loop time of each server (`/stats`; the game's server time chart with `/serverstats on` and Options > debug display) and an inventory
  audit that counts the actions the client reports with an id the server never gave out (or twice).
- Code in `Server\Admin\`, `Server\Network\AccountLogins.cs`, `Server\Enemies.Debug.cs`; details in `docs/admin-tools.md`.

### Dungeons
- New map value `values.maxEnemies`: a map can raise the limit of 140 living enemies per dungeon (the Map Maker enemies map needs 840).
- **Iron Lair** rebuilt as a donut arena (a round ring around the square room where you are born, a SN-Z that keeps flying around it, slow
  slow shots and mines). **It failed the 23/11 test** (too deadly: planes of level 1 to 9 fell in 10 to 60 s); it is being tuned.
  SN-Z's shots are no longer homing, and its bombs are now **mines**: they stay where they are dropped, do not shoot, show their trigger area
  as a red ring (the game's own `detectionZone_1` effect) and only go off, releasing the blast, when a player flies into that area (or shoots them).
  New unit behavior `mine` for map authors (`contactRadius` = trigger radius, `visualEffect` = ring).
- Bomb Factory, Crypt of Cathulhu, Laser Playground and The Arena were completed by 3 players in the test; the Command Center was played
  up to the boss room.
- The dungeon server now reports where each player is (for the launcher and Discord).

### Launcher
- **Play test** in the map editor (button at the top right, `F5`): saves the map, starts a small server that has nothing but that map and opens the
  game logging in to it, straight into the map like the tutorial. No account to make, no Nexus, this PC only, ports of its own (the normal server
  can stay on). The test pilot is a walker with complete tier 10 equipment and the EULA and privacy policy already accepted. After changing the map: save and type `/restart` in the game's chat. Server mode `--test-map` in `Server\Dungeons\MapTest.cs`. *(Not played yet.)*
- **Discord Rich Presence**: your Discord profile can show your plane, its level, your pilot rank and the place (Nexus, tutorial, open
  world, dungeon) while you play, and your server name and pilots online while you host. Never the IP, port, account or player name.
  Turn it off in Settings > General.
- The *Join the Discord* buttons use the Discord logo.
- **PLAY asks for confirmation** when the Quieter plugin or BepInEx is missing (without the plugin the Flock Hall is invisible, for one).
- The server console shows `WARNING: ... did not arrive` lines in the warning color.
- **Chat monitor:** new *Chat* page in the Server tab (under Monitor). It lists what the players write, separated by map (Rebel City, the open
  world, each dungeon instance such as "Iron Lair #3"); *All maps* shows everything with the map name in front. Whispers and flock chat are
  listed under the map of their sender and can be hidden. Filter, auto-scroll, copy. The server prints one `[CHATMON]` line per message
  (`Server\Network\ChatMonitor.cs`); the launcher keeps it out of the console. The history starts empty at every server start.
- The server's `/revive/info` answer has `playerDetails` (plane, level, place of each player; `infoVersion` 2).

### Fixes found in the 23/11 test
- **Data of other players:** the client asks again on every frame when its copy is older than 30 s, and the server answered every time (28,000
  answers, ~200 per second, a 4 MB report). Now one answer per second for each player and account. Requests with an empty account id are ignored.
- **Friends on the radar:** the friend list now goes to the client when it enters each server. Before, it only arrived when the friend
  window opened, so the arrows of friends were gone in the open world.
- **Server switches:** if the client does not close the old connection 6 s after a switch (a lost packet on a bad link), the server closes it.
- **Port not forwarded:** when a player on another PC does not arrive at the server he was sent to within 20 s, the console shows a
  warning with the UDP port to open in the router.
- **Quests of the mission list:** `ServerRequestQuestGoalSourceInfo` and `ServerRequestQuestRewards` are answered (empty) instead of an
  error or silence.
- **Plane variants:** after unlocking one, the chat says it is picked in Options > Variant. (A paladin variant bought in the test was not
  applied and the cause is not known yet: no request reached the server.)
- Logs: the "marked as finished" line is written once, and a death shows the last position of the plane instead of (0, 0).
- **Boss damage share:** the text on the ground after a boss ("Your damage: X%") always said 100%. The server now records the health each
  player really took from the enemy and sends each player their own share. As in the original, a boss only drops loot for a player whose
  share is at least the unit's `damageThreshold` (default 0.5%, `progression.json`); XP is unchanged.

### Docs
- New `docs/radmin-vpn-guide.md`: step by step to play over Radmin VPN. Linked from the `README.md`.
- `CHECKLIST.md`: the 23/11 test marked what worked (97 done, 83 partial, 43 missing at the time of writing).

### Upgrade notes
- `social.json` of an existing server does not have `tradeEnabled` and `tradeTaxes`: the defaults (on) apply; add the keys to change them.
- Maps and settings of 0.5.0 keep working. `maps\tutorial_walker.json` and `maps\dungeon_sneezy.json` changed.
- Plugin 1.22.0 and the client build **201219.1413** are unchanged.

## Português

**Lançada em 24/11/2026.** Arquivo fechado; as alterações seguintes ficam em `docs/update-notes-0.7.0.md`.

Grande teste de 23/11 com 3 PCs (Brasil, Argentina e Tailândia): o tutorial, 4 dungeons, o mundo aberto, loot, lojas e amigos funcionaram; os
problemas que ele achou estão corrigidos abaixo. O que está marcado *não jogado* foi compilado e conferido pelo código do cliente, mas ainda
não foi jogado com duas ou mais pessoas. Avise se algo estranho aparecer.

### Novo: troca segura entre jogadores *(não jogado)*
- Menu do jogador > Trade, ou `/trade NOME`; o outro responde `/trade` ou `/trade_reject`, ou clica no balão sobre o avião.
- Até 4 itens do cargo e ouro de cada lado; a taxa (ouro ou ferro, paga por um lado ou pelos dois) é recalculada a cada mudança. O servidor
  confere os dois inventários de novo antes da troca e desfaz a primeira conta se a segunda falhar.
- A taxa é uma reconstrução da config `trade` do próprio jogo. `social.json`: `tradeEnabled` e `tradeTaxes` (false = grátis).
- Código em `Server\Social\SecureTrade*.cs`; detalhes em `docs/social.md`.

### Novo: roteiros de mapa
- O arquivo de mapa pode ter uma seção `script` (aparições, regras com condições e ações, saltos `/step`) rodada pelo motor novo em
  `Server\Scripting\`. O tutorial do walker agora roda pelo `maps\tutorial_walker.json` em vez de código C#; o servidor de dungeon também roda
  roteiros. Jogado no teste de 23/11 do hangar até o portal de saída. Formato em `docs/map-script.md`.

### Novo: mapas-catálogo Map Maker *(não jogado)*
- Três mapas pequenos para quem faz mapas, cada um uma grade compacta com o nome de cada item escrito no chão à frente dele:
  `maps\map_maker_floors.json` (**todas as peças de chão**, 349, um tile cada), `map_maker_decor.json` (**todas as decorações**, 292) e
  `map_maker_enemies.json` (**todas as unidades inimigas**, 822, os 38 chefes nas últimas fileiras). Os inimigos ficam dormindo e **só acordam
  quando levam dano** (e só o que foi atingido). `/room N` pula para um grupo de fileiras. Cada um tem chão liso com paredes em volta, e o jogador começa no meio (no de inimigos, entre os inimigos comuns e os chefes; ali o chão afina onde estão os chefes para ninguém se perder, e as unidades ficam a 10 u uma da outra). Abra um no editor de mapas e clique em **Play test**.
  Gerados a partir dos dados do jogo por `tools\make_map_maker_helper.py`; detalhes em `docs/map-format.md`. (A primeira versão era um mapa
  comprido: levava uns 5 minutos para chegar aos inimigos, então virou três mapas com os itens duas vezes mais perto.)

### Novo: ferramentas de admin, debug e login *(não jogado)*
- **Contas com senha (opcional).** `network.json` `passwordLogin: true`: um jogador novo da Steam vê a tela de registro do próprio jogo (nome, e-mail, senha);
  a tela de login do jogo (sem Steam) aceita usuário e senha. Contas que já existem continuam entrando pela Steam. `/passwd NOME SENHA` (host) redefine uma.
  A senha é guardada como hash com salt em `accounts\<chave>\login.json`. Também respondidos agora: `/user/create`, `/user/change`, `/user/reset_request`,
  `/activation/*` e `/msg_interface_config`.
- **Ferro por XP de piloto.** Quando um avião é destruído (e, como antes, quando é aposentado) o XP de piloto que ele paga também vira moeda na taxa do
  jogo (`pilotXpPer1Currency`, a partir do mínimo) e o jogo mostra "você recebeu N por X de XP de piloto" (`ClientNotificationGiveCurrencyForPilotXp`).
- **Sons enviados pelo servidor.** `/sound NOME`, `/soundall NOME` e a nova ação `sound` dos roteiros de mapa.
- **Debug e admin.** `/debug`, `/god` (ajustes de debug do avião: dano, velocidade, vida...), `/watchai`, `/screenshot [JOGADOR]`, `/stats`, `/serverstats`,
  `/audit`, `/admin`. O servidor também responde às mensagens de debug do próprio cliente e ao `ServerQuery` (unidades que o cliente nunca viu nascer).
- **Estatísticas do servidor e auditoria.** O tempo do loop de cada servidor (`/stats`; o gráfico de tempo do servidor do jogo com `/serverstats on` e
  Opções > debug display) e uma auditoria do inventário que conta as ações que o cliente informa com um id que o servidor nunca deu (ou repetido).
- Código em `Server\Admin\`, `Server\Network\AccountLogins.cs`, `Server\Enemies.Debug.cs`; detalhes em `docs/admin-tools.md`.

### Dungeons
- Valor novo do mapa `values.maxEnemies`: um mapa pode subir o limite de 140 inimigos vivos por dungeon (o mapa Map Maker de inimigos precisa de 840).
- **Iron Lair** refeita como arena em rosca (anel redondo em volta da sala quadrada onde se nasce, um SN-Z que não para de voar em volta,
  tiros lentos e minas). **Reprovou no teste de 23/11** (mortal demais: aviões de nível 1 a 9 caíram em 10 a 60 s); está sendo ajustada.
  Os tiros do SN-Z deixaram de ser teleguiados e as bombas viraram **minas**: ficam onde foram soltas, não atiram, mostram a área de disparo
  como um anel vermelho (o efeito `detectionZone_1` do próprio jogo) e só explodem, soltando o estouro, quando um jogador entra nessa área (ou atira nelas).
  Comportamento novo `mine` para quem faz mapas (`contactRadius` = raio de disparo, `visualEffect` = anel).
- Bomb Factory, Crypt of Cathulhu, Laser Playground e The Arena foram concluídas por 3 jogadores no teste; o Command Center foi jogado até a
  sala do chefe.
- O servidor de dungeon agora informa onde cada jogador está (para o launcher e o Discord).

### Launcher
- **Play test** no editor de mapas (botão no canto superior direito, `F5`): salva o mapa, inicia um servidorzinho que só tem esse mapa e abre o jogo
  entrando nele, direto no mapa como o tutorial. Sem conta para criar, sem Nexus, só neste PC, portas próprias (o servidor normal pode ficar ligado).
  O piloto de teste é um walker com equipamento tier 10 completo e o EULA e a política de privacidade já aceitos. Depois de mudar o mapa: salve e digite `/restart` no chat do jogo. Modo `--test-map` do servidor em `Server\Dungeons\MapTest.cs`. *(Ainda não jogado.)*
- **Discord Rich Presence**: seu perfil do Discord pode mostrar o avião, o nível, a patente de piloto e o lugar (Nexus, treino, mundo aberto,
  dungeon) enquanto você joga, e o nome do servidor e os pilotos online enquanto você hospeda. Nunca IP, porta, conta ou nome do jogador.
  Desligue em Settings > General.
- Os botões *Join the Discord* usam o logo do Discord.
- **O PLAY pede confirmação** quando o plugin Quieter ou o BepInEx faltam (sem o plugin o Flock Hall fica invisível, por exemplo).
- O console do servidor mostra as linhas `WARNING: ... did not arrive` na cor de aviso.
- **Monitor de chat:** nova página *Chat* na aba Server (em Monitor). Lista o que os jogadores escrevem, separado por mapa (Rebel City, mundo
  aberto, cada instância de dungeon como "Iron Lair #3"); *All maps* mostra tudo com o nome do mapa na frente. Sussurros e chat do bando
  aparecem no mapa de quem enviou e podem ser escondidos. Filtro, rolagem automática e copiar. O servidor imprime uma linha `[CHATMON]` por
  mensagem (`Server\Network\ChatMonitor.cs`); o launcher não a mostra no console. O histórico começa vazio a cada início do servidor.
- A resposta `/revive/info` do servidor tem `playerDetails` (avião, nível e lugar de cada jogador; `infoVersion` 2).

### Correções do teste de 23/11
- **Dados de outros jogadores:** o cliente pergunta de novo a cada frame quando a cópia passa de 30 s, e o servidor respondia sempre (28 mil
  respostas, ~200 por segundo, relatório de 4 MB). Agora é uma resposta por segundo para cada jogador e conta. Pedidos com id de conta vazio
  são ignorados.
- **Amigos no radar:** a lista de amigos agora vai ao cliente ao entrar em cada servidor. Antes só chegava quando a janela de amigos abria, e
  as setas dos amigos sumiam no mundo aberto.
- **Troca de servidor:** se o cliente não fechar a conexão antiga em 6 s depois de uma troca (pacote perdido em ligação ruim), o servidor fecha.
- **Porta sem encaminhar:** quando um jogador de outro PC não chega ao servidor para onde foi mandado em 20 s, o console mostra um aviso
  com a porta UDP a liberar no roteador.
- **Quests da lista de missões:** `ServerRequestQuestGoalSourceInfo` e `ServerRequestQuestRewards` são respondidos (vazio) em vez de erro ou silêncio.
- **Variantes de avião:** ao desbloquear uma, o chat avisa que se escolhe em Options > Variant. (Uma variante do paladin comprada no teste não
  foi aplicada e a causa ainda não se sabe: nenhum pedido chegou ao servidor.)
- Logs: a linha "marked as finished" sai uma vez só, e a morte mostra a última posição do avião em vez de (0, 0).
- **Parcela de dano no boss:** o texto no chão depois de um boss ("Your damage: X%") sempre dizia 100%. Agora o servidor registra a vida que
  cada jogador realmente tirou do inimigo e manda a cada um a sua parcela. Como no original, o boss só solta loot para quem fez pelo menos o
  `damageThreshold` da unidade (padrão 0,5%, `progression.json`); o XP não mudou.

### Documentação
- Novo `docs/radmin-vpn-guide.md`: passo a passo para jogar pela Radmin VPN. Ligado no `README.md`.
- `CHECKLIST.md`: o teste de 23/11 marcou o que funcionou (97 feitos, 83 parciais, 43 faltando quando isto foi escrito).

### Notas de atualização
- O `social.json` de um servidor que já existe não tem `tradeEnabled` e `tradeTaxes`: valem os padrões (ligado); acrescente as chaves para mudar.
- Mapas e configurações da 0.5.0 continuam funcionando. `maps\tutorial_walker.json` e `maps\dungeon_sneezy.json` mudaram.
- O plugin 1.22.0 e a build do cliente **201219.1413** não mudaram.
