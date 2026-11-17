# Mapas do esboço

Cada mapa que o servidor usa é um arquivo `.json` nesta pasta. O nome do arquivo é o id do mapa.

| Arquivo | Mapa | Quem usa |
|---|---|---|
| `nexus.json` | Rebel City (o Nexus) | `NexusEsboco.cs` |
| `tutorial_walker.json` | Training Grounds (tutorial do walker) | `TutorialEsboco.cs` (o roteiro continua no código e pega os lugares daqui pelo nome) |
| `dungeon_*.json` (17) | Dungeons da build 190828 (veja a tabela abaixo) | `Dungeons\DungeonServer.cs` (servidor de dungeon, porta 1113). Quais dungeons ficam abertas e qual arquivo cada uma usa: `dungeons.json` (veja `README-dungeons.md`) |

O servidor procura a pasta `mapas` primeiro onde ele roda (`Server\maps`) e depois ao lado do executável. Ele só lê o arquivo do mapa que pede, então os arquivos de dungeon não mudam nada no Nexus nem no tutorial.

**Recarga:** o servidor relê o arquivo quando ele muda, na próxima vez que o mapa é pedido. No Nexus isso acontece quando um jogador entra. No tutorial, quando uma sessão começa (jogador entrando ou `/restart`). Se o arquivo novo tiver erro, o servidor continua com a versão anterior e escreve o motivo no log (`[MAPA] ERRO ...`). Sem nenhuma versão boa, o servidor não sobe e mostra o motivo.

O arquivo é JSON comum. O servidor também aceita comentários `//` e vírgula sobrando no fim de uma lista, mas um editor que regrave o arquivo pode perder os comentários. Para anotar alguma coisa, use um campo `"nota"`: o servidor ignora campos que não conhece.

## Coordenadas

O cliente desenha o chão numa grade de tiles de 12 unidades, girada 45° na tela. Os mapas usam essa grade:

- `u`: unidades ao longo do eixo **a** da grade (na tela, a diagonal para cima e para a direita).
- `v`: unidades ao longo do eixo **b** (na tela, a diagonal para cima e para a esquerda).
- No mundo: `x = (u − v) / √2` e `y = (u + v) / √2`. O ponto `u = 0, v = 0` é o vértice de tile que fica na origem do mundo.
- Medidas fixas:
  - 1 tile = 12 u. Tile `(a, b)` vai de `u = 12a` até `12a + 12`.
  - 1 slot de peça de chão = 6 u. Slot `(p, q)` vai de `u = 6p` até `6p + 6`; o centro fica em `6p + 3`.
  - 1 sub-célula de muro = 3 u. Sub-célula `(si, sj)` vai de `u = 3si` até `3si + 3`.
- Rotação (`rot`): graus no mundo, com 0 = +y e 90 = +x, sentido horário (o mesmo das entidades). Uma rotação de 45 alinha a coisa com a grade.
  - 315 = de frente para +v (o fundo da dungeon), 135 = de frente para −v (a entrada), 45 = +u, 225 = −u.
  - **Exceção:** em `chao.pecas`, `rot` é medida em relação ao tile, anti-horária, de 2 em 2 graus.

## Campos

```jsonc
{
  "formato": "steambirds-esboco-mapa",   // obrigatório
  "versao": 1,                            // obrigatório
  "id": "nexus",                          // igual ao nome do arquivo
  "nome": "Rebel City",                   // aparece no log e nas mensagens
  "terreno": "Nexus",                     // terreno do cliente (ClientMapInfo, terreno_config_*.txt)
  "territorio": 37,                       // TerritoryTileConfig de todos os tiles: luz, neblina, cor do minimapa, muros

  "chao": {
    "murosNasBordas": true,     // muro automático na borda de um tile com chão quando o vizinho daquele lado não tem chão
    "vazioEmVolta": 4,          // anel de tiles sem chão (abismo escuro) em volta do chão, em tiles; 0 = nenhum
    "territorioDoVazio": 74,    // território usado só para escolher o "chão" do abismo (74 = dungeonsky, sem chão)
    "pecasDoDesenho": [61856, 61857, 61858, 61851],   // peças sorteadas (sempre iguais) nos slots do desenho
    "desenho": {
      "siInicial": -8, "sjInicial": 0,
      "linhas": ["######..######", "..."]   // veja "Desenho" abaixo
    },
    "pecas": [ {"p": 1, "q": 1, "id": 29470, "nome": "RebelCity_Runway_FunnelA", "rot": 0} ]
  },

  "enfeites": [ {"id": 47411, "nome": "RebelCity_LowerRock01", "u": -7, "v": 36, "z": 0.5, "rot": 45} ],
  "predios":  [ {"prefab": 24, "nome": "Loja (Store)", "u": 3, "v": 15, "rot": 315, "loja": "store_general"} ],
  "nascimento": {"u": 3, "v": 3, "rot": 45},

  "pontos":   { "instrutor": {"u": 6, "v": 35.4, "rot": 135} },
  "grupos":   { "torres": [ {"u": -1.5, "v": 40.5, "rot": 315} ] },
  "areas":    { "hangar": {"minU": -5.4, "maxU": 17.4, "minV": 31.2, "maxV": 73.8} },
  "linhas":   { "instrutor": 29.4 },
  "valores":  { "anelQuantidade": 14 },
  "pontosDeControle": [ {"nome": "sala de partida", "ativaEmV": 0, "u": 6, "v": 14.52, "rot": 315} ],
  "portoes":  [ {"nome": "portao", "tipo": "tutorial_parede", "sj": 57, "siDe": 0, "siAte": 3, "liberado": false} ],
  "placas":   [ {"chave": "newtutorialwalkergroundsign1", "u": 6, "v": 13.8, "raio": 14, "rot": 0} ]
}
```

