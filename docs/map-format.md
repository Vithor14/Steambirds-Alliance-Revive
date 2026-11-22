# Mapas em arquivo (formato "steambirds-esboco-mapa" v1)

Desde a v5 (14/11/2026) os mapas que o esboço usa ficam em `Server\maps`, um `.json` por mapa. O id do mapa é o nome do arquivo. Objetivo: um editor de mapas no futuro e readicionar dungeons feitas à mão. A documentação para quem edita é o `docs/map-format.md`, que vai junto com o servidor (e `maps\README-dungeons.md` para o `dungeons.json`).

| Arquivo | Mapa | Quem usa |
|---|---|---|
| `nexus.json` | Rebel City | `NexusEsboco.cs` (chão, enfeites, prédios, nascimento) |
| `tutorial_walker.json` | Training Grounds | `TutorialEsboco.cs` (o roteiro continua no código e pega os lugares pelo nome) |
| `dungeon_*.json` (17) | Dungeons da build 190828 recriadas (15/11/2026) | `Dungeons\DungeonServer.cs` (v8, porta 1113). Qual arquivo cada dungeon usa: `dungeons.json`. Detalhes em `docs/dungeons-190828.md` e `docs/dungeon-server.md` |

## Coordenadas
- `u`, `v` = unidades ao longo dos eixos a e b da grade do cliente (u = 12·a, v = 12·b, com a, b em tiles do ChaoEsboco). No mundo: x = (u − v)/√2, y = (u + v)/√2.
- 1 tile = 12 u; slot de peça (p, q) = 6 u (centro em 6p + 3); sub-célula de muro (si, sj) = 3 u.
- O cliente guarda 54 tiles para cada lado da origem (`ChaoEsboco.CentroDaGrade`): mapas compridos precisam ficar centrados (as dungeons geradas são).
- Conversão do Rebel City antigo: u = A + 3, v = B + 3 (o A/B do NexusEsboco antigo tinha o centro do slot em múltiplos de 6).
- `rot` = graus no mundo (0 = +y, 90 = +x, horário). 315 = de frente para +v, 135 = para −v. Exceção: `chao.pecas[].rot` é relativa ao tile, anti-horária, de 2 em 2 graus; sem `rot` o cliente sorteia giro e espelho.

## Seções
- `chao`: `murosNasBordas`, `vazioEmVolta` (anel de tiles de abismo: território do mapa com override `territorioDoVazio` 74), `pecasDoDesenho` (variante (a+b+k+l) mod n, sem rotação), `desenho` {siInicial, sjInicial, linhas: `.` chão livre, `#` muro, `-` sem chão; **primeira linha = maior sj**, como o ASCIIMap do jogo} e `pecas` [{p, q, id, nome, rot}]. Um tile tem chão se alguma sub-célula dele não é `-`.
- `enfeites` [{id, nome, u, v, z, rot}]: scatter de precisão total, mandado no tile onde cai (floor(u/12), floor(v/12)). z 0,5 = chão; menor = mais alto.
- `predios` [{prefab, nome, u, v, rot, loja}] (shortIds 70001+ na ordem do arquivo); `nascimento` {u, v, rot}.
- Para roteiros: `pontos` {nome: ponto}, `grupos` {nome: [pontos]}, `areas` {nome: {minU, maxU, minV, maxV}}, `linhas` {nome: v}, `valores` {nome: número}, `pontosDeControle` [{nome, ativaEmV, u, v, rot}], `portoes` [{nome, tipo, sj, siDe, siAte, liberado}], `placas` [{chave, u, v, raio, rot}].
- Campos desconhecidos (`nota`, `legenda`) são ignorados. O leitor aceita comentários `//` e vírgula sobrando.

