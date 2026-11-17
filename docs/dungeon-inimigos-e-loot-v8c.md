# Dungeons: inimigos sem arma, anéis de explosão e loot (v8c, 15/11/2026)

Segue `docs/servidor-de-dungeon.md` (v8/v8b) e `docs/itens-e-loot.md` (v9). Ainda não testado no jogo. Relatório de teste que originou isso: Bomb Factory completa, sem nenhuma dungeon soltando item, bombas sem dano, "bichinhos" imóveis pequenos com tiros curtos e inimigos que só andavam erraticamente.

## Causas achadas

- **Sem loot nas dungeons:** o loot das unidades de dungeon não existe no jogo (201219) e o servidor caía em `normal`/`boss` só quando a unidade não tinha tabela; os minichefes não são "chefe" e usavam `normal`. Agora há tabelas por unidade (abaixo).
- **Bombas sem dano:** o dano ao jogador é aplicado pelo cliente a partir de balas; o servidor não tem dano por contato. `homingbomb`, `homingbomblayer`, `flakblimp`, `flakblimpbig`, `bomberjet`, `fatblimp` e `bombboss` não têm `gunMounts` no cliente, então nunca causavam dano. `bombfuse` também (tamanho 0,4 e alcance 1,5 da arma vêm do cliente; não dá para mudar no servidor).
- **Movimento "errático":** `investida` mergulha e dá a volta; `perseguir`/`circular` orbitam em `distanciaPreferida`. Se ela é maior que o alcance da arma, a unidade gira em volta do jogador sem atirar (`AlcanceDaArma` = velocidade × duração da bala; as minas-robô tinham alcance ≈3,3 e órbita de 7).

## O que mudou

- Servidor (`InimigosEsboco.cs`): comportamento `kamikaze`. Mira um pouco à frente do jogador, a 60% da velocidade máxima (dá para fugir ou derrubar), e **explode ao encostar** (`raioDeContato`, padrão 1,4 u): a unidade some e solta o `aoMorrer` (anel de balas `bombbarrelboom`, 12 balas lentas, dano 1,5). Sem XP nem loot na explosão.
- `DungeonServer.cs` / `DungeonMapData.cs`: `invocacaoPeriodica.contaNaSala` (padrão true); false = as bombas largadas por carregadores e dirigíveis não contam para limpar a sala.
- Gerador (`unit_types.py`, `dungeons_190828.py`): `weapon_reach`, `distanciaPreferida` limitada ao alcance da arma (atinge todas as dungeons com perseguir/circular/investida); `bomb_types(m)` (bombas teleguiadas e jatos viram kamikaze; carregadores/dirigíveis largam bombas); `bombfuse` do depósito virou `bombturret`. Os 17 mapas foram regerados (`check_maps`: 0 problemas).
- Loot: `tools\dungeons-190828\make_loot_tables.py` lê os mapas e gera `loot_tables\dungeon_chest`, `dungeon_chest_big`, `dungeon_miniboss` (um item da classe um tier acima + extras, vida, sucata) e `dungeon_no_loot` (muros, barris, bombas, minas, destroços; um sorteio vazio, porque `rolls` vazio é rejeitado pelo carregador). Chefes continuam em `boss.json`. Tabelas recarregam sozinhas a cada 2 s.

## Decisões e riscos

- Dificuldade não foi escalada (os itens vão reequilibrar).
- Comportamento de bombas, cadência e valores de loot são reconstrução.
- A explosão também sai quando o jogador mata a bomba a tiros (aceito).
- Mudar a órbita afeta também dungeons ainda fechadas (não testadas).
- Campos novos de mapa: comportamento `kamikaze`, `raioDeContato`, `invocacaoPeriodica.contaNaSala` (documentados em `maps\README.md`).

## Falta

Retestar a Bomb Factory (loot de baús/minichefes, bombas, minas, sala 2 e sala 4) e as demais dungeons ao abrir uma a uma; loot dos chefes ainda genérico (`boss.json`).