### Chão

O chão pode vir de duas formas, e as duas podem estar juntas no mesmo mapa.

**Desenho**, como numa dungeon: cada caractere é uma sub-célula de 3 u.

- Cada linha é uma fileira `sj`. A **primeira linha é a última fileira** (o fundo do mapa), como nos mapas ASCII do próprio jogo. A última linha é `sjInicial`.
- Cada caractere é uma sub-célula `si`, começando em `siInicial`.
- `.` é chão livre, `#` é muro e `-` (ou espaço) é sem chão.
- Um tile de 12 u tem chão se alguma das 16 sub-células dele não é `-`. Os 4 slots de um tile com chão recebem as `pecasDoDesenho`, sem rotação: o cliente sorteia o giro.

**Peças**, como no Rebel City: uma peça por slot de 6 u.

- Use `p`, `q` e o `id` da peça (GroundAsset).
- Com `rot`, o cliente não sorteia o giro nem o espelho. Sem `rot`, sorteia. Peças de pista precisam de `rot`; sem ela o espelho dá erro de MeshCollider.
- Uma peça num slot do desenho troca a peça sorteada daquele slot.
- Num tile que só tem peças, os slots sem peça ficam vazios.

**Enfeites** (scatter) usam posição e rotação exatas no mundo. `z` é a altura: 0,5 é o nível do chão e um número **menor** fica mais **alto**. O servidor manda cada enfeite junto com o tile onde ele cai.

### Prédios

Cada prédio é uma entidade com arte, atracadouro e o texto com seta no chão:

- `prefab` é o índice no `prefab_table.txt`: 12 Hangar, 14 Storage, 23 Tavern, 24 StoreBuilding, 26 Crafter, 27 PilotSchool, 28 FlockHall, 29 CombatSim, 30 MegaCarrier.
- `loja` é o storeId, só para a loja.
- A frente da arte é o lado −y local. Com `rot` 315, a frente fica virada para −v.

### Para roteiros

Pontos, grupos, áreas, linhas, valores, pontos de controle, portões e placas são lidos pelo nome por quem roda o mapa: o roteiro do tutorial e o servidor de dungeon. Se faltar um nome que o roteiro usa, o servidor avisa qual e de qual seção.

- `areas`: retângulos em u/v. A IA dos inimigos não sai deles.
- `linhas`: valores de `v`. O passo do roteiro acontece quando o jogador passa daquela linha.
- `pontosDeControle`: onde o avião renasce depois de uma queda. O ponto vale quando o jogador passa de `ativaEmV`. No tutorial, o primeiro é onde o avião nasce. Nas dungeons, `/room N` pula para o ponto N (o que fica para trás conta como limpo).
- `portoes`: fileira `sj` de paredes de caixas. Vai uma caixa por sub-célula livre de `siDe` até `siAte`. `tipo` é o tipo de inimigo (do `enemies.json` ou, nas dungeons, da seção `tipos` do próprio mapa), e com `liberado` falso as caixas nascem sem levar dano.
- `placas`: textos do chão (chaves da tabela "lore" do cliente). O cliente cria cada texto sozinho quando o avião chega a menos de `raio` do ponto. Nas dungeons o servidor junta as placas ao ClientMapInfo do terreno (o original do cliente, quando existe, ou um novo).

## Dungeons da build 190828

