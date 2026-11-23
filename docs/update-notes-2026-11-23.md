# Update notes — 23/11/2026

Everything below is in the commit that follows `2a4b976` (not pushed yet). / Tudo abaixo está no commit depois do `2a4b976` (ainda sem push).

## English

### Launcher: map editor
- **Pictures in lists**: unit types, enemy groups of the rooms, decoration and floor pieces show the game picture of each one. The drop-downs that pick a game unit, a unit type or a ground asset (and the Enemy type / Gate type boxes of the tool palette) show it too. Without the `editor-assets` folder nothing changes.
- **Units tab**: what the Enemy AI editor already says is gone from the tab (built-in `behavior`, aggro radius, preferred distance, contact radius, shooting range, aim lead, weapons; and `behavior` / `guns` of boss phases, which now have an `ai` field to switch the boss to another AI). Values already in a map are kept and still work when a type has no AI.
- **Open in the AI editor** (Units → Enemy AI): goes to the Enemy AI tab with the unit type already in the simulator (`map type: ...`: its game unit, speed / turn / fire-rate multipliers, aim lead, free aim and its AI open in the editor).
- **Help** menu and `F1`: a topic for every menu, tool, option and tab, and what each one causes in the game.

### Launcher: Enemy AI editor
- The buttons at the top are now a **menu bar** like the map editor's: File, Edit, Help. Shortcuts: Ctrl+S, Ctrl+Shift+S, Ctrl+N, F1.
- **Help** with 11 topics (states, transitions, movement, weapons, simulator, using an AI in the game, sharing, limits).
- Weapons (mounts) is a checklist made from the guns of the unit in the simulator; clicking a name shows its data (pattern, arc, fire rate, burst, damage, reach, shot type). Every gun has its own color in the simulator.
- The editor only edits the AI of the unit in the simulator; other files are loaded on the unit with *Load on the unit*.

### Plugin / shared code
- `units_config.txt` gun lines have a new last column, the gun type (shot type). Rebuild the plugin and export the game files again to get it.
- `Shared\EnemyAi`: names and explanations of the gun patterns, and extra mount data for the launcher.

### Translations
- `pt-BR.json`: all new texts, including the whole Help of both editors.

### Docs
- `Launcher\README.md`, `Launcher\Editor\README.md`, `docs\enemy-ai.md` and `CHECKLIST.md` updated.

### Not verified
- Compiled and the launcher opens without errors; the pictures in the lists, the Help window and the AI menu were checked by the maintainer in the launcher. Plugin change (gun type column) needs a plugin rebuild.

## Português

### Launcher: editor de mapas
- **Imagens nas listas**: tipos de unidade, grupos de inimigos das salas, decoração e peças de chão mostram a imagem de cada um. Os seletores de unidade do jogo, tipo de unidade e ground asset (e as caixas Tipo de inimigo / Tipo de portão da paleta) também. Sem a pasta `editor-assets` nada muda.
- **Aba Unidades**: saiu o que o editor de IA já diz (`behavior` embutido, raio de aggro, distância preferida, raio de contato, alcance de tiro, antecipação da mira, armas; e `behavior` / `guns` das fases de chefe, que agora têm o campo `ai` para trocar o chefe de IA). Valores que já estão nos mapas são mantidos e continuam valendo quando o tipo não tem IA.
- **Abrir no editor de IA** (Unidades → IA dos inimigos): vai para a aba IA dos inimigos com o tipo de unidade já no simulador (`map type: ...`: a unidade do jogo, multiplicadores de velocidade / giro / cadência, antecipação da mira, mira livre e a IA dele aberta no editor).
- Menu **Ajuda** e `F1`: um tópico para cada menu, ferramenta, opção e aba, e o que cada uma causa no jogo.

### Launcher: editor de IA
- Os botões do topo viraram uma **barra de menus** igual à do editor de mapas: Arquivo, Editar, Ajuda. Atalhos: Ctrl+S, Ctrl+Shift+S, Ctrl+N, F1.
- **Ajuda** com 11 tópicos (estados, transições, movimento, armas, simulador, usar uma IA no jogo, compartilhar, limites).
- "Armas (suportes)" é uma lista de marcar feita com as armas da unidade do simulador; clicar no nome mostra os dados (padrão, arco, cadência, rajada, dano, alcance, tipo de tiro). Cada arma tem uma cor no simulador.
- O editor só edita a IA da unidade que está no simulador; outros arquivos entram na unidade com *Carregar na unidade*.

### Plugin / código compartilhado
- As linhas `gun` do `units_config.txt` ganharam uma última coluna, o tipo de tiro. Recompile o plugin e exporte os arquivos do jogo de novo para tê-la.
- `Shared\EnemyAi`: nomes e explicações dos padrões de arma e dados extras do mount para o launcher.

### Traduções
- `pt-BR.json`: todos os textos novos, inclusive a Ajuda inteira dos dois editores.

### Docs
- `Launcher\README.md`, `Launcher\Editor\README.md`, `docs\enemy-ai.md` e `CHECKLIST.md` atualizados.

### Verificação
- Compilado e o launcher abre sem erros; as imagens nas listas, a janela de Ajuda e o menu da IA foram conferidos pelo mantenedor no launcher. A mudança do plugin (coluna do tipo de tiro) precisa de recompilação do plugin.
