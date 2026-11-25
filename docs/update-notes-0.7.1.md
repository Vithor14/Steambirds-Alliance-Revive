# Steambirds Alliance Revive 0.7.1

Patch on top of 0.7.0. Client build **201219.1413** and plugin **1.22.0** are unchanged. Settings and maps from 0.7.0 keep working.
Patch sobre a 0.7.0. O cliente (build **201219.1413**) e o plugin (**1.22.0**) não mudam. Configurações e mapas da 0.7.0 continuam valendo.

## English

### Launcher

- **Open world: *Enemies of each biome* is its own tab.** *Server > World map* now has *Map*, *Enemies* (how enemies spawn), *Enemies of each biome* and *World settings*.
- **Bigger enemy pictures.** The rows of the biome list are twice as tall (52 instead of 26) and the picture of each enemy grows from 24 to 48 pixels.

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

### Correções

- **As nuvens das habilidades seguem a variante do avião.** Variantes como `EpicA`..`EpicD` trocam o efeito da habilidade da classe (nuvens e explosões
  de veneno, cura, dano e escudo, auras, a nuvem de mordida) pela config `art_replacements` do jogo. O avião já mudava, mas o servidor mandava toda
  nuvem sem substituição de arte, então ela saía sempre com o visual padrão. O spawn da nuvem (`Game.Shots.cs`, `SendCloudSpawn`) agora leva o id de rede
  da substituição de arte da variante do avião que usou a habilidade (`PlaneVariants.ArtReplacementId`), para o dono e para quem o vê.
  Só a imagem muda: raio, duração, dano e cura continuam como antes. A linha de log da nuvem (`[SKILL] ... created the unit ...`) agora diz qual
  visual de variante foi usado.
- **Mundo aberto: faltavam vários inimigos nas listas dos biomas.** O servidor só lia os arquivos de unidade chamados `GRP<n>_` e `SLO<n>_`, então tudo que o jogo guardou com outros nomes ficava de fora (`SLO-` com hífen, `SOL`, `SPC`, `HVY`, `XXX`, `unit_`). Agora eles entram nas listas, cada um sozinho: tetra, blink e blinkleader (Egyptian Dig), birdcatcher e vampire (Noble Estates), duelist, marksman, barrager, hammerplanets, hyperdrive, lion, lion cub, destroyer e sentinel (Imperial Palace), hurtsnake (Hospital Area), zombie28days e zombieromero (Graveyard), remora (Fishing Zone). Mísseis, bombas, lasers, bolas de fogo e martelos disparados por outras unidades, e as partes das torres Tesla/Robo, continuam de fora. Regiões que você já salvou em `world-enemies.json` com `groups` próprios mantêm a lista: adicione os novos em *Server > World map > Enemies*, ou remova a região para voltar à lista do jogo.
