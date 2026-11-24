# Steambirds Alliance Revive 0.7.0 (in progress)

**Status: not released.** This file is fed with every change until 0.7.0 is out; add a line here (English and Portuguese) in the same change
that touches the game, the server, the launcher or the editors. / **Em andamento:** este arquivo recebe cada alteração até o lançamento da 0.7.0.

Base: `1b6148c` (the last commit of 0.6.0). / Base: `1b6148c` (último commit da 0.6.0).

## English

### Launcher and editor

- **Choose the Nexus and tutorial maps:** the Server tab, *Maps and dungeons* page, now has a working choice of map file for the Nexus (Rebel City) and for the tutorial (Training Grounds). It is saved in `dungeons.json` (`nexusMap`, `tutorialMap`; the originals are `nexus` and `tutorial_walker`) and the server reads it again by itself: the new map applies to the next player who joins the Nexus or starts the tutorial. A name without a file falls back to the original map (logged). To make a version, open `nexus` or `tutorial_walker` in the Map editor, change it (buildings, spawn, floor, the tutorial `script`...) and use *Save as* with a new name.
- **Server console commands:** the Console page of the Server tab has a command bar. *Run for* picks the player (the game of this PC, all players online, or one player of the list, read from the server), the box takes a chat command (Enter sends it, the arrow keys bring back the last ones) and *Command list...* opens the list of the server's commands (about 70, with a filter and a description each). The launcher writes the line to the standard input of the server and `Server\Admin\ConsoleCommands.cs` runs the command as that player (`Game.RunCommand`, on the thread of the server the player is on), ignoring `chatCommands` of `network.json`. The answer goes to the player's chat and a `[CONSOLE]` line to the console. The same lines work in a terminal: `NAME<TAB>/command`, `*<TAB>/command` or just `/command` (the game of this PC). `/players` lists who is online. Replaces the greyed-out command box.
- **Map preview:** the *Maps and dungeons* page has a *Map preview* card next to the table with a picture of the map of the selected row (or of the Nexus / tutorial choice), drawn by the map editor's own canvas. Without the game pictures the pieces of the Nexus floor are flat squares (the editor does the same now).
- **First-run window runs SteambirdsGameFiles.exe:** after the game is downloaded with Steam (the window of the first run and the Client tab), the launcher runs `SteambirdsGameFiles.exe --game <folder> --no-pause` in a window that shows its output (with Cancel) and, when it ends well, reloads the game data. If the tool is not next to the launcher the message says so.
- **Open in the editor:** each map of that page (Nexus, tutorial and the dungeon of the selected row) has a button that loads the map file in the Map editor tab and goes to it (it asks first about changes the editor has not saved). It replaces the greyed-out "View the selected map".

### Fixes

- **Safe trade:** a trade request was declined by itself about 1.3 s after it was sent, and the other player then got "no trade to answer". The game client declines on its own when the requester's plane leaves the small trigger area spawned at the request. The server now ignores a decline that arrives in the first 4 s, keeps the request open and tells the target to type `/trade`.

## Português

### Launcher e editor

- **Escolher os mapas do Nexus e do tutorial:** a página *Maps and dungeons* da aba Server agora tem a escolha do arquivo de mapa do Nexus (Rebel City) e do tutorial (Training Grounds). Fica salva no `dungeons.json` (`nexusMap`, `tutorialMap`; os originais são `nexus` e `tutorial_walker`) e o servidor relê sozinho: o mapa novo vale para o próximo jogador que entrar no Nexus ou começar o tutorial. Um nome sem arquivo volta ao mapa original (com aviso no log). Para fazer uma versão, abra `nexus` ou `tutorial_walker` no editor de mapas, altere (prédios, ponto de entrada, chão, o `script` do tutorial...) e use *Save as* com outro nome.
- **Comandos no console do servidor:** a página Console da aba Server tem uma barra de comandos. *Run for* escolhe o jogador (o jogo deste PC, todos os jogadores online ou um jogador da lista, lida do servidor), a caixa recebe um comando de chat (Enter envia, as setas trazem os últimos) e *Command list...* abre a lista de comandos do servidor (cerca de 70, com filtro e uma descrição de cada). O launcher escreve a linha no stdin do servidor e `Server\Admin\ConsoleCommands.cs` roda o comando como aquele jogador (`Game.RunCommand`, na thread do servidor em que ele está), ignorando o `chatCommands` do `network.json`. A resposta vai para o chat do jogador e uma linha `[CONSOLE]` para o console. As mesmas linhas funcionam num terminal: `NOME<TAB>/comando`, `*<TAB>/comando` ou só `/comando` (o jogo deste PC). `/players` lista quem está online. Substitui a caixa de comando acinzentada.
- **Prévia do mapa:** a página *Maps and dungeons* tem um cartão *Map preview* ao lado da tabela com a imagem do mapa da linha selecionada (ou do Nexus / tutorial escolhido), desenhada pelo próprio canvas do editor de mapas. Sem as imagens do jogo, as peças do chão do Nexus aparecem como quadrados lisos (o editor também faz isso agora).
- **A janela de primeira execução roda o SteambirdsGameFiles.exe:** depois que o jogo é baixado pela Steam (janela de primeira execução e aba Client), o launcher roda `SteambirdsGameFiles.exe --game <pasta> --no-pause` numa janela que mostra a saída (com Cancelar) e, se terminar bem, recarrega os dados do jogo. Se a ferramenta não estiver ao lado do launcher, a mensagem avisa.
- **Abrir no editor:** cada mapa dessa página (Nexus, tutorial e a dungeon da linha selecionada) tem um botão que carrega o arquivo no editor de mapas e vai para a aba (antes pergunta sobre alterações não salvas no editor). Substitui o "View the selected map" acinzentado.

### Correções

- **Troca segura:** o pedido de troca era recusado sozinho cerca de 1,3 s depois de enviado, e o outro jogador recebia "no trade to answer". O cliente do jogo recusa por conta própria quando o avião de quem pediu sai da pequena área (gatilho) criada no pedido. Agora o servidor ignora uma recusa que chegue nos primeiros 4 s, mantém o pedido aberto e avisa o alvo para digitar `/trade`.
