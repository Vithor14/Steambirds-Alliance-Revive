# Inimigos do servidor (`Server\Enemies*.cs`)

Referência técnica do sistema de inimigos, conferida no código decompilado (build 201219.1413). Revisada em 23/11/2026: o antigo `InimigosEsboco.cs` virou `Enemies.cs` e os arquivos `partial` `Enemies.Ai`, `.Allies`, `.Brains`, `.Encounters`, `.Files`, `.Guns`, `.Interest`, `.Messages`, `.Scripts`, `.Util`.

## Por que o servidor faz a IA
`AI.cs` e `ModalAI.cs` estão vazios no cliente: a IA original era só do servidor. O servidor pilota os inimigos.

## Protocolo usado
- **Nascer**: `SpawnUnitsBatch` (45862), prefab `GenericUnit` = 9, `templateHash` = `UnitConfig.hash`, time `aiTeam` = 2 (aliado: 1), autoridade = `server.UniqueIdentifier`, `unitOptions` nulo. Campos POS|VEL|ROT|HEALTH.
- **Mover**: `UpdateUnitsBatch` (45864) 10x/s. FieldUpdate: pos/vel ×1024, rot ×32, vida ×512 (fração 0–1).
- **Atirar**: `GunFire` (62326), `isPlayer` = false, `mountIndex` = índice em `UnitConfig.gunMounts`, `idCounter` += multiBullets. Com `targetShortId` a bala vira teleguiada (acerto garantido).
- **Dano no jogador**: o cliente aplica sozinho (o avião dele é autoritativo). A "star rule" já está nesse caminho (`UnitScript.takeDamage`, build 201219.1413), só para balas no avião local (`isRateLimited`): uma janela rolante de 1 s guarda a idade de cada acerto (`m_hitsRecord`, envelhecida no `LateFixedUpdate`). Os 6 primeiros acertos dessa janela tiram vida; do 7º em diante (`Count > 5`) o dano vai a zero, o acerto não entra na lista, e o cliente toca `DodgedBulletFX` (sprite amarelo que sobe; o fade começa aos 0,5 s). O número de dano trata dano zero como escudo e mostra o texto `damage_shielded` ("shielded!"; some se repetir em menos de 3 s), e o tiro conta como `BulletHitResult.Shield` (faísca e som de escudo). Nuvens passam `isRateLimited: false` e não entram na regra. Outra regra, separada, no mesmo método: one-shot protection (`global` do configBinary) — com vida atual ≥ 75% da máxima, um único acerto fica limitado a 75% da vida máxima.
- **Dano no inimigo**: `ServerReportBulletHit` → `(damage + effectDamage) × multiple`; `ServerReportUnitDamage` → effectDamage.
- **Morte**: `KillUnitsBatch` (45863) com `DestroyedFromDamage` (10); remoção silenciosa com `Fizzled` (11).
- **HUD**: `ClientSetNumBossesRemaining` (18380, int). Chat: `ClientReceiveText` (14789, string + byte 0).
- **Comandos**: comandos de chat desconhecidos do cliente chegam como `SlashCommand` (29486, List<string>).
- Convenção de ângulo: 0 = +Y, 90 = +X; direção = (sin r, cos r). `maxTurnRate` em graus/s.

## IA em arquivos (22/11; pastas e fases em 24/11; mundo em 25/11)
As IAs e os comportamentos vêm de arquivos (`Server/ai/brains`, pastas `generic`, `world` e `dungeons/<dungeon>`), feitos e simulados na aba Enemy AI do launcher; um tipo usa `"ai": "<id>"`. Desde a 0.7.0 não há `behavior` nem `phases` nos tipos: o voo antigo é uma IA genérica e uma fase de chefe é um estado com `healthBelow`. Formações de chefes do mundo (`formations.json`) e armas `anydir` estão em `docs/open-world.md` e `docs/update-notes-0.8.0.md`. Detalhes em `docs/enemy-ai.md`; formato em `Server/ai/README.md`.

## Raio de visão (23/11)
No mundo aberto cada cliente só recebe os inimigos dentro de `enemies.viewRadius` (`Enemies.Interest.cs`; padrão 90, 0 desliga), então o radar não aponta para inimigos distantes. Dungeons e tutorial mandam tudo. Ver `docs/open-world.md`.

## Arquivos
- `units_config.txt` — gravado pelo plugin Quieter (hash do Mono não dá para calcular no .NET 8) e copiado para `Server\data` pelo `SteambirdsGameFiles.exe`. Tem uma coluna nova no fim de cada linha `gun` desde a 0.5.0 (o tipo de tiro, que o editor de IA mostra).
- `Server\enemies.json` — tipos (cada um com `"ai"`), encontros/ondas, `effects` (efeitos de tela) e `globalHealthMultiplier`. Relido ao mudar. Desde a 0.7.0 não há mais `phases` nem `behavior` neste arquivo: uma fase é um estado da IA (`healthBelow`), e o voo antigo é uma IA genérica (`generic_chase`, `generic_mine`, …). Tipos sem entrada no json podem vir do código (`RegisterDefaultType`, usado pelo tutorial e pelos mapas de dungeon).

## Roteiros (`Tutorial*.cs`)
- Unidade num ponto fixo, com grupo (contagem de vivos) e área (retângulo de tiles da sala, girado 45°). A área prende o voo dentro da sala; sem área vale o quadrado `arena`.
- Campos de tipo: `team` (2 inimigo, 1 aliado), `invulnerable`, `opensAfterHitSeconds` (baú: abre N s após o primeiro tiro), `freeRoam` (sem limite de área).
- O campo `behavior` saiu dos mapas e do `enemies.json` na 0.7.0. O que ele fazia são as IAs genéricas: `generic_idle` (paredes, baús, destroços), `generic_escort` (aliado que segue o jogador ou vai até um destino), `generic_fixed` (torre que não gira), `generic_kamikaze` (bombas das dungeons que voam até o jogador), `generic_mine` (mina parada: explode quando o jogador entra no `contactRadius`; use `visualEffect` `detectionZone_1..3` para mostrar a área em vermelho), e `generic_chase`, `generic_circle`, `generic_charge`, `generic_turret`, `generic_patrol`, `generic_route`. Um tipo antigo sem `ai` ainda cai em `generic_<behavior>`. `generic_orbit_leader` (25/11) circula o líder do grupo.
- No servidor de tutorial nenhum encontro começa sozinho; o encontro antigo virou `waves` (desligado, `/encounter waves`).

## Comandos no chat
`/enemies`, `/wave N`, `/next`, `/boss`, `/enemy TYPE [count]`, `/spawn TEMPLATE [count]`, `/clear`, `/reset`, `/encounter [name]`, `/types`, `/catalog [filter]`, `/brains`. No tutorial: `/step N`, `/portal`, `/restart`, `/tutorialhelp`. Lista completa em `Chat commands.md`.

## Pendências conhecidas
- A rotação visual das torretas existe desde 22/11 (`Enemies.Guns.cs`, `FIELDMASK_GUNS`: armas "Aimed" seguem o jogador, as de padrão mostram o ângulo do último tiro). Efeitos de tela em `ScreenEffects.cs` (opções em `enemies.json` `effects`).
- Inimigos do servidor atravessam muros de chão (só ficam presos à área da sala).
- A IA original de cada chefe (Apêndice C do checklist) não existe em nenhuma build. Desde a 0.7.0 cada chefe enviado tem um arquivo de IA (um estado por fase, transição `healthBelow`); o voo padrão é uma IA genérica. Os arquivos ficam em `Server\ai\brains\`.
