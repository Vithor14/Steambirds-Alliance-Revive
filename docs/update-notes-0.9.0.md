# Steambirds Alliance Revive 0.9.0 (in progress)

**Status: not released.** This file is fed with every change until 0.9.0 is out; add a line here (English and Portuguese) in the same change
that touches the game, the server, the launcher or the editors. / **Em andamento:** este arquivo recebe cada alteração até o lançamento da 0.9.0.

Base: 0.8.1 (27/11/2026). / Base: 0.8.1 (27/11/2026).

## English

### Launcher and editor

### Maps

### Server

- **Enemy shield paint:** while a shield is up (an AI state, a formation link, or `statusEffect: "shielded"` on the unit) the server sets the client flag `shielded` and sends it in FieldUpdate. When the shield ends it sends 0, unless the type's `statusEffect` keeps the paint. The other status names (`slow`, `burn`, `blind`, …) are sent the same way.

### Fixes

- **Plugin build finds the game on any drive.** `QuieterPlugin.csproj`, `Build plugin.ps1`, `Build launcher.ps1` and `Clean project.ps1` look for depot 386011 in `STEAMBIRDS_GAME`, then in the Steam folder from the registry (like the launcher), then in the default `C:\Program Files (x86)\Steam`. `Build plugin.ps1` no longer needs the repo at `C:\Steambirds`.

## Português

### Launcher e editor

### Mapas

### Servidor

- **Pintura de escudo do inimigo:** enquanto um escudo está ativo (estado da IA, elo de formação, ou `statusEffect: "shielded"` na unidade) o servidor liga o flag `shielded` do cliente e manda no FieldUpdate. Quando o escudo acaba, manda 0, a menos que o `statusEffect` do tipo mantenha a pintura. Os outros status (`slow`, `burn`, `blind`, …) seguem o mesmo caminho.

### Correções

- **Build do plugin acha o jogo em qualquer disco.** `QuieterPlugin.csproj`, `Build plugin.ps1`, `Build launcher.ps1` e `Clean project.ps1` procuram o depot 386011 em `STEAMBIRDS_GAME`, depois na pasta da Steam pelo registro (como o launcher) e por último no padrão `C:\Program Files (x86)\Steam`. O `Build plugin.ps1` não exige mais o repositório em `C:\Steambirds`.