Os arquivos `Dungeons/dungeon_*` do configBinary da 190828 (e da 190426) estão **vazios** (`{}`): o desenho das salas ficava só no servidor oficial. Por isso as dungeons foram **recriadas** a partir do que o cliente ainda tem: as unidades de cada dungeon (pastas `Units/60D..999D`), nomes e tiers (`Texts/en-US/dungeons`), chaves, território e peças de chão, placas (`*dungeonsign1/2`), falas dos chefes (`bark`) e as quests das dungeons. A ordem dos chefes e as mecânicas vêm da wiki e das notas de patch. O formato das salas e a posição dos inimigos são invenção; cada arquivo diz isso em `fonte`.

Os mapas ficam centrados na grade do cliente (o cliente guarda 54 tiles para cada lado da origem; os mais compridos, como The Dig, têm 56 tiles de ponta a ponta). Todos os templates usados existem no cliente 201219. Os arquivos foram gerados por `tools\dungeons-190828\dungeons_190828.py` e conferidos por `check_maps.py` (tudo no chão livre, dentro das áreas, portões fechando a passagem inteira, placas e falas com texto no cliente).

| Arquivo | Dungeon | Tier | Terreno / território | Minichefe → chefe |
|---|---|---|---|---|
| `dungeon_bomb` | Bomb Factory | 2 | DungeonBomb / 41 | Overseer Charles → The Bomb Maker, Big Treasure |
| `dungeon_obstacle` | Laser Playground | 3 | DungeonObstacle / 44 | The Grinder → Lazer, the Victorious, Big Treasure |
| `dungeon_arena` | The Arena | 5 | DungeonArena1 / 64 | 10 ondas (2 Bruisers na 10) → Commodus |
| `dungeon_grind` | Home for Special Children | 6 | DungeonGrind / 43 | N.A.N.A. → Dr. Geckelston |
| `dungeon_eye` | Crypt of Cathulhu | 9 | DungeonEye / 42 | 2× Psychopus → Cyclopus |
| `dungeon_fire` | Reeducation Camp | 10 | DungeonFire / 36 | Converted Prisoner → Traitor Phoenix (renasce 2×) |
| `dungeon_ice` | Frozen Owl Monastery | 10 | DungeonIce / 31 | Husky Gang Leader → The Frozen Horror |
| `dungeon_snake` | Venom Compound | 10 | DungeonSnake / 34 | Poison Convoy → Giant Egg → Poison God |
| `dungeon_thief` | The Vaults | 10 | DungeonThief / 59 | Security Chief 001/002/003 → chave → altar de Horus |
| `dungeon_space` | Super Secret Base | 10 | DungeonSpace / 58 | emboscada, Mammoth-1 → Doomsday Mechanism |
| `dungeon_temple` | Temple of Bast | 10 | DungeonTemple / 61 | 2 salas de altar, Bast Statue → High Priestess Tiffany |
| `dungeon_sneezy` | Iron Lair | 10 | DungeonSneezy / 67 | SN-Z |
| `dungeon_arena2` | The Elder Arena | 10 | DungeonArena2 / 65 | 15 ondas (minichefes na 8 e na 15) → Dark Sphinx |
| `dungeon_rescue` | The Dig | 10 | DungeonRescue / 45 | escolta da broca: Deep Leviathan, Stone Worm → Buried Guardian |
| `dungeon_finalboss` | The Great Leader's Command Center | 10 | DungeonFinalBoss / 35 | Godleader Meowza (fase 1) |
| `dungeon_3brothers` | The Three Brothers | 10 | Dungeon3Brothers / 73 | Golden Kai → Golden Jinn → Golden Rei |
| `dungeon_finalboss2` | The Great Leader's Inner Sanctum | 10 | DungeonFinalBoss2 / 73 | Godleader Meowza (fase 2) |

Ficaram de fora: `dungeon_tutorial` (tutorial do olfaithful, outro item do checklist), `dungeon_0test` (teste) e `dungeon_gate` (sem nome, sem chave e sem unidades próprias na 190828; a pasta "Pacifist Dungeon" pode ser dela, mas não há como confirmar).

### Campos novos (só nas dungeons)

O `MapaEsboco.cs` ignora estes campos. Quem lê é o servidor de dungeon (`Dungeons\DungeonMapData.cs` e `DungeonServer.cs`). Campo que ele ainda não usa fica marcado como **(ainda não)**.

