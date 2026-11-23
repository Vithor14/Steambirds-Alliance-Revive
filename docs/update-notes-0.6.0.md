# Steambirds Alliance Revive 0.6.0 (in progress)

**Status: not released.** This file is fed with every change until 0.6.0 is out; add a line here (English and Portuguese) in the same change
that touches the game, the server, the launcher or the editors. The version number of the launcher and the server is still 0.5.0 in the
code: it changes to 0.6.0 when the release is built. / **Em andamento:** este arquivo recebe cada alteração até o lançamento da 0.6.0.

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

### Dungeons
- **Iron Lair** rebuilt as a donut arena (a round ring around the square room where you are born, a SN-Z that keeps flying around it, slow
  homing shots and bombs). **It failed the 23/11 test** (too deadly: planes of level 1 to 9 fell in 10 to 60 s); it is being tuned.
- Bomb Factory, Crypt of Cathulhu, Laser Playground and The Arena were completed by 3 players in the test; the Command Center was played
  up to the boss room.
- The dungeon server now reports where each player is (for the launcher and Discord).

### Launcher
- **Discord Rich Presence**: your Discord profile can show your plane, its level, your pilot rank and the place (Nexus, tutorial, open
  world, dungeon) while you play, and your server name and pilots online while you host. Never the IP, port, account or player name.
  Turn it off in Settings > General.
- The *Join the Discord* buttons use the Discord logo.
- **PLAY asks for confirmation** when the Quieter plugin or BepInEx is missing (without the plugin the Flock Hall is invisible, for one).
- The server console shows `WARNING: ... did not arrive` lines in the warning color.
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

### Docs
- New `docs/radmin-vpn-guide.md`: step by step to play over Radmin VPN. Linked from the `README.md`.
- `CHECKLIST.md`: the 23/11 test marked what worked (97 done, 83 partial, 43 missing at the time of writing).

### Upgrade notes
- `social.json` of an existing server does not have `tradeEnabled` and `tradeTaxes`: the defaults (on) apply; add the keys to change them.
- Maps and settings of 0.5.0 keep working. `maps\tutorial_walker.json` and `maps\dungeon_sneezy.json` changed.
- Plugin 1.22.0 and the client build **201219.1413** are unchanged.

## Português

**Em andamento, não lançada.** Este arquivo recebe uma linha (em inglês e em português) a cada alteração do jogo, do servidor, do launcher
ou dos editores, até a 0.6.0 sair. O número de versão do launcher e do servidor ainda é 0.5.0 no código: vira 0.6.0 quando o release for
montado.

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

### Dungeons
- **Iron Lair** refeita como arena em rosca (anel redondo em volta da sala quadrada onde se nasce, um SN-Z que não para de voar em volta,
  tiros teleguiados lentos e bombas). **Reprovou no teste de 23/11** (mortal demais: aviões de nível 1 a 9 caíram em 10 a 60 s); está sendo ajustada.
- Bomb Factory, Crypt of Cathulhu, Laser Playground e The Arena foram concluídas por 3 jogadores no teste; o Command Center foi jogado até a
  sala do chefe.
- O servidor de dungeon agora informa onde cada jogador está (para o launcher e o Discord).

### Launcher
- **Discord Rich Presence**: seu perfil do Discord pode mostrar o avião, o nível, a patente de piloto e o lugar (Nexus, treino, mundo aberto,
  dungeon) enquanto você joga, e o nome do servidor e os pilotos online enquanto você hospeda. Nunca IP, porta, conta ou nome do jogador.
  Desligue em Settings > General.
- Os botões *Join the Discord* usam o logo do Discord.
- **O PLAY pede confirmação** quando o plugin Quieter ou o BepInEx faltam (sem o plugin o Flock Hall fica invisível, por exemplo).
- O console do servidor mostra as linhas `WARNING: ... did not arrive` na cor de aviso.
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

### Documentação
- Novo `docs/radmin-vpn-guide.md`: passo a passo para jogar pela Radmin VPN. Ligado no `README.md`.
- `CHECKLIST.md`: o teste de 23/11 marcou o que funcionou (97 feitos, 83 parciais, 43 faltando quando isto foi escrito).

### Notas de atualização
- O `social.json` de um servidor que já existe não tem `tradeEnabled` e `tradeTaxes`: valem os padrões (ligado); acrescente as chaves para mudar.
- Mapas e configurações da 0.5.0 continuam funcionando. `maps\tutorial_walker.json` e `maps\dungeon_sneezy.json` mudaram.
- O plugin 1.22.0 e a build do cliente **201219.1413** não mudaram.