## Campos das dungeons (lidos por `Dungeons\DungeonMapData.cs`; o `MapaEsboco.cs` ignora)
- Metadados: `fonte`, `chave`, `itemDoPortal`, `missao`, `saida` {ponto, quando} (sem `saida`: fim quando as salas de chefe estão limpas), `proximaDungeon` {mapa, ponto, quando} (ainda não usado); `valores.tier` e `valores.territorioDoChefe`.
- `portoesLaterais` [{nome, tipo, si, sjDe, sjAte, liberado}]: portões dos corredores laterais. Ficam fora de `portoes` porque o `MapaEsboco` só entende fileiras `sj`.
- Portões: `liberado` = barricada (leva dano; uma parede quebrada derruba o portão); senão invulnerável e abre por `abreQuando`: `limpa:<sala>` (com `+` para juntar), `morte:<tipo>`, `quest:<id>` (por enquanto sempre verdadeira). `sala`: para onde o portão leva (fecha atrás do jogador se a sala tem `fechaAoEntrar`).
- `rotas` {nome: [{u, v}]}: caminho da broca (The Dig) e trilho das Blood Nurses (ainda não usado).
- `tipos`: inimigos do mapa com os campos do `enemies.json` (entram no InimigosEsboco como `<mapa>/<tipo>`) + `minichefe`, `aoMorrer`, `sequencia`, `falas` (chaves de `bark`: `aoAcordar`, `fase`, `aoMorrer`), `duracaoSegundos`, `efeitoVisual` (statefulEffects), `armas` (índices de gunMounts), `invocacaoPeriodica` {tipo, segundos, quantidade, maximo, abaixoDeVida}, `bauDeDps` {segundos, recompensa, falha}, `rota`, `alvo`, `nota`. Fases: `escudoSegundos`, `efeitoVisual` (padrão `tankShield` no escudo), `armas`, `invocar`.
- `encontros` (um por sala): `sala`, `area`, `linha`, `acorda` (aoEntrar / `morte:<tipo>`; roteiro e broca ainda acordam como aoEntrar), `inimigos` [{tipo, pos: [{u, v, rot}], acorda?}], `ondas` (formato do `enemies.json` com posições; a próxima vem quando a anterior morre inteira), `chefe`, `minichefe`, `fechaAoEntrar`, `salaDeAltar`, `emboscada`, `controladores` (unidades invisíveis do servidor original, só referência).
- Sala limpa = sem inimigos que contam (baús, paredes, destroços, invulneráveis e parados não contam) e sem ondas pendentes.

## Implementação (MapaEsboco.cs)
- `MapaEsboco.Obter(id, log)`: procura `cwd\maps\<id>.json` e depois `AppContext.BaseDirectory\mapas`. Relê quando a data do arquivo muda. Se o arquivo novo tiver erro, fica a versão anterior (log `[MAPA] ERRO ...`). Sem nenhuma versão boa, lança `InvalidOperationException` (o Nexus e o tutorial não sobem; a dungeon avisa no chat e manda o jogador de volta ao Nexus). Só lê o arquivo do mapa pedido.
- Desserialização com `System.Text.Json` (`PropertyNameCaseInsensitive`); `valores` e `linhas` são `Dictionary<string, float>`, então só aceitam números.
- `Mapa.CriarPlanta()`: monta a `ChaoEsboco.Planta` (tiles do desenho; `#` que não é borda vira `FecharSub`).
- `Mapa.EscreverChao(buf, planta?, out resumo)`: escreve o ClientReceiveTerrainTiles de qualquer mapa: tiles do desenho, depois tiles de peças e de enfeites (por b e a), e por fim o abismo.
- `MapaEsboco.AdicionarPlacas(info, mapa)`: um evento por placa na ClientMapInfo (`repeatEvery_1` + PlayerNearPos → `lore_text_relative_pos`). O servidor de dungeon registra isso para cada terreno do `dungeons.json`.
- Tutorial: `MapaParaSessao()` + `Validar()` conferem todos os nomes usados pelo roteiro. Mapa com nome faltando → fica o último válido. Cada `Sessao` guarda o seu `Mapa`; `/recomecar` pega a versão nova do arquivo.
- Nomes que o roteiro do tutorial usa:
  - pontos: instrutor, anel, caixa, chefe, portal, guiaDrones, guiaCorredorFinal
  - grupos: torres, drones1, drones2
  - áreas: hangar, antessala, drones, anel, arena
  - linhas: instrutor, torres, falaDasBalas, saidaDoHangar, torresQuietas, corredorDosDrones, corredorFinal
  - valores: anelQuantidade, anelAbertura, anelRaioU, anelRaioV, anelRaioDoMeio, raioDosDrones1
  - pontos de controle: o primeiro é o nascimento; /etapa usa antessala, sala do anel, corredor da caixa, corredor final, arena
  - portões: 3, na ordem parede de caixas, portão, portão do chefe

## Conversão conferida
Os .json do Nexus e do tutorial foram gerados por um script que reproduz o código antigo (v4.4). Uma porta do leitor/escritor novo comparou tile a tile (território, peças com bandeiras/rotação, enfeites com posição, muros 4×4, abismo) com a saída do código antigo. Resultado: Nexus 24 tiles iguais; tutorial 71 tiles + 272 de abismo iguais. Não houve compilação local (sem dotnet no ambiente).

As dungeons vêm de `tools\dungeons-190828` (Python) e passam pelo `check_maps.py`: posições no chão livre e dentro da área da sala, portões cobrindo o corredor inteiro, templates existentes no cliente 201219, placas e falas com texto, armas por índice, invocações, baús de DPS, condições e tamanho da grade.

## Falta
- O editor de mapas.
- No Nexus e no tutorial, os encontros continuam no `enemies.json` (por servidor, não por mapa).
- Campos em inglês antes do editor (item 14 do checklist).