- `fonte`, `chave` (item que abre), `itemDoPortal` (id em `Items/Consumables/dungeon`), `missao` (quest da dungeon).
- `rascunho`: `true` quando o editor de mapas do launcher salvou o mapa com erros (só uma marca do editor; o servidor não lê).
- `ia` (dentro de um tipo de `tipos`): objeto opcional reservado para o futuro editor de IA dos inimigos. O servidor ignora; o editor de mapas o preserva como está.
- `saida`: `{ "ponto": "portal", "quando": "morte:<tipo>" }`, onde aparece o porta-aviões de saída.
  Sem `saida`, a dungeon termina quando todas as salas de chefe estão limpas. O porta-aviões aparece 2,5 s depois; quem não entrar volta ao Nexus sozinho depois de `saidaAutomaticaSegundos` (do `dungeons.json`).
- `proximaDungeon`: `{ "mapa", "ponto", "quando" }`, para a corrente Meowza → Three Brothers → Inner Sanctum **(ainda não)**.
- `valores.tier` e, quando a sala do chefe usava outro território no original, `valores.territorioDoChefe`.
- `portoesLaterais`: portões nos corredores laterais (tesouros). Usam `si` (coluna) com `sjDe`..`sjAte`, porque o corredor corre ao longo de u.
- Portões ganham `abreQuando`: `limpa:<sala>` (todos os inimigos da sala mortos, `+` junta duas condições), `morte:<tipo>` ou `quest:<id>` (quests ainda não existem no servidor: `quest:` conta como cumprida).
  - Com `liberado` verdadeiro o portão é uma barricada: as paredes levam dano e, quebrada uma, o portão inteiro cai.
  - Com `liberado` falso as paredes não levam dano e o portão abre sozinho quando `abreQuando` fica verdadeiro.
  - `sala`: a sala para onde o portão leva. Numa sala com `fechaAoEntrar`, os portões dela fecham de novo atrás do jogador e só abrem quando a sala fica limpa.
- `rotas`: listas de pontos `{u, v}` em ordem (o caminho da broca em The Dig, o trilho das Blood Nurses) **(ainda não)**.
- `tipos`: os inimigos do mapa, com os mesmos campos do `enemies.json` (`template`, `comportamento`, `chefe`, `fases`, `vidaMaxima`...), mais:
  - `minichefe`: true.
  - `aoMorrer`: tipo que nasce no lugar (destroços `*DEATH`, bau de recompensa).
  - `sequencia`: tipos que nascem um depois do outro quando o anterior morre (ovo da Fênix, Giant Egg → Poison God).
  - `falas`: chaves da tabela `bark` do cliente por momento, para `ClientShowUnitChat` (balão + chat). O servidor usa `aoAcordar`, `fase` (quando a vida passa da primeira fase) e `aoMorrer` (dita pelos destroços). Faixas (`x_0..x_4`) e prefixos (`x_`) são só anotação.
  - `armas` (do tipo ou de uma fase): índices dos `gunMounts` da unidade que atiram (0 = o primeiro). Sem a lista, todas atiram.
  - Nas `fases`: `escudoSegundos` (o chefe não leva dano por esse tempo, como o "toggle_safety" do SN-Z na build 190426) e `efeitoVisual` (nome de um `statefulEffects` do cliente, padrão `tankShield` durante o escudo). `efeitoVisual` também vale no tipo, desde o nascimento.
  - `invocacaoPeriodica`: `{ "tipo", "segundos", "quantidade", "maximo", "abaixoDeVida" }`. Enquanto o chefe está acordado, chama `quantidade` unidades a cada `segundos`, sem passar de `maximo` vivas (e só com a vida abaixo de `abaixoDeVida`, se for menor que 1). Ex.: The Bomb Maker, que não tem armas e ataca com minas-robô. Quando o chefe ou minichefe morre, os invocados explodem.
  - `comportamento: "kamikaze"`: a unidade corre atras do jogador (mirando um pouco a frente do movimento) e **explode ao encostar**: some e solta o `aoMorrer` (o anel de balas da explosao). E o ataque das bombas teleguiadas e dos jatos bombardeiros, que nao tem arma no cliente. `raioDeContato` (padrao 1,4 u) e a distancia em que explode.
  - `invocacaoPeriodica.contaNaSala`: false = o que o invocador chama nunca conta para limpar a sala (bombas largadas por carregadores e dirigiveis). O padrao e true.
  - Alcance das armas: o servidor so atira se o jogador esta dentro do alcance da arma (velocidade x duracao da bala). `perseguir`/`circular` orbitam em `distanciaPreferida`; se ela for maior que o alcance a unidade nunca atira (as minas-robo faziam isso). O gerador (`unit_types.py`) agora limita `distanciaPreferida` ao alcance da arma.
  - `bauDeDps`: `{ "segundos", "recompensa", "falha" }`, o Big Treasure. O primeiro tiro começa a contar; quebrado a tempo, nasce `recompensa` no lugar; senão o baú some e fica `falha`. A `vida` do tipo é a vida do baú.
  - `duracaoSegundos`: a unidade explode sozinha depois desse tempo (ovos da sequência da Fênix).
  - `naoContaNaSala`: true = a unidade nunca conta para limpar a sala, mesmo armada. Serve para o atirador invisível que vive um instante (veja "Barril + atirador invisível" abaixo).
  - `alcanceDeTiro` (do `enemies.json`) vale aqui também; `raioDeAgro: 0` faz a unidade atirar assim que nasce; `atrasoDoPrimeiroTiro: 0` tira a espera antes do primeiro tiro.
  - **Barril + atirador invisível:** no jogo original o barril (`bombbarrel`, `firebarrel`) é a parte visível e o atirador (`bombbarrelboom`, `firebarrelboom`, arte `!EnemyInvisible`) só existe para soltar UM anel de balas onde o barril explodiu. Por isso o barril leva `aoMorrer: "<barril>boom"` e o atirador leva `comportamento: "torre"`, `raioDeAgro: 0`, `atrasoDoPrimeiroTiro: 0`, `alcanceDeTiro: 999`, `naoContaNaSala: true` e `duracaoSegundos` curto. **Nunca coloque um atirador invisível solto numa sala**: ele atira no jogador de longe, sem ser visto.
  - `rota`, `alvo`, `invulneravelAInimigosComuns` **(ainda não)**, `nota`.
