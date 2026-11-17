# Servidor de dungeon

O esboço agora sobe três servidores UDP:

| Porta | Papel (ServerRole) | O que roda |
|---|---|---|
| 1111 | Nexus (0) | Rebel City (`maps\nexus.json`) |
| 1112 | Tutorial (3) | Training Grounds (`maps\tutorial_walker.json`) |
| 1113 | Dungeon (2) | qualquer `maps\dungeon_*.json` listado no `dungeons.json` |

O código fica em `Server\Dungeons\`:

- `DungeonCatalog.cs`: lê o `dungeons.json`, põe os porta-aviões das dungeons no Nexus e diz qual dungeon cada porta-aviões leva.
- `DungeonMapData.cs`: lê as seções de dungeon do mapa (`tipos`, `encontros`, `portoes`, `portoesLaterais`, `saida`...). Os tipos entram no `Enemies` com o prefixo do mapa (`dungeon_bomb/bombboss`), então dois mapas podem usar o mesmo nome de tipo com valores diferentes.
- `DungeonServer.cs`: a dungeon em si. Cada conexão ganha a sua cópia (por enquanto um jogador por instância, como no tutorial).

## Como entrar

- **Pelo Nexus:** cada dungeon aberta com `noNexus` ganha um porta-aviões de dungeon numa fileira abaixo da cidade. O cliente mostra o nome e o tier da dungeon (tabela `Texts/dungeons` do jogo). Entrar troca o jogador para o servidor de dungeon com o mesmo avião.
- **Pelo chat:** `/dungeons` lista as dungeons; `/dungeon bomb` (ou `iron`, `DungeonSneezy`, `dungeon_bomb`...) vai direto para ela.

Dentro da dungeon:

| Comando | O que faz |
|---|---|
| `/dungeonhelp` | ajuda e o estado da dungeon |
| `/room` | lista as salas (pontos de controle) |
| `/room N` | pula para a sala N: o que fica para trás some e conta como limpo, os portões do caminho abrem |
| `/boss` | pula para a sala do chefe |
| `/restart` | recomeça a dungeon do zero |
| `/leave` | volta ao Nexus |

## O que acontece numa dungeon

1. O welcome leva o terreno da dungeon (`DungeonBomb`...): o cliente carrega luz, música e território dela e o rótulo de lugar mostra o nome.
2. O servidor manda o chão e os muros do mapa e o ClientMapInfo com as placas do chão.
3. Os portões nascem: barricadas (`liberado`) levam dano; portas trancadas não levam e abrem sozinhas por `abreQuando` (sala limpa, chefe morto).
4. Cada sala monta seus inimigos quando o jogador chega perto, parados no lugar. A sala acorda quando o jogador entra (ou alguém leva um tiro). Arenas mandam ondas.
5. Na sala do chefe os portões fecham atrás do jogador até a sala ficar limpa. Fases de chefe trocam armas, dão escudo (com efeito visual), chamam ajudantes. Chefes que voltam (sequências) e destroços (`*DEATH`) com a fala de morte também funcionam.
6. Cair do avião: ele renasce no último ponto de controle alcançado e a dungeon continua de onde estava.
7. Quando a condição de `saida` fica verdadeira (em geral, o chefe morre), o que sobrou da sala do chefe explode e o porta-aviões de saída aparece. Entrar nele marca a dungeon como terminada na conta (`finishedDungeonTypes`) e leva ao Nexus com o mesmo avião. Quem não entrar volta sozinho depois de `saidaAutomaticaSegundos`.
8. Baú grande (Big Treasure): o primeiro tiro começa a contar; quebrado a tempo, vira o baú de recompensa.

XP dos inimigos e chefes vem da progressão normal (`Progression\`). Loot ainda não existe.

## dungeons.json

Fica ao lado do `enemies.json` (`Server\dungeons.json`). O servidor relê o arquivo quando ele muda (para conexões novas). Sem o arquivo, valem Iron Lair e Bomb Factory.

```jsonc
{
  "portaisNoNexus": {
    "ligados": true,                 // false: nenhum porta-aviões de dungeon no Nexus (só /dungeon)
    "inicio": {"u": -36, "v": -42},  // onde fica o primeiro, em u/v do mapa do Nexus
    "passo": {"u": 30, "v": 0},      // distância entre um e o próximo
    "rot": 0
  },
  "maxJogadores": 10,                // aparece no porta-aviões como (jogadores/máximo)
  "saidaAutomaticaSegundos": 180,    // depois do fim, quem não entrar no porta-aviões volta sozinho; 0 = nunca
  "dungeons": [
    {
      "tipo": "DungeonBomb",         // terreno do cliente: nome, tier, luz e música vêm dele
      "mapa": "dungeon_bomb",        // arquivo da pasta mapas (sem .json) — troque para usar outra versão da dungeon
      "nome": "Bomb Factory",        // só para o log e o chat
      "ativa": true,                 // false: fechada
      "noNexus": true,               // porta-aviões no Nexus
      "multiplicadorDeVida": 1.0,    // vida de todos os inimigos desta dungeon (dificuldade)
      "abreEm": "",                  // "2026-12-01 20:00" (hora local): fechada até essa hora
      "nota": "..."
    }
  ]
}
```

### Usar outra versão de uma dungeon

Copie o mapa (por exemplo `dungeon_bomb.json` para `dungeon_bomb_dificil.json`), mude o que quiser e troque `"mapa": "dungeon_bomb_dificil"` no `dungeons.json`. O `"tipo"` continua `DungeonBomb`, porque é ele que diz ao cliente qual dungeon é. O formato do mapa está em `maps\README.md`.

## Estado

- **Ligadas para o primeiro teste:** Iron Lair (`dungeon_sneezy`) e Bomb Factory (`dungeon_bomb`).
- As outras 15 estão no `dungeons.json` com `"ativa": false`. Elas usam o mesmo servidor, mas The Dig (broca), The Vaults (chave e emboscadas), Temple of Bast (maldições dos altares), as arenas e a corrente da Meowza (`proximaDungeon`) têm partes que ainda não estão no servidor.
- **Loot:** `loot_tables\dungeon_chest*.json`, `dungeon_miniboss.json` e `dungeon_no_loot.json` (gerados por `tools\dungeons-190828\make_loot_tables.py`): baús e Big Treasure soltam itens, minichefes soltam um item acima do tier, e muros, barris, bombas, minas e destroços não soltam nada. Chefes usam `boss.json`.
- Ainda não existe: consumo de chaves, modificadores de dungeon, vários jogadores na mesma instância e as máquinas de estado originais das unidades (a camada de IA da build 190426 foi exportada em `tools\dungeons-190828\ia_190426.json` para isso).

### Iron Lair (SN-Z)

A build 190426 ainda tem a camada de IA "toggle_safety" do SN-Z: escudo por 10 s com a vida em 90, 70, 50, 30 e 10%. O mapa usa isso nas fases e troca as armas pelos grupos de `gunMounts` do próprio SN-Z (`mainphase1`, `mainphase2xx`, `mainphase301`, `mainphase40x`, `finalattack*`). Os esquadrões que ele chama em cada fase são reconstrução.

### Bomb Factory

The Bomb Maker não tem nenhuma arma no cliente: o ataque dele é uma chuva de minas-robô (`invocacaoPeriodica`). Overseer Charles também não tem armas e solta bombas teleguiadas. O Big Treasure do cofre é um baú de DPS. As bombas teleguiadas e os jatos bombardeiros (sem arma no cliente) são `kamikaze`: correm atrás do jogador e explodem num anel de balas ao encostar. Os carregadores e dirigíveis largam bombas (`invocacaoPeriodica`, fora da contagem da sala). Os `bombfuse` do depósito viraram torres, porque o tamanho e o alcance deles vêm do cliente e não dá para mudar no servidor. Cadências, vida do baú e tempo são reconstrução e ficam no mapa para ajustar.


## Unidades invisíveis armadas que ainda precisam de teste

Estas unidades têm arma e arte `!EnemyInvisible` (no jogo original ficavam presas a um desenho do cenário ou eram comandadas por um roteiro). Estão colocadas nos mapas das dungeons que ainda estão fechadas; ao abrir cada uma, conferir se alguma atira no jogador sem ser vista:

- Reeducation Camp: `lavapillar` (fornalha, ninho da Fênix), `riotguardleader`. O `firebarrelboom` já foi corrigido como o da Bomb Factory.
- Frozen Owl Monastery: `blizzard` (salão da nevasca, 7 armas).
- Inner Sanctum / Meowza: `meowzalaserB` e `meowzalaserD`.
- Command Center: `avatarcannon`, `sunfoxtrap`.
- The Vaults: `lasergateWE` (corredor dos lasers).
- The Elder Arena: `beartrap`, `arena2tripodminionmaster`.
- Outras: `dochatcher` (The Dig / Home), `trapbutton` (Laser Playground), `coilsnake01neck` (Venom Compound).
