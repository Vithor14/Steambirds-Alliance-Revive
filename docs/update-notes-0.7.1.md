# Steambirds Alliance Revive 0.7.1

Patch on top of 0.7.0. Client build **201219.1413** and plugin **1.22.0** are unchanged. Settings and maps from 0.7.0 keep working.
Patch sobre a 0.7.0. O cliente (build **201219.1413**) e o plugin (**1.22.0**) não mudam. Configurações e mapas da 0.7.0 continuam valendo.

## English

### Launcher

- **Open world: *Enemies of each biome* is its own tab.** *Server > World map* now has *Map*, *Enemies* (how enemies spawn), *Enemies of each biome* and *World settings*.
- **Bigger enemy pictures.** The rows of the biome list are twice as tall (52 instead of 26) and the picture of each enemy grows from 24 to 48 pixels.
- **Enemy AI editor: generic AIs can no longer be overwritten.** The AIs of `ai\brains\generic` (and the behavior files they use) run for every unit that has no AI of its own, so the editor never changes, saves over, renames, moves or deletes them. Loading a unit still opens its generic AI as it is; the first change of any kind turns what is open into **"new {unit} AI"**: a new AI with your changes, made for the unit in the simulator (`units` lists it, so it is offered in the AI box) and kept in the folder of that unit (`dungeons\<dungeon>` or `world`), while the generic AI goes back to what is on the disk. The simulation goes on and the banner says what happened. **Save** writes the new AI in that folder. **Save as...** (new, *File* menu), **Duplicate**, **Export** and **New AI** do the same for a generic AI: what comes out is an AI made for the selected unit, never a generic one; an imported AI is also placed in the unit's folder and made for it.
- **Enemy AI editor: the *Behavior values* fields that the movement mode does not use are greyed out.** For example *Distance* is only active in `orbit` and `wander`, *Lead* in `approach`, *Interval* in `wander`, *Face the player* in `hold`, and the built-in flight and its speed multiplier in `builtin`; the others (`straight`, `flee`, `home`) only use *Speed*. Changing the mode updates the fields at once. The weapon and contact fields are not affected.

### Fixes

- **Skill clouds follow the plane variant.** Variants such as `EpicA`..`EpicD` replace the effect of a class skill (poison, heal, damage and shield
  clouds and blasts, auras, the bite cloud) through the game's `art_replacements` config. The plane already changed, but the server sent every cloud
  with no art replacement, so the cloud always looked like the default one. The cloud spawn (`Game.Shots.cs`, `SendCloudSpawn`) now carries the network id of
  the art replacement of the variant worn by the plane that used the skill (`PlaneVariants.ArtReplacementId`), for the owner and for the players who see it.
  Only the picture changes: radius, duration, damage and healing are the same as before. The server log line of the cloud
  (`[SKILL] ... created the unit ...`) now says which variant look it used.
- **Open world: the biome enemy lists were missing many enemies.** The server only read the unit files named `GRP<n>_` and `SLO<n>_`, so everything the game filed under other names was left out (`SLO-` with a dash, `SOL`, `SPC`, `HVY`, `XXX`, `unit_`). Now they are in the lists, each one alone: tetra, blink and blinkleader (Egyptian Dig), birdcatcher and vampire (Noble Estates), duelist, marksman, barrager, hammerplanets, hyperdrive, lion, lion cub, destroyer and sentinel (Imperial Palace), hurtsnake (Hospital Area), zombie28days and zombieromero (Graveyard), remora (Fishing Zone). Missiles, bombs, lasers, fireballs and hammers fired by other units, and the Tesla/Robo tower parts, stay out. Regions you already saved in `world-enemies.json` with their own `groups` keep their list: add the new ones in *Server > World map > Enemies*, or remove the region to go back to the game's list.

## Português

### Launcher

- **Mundo aberto: *Enemies of each biome* virou uma aba própria.** *Server > World map* agora tem *Map*, *Enemies* (como os inimigos nascem), *Enemies of each biome* e *World settings*.
- **Miniaturas maiores.** As linhas da lista de biomas ficaram com o dobro da altura (52 em vez de 26) e a imagem de cada inimigo passou de 24 para 48 pixels.
- **Editor de IA dos inimigos: IAs genéricas não podem mais ser sobrescritas.** As IAs de `ai\brains\generic` (e os behaviors que elas usam) rodam para toda unidade sem IA própria, então o editor nunca as altera, salva por cima, renomeia, move nem apaga. Carregar uma unidade continua abrindo a IA genérica dela como está; a primeira alteração de qualquer tipo transforma o que está aberto em **"new {unidade} AI"**: uma IA nova com as suas mudanças, feita para a unidade do simulador (o `units` a lista, então ela aparece na caixa de IAs) e guardada na pasta daquela unidade (`dungeons\<dungeon>` ou `world`), enquanto a IA genérica volta ao que está no disco. A simulação continua e o aviso do topo explica o que aconteceu. **Salvar** grava a IA nova nessa pasta. **Salvar como...** (novo, menu *File*), **Duplicar**, **Exportar** e **Nova IA** fazem o mesmo com uma IA genérica: o que sai é uma IA feita para a unidade selecionada, nunca genérica; uma IA importada também vai para a pasta da unidade e é feita para ela.
- **Editor de IA dos inimigos: os campos de *Behavior values* que o modo de movimento não usa ficam acinzentados.** Por exemplo, *Distance* só vale em `orbit` e `wander`, *Lead* em `approach`, *Interval* em `wander`, *Face the player* em `hold`, e o voo embutido e o multiplicador dele em `builtin`; os outros (`straight`, `flee`, `home`) só usam *Speed*. Trocar o modo atualiza os campos na hora. Os campos de arma e de contato não mudam.

### Correções

- **As nuvens das habilidades seguem a variante do avião.** Variantes como `EpicA`..`EpicD` trocam o efeito da habilidade da classe (nuvens e explosões
  de veneno, cura, dano e escudo, auras, a nuvem de mordida) pela config `art_replacements` do jogo. O avião já mudava, mas o servidor mandava toda
  nuvem sem substituição de arte, então ela saía sempre com o visual padrão. O spawn da nuvem (`Game.Shots.cs`, `SendCloudSpawn`) agora leva o id de rede
  da substituição de arte da variante do avião que usou a habilidade (`PlaneVariants.ArtReplacementId`), para o dono e para quem o vê.
  Só a imagem muda: raio, duração, dano e cura continuam como antes. A linha de log da nuvem (`[SKILL] ... created the unit ...`) agora diz qual
  visual de variante foi usado.
- **Mundo aberto: faltavam vários inimigos nas listas dos biomas.** O servidor só lia os arquivos de unidade chamados `GRP<n>_` e `SLO<n>_`, então tudo que o jogo guardou com outros nomes ficava de fora (`SLO-` com hífen, `SOL`, `SPC`, `HVY`, `XXX`, `unit_`). Agora eles entram nas listas, cada um sozinho: tetra, blink e blinkleader (Egyptian Dig), birdcatcher e vampire (Noble Estates), duelist, marksman, barrager, hammerplanets, hyperdrive, lion, lion cub, destroyer e sentinel (Imperial Palace), hurtsnake (Hospital Area), zombie28days e zombieromero (Graveyard), remora (Fishing Zone). Mísseis, bombas, lasers, bolas de fogo e martelos disparados por outras unidades, e as partes das torres Tesla/Robo, continuam de fora. Regiões que você já salvou em `world-enemies.json` com `groups` próprios mantêm a lista: adicione os novos em *Server > World map > Enemies*, ou remova a região para voltar à lista do jogo.
