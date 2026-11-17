# Tutorial do walker ("Training Grounds") e saída para o Nexus (TutorialEsboco.cs)

Referência técnica do tutorial refeito no esboço (cliente 201219.1413). Atualizado em 14/11/2026 (4ª versão) com base no vídeo do Open Beta (https://www.youtube.com/watch?v=zr8a4QtMWSU, canal Iczel Gaming, 2019) e no `configBinary` do cliente.

## Mapa em arquivo (v5)
- Desde a v5, o chão, os muros, os pontos, as áreas, as linhas do roteiro, os portões, os pontos de controle e as placas ficam em `Server\maps\tutorial_walker.json`. O formato está em `docs/formato-de-mapas.md`.
- O roteiro continua em `TutorialEsboco.cs` e lê cada coisa pelo nome.
- As medidas abaixo (em tiles a, b) viraram u = 12a, v = 12b no arquivo. O perfil e o pilar estão no "desenho": 84 linhas × 20 colunas, si −8..11, sj 0..83.

## Sem plugin (3ª versão)
- O jogo conecta no esboço só com `OZY_URL` + `-connecttolocal` (Run game.ps1). O plugin QuieterPlugin é opcional (logs, diagnóstico).
- Os dados que o plugin exportava agora vêm com o servidor em `Server\data`. DadosDoJogo.cs procura lá primeiro, depois no BepInEx:
  - `acoes_config.txt` (nuvens e manobras)
  - `unidades_config.txt` (1641 unidades)
  - `mapinfo_config.txt` (ClientMapInfo de 10 terrenos)
- Esses arquivos são fixos para a versão 201219.1413.
- Para desligar o BepInEx: renomear `winhttp.dll` na pasta do jogo.

## O que existe no cliente e o que era só do servidor
- `Dungeons/dungeon_tutorial` e as IAs das unidades **estão vazios** em todas as versões conferidas (201219.1413, 190828.2045, 190426.2227). O vídeo é a única fonte do mapa.
- Textos:
  - Placas `newtutorialwalkergroundsign1..10` e `6_2` vêm da tabela `lore`.
  - A placa `11` ("Basic training: COMPLETE!") vem do evento `portal` da ClientMapInfo.
  - Falas (`bark`) vão por `ClientShowUnitChat` (7332), formato 4, com os retratos `sergeant` / `bosscat`.
- Portal (prefab 11): com o tipo de dungeon vazio, a arte é `PortalCarrierPrefab` (porta-aviões). No vídeo, de 2019, o tutorial mandava para "Gull Mountain"/City Ruins.

## ClientMapInfo (InfoDeMapaEsboco)
- Formato Copper (Lidgren, bits LSB primeiro): string autoRespawn, short nChaves + strings, short nEventos + eventos, 3 bools.
  - Evento: string trigger, once_key, timeout_key, varint duração, bool + condição, int nReações, reações (3 strings + 4 floats).
  - Condição: int code, string name, 3 floats, int nSub + sub.
- Códigos de condição: And 1, Not 3, InventoryOpen 9, LowHealth 10, OnceKeySet 14, NotOnceKeySet 15, ApplicationFocused 18, **PlayerNearPos 31** (x, y, raio).
- Gatilhos de tempo: `repeatEvery_N`, `each_N`, `onceAfter_N`, `after_N`. A posição desses gatilhos é 0, então `lore_text_relative_pos` usa F0/F1 como posição absoluta.
- `RegistrarMapa(terreno, montar)` lê os bytes originais, acrescenta eventos e reescreve.
- O tutorial acrescenta 1 evento por placa: `repeatEvery_1` + PlayerNearPos(x, y, raio) → `lore_text_relative_pos` (chave, LoreTextClient). Funciona sem a entidade LoreText do servidor, que nunca apareceu. Desde a v5 o evento é montado por `MapaEsboco.AdicionarPlacas` a partir de `placas` do mapa.
- Rotação do texto: `Quaternion.Euler(0, 0, rot)`. O prefab LoreTextClient já sai na diagonal do chão com rotação 0 (confirmado no teste: com 45 o texto ficou em pé na tela). As placas usam **0**.

## Visual escuro (abismo em volta)
- Célula sem tile: o cliente desenha o chão do território padrão (grama/água, nuvens brancas), e o tutorial parecia de dia.
- Agora há tiles "vazios" até 4 tiles em volta de todo o chão (`vazioEmVolta` 4 no mapa): território 63 (dungeontutorialwalker), peças com `territoryIDOverride` 74 (dungeonsky, groundTag `_none_`), asset 0, sem muros.
- Resultado: luz, neblina, nuvens azuis (firecloud) e roxo do minimapa do tutorial, sem chão. Confirmado no teste da 3ª versão.
- `groundFloorHeight` do tutorial herda 19 do `default`.

## Escala
- 1 px do minimapa (1080p) ≈ 1,1 unidade (velocidade do walker 3 un/s). O portão do vídeo tem 4 caixas de parede a parede.

## Layout (sub-células de 3 un; si = 4a, sj = 4b; meio em si = 2)
- Chão: tiles a −1..2, b 0..17, mais a arena a −2..3, b 17..21.
- Perfil (largura livre em sub-células, por fileira sj):

| sj | Largura | Trecho |
|---|---|---|
| 1–9 | 6 | sala de partida (pilar em si 1–2, sj 7) |
| 10 | 4 | |
| 11–23 | 6 | hangar |
| 24 | 2 | |
| 25–30 | 4 | antessala |
| 31 | 2 | parede `tutorialwall0` |
| 32–41 | 4 | |
| 42–47 | 6 | anel |
| 48–56 | 4 | |
| 57 | — | portão (4 caixas) |
| 58–66 | 4 | |
| 67 | — | portão do chefe |
| 68–82 | 8, 12, 14, 16×9, 14, 12, 8 | arena (48 × 45 un) |

- Paredes de caixas: rotação 0, no centro da sub-célula, sem muro por baixo (o `WallCollider` vem da unidade `isWall`).
- Chefe em (0,5; 19,9); porta-aviões no centro da arena (0,5; 18,9).

## Inimigos
- Pré-colocados e parados (`DefinirAdormecido`/`AcordarGrupo`); acordam por proximidade, pelo roteiro ou com um tiro.
- Hangar: **5 Training Turrets** (`tutorial_canhao`) na parede esquerda.
  - Comportamento **"fixo"**: não gira, atira com a rotação da unidade + ângulo da arma. São invulneráveis, com alcance 40.
  - Posição: a −0,125; b de 3,375 a 5,375, de 0,5 em 0,5. **Rotação 315 (de frente para +b).**
  - A arma `tutorialcannon` tem multiBullets 2 e **multiSpread 180**: as duas balas saem a −90° e +90° da frente (GunWielder: mountRotation − spread/2·(n−1) + ownerRotation), então cruzam a sala nos dois sentidos de a.
  - A arte (TutorialEnemyTurret) tem o cano a +90° da frente. Com rotação 45 (3ª versão), as balas iam ao longo do corredor.
  - Acordam em b ≥ 2,9, uma a cada 0,35 s (rajadas desencontradas), e dormem em b ≥ 6,3.
  - Arma: modo 7, 0,85 tiro/s, velocidade 2, 10 s, dano 0,01.
- Drones 1 (antessala, acordam a 9 un) e drones 2 (acordam quando a parede cai).
- Anel de 14 drones de gás, com abertura de 70° na entrada.
- Caixa de suprimentos.
- Chefe: criado no "Prepare yourself!".

## Saída
- Portal de saída (flags 2|4) → `SendMarkDungeonFinished` + `ServerSendMeThroughPortal` → `ClientPortalResult` + `ClientSwitchToServerWithPlane`.
- **Causa do porta-aviões "sumido" (achada na 4ª versão):**
  - O nascimento ia só com posição e rotação.
  - O cliente extrapola entidades do servidor com `posição + velocidade × tempo` (UnitUpdateCollection.Extrapolate). Sem o campo de velocidade, usa o valor de "não definido" do FieldUpdate (1234567,9; −1234567,9).
  - Como o Portal tem `minUpdateInterval` 1000, a extrapolação não é limitada: o porta-aviões saía andando na diagonal (+x, −y), corredor abaixo. O jogador o viu "lá embaixo", perto do anel.
  - Agora `ObjetosEsboco.EscreverNascimento` manda sempre a velocidade (0, 0) (máscara 1|2|4).
- Saída de segurança mantida: 90 s depois do portal, ou `/sair` depois do chefe, o servidor marca o tutorial e manda para o Nexus (`TutorialEsboco.SairParaONexus`, ligado no Program.cs).

## Comandos no chat (tutorial)
`/etapa N` (1 drones, 2 anel, 3 caixa, 4 portão do chefe, 5 portal), `/portal`, `/sair`, `/recomecar`, `/tutorialajuda`. O `/recomecar` também pega o mapa novo se o arquivo mudou.

## Pendências
- Conferir no jogo que a v5 (mapa em arquivo) ficou igual à v4.4.
- Loot da caixa e XP. Nexus: ver `docs/nexus-rebel-city.md`.
