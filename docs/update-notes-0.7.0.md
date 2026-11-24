# Steambirds Alliance Revive 0.7.0 (in progress)

**Status: not released.** This file is fed with every change until 0.7.0 is out; add a line here (English and Portuguese) in the same change
that touches the game, the server, the launcher or the editors. / **Em andamento:** este arquivo recebe cada alteração até o lançamento da 0.7.0.

Base: `1b6148c` (the last commit of 0.6.0). / Base: `1b6148c` (último commit da 0.6.0).

## English

### Fixes

- **Safe trade:** a trade request was declined by itself about 1.3 s after it was sent, and the other player then got "no trade to answer". The game client declines on its own when the requester's plane leaves the small trigger area spawned at the request. The server now ignores a decline that arrives in the first 4 s, keeps the request open and tells the target to type `/trade`.

## Português

### Correções

- **Troca segura:** o pedido de troca era recusado sozinho cerca de 1,3 s depois de enviado, e o outro jogador recebia "no trade to answer". O cliente do jogo recusa por conta própria quando o avião de quem pediu sai da pequena área (gatilho) criada no pedido. Agora o servidor ignora uma recusa que chegue nos primeiros 4 s, mantém o pedido aberto e avisa o alvo para digitar `/trade`.
