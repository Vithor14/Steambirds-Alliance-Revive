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

## Português

### Launcher e editor

### Mapas

### Servidor

- **Pintura de escudo do inimigo:** enquanto um escudo está ativo (estado da IA, elo de formação, ou `statusEffect: "shielded"` na unidade) o servidor liga o flag `shielded` do cliente e manda no FieldUpdate. Quando o escudo acaba, manda 0, a menos que o `statusEffect` do tipo mantenha a pintura. Os outros status (`slow`, `burn`, `blind`, …) seguem o mesmo caminho.

### Correções
