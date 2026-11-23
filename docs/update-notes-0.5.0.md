# Steambirds Alliance Revive 0.5.0

Covers the 6 commits that are not pushed yet: `a005f40`, `abd2987`, `d1eb1c0`, `2a4b976`, `7110c14`, `dcf0318`. / Cobre os 6 commits que ainda não têm push.

## English

Big update: open world, social features, a fully redesigned launcher, new editors features and many gameplay additions.
**Note:** the server-side features below were written from the client's behavior and have not been tested in the game yet. Please report anything odd.

### New: Open World
- New open world server (`Basic`, port 1114) with a generated map, regions, enemy spawn sites and carriers.
- Configurable through `world.json`.
- Launcher pages for the world map (with each player's position) and world settings.

### New: Social
- Friends list, flocks (Flock Hall), whispers and flock chat.
- Teleport to a friend and "Join Server".
- Settings in `social.json`, flock data in `flocks.json`.
- New Social page in the launcher.

### Gameplay
- **Plane variants:** changing a plane's variant is now supported (checked against class variants and account unlocks).
- **XP boost:** plane XP boost consumables work and apply to kill XP.
- **Paint and trails:** dyes and trails are saved on the plane and shown to other players. Dyes and trails without `uses` stay after being used.
- **Variants:** the variant decoration is read from the variant itself, and the plane spawn and the relay carry the variant.
- **Dungeon keys:** using a key summons a drop ship to the dungeon (Nexus only, open dungeons).
- **Career rewards:** item rewards are delivered to cargo/storage, with a pending queue if there is no room.
- **Store:** "use instantly" leaves dyes and keys in your inventory. A store product can now give other items (`"items"` in `stores.json` > `products`; name, icon and price stay). The flock founder token (`create_flock_token`) is sold by `store_key_eye` (the key of the Crypt of Cathulhu, a closed dungeon here) for 750 gold, because the game has no product for it. Tested: the purchase takes the gold, puts the token in the cargo and the flock is created with it.
- **Enemies:** turrets now visibly rotate to aim.
- **Screen effects:** spark on turret hits, camera shake on boss death and dungeon clear, and the "mission accomplished" banner. Configurable in `enemies.json` ("effects").
- **Other players' bullets** are now shown correctly, and their skills are forwarded to the cloud owner.
- **Enemy view radius:** each client only knows the enemies within `enemies.viewRadius` (`world.json`, default 90), like the original. The radar no longer points at far-away enemies. Set it to 0 to turn it off. Dungeons and the tutorial still send everything.

### Launcher redesign
- New theme built on the project branding, in dark and light.
- Main tabs grouped as Client, Server | Map editor, Enemy AI | Settings, with the server and game state in the header.
- Client tab: Servers / Game / Console sub-tabs, a Play bar that stays in view, and the Steam download button shown only when the game is missing or on the wrong build.
- Server tab: side navigation (Monitor, Game rules, Players, World, Connection), with "New accounts" split from "Accounts" and starting unlocks shown as compact pills.
- Settings in four sub-tabs (General, Game, Server, Folders and files). The server `.json` settings are now tiles with a filter box, and long explanations moved behind "?" buttons.
- Icons on every tab, new custom controls, DPI fixes and updated Portuguese translation.
- Network settings and server status improvements.

### Map editor and Enemy AI editor
- **Pictures in lists**: unit types, enemy groups of the rooms, decoration and floor pieces show the game picture of each one, and so do the drop-downs that pick a game unit, a unit type or a ground asset. Without the `editor-assets` folder nothing changes.
- **Units tab**: what the Enemy AI editor already says is gone from the tab (built-in `behavior`, aggro radius, preferred distance, contact radius, shooting range, aim lead, weapons; and `behavior` / `guns` of boss phases, which now have an `ai` field to switch the boss to another AI). Values already in a map are kept and still work when a type has no AI.
- **Open in the AI editor** (Units → Enemy AI): goes to the Enemy AI tab with the unit type already in the simulator (`map type: ...`: game unit, multipliers, aim lead, free aim and its AI open in the editor).
- **Help** menu and `F1` in both editors: a topic for every menu, tool, option and tab, and what each one causes in the game.
- The Enemy AI buttons are now a **menu bar** like the map editor's (File, Edit, Help; Ctrl+S, Ctrl+Shift+S, Ctrl+N).
- Weapons (mounts) is a checklist made from the guns of the unit in the simulator; clicking a name shows its data (pattern, arc, fire rate, burst, damage, reach, shot type). Every gun has its own color in the simulator.

### Upgrade notes
- Copy your `accounts\` folder over as usual. New settings files (`world.json`, `social.json`, `flocks.json`) are created or filled in with defaults.
- Rebuild the plugin and re-run `SteambirdsGameFiles.exe`: `units_config.txt` gun lines have a new last column (the shot type) that the Enemy AI editor shows. The client build is still **201219.1413**.
- Launcher version is now 0.5.0. The new texts are translated in `pt-BR.json`.

## Português

Grande atualização: mundo aberto, recursos sociais, launcher totalmente redesenhado, novidades nos editores e muitas adições de jogabilidade.
**Atenção:** os recursos do servidor abaixo foram escritos a partir do comportamento do cliente e ainda não foram testados no jogo. Avise se algo estranho aparecer.

### Novo: mundo aberto
- Novo servidor de mundo aberto (`Basic`, porta 1114) com mapa gerado, regiões, pontos de aparição de inimigos e carriers.
- Configurável pelo `world.json`.
- Páginas do launcher para o mapa do mundo (com a posição de cada jogador) e para as configurações do mundo.

### Novo: social
- Lista de amigos, flocks (Flock Hall), sussurros e chat do flock.
- Teleporte até um amigo e "Join Server".
- Configurações em `social.json`, dados dos flocks em `flocks.json`.
- Nova página Social no launcher.

### Jogabilidade
- **Variantes de avião:** trocar a variante de um avião agora é suportado (conferido com as variantes da classe e os desbloqueios da conta).
- **Bônus de XP:** os consumíveis de bônus de XP do avião funcionam e valem para o XP de abate.
- **Pintura e rastros:** tintas e rastros ficam salvos no avião e aparecem para os outros jogadores. Tintas e rastros sem `uses` continuam depois de usados.
- **Variantes:** a decoração da variante é lida da própria variante, e o spawn do avião e o relay levam a variante.
- **Chaves de dungeon:** usar uma chave chama uma drop ship até a dungeon (só no Nexus, dungeons abertas).
- **Recompensas de carreira:** as recompensas em item vão para o cargo/armazém, com uma fila pendente se não houver espaço.
- **Loja:** "usar na hora" deixa tintas e chaves no inventário. Um produto da loja agora pode dar outros itens (`"items"` em `stores.json` > `products`; nome, ícone e preço continuam). O token de fundador de flock (`create_flock_token`) é vendido pelo `store_key_eye` (a chave da Crypt of Cathulhu, dungeon fechada aqui) por 750 de ouro, porque o jogo não tem produto para ele. Testado: a compra tira o ouro, põe o token no cargo e o flock é criado com ele.
- **Inimigos:** as torres agora giram visivelmente para mirar.
- **Efeitos de tela:** faísca ao acertar torres, tremor de câmera na morte de chefe e ao limpar a dungeon, e o banner de "missão cumprida". Configurável no `enemies.json` ("effects").
- **Balas dos outros jogadores** agora aparecem corretamente, e as habilidades deles são repassadas ao dono da nuvem.
- **Raio de visão de inimigos:** cada cliente só conhece os inimigos dentro de `enemies.viewRadius` (`world.json`, padrão 90), como no original. O radar não aponta mais para inimigos distantes. Use 0 para desligar. Dungeons e o tutorial continuam enviando tudo.

### Redesign do launcher
- Novo tema feito sobre a marca do projeto, escuro e claro.
- Abas principais agrupadas como Cliente, Servidor | Editor de mapas, IA dos inimigos | Configurações, com o estado do servidor e do jogo no cabeçalho.
- Aba Cliente: sub-abas Servidores / Jogo / Console, barra de Jogar sempre à vista, e o botão de download da Steam só aparece quando o jogo falta ou está na build errada.
- Aba Servidor: navegação lateral (Monitor, Regras do jogo, Jogadores, Mundo, Conexão), com "Contas novas" separada de "Contas" e os desbloqueios iniciais em pílulas compactas.
- Configurações em quatro sub-abas (Geral, Jogo, Servidor, Pastas e arquivos). As configurações `.json` do servidor viraram blocos com caixa de filtro, e as explicações longas foram para botões "?".
- Ícones em todas as abas, controles novos, correções de DPI e tradução em português atualizada.
- Melhorias nas configurações de rede e no estado do servidor.

### Editor de mapas e editor de IA
- **Imagens nas listas**: tipos de unidade, grupos de inimigos das salas, decoração e peças de chão mostram a imagem de cada um, assim como os seletores de unidade do jogo, tipo de unidade e ground asset. Sem a pasta `editor-assets` nada muda.
- **Aba Unidades**: saiu o que o editor de IA já diz (`behavior` embutido, raio de aggro, distância preferida, raio de contato, alcance de tiro, antecipação da mira, armas; e `behavior` / `guns` das fases de chefe, que agora têm o campo `ai` para trocar o chefe de IA). Valores que já estão nos mapas são mantidos e continuam valendo quando o tipo não tem IA.
- **Abrir no editor de IA** (Unidades → IA dos inimigos): vai para a aba IA dos inimigos com o tipo de unidade já no simulador (`map type: ...`: unidade do jogo, multiplicadores, antecipação da mira, mira livre e a IA dele aberta no editor).
- Menu **Ajuda** e `F1` nos dois editores: um tópico para cada menu, ferramenta, opção e aba, e o que cada uma causa no jogo.
- Os botões do editor de IA viraram uma **barra de menus** igual à do editor de mapas (Arquivo, Editar, Ajuda; Ctrl+S, Ctrl+Shift+S, Ctrl+N).
- "Armas (suportes)" é uma lista de marcar feita com as armas da unidade do simulador; clicar no nome mostra os dados (padrão, arco, cadência, rajada, dano, alcance, tipo de tiro). Cada arma tem uma cor no simulador.

### Notas para atualizar
- Copie a pasta `accounts\` como de costume. Os arquivos de configuração novos (`world.json`, `social.json`, `flocks.json`) são criados ou preenchidos com os padrões.
- Recompile o plugin e rode o `SteambirdsGameFiles.exe` de novo: as linhas `gun` do `units_config.txt` ganharam uma última coluna (o tipo de tiro) que o editor de IA mostra. A build do cliente continua **201219.1413**.
- A versão do launcher agora é 0.5.0. Os textos novos estão traduzidos em `pt-BR.json`.
