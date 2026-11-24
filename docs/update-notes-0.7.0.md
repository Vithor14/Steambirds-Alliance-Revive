# Steambirds Alliance Revive 0.7.0 (in progress)

**Status: not released.** This file is fed with every change until 0.7.0 is out; add a line here (English and Portuguese) in the same change
that touches the game, the server, the launcher or the editors. / **Em andamento:** este arquivo recebe cada alteração até o lançamento da 0.7.0.

Base: `1b6148c` (the last commit of 0.6.0). / Base: `1b6148c` (último commit da 0.6.0).

## English

### Launcher and editor

- **Choose the Nexus and tutorial maps:** the Server tab, *Maps and dungeons* page, now has a working choice of map file for the Nexus (Rebel City) and for the tutorial (Training Grounds). It is saved in `dungeons.json` (`nexusMap`, `tutorialMap`; the originals are `nexus` and `tutorial_walker`) and the server reads it again by itself: the new map applies to the next player who joins the Nexus or starts the tutorial. A name without a file falls back to the original map (logged). To make a version, open `nexus` or `tutorial_walker` in the Map editor, change it (buildings, spawn, floor, the tutorial `script`...) and use *Save as* with a new name.
- **Open in the editor:** each map of that page (Nexus, tutorial and the dungeon of the selected row) has a button that loads the map file in the Map editor tab and goes to it (it asks first about changes the editor has not saved). It replaces the greyed-out "View the selected map".

### Fixes

- **Safe trade:** a trade request was declined by itself about 1.3 s after it was sent, and the other player then got "no trade to answer". The game client declines on its own when the requester's plane leaves the small trigger area spawned at the request. The server now ignores a decline that arrives in the first 4 s, keeps the request open and tells the target to type `/trade`.

## Português

### Launcher e editor

- **Escolher os mapas do Nexus e do tutorial:** a página *Maps and dungeons* da aba Server agora tem a escolha do arquivo de mapa do Nexus (Rebel City) e do tutorial (Training Grounds). Fica salva no `dungeons.json` (`nexusMap`, `tutorialMap`; os originais são `nexus` e `tutorial_walker`) e o servidor relê sozinho: o mapa novo vale para o próximo jogador que entrar no Nexus ou começar o tutorial. Um nome sem arquivo volta ao mapa original (com aviso no log). Para fazer uma versão, abra `nexus` ou `tutorial_walker` no editor de mapas, altere (prédios, ponto de entrada, chão, o `script` do tutorial...) e use *Save as* com outro nome.
- **Abrir no editor:** cada mapa dessa página (Nexus, tutorial e a dungeon da linha selecionada) tem um botão que carrega o arquivo no editor de mapas e vai para a aba (antes pergunta sobre alterações não salvas no editor). Substitui o "View the selected map" acinzentado.

### Correções

- **Troca segura:** o pedido de troca era recusado sozinho cerca de 1,3 s depois de enviado, e o outro jogador recebia "no trade to answer". O cliente do jogo recusa por conta própria quando o avião de quem pediu sai da pequena área (gatilho) criada no pedido. Agora o servidor ignora uma recusa que chegue nos primeiros 4 s, mantém o pedido aberto e avisa o alvo para digitar `/trade`.
