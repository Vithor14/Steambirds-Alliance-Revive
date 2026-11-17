# Dungeons da build 190828 (gerador)

Gera os mapas `Server\maps\dungeon_*.json` a partir das unidades do configBinary da build 190828.2045. O formato dos arquivos está em `Server\maps\README.md`.

Precisa de Python 3 (o `preview_maps.py` também precisa do Pillow: `pip install pillow`).

| Arquivo | O que faz |
|---|---|
| `extract_dungeon_units.py` | Lê um configBinary e grava `dungeon_units.json` (as unidades de cada dungeon com nome, vida, velocidade, armas e papel) |
| `dungeon_units.json` | Saída do extrator para a build 190828 (883 unidades em 20 pastas) |
| `dungeon_builder.py` | Monta salas, corredores, portões, pontos e encontros e escreve o JSON |
| `unit_types.py` | Cria os `tipos` de inimigo de cada mapa a partir das unidades |
| `dungeons_190828.py` | As 17 dungeons: salas, inimigos, chefes, fases, falas e portões |
| `check_maps.py` | Confere os mapas (chão, áreas, portões, templates do cliente 201219, placas e falas) |
| `preview_maps.py` | Desenha uma prévia de todos os mapas (`previa-dungeons.png`) |
| `ia_190426.json` | Camada de IA que só a build 190426 ainda tem (`additionalStates` de 53 unidades, `includedStates`, `namedUnitStates`, `difficultyTable`). Referência para recriar a IA original; o escudo do SN-Z saiu daqui |

## Uso (PowerShell, nesta pasta)

```powershell
# 1. (opcional) extrair de novo as unidades
python extract_dungeon_units.py ..\..\VersoesAntigas\386011-1702765646885248947\Steambirds_Data\StreamingAssets\configBinary dungeon_units.json config190828.json

# 2. gerar os mapas direto na pasta do servidor
python dungeons_190828.py ..\..\Server\maps

# 3. conferir
python check_maps.py ..\..\Server\maps ..\..\Server\data\unidades_config.txt config190828.json

# 4. prévia
python preview_maps.py ..\..\Server\maps previa-dungeons.png
```

O passo 3 precisa do `config190828.json` do passo 1 (o terceiro argumento do extrator), porque confere as placas e as falas. Ele também confere `armas` (índice de arma que a unidade tem), `invocacaoPeriodica`, `bauDeDps`, as condições (`limpa:`/`morte:`), a `sala` dos portões e se o mapa cabe na grade do cliente (54 tiles para cada lado).

O gerador centraliza cada mapa na grade do cliente (move tudo em v por tiles inteiros) e transforma todo `*treasureBIG` que tem `_reward` num baú de DPS (`bauDeDps`, com vida e tempo pelo tier da dungeon).

Na prévia: chão livre em bege, muros em roxo-escuro, nascimento em verde, portal de saída em ciano, chefe em vermelho, minichefe em laranja, baús em amarelo, inimigos fixos em azul-claro, inimigos que se movem em azul, inimigos de ondas em roxo, portões trancados em branco e barricadas destrutíveis em cinza.

## Mudar uma dungeon

Edite a função dela em `dungeons_190828.py` e rode de novo. Também dá para editar o `.json` direto, mas rodar o gerador de novo sobrescreve a mudança.

- `m.chain([...])`: salas do caminho principal, em sub-células de 3 u (`w` largura, `h` comprimento, `dx` desvio do eixo, `gap` corredor antes da sala).
- `m.side_room(sala, nome, "L" ou "R", w, h)`: sala lateral (tesouros).
- `m.put(sala, template, pat(...))`: inimigos. `pat` tem `center`, `line`, `column`, `leftwall`, `rightwall`, `bothwalls`, `topwall`, `corners`, `ring`, `grid`, `scatter` e `at` (posições em fração da sala, 0 a 1).
- `boss(...)`: chefe, destroços, falas, fases e o portal de saída. `extra={...}` junta campos ao tipo (`armas`, `invocacaoPeriodica`...). Nas fases: `escudoSegundos`, `efeitoVisual`, `armas`, `invocar`.
- `m.gate_before(sala, nome, template, liberado, opens_after="limpa:<sala>")`: portão no corredor antes da sala (grava `sala` no portão: numa sala com `fechaAoEntrar` ele fecha atrás do jogador).
- `barrel_types(m, "bombbarrel", "bombbarrelboom")`: liga o barril visível ao atirador invisível do anel de balas (o barril solta o atirador quando é destruído). Use para `firebarrel` também; não coloque `*boom` solto com `m.put`.
- `bomb_types(m)`: bombas teleguiadas e jatos bombardeiros viram `kamikaze` com anel de explosão; carregadores e dirigíveis largam bombas que não contam para a sala.

## Tabelas de loot das dungeons

```powershell
python make_loot_tables.py ..\..\Server\maps ..\..\Server\loot_tables
```

Lê os mapas e gera `dungeon_chest`, `dungeon_chest_big`, `dungeon_miniboss` e `dungeon_no_loot` com a lista de unidades de cada grupo.
