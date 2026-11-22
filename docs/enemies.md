# Inimigos no esboço do servidor (InimigosEsboco.cs)

Referência técnica do sistema de inimigos, conferida no código decompilado (build 201219.1413).

## Por que o servidor faz a IA
`AI.cs` e `ModalAI.cs` estão vazios no cliente: a IA original era só do servidor. O esboço pilota os inimigos.

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
As IAs e os comportamentos também podem vir de arquivos (`Serveri\`), feitos e simulados na aba IA dos inimigos do launcher; um tipo usa `"ai": "<id>"`. Detalhes em `docs/enemy-ai.md`.

## Arquivos
- `BepInEx\unidades_config.txt` — gravado pelo plugin Quieter 1.14.0+ (hash do Mono não dá para calcular no .NET 8).
- `Server\enemies.json` — tipos, chefes com fases, encontros/ondas. Relido ao mudar. Tipos sem entrada no json podem vir do código (`RegistrarTipoPadrao`, usado pelo TutorialEsboco).

## Roteiros (TutorialEsboco)
- `CriarDoRoteiro`: unidade num ponto fixo, com grupo (contagem de vivos) e área (retângulo de tiles da sala, girado 45°). A área prende o voo dentro da sala; sem área vale o quadrado `arena`.
- Campos novos de tipo: `time` (2 inimigo, 1 aliado), `invulneravel`, `abreAoSerAtingido` (baú: abre N s após o primeiro tiro), `livre` (sem limite de área).
- Comportamentos novos: `parado` (paredes, baús, destroços) e `escoltar` (aliado que segue o jogador ou vai até um destino).
- No servidor de tutorial nenhum encontro começa sozinho; o encontro antigo virou `ondas` (desligado, `/encontro ondas`).

## Comandos no chat
`/inimigos`, `/onda N`, `/proxima`, `/chefe`, `/inimigo TIPO [qtd]`, `/spawn TEMPLATE [qtd]`, `/limpar`, `/reiniciar`, `/encontro [nome]`, `/tipos`, `/catalogo [filtro]`. No tutorial: `/etapa N`, `/portal`, `/recomecar`, `/tutorialajuda`.

## Pendências conhecidas
- Sem XP/loot ao abater; sem rotação visual das torretas (`FIELDMASK_GUNS`).
- Inimigos do servidor atravessam muros de chão (só ficam presos à área da sala).
