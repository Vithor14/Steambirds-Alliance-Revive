# Dungeons: inimigos sem arma, anéis de explosão e loot (v8c, 15/11/2026)

Segue `docs/dungeon-server.md` (v8/v8b) e `docs/items-and-loot.md` (v9). Revisado em 23/11/2026 com os nomes atuais (campos de mapa e comportamentos em inglês). Ainda não testado no jogo; as correções de 22/11 estão em `docs/dungeon-tuning.md`. Relatório de teste que originou isso: Bomb Factory completa, sem nenhuma dungeon soltando item, bombas sem dano, "bichinhos" imóveis pequenos com tiros curtos e inimigos que só andavam erraticamente.

## Causas achadas

- **Sem loot nas dungeons:** o loot das unidades de dungeon não existe no jogo (201219) e o servidor caía em `normal`/`boss` só quando a unidade não tinha tabela; os minichefes não são "chefe" e usavam `normal`. Agora há tabelas por unidade (abaixo).
- **Bombas sem dano:** o dano ao jogador é aplicado pelo cliente a partir de balas; o servidor não tem dano por contato. `homingbomb`, `homingbomblayer`, `flakblimp`, `flakblimpbig`, `bomberjet`, `fatblimp` e `bombboss` não têm `gunMounts` no cliente, então nunca causavam dano. `bombfuse` também (tamanho 0,4 e alcance 1,5 da arma vêm do cliente; não dá para mudar no servidor).
- **Movimento "errático":** `charge` mergulha e dá a volta; `chase`/`circle` orbitam em `preferredDistance`. Se ela é maior que o alcance da arma, a unidade gira em volta do jogador sem atirar (alcance da arma = velocidade × duração da bala; as minas-robô tinham alcance ≈3,3 e órbita de 7).

## O que mudou

- Servidor (`Enemies.Ai.cs`, `Enemies.cs`): comportamento `kamikaze`. Mira um pouco à frente do jogador, a 60% da velocidade máxima (dá para fugir ou derrubar), e **explode ao encostar** (`contactRadius`, padrão 1,4 u): a unidade some e solta o `onDeath` (anel de balas `bombbarrelboom`, 12 balas lentas, dano 1,5). Sem XP nem loot na explosão. O comportamento `mine` é o mesmo gatilho (`contactRadius`), mas a unidade não se move nem atira (Iron Lair, 23/11).
- `DungeonServer*.cs` / `DungeonMapData.cs`: `periodicSummon.countsForRoomClear` (padrão true); false = as bombas largadas por carregadores e dirigíveis não contam para limpar a sala.
- Nos mapas (gerados em 15/11 pelos scripts que saíram do repositório; hoje o `.json` é a fonte): `preferredDistance` limitado ao alcance da arma (todas as dungeons com `chase`/`circle`/`charge`); bombas teleguiadas e jatos viram `kamikaze`; carregadores e dirigíveis largam bombas; `bombfuse` do depósito virou `bombturret`.
- Loot: `loot_tables\dungeon_chest`, `dungeon_chest_big`, `dungeon_miniboss` (um item da classe um tier acima + extras, vida, sucata) e `dungeon_no_loot` (muros, barris, bombas, minas, destroços; um sorteio vazio, porque `rolls` vazio é rejeitado pelo carregador). Foram geradas por um script a partir dos mapas (o script saiu do repositório; os arquivos passam a ser editados à mão, ver `Server\loot_tables\README.md`). Chefes continuam em `boss.json`. Tabelas recarregam sozinhas a cada 2 s.

## Decisões e riscos

- Dificuldade não foi escalada (os itens vão reequilibrar).
- Comportamento de bombas, cadência e valores de loot são reconstrução.
- A explosão também sai quando o jogador mata a bomba a tiros (aceito).
- Mudar a órbita afeta também dungeons ainda fechadas (não testadas).
- Campos de mapa e de tipo: `behavior: "kamikaze"`, `contactRadius`, `periodicSummon.countsForRoomClear` (documentados em `docs/map-format.md`).

## Falta

Retestar a Bomb Factory (loot de baús/minichefes, bombas, minas, sala 2 e sala 4) e as demais dungeons ao abrir uma a uma; loot dos chefes ainda genérico (`boss.json`).
