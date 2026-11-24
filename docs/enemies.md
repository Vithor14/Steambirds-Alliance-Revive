# Inimigos do servidor (`Server\Enemies*.cs`)

Referência técnica do sistema de inimigos, conferida no código decompilado (build 201219.1413). Revisada em 23/11/2026: o antigo `InimigosEsboco.cs` virou `Enemies.cs` e os arquivos `partial` `Enemies.Ai`, `.Allies`, `.Brains`, `.Encounters`, `.Files`, `.Guns`, `.Interest`, `.Messages`, `.Scripts`, `.Util`.

## Por que o servidor faz a IA
`AI.cs` e `ModalAI.cs` estão vazios no cliente: a IA original era só do servidor. O servidor pilota os inimigos.

## Protocolo usado
- **Nascer**: `SpawnUnitsBatch` (45862), prefab `GenericUnit` = 9, `templateHash` = `UnitConfig.hash`, time `aiTeam` = 2 (aliado: 1), autoridade = `server.UniqueIdentifier`, `unitOptions` nulo. Campos POS|VEL|ROT|HEALTH.
- **Mover**: `UpdateUnitsBatch` (45864) 10x/s. FieldUpdate: pos/vel ×1024, rot ×32, vida ×512 (fração 0–1).
- **Atirar**: `GunFire` (62326), `isPlayer` = false, `mountIndex` = índice em `UnitConfig.gunMounts`, `idCounter` += multiBullets. Com `targetShortId` a bala vira teleguiada (acerto garantido).
- **Dano no jogador**: o cliente aplica sozinho (o avião dele é autoritativo).
- **Dano no inimigo**: `ServerReportBulletHit` → `(damage + effectDamage) × multiple`; `ServerReportUnitDamage` → effectDamage.
- **Morte**: `KillUnitsBatch` (45863) com `DestroyedFromDamage` (10); remoção silenciosa com `Fizzled` (11).
- **HUD**: `ClientSetNumBossesRemaining` (18380, int). Chat: `ClientReceiveText` (14789, string + byte 0).
- **Comandos**: comandos de chat desconhecidos do cliente chegam como `SlashCommand` (29486, List<string>).
- Convenção de ângulo: 0 = +Y, 90 = +X; direção = (sin r, cos r). `maxTurnRate` em graus/s.

## IA em arquivos (22/11)
As IAs e os comportamentos também podem vir de arquivos (`Server\ai\`), feitos e simulados na aba Enemy AI do launcher; um tipo usa `"ai": "<id>"` (também em `phases`). Detalhes em `docs/enemy-ai.md`; formato em `Server\ai\README.md`.

## Raio de visão (23/11)
No mundo aberto cada cliente só recebe os inimigos dentro de `enemies.viewRadius` (`Enemies.Interest.cs`; padrão 90, 0 desliga), então o radar não aponta para inimigos distantes. Dungeons e tutorial mandam tudo. Ver `docs/open-world.md`.

## Arquivos
- `units_config.txt` — gravado pelo plugin Quieter (hash do Mono não dá para calcular no .NET 8) e copiado para `Server\data` pelo `SteambirdsGameFiles.exe`. Tem uma coluna nova no fim de cada linha `gun` desde a 0.5.0 (o tipo de tiro, que o editor de IA mostra).
- `Server\enemies.json` — tipos, chefes com fases, encontros/ondas, `effects` (efeitos de tela) e `globalHealthMultiplier`. Relido ao mudar. Tipos sem entrada no json podem vir do código (`RegisterDefaultType`, usado pelo tutorial e pelos mapas de dungeon).

## Roteiros (`Tutorial*.cs`)
- Unidade num ponto fixo, com grupo (contagem de vivos) e área (retângulo de tiles da sala, girado 45°). A área prende o voo dentro da sala; sem área vale o quadrado `arena`.
- Campos de tipo: `team` (2 inimigo, 1 aliado), `invulnerable`, `opensAfterHitSeconds` (baú: abre N s após o primeiro tiro), `freeRoam` (sem limite de área).
- Comportamentos: `idle` (paredes, baús, destroços), `escort` (aliado que segue o jogador ou vai até um destino), `fixed` (torre que não gira), `kamikaze` (bombas das dungeons que voam até o jogador), `mine` (mina parada: explode quando o jogador entra no `contactRadius`; use `visualEffect` `detectionZone_1..3` para mostrar a área em vermelho). Os demais: `chase`, `circle`, `charge`, `turret`, `patrol`.
- No servidor de tutorial nenhum encontro começa sozinho; o encontro antigo virou `waves` (desligado, `/encounter waves`).

## Comandos no chat
`/enemies`, `/wave N`, `/next`, `/boss`, `/enemy TYPE [count]`, `/spawn TEMPLATE [count]`, `/clear`, `/reset`, `/encounter [name]`, `/types`, `/catalog [filter]`, `/brains`. No tutorial: `/step N`, `/portal`, `/restart`, `/tutorialhelp`. Lista completa em `Chat commands.md`.

## Pendências conhecidas
- A rotação visual das torretas existe desde 22/11 (`Enemies.Guns.cs`, `FIELDMASK_GUNS`: armas "Aimed" seguem o jogador, as de padrão mostram o ângulo do último tiro). Efeitos de tela em `ScreenEffects.cs` (opções em `enemies.json` `effects`).
- Inimigos do servidor atravessam muros de chão (só ficam presos à área da sala).
- A IA original de cada chefe (Apêndice C do checklist) não existe em nenhuma build; hoje valem comportamentos genéricos, IAs em arquivo e as fases descritas nos mapas.
