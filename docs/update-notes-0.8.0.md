# Steambirds Alliance Revive 0.8.0 (in progress)

**Status: not released.** This file is fed with every change until 0.8.0 is out; add a line here (English and Portuguese) in the same change
that touches the game, the server, the launcher or the editors. / **Em andamento:** este arquivo recebe cada alteração até o lançamento da 0.8.0.

Base: the last commit of 0.7.0. / Base: o último commit da 0.7.0.

## English

### Enemy AI

- **Inquisitor chase AI** (`Server\ai\brains\world\inquisitor_chase.json`, for the open world's `highmountainhunter`): it wanders without
  shooting, spots a player 45 units away (or when shot) and locks on, chasing faster than its normal top speed and turning 2.5 times faster
  than its unit type, aiming ahead of the plane and firing its short forward cone. Within 2 units it keeps pursuing but slows down and turns 3 times
  faster, so its circles around a player who stands still are tight enough to keep its gun on them; past 3 it speeds up into the chase again. It only gives up past 80 units.
  A reconstruction (the original AI is lost). Pick it for the unit in *Server > World map > Enemies* (highmountains); `/enemy ai_inquisitor`
  creates one anywhere to test it.
- **New AI field `move.turnMult`** (every move mode, default 1): multiplies the unit's turn rate while the state lasts. Read by the server and
  the launcher's simulator, and editable in the Enemy AI tab (*Turn multiplier*).
- **Ambassador world boss AI** (`Server\ai\brains\world\ambassador*.json`, the Imperial Palace's `ufo`): it never turns to face the player;
  its body turns slowly by itself, counter-clockwise first and the other way at each phase change, and its cat representatives are fixed close
  around it, turning with it (the first cats and the outer ring face the way they circle). Shielded with 3 cats (shielded for their first 5.5 s); when they die it slides along a small triangle, open to damage,
  for 13–15 s, then shields itself and calls 15 diplomats in 3 waves; when they die it goes back to the triangle. Under 30% health it returns
  to the center with an inner ring of 3 cats (shielded until the outer ring dies; their side guns fire the big slow circles through the boss,
  drawing a spiral as it turns) and an outer ring of 3; when all are dead it fights in place without a shield. When it dies it leaves its
  death animation (`ufoDEATH`). A reconstruction from a video. The open world gives it to the Ambassador by itself; `/enemy ai_ambassador`
  creates one anywhere to test it.
- **New AI features**, read by the server and the launcher (Enemy AI tab): move mode **`path`** (`points`, `loop`: flies through points
  around its home), **`move.around`** for `orbit` (`player`, `summoner`, `home`: minions can circle their boss), **`move.topSpeed`**
  (units/s, for units the game made immobile), summons **`summonPlace: around`** and **`summonDistance`** (a ring of guards), and the
  condition **`siblingsAtMost`** (with `unit`: the other summons of the same summoner, by type). Also **`move.spinRate`** (the body turns
  by itself, degrees/s, and the unit slides where it goes) and **`lock`** / **`faceOffset`** for an `orbit` around the summoner (a guard
  fixed to its boss's body, turning with it; `faceForward` makes it face the way it circles). The client now gets the velocity along the way a sliding unit really moves.
- **Open world bosses:** a boss with no AI chosen by the host runs the `ai\brains\world` AI made for it, and the minions that AI summons by
  itself are not created with it. The Ambassador is now a boss of its own in the Imperial Palace (it came in one group with Solaris).
- **Death animations:** new unit type fields **`deathUnit`** / **`deathSeconds`**: a unit (a type or a game unit such as `ufoDEATH`) left
  where the unit dies, still and invulnerable, for 6 s by default. The open world's bosses leave their game `<unit>DEATH` model by themselves.
  The wreck fires its guns the whole time (the burning smoke of the boss death models) and, when its time is up, is destroyed instead of
  fading out, so the client plays its big final explosion (`explosionPrefab`, e.g. `ExplosionB_Final` for `ufoDEATH`). Dungeon wrecks too.
- **Ambassador, second round of fixes:** the first diplomats are forced when it has lost a quarter of its health (a burst of damage no
  longer skips them); after its last cats die it slides along a bigger triangle, faster and turning faster, instead of standing still; the
  inner-ring cats face the way they circle (the last phase turns counter-clockwise, so their side guns still fire through the boss). The
  **diplomats** have their own AI: they zigzag toward the nearest player like hummingbirds (4.5-unit dashes, facing each one), shielded for
  the first 2 dashes, and start shooting at the end of the 3rd.
- **New AI move `zigzag`** (dashes of `distance` units, `deviation` degrees to each side of the line to the player, facing each one; it
  weaves around the player when close) and **condition `legsDone`** (dashes, or path points, done in the state).
- **Ambassador, third round:** the diplomats were too strong: their 3 dashes are 65% shorter (1.6 units), then they stay still as turrets
  facing and shooting the player; a player more than 8 units away makes them wander, and coming back within 8 makes them turrets again.
  The outer ring of cats is turned 60 degrees, so each outer cat sits between two inner ones (new summon field **`summonAngle`**).
- **Ambassador, fourth round:** the diplomats' first two dashes go a random 15 to 75 degrees off the line to the player (new zigzag field
  **`jitter`**), so they are harder to read. New unit type field **`deathEffect`**: a client effect on the wreck while it lasts (empty =
  none; `enemyEffects_spinningLight` was tried for the beams of light of the death animation, but it is an alarm light, so it is not the default).
- **Ambassador, fifth round:** the diplomats dash faster (8 units/s) in a narrower cone (28 to 52 degrees), and before every dash but the
  first they stop and turn to face the player (new zigzag field **`pause`**); they lose aggro beyond 9 units instead of 8. A unit that
  slides (zigzag, path, a locked guard) now stops dead when it reaches its point and speeds up twice as fast.
- **New chat command `/wreck UNIT [seconds] [effect] [state]`:** puts a death model (`ufoDEATH`, `gargantuaDEATH`...) in front of you as a
  boss leaves it (still, firing its gun, with an effect and a unit state, exploding after the seconds, 20 by default), to find out what
  brings the beams of light and small explosions of the death animations (still unknown).
- **Units can have a state** (the client's `UnitScript.States`, sent in their updates): any state but Default and Chilling makes the client
  play the unit's "maneuvering" animations. New unit type field **`deathState`** for the wreck's state (being tried for the beams).
- **Ambassador, sixth round:** it shot only when a player was very close (its guns' own ranges are 3 to 9 units): every state that shoots
  now has a range of 25.
- **Boss death animations:** the boss death of the game is back. A `*DEATH` wreck plays the client's instant effects (new server message
  `ClientInstantEffect`): the first big explosion (`explosionBossInitial`), debris, the death model's own animation (`deathAnimation`: it
  shakes and throws its beams of white light) and small and medium explosions with their sounds (`epicDeath1-4`) over its 6 s, before
  the final explosion. Open-world bosses, `enemies.json` types with `deathUnit` and the dungeons' `*DEATH` wrecks all do it. New unit
  type field **`deathSequence`** (`"SECONDS EFFECT"` steps) to change it; the original timing is lost.
- **Ambassador, seventh round:** fewer bullets. While summons are alive it fires only one ring of bullets every 4 s; open on the triangle,
  two rings, each every 4 s; in the last stand the two rings and its bullet spiral at 60% of their rate.

### Launcher and editor

### Fixes

- **Shielded enemies glow again:** a unit in an AI shield state now shows the game's own pink shielded glow (`Entity.FLAG_SHIELDED`,
  sent in the unit's field updates), as bosses did in the original game. Before, it looked open to damage but its health bar did not move.
  `shieldEffect` is now an extra effect on top of the glow (empty = only the glow; it used to default to `tankShield`). New unit type field
  **`shieldedLook`** (*Shielded glow* in the map editor's Units tab): an `invulnerable` unit shows the same glow; off by default, so walls,
  keys and scenery stay as they are.

## Português

### IA dos inimigos

- **IA de perseguição do Inquisidor** (`Server\ai\brains\world\inquisitor_chase.json`, para o `highmountainhunter` do mundo aberto): vaga
  sem atirar, avista um jogador a 45 unidades (ou ao levar um tiro) e trava nele, perseguindo mais rápido que sua velocidade normal e girando
  2,5 vezes mais rápido que o tipo de unidade, mirando à frente do avião e disparando o cone curto da frente. A menos de 2 unidades continua
  perseguindo, mas desacelera e gira 3 vezes mais rápido, de modo que suas voltas em torno de um jogador parado ficam fechadas o bastante para
  manter a arma nele; além de 3 volta a acelerar na perseguição. Só
  desiste além de 80 unidades.
  É uma reconstrução (a IA original se perdeu). Escolha-a para a unidade em *Server > World map > Enemies* (highmountains);
  `/enemy ai_inquisitor` cria um em qualquer lugar para testar.
- **Novo campo de IA `move.turnMult`** (todos os modos de movimento, padrão 1): multiplica a velocidade de giro da unidade enquanto o estado
  dura. Lido pelo servidor e pelo simulador do launcher, e editável na aba Enemy AI (*Turn multiplier*).
- **IA do chefe do mundo Ambassador** (`Server\ai\brains\world\ambassador*.json`, o `ufo` do Imperial Palace): nunca se vira para o
  jogador; o corpo gira devagar sozinho, no sentido anti-horário primeiro e no outro sentido a cada mudança de fase, e os gatos representantes
  ficam presos bem perto dele, girando junto (os primeiros gatos e o anel externo ficam virados para onde circulam). Com escudo e 3 gatos (com escudo nos primeiros 5,5 s); quando eles morrem, desliza por um pequeno
  triângulo, sem escudo, por 13–15 s, depois se protege e chama 15 diplomatas em 3 ondas; quando eles morrem, volta ao triângulo. Abaixo de
  30% de vida volta ao centro com um anel interno de 3 gatos (com escudo até o anel externo morrer; as armas laterais deles disparam os
  círculos grandes e lentos através do chefe, desenhando uma espiral enquanto ele gira) e um anel externo de 3; quando todos morrem, luta
  parado e sem escudo. Ao morrer, deixa a animação de morte (`ufoDEATH`). É uma reconstrução a partir de um vídeo. O mundo aberto a dá ao
  Ambassador sozinho; `/enemy ai_ambassador` cria um em qualquer lugar para testar.
- **Novos recursos de IA**, lidos pelo servidor e pelo launcher (aba Enemy AI): modo de movimento **`path`** (`points`, `loop`: voa por
  pontos em volta da casa), **`move.around`** no `orbit` (`player`, `summoner`, `home`: capangas podem girar em volta do chefe),
  **`move.topSpeed`** (unidades/s, para unidades que o jogo deixou imóveis), invocações **`summonPlace: around`** e **`summonDistance`**
  (um anel de guardas) e a condição **`siblingsAtMost`** (com `unit`: as outras invocadas de quem invocou, por tipo). Também
  **`move.spinRate`** (o corpo gira sozinho, graus/s, e a unidade desliza até onde vai) e **`lock`** / **`faceOffset`** num `orbit` em volta
  de quem invocou (um guarda preso ao corpo do chefe, girando com ele; `faceForward` o deixa virado para onde circula). O cliente agora recebe a velocidade na direção em que uma unidade que
  desliza realmente se move.
- **Chefes do mundo aberto:** um chefe sem IA escolhida pelo host usa a IA de `ai\brains\world` feita para ele, e os capangas que essa IA
  invoca sozinha não nascem junto. O Ambassador agora é um chefe separado no Imperial Palace (vinha num grupo só com o Solaris).
- **Animações de morte:** novos campos de tipo de unidade **`deathUnit`** / **`deathSeconds`**: uma unidade (um tipo ou uma unidade do jogo
  como `ufoDEATH`) deixada onde a unidade morre, parada e invulnerável, por 6 s por padrão. Os chefes do mundo aberto deixam o modelo
  `<unidade>DEATH` do jogo sozinhos. O destroço dispara suas armas o tempo todo: os modelos de morte dos chefes do jogo têm uma arma
  inofensiva (a fumaça do modelo de morte) e, quando o tempo acaba, é destruído em vez de sumir, então o cliente toca a grande explosão
  final (`explosionPrefab`, por exemplo `ExplosionB_Final` do `ufoDEATH`). Os destroços das dungeons também.
- **Ambassador, segunda rodada de ajustes:** os primeiros diplomatas são forçados quando ele perde um quarto da vida (uma rajada de dano
  não os pula mais); depois que os últimos gatos morrem, ele desliza por um triângulo maior, mais rápido e girando mais rápido, em vez de
  ficar parado; os gatos do anel interno ficam virados para onde circulam (a última fase gira no sentido anti-horário, então as armas
  laterais continuam disparando através do chefe). Os **diplomatas** têm IA própria: vêm em ziguezague até o jogador mais próximo como
  beija-flores (arrancadas de 4,5 unidades, virados para cada uma), com escudo nas 2 primeiras arrancadas, e começam a atirar no fim da 3ª.
- **Novo movimento de IA `zigzag`** (arrancadas de `distance` unidades, `deviation` graus para cada lado da linha até o jogador, virada para
  cada uma; perto do jogador circula em volta dele) e **condição `legsDone`** (arrancadas, ou pontos do caminho, feitas no estado).
- **Ambassador, terceira rodada:** os diplomatas estavam fortes demais: as 3 arrancadas ficaram 65% mais curtas (1,6 unidades) e depois eles
  ficam parados como torretas, virados para o jogador e atirando; um jogador a mais de 8 unidades os faz vagar, e voltar a menos de 8 os
  torna torretas de novo. O anel externo de gatos é girado 60 graus, então cada gato de fora fica entre dois de dentro (novo campo de
  invocação **`summonAngle`**).
- **Ambassador, quarta rodada:** as duas primeiras arrancadas dos diplomatas vão num ângulo aleatório de 15 a 75 graus da linha até o
  jogador (novo campo de ziguezague **`jitter`**), então ficam mais difíceis de prever. Novo campo de tipo de unidade **`deathEffect`**: um
  efeito do cliente no destroço enquanto ele dura (vazio = nenhum; o `enemyEffects_spinningLight` foi testado para os feixes de luz da
  animação de morte, mas é uma luz de alarme, então não é o padrão).
- **Ambassador, quinta rodada:** os diplomatas arrancam mais rápido (8 unidades/s) num cone mais estreito (28 a 52 graus) e, antes de cada
  arrancada menos a primeira, param e se viram para o jogador (novo campo de ziguezague **`pause`**); perdem o interesse além de 9 unidades
  em vez de 8. Uma unidade que desliza (ziguezague, caminho, guarda preso) agora para de vez ao chegar ao ponto e acelera duas vezes mais rápido.
- **Novo comando de chat `/wreck UNIDADE [segundos] [efeito] [estado]`:** coloca um modelo de morte (`ufoDEATH`, `gargantuaDEATH`...) na
  sua frente como um chefe o deixa (parado, disparando a arma, com um efeito e um estado de unidade, explodindo depois dos segundos, 20 por
  padrão), para descobrir o que traz os feixes de luz e as pequenas explosões das animações de morte (ainda desconhecido).
- **Unidades podem ter um estado** (o `UnitScript.States` do cliente, enviado nas atualizações): qualquer estado menos Default e Chilling faz
  o cliente tocar as animações de "manobra" da unidade. Novo campo de tipo de unidade **`deathState`** para o estado do destroço (em teste
  para os feixes).
- **Ambassador, sexta rodada:** ele só atirava com o jogador muito perto (o alcance das armas dele no jogo é de 3 a 9 unidades): todo estado
  que atira agora tem alcance 25.
- **Animações de morte dos chefes:** a morte de chefe do jogo voltou. Um destroço `*DEATH` toca os efeitos instantâneos do cliente (nova
  mensagem do servidor `ClientInstantEffect`): a primeira grande explosão (`explosionBossInitial`), destroços, a animação do próprio modelo
  de morte (`deathAnimation`: ele treme e solta os feixes de luz branca) e explosões pequenas e médias com som (`epicDeath1-4`) durante os
  6 s, antes da explosão final. Chefes do mundo aberto, tipos do `enemies.json` com `deathUnit` e os destroços `*DEATH` das dungeons fazem
  isso. Novo campo de tipo de unidade **`deathSequence`** (passos `"SEGUNDOS EFEITO"`) para mudar; o tempo original se perdeu.
- **Ambassador, sétima rodada:** menos balas. Com invocadas vivas ele dispara só um anel de balas a cada 4 s; aberto no triângulo, dois
  anéis, cada um a cada 4 s; na última resistência os dois anéis e a espiral de balas a 60% da cadência.

### Launcher e editor

### Correções

- **Inimigos com escudo voltam a brilhar:** uma unidade num estado de IA com escudo agora mostra o brilho rosa de escudo do próprio jogo
  (`Entity.FLAG_SHIELDED`, enviado nas atualizações de campos da unidade), como os chefes no jogo original. Antes ela parecia vulnerável, mas a
  barra de vida não descia. O `shieldEffect` agora é um efeito extra por cima do brilho (vazio = só o brilho; antes o padrão era `tankShield`).
  Novo campo de tipo de unidade **`shieldedLook`** (*Shielded glow* na aba Units do editor de mapas): uma unidade `invulnerable` mostra o
  mesmo brilho; desligado por padrão, para que muros, chaves e cenário fiquem como estão.