- `encontros`: um por sala.
  - `sala`, `area`, `linha`: a sala, a área da IA e a linha que acorda a sala.
  - Os inimigos de uma sala nascem parados no lugar quando o jogador chega perto (3 tiles) e acordam quando ele entra na área (ou leva um tiro).
  - `acorda`: `aoEntrar` (jogador entra na área), `morte:<tipo>` (a sala acorda quando esse tipo morre), `roteiro` e `broca` (The Dig) — esses dois ainda não têm roteiro e acordam como `aoEntrar`.
  - `inimigos`: `[{ "tipo", "pos": [{u, v, rot}], "nota"?, "rota"?, "comportamento"?, "acorda"? }]`. Com `acorda` (ex.: `morte:<tipo>`) a unidade só nasce quando a condição fica verdadeira.
  - `ondas`: para arenas e emboscadas, no formato das ondas do `enemies.json`, com posições. A primeira vem `intervaloAntesSegundos` depois que o jogador entra; cada uma das outras vem quando a anterior morreu inteira.
  - Uma sala fica **limpa** quando não sobra nenhum inimigo que conta (baús, paredes, destroços, invulneráveis e coisas paradas não contam) e as ondas acabaram.
  - `chefe`, `minichefe`, `fechaAoEntrar` (fecha os portões da sala enquanto houver inimigos), `salaDeAltar` (Temple of Bast) e `emboscada` (The Vaults) **(esses dois ainda não)**.
  - `controladores`: unidades invisíveis que o servidor original usava para dirigir a sala (`*master`, `*spawner`, `trapmaster`). Ficam só como referência; o servidor novo faz o papel delas.

## Editar com o editor de mapas

A aba **Map editor** do launcher (`Launcher\Editor\README.md`) abre e grava estes arquivos: cria um mapa a partir de uma dungeon do jogo ou de uma vazia, desenha o chão, coloca entrada, saída, portões, salas e inimigos com as peças do jogo, e confere os objetos obrigatórios antes de salvar. Campos que o editor não conhece ficam como estão no arquivo.

## Onde achar os ids

Os arquivos ficam na pasta `BepInEx` do jogo e são gravados pelo plugin Steambirds Quieter:

- Peças e enfeites: `ground_assets.txt` (`ID=... name=...`). A pasta `PrefabHierarchyObject` do AssetRipper mostra a hierarquia dos prefabs.
- Territórios: `terreno_config_*.txt` (Tipos de tile).
- Prefabs de entidades: `prefab_table.txt`.
- Unidades das dungeons: `tools\dungeons-190828\dungeon_units.json` (de `extract_dungeon_units.py`).

## O que ainda falta

- Testar no jogo: só Iron Lair e Bomb Factory estão ligadas no `dungeons.json`. As outras 15 rodam pelo mesmo servidor, mas ainda não foram vistas no jogo.
- Roteiros especiais: a broca de The Dig, a chave de The Vaults, as maldições das salas de altar do Temple of Bast, as emboscadas, `proximaDungeon`, loot e o consumo das chaves.
- Conferir no jogo o tamanho das salas, que foi estimado (o walker anda 3 u/s; as salas têm de 36 a 108 u).
- O roteiro de cada mapa continua em código. Só os lugares que ele usa ficam no arquivo.
