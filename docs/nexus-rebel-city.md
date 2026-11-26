# Nexus = Rebel City (`Server\Nexus.cs`)

Remontagem do Rebel City do Open Beta (cliente 201219.1413). Criado em 14/11/2026 (4ª versão; ajustes na 4.1 a 4.4); atualizado em 23/11/2026. A planta não mudou nas 0.6.0–0.8.0. A colisão do chão (`collisions` no mapa) continua sem teste no jogo (`docs/dungeon-tuning.md`).

**Desde a v5, a planta fica em `Server\maps\nexus.json`** (formato em `docs/map-format.md`).
- O arquivo usa u = A + 3 e v = B + 3 em relação às coordenadas (A, B) desta página.
- O `Nexus.cs` só lê o arquivo e manda chão, enfeites, prédios e colisões. No arquivo os campos são em inglês (`floor`, `decor`, `buildings`, `spawn`, `collisions`; `docs/map-format.md`).
- As tools `preview2.py` e `encostar.py` (de um chat antigo) não estão no repositório; o mapa se edita no editor de mapas do launcher.

## Fontes
- O mapa original era só do servidor (WorldLayout.PlaceManualScatters e tiles manuais). O território `nexus` e as plantas vêm vazios no cliente.
- Vídeos:
  - Live do Vithor "Jogando Steambirds Alliance uma ultima vez pt 2" (https://youtu.be/knlklAYgedk). De 13:02 em diante passa por todos os prédios.
  - "Steambirds Alliance 2021/02/19 (The last 30 minutes before shutdown)" (https://www.youtube.com/watch?v=8BstykM2Gts).
  - "Empty Rebel City" (https://www.youtube.com/watch?v=JiQo5tyHI-I).
- Marcos do vídeo pt 2:
  - 13:04–13:12: praça da águia entre Store e Hangar (pista lisa, sem heliponto).
  - 14:26–14:40: Flock Hall (atraca às 14:29, sai às 14:40).
  - 14:42–14:53: Storage.
- Print de uma recriação do Rebel City vista de cima: ilha de pedra, cruz de pistas, porta-aviões no fim da pista.
- Textos com seta do chão: a ClientMapInfo do Nexus põe cada um num deslocamento fixo do prédio.

| Gatilho | Deslocamento | Extra |
|---|---|---|
| `store`, storage, pub, crafter | +4, −3 | |
| hangar | −3, −4 | rot −90 |
| pilotschool | −3, −3 | rot −90 |
| combatsim | +3, +3 | ArrowDown |
| flockhall | +5.1, −5 | |
| megacarrier | −1.5, −4.5 | ArrowDark |

- Os prédios anunciam o gatilho sozinhos (`Building.AnnounceClientTrigger`, InvokeRepeating).

## Escala e território
- Território `nexus` (37): groundTag/cliffTag/scatterTag `_none_`, wallTag `nexus`, groundFloorHeight 0,5.
  - `cameraSideLengthOverride` 11: câmera mais perto, ~49 px por unidade a 720p.
  - Células sem tile mostram o céu laranja.
- Prédios: arte de 6 × 6 un (meio tile = um slot de chão), a 12 un um do outro.
- Peças RebelCity_Runway_* de 6 × 6 (Filler, FunnelA/B, Arrow1/2, Insignia, StraightA1/A2/B, Corner1/2): geometria ±3,08 com a borda.
- Cap é meio slot: 3 un da ponta ao lado largo, borda até 3,08, encaixes até 3,45 por baixo da peça vizinha.
- Straight/Corner/Cap têm treliças de madeira por baixo (até z 25). A superfície das pistas fica em z −0,029 local.
- As malhas exportadas pelo AssetRipper (glTF) têm o x espelhado em relação ao jogo (a águia do chão aparece com o bico do outro lado no vídeo).
- Altura:
  - Peças de slot: `localPosition` z = groundFloorHeight sob a raiz do tile (z 0).
  - Scatter com precisão total: posição no mundo direto (o esboço manda z 0,5, mesma altura).
  - −z é para cima.
- A arte de cada prédio não está no prefab da entidade (FlockHall.glb, Tavern.glb… só têm `artParent`). `Building.Init` chama `SetupArtObject(artPrefab.name)` e pega a arte do pool "Planes".
- Peças de prédio que também existem como ground assets: RebelCity_Building_Tavern 12847, Store 17504, Hangar 16084, Crafting 19831, CombatSim 31070, Storage 45440, School 53437.
- Props de guilda: GuildSign 14286, Guild_PurpleBanners 13767, Guild_TealBanner 49464, Guild_BridgeLights 29884, GuildWindowGlow 40066, Guild_Lights 61055.
- RebelCity_Building_FlockHall não é ground asset.
- Enfeites do fundo, parte "sólida" (no nível do chão ou acima, rotação de planta 0, em torno da origem), e altura acima do chão. O resto da malha é penhasco que desce até 16.

| Enfeite | A | B | Altura |
|---|---|---|---|
| BackWall01 | −4,5..3,5 | −4,2..6 | 6 |
| BackWall02 | −3,5..5 | −5..3,2 | 7,7 |
| BackWall03 | −7..6 | −3,5..5,2 | 3,9 |
| BackWall04 | −7,2..5,2 | −3..7 | 5,3 |
| BackWall05 | −7,8..3,5 | −5,2..10,8 | 10,1 |
| BackWall06 | −4..6 | −4..6,8 | 8,2 |
| BackWall7 | −4,5..10,2 | −3,8..10,2 | 11,9 |
| BackWall08 | −4,2..4 | −6,2..5 | 5,2 |
| BackWall09 | −3..2,8 | −3,5..4 | 5,1 |
| LowerRock | −11,5..10,2 | −10,2..11 | 13,2 |

## Flock Hall
- A arte RebelCity_Building_FlockHall tem 6 × 6 e 5,3 de altura: base quadrada, caixa-d'água, tanque com emblema de coração e tampa redonda laranja, cano, porta iluminada.
- Ela chega do pool com os 7 renderers desligados (`buildings_log.txt` de 14/11: "renderers=7 (ligados 0)"). Nenhum código do cliente liga esses renderers (procurado em ArtInfo, Entity, Building, LODMesh, ClientEventManager).
- Plugin 1.18.0+: liga os MeshRenderer/SkinnedMeshRenderer/SpriteRenderer da arte (e os pais desligados) e confere a cada segundo. Config `[Fixes] ShowFlockHallArt`. Sem o plugin, o Flock Hall fica invisível.
- Plugin 1.19.0: o grupo `Signs` (sign, sign (1)) fica desligado. São letreiros curvos com "HANGAR", que apareceram no jogo em 14/11 e não existem no vídeo.
- No vídeo (14:29–14:40), o Flock Hall fica no fim da rua dele, encaixado entre prédios da cidade: telhados verdes, torres com janelas verticais, chaminé com vapor, um prédio sobre palafitas com escada vermelha à direita.
- A porta iluminada amarela fica de frente para a rua, com um tanque redondo de tampa laranja do lado. Combina com a arte do jogo.
- Num prédio elevado perto da Store aparece um heliponto com "H" e luzes nos cantos.

## Coordenadas da planta
(A, B) em unidades:
- A: eixo a da grade (diagonal +x +y, para o porta-aviões).
- B: eixo b (−x +y, para o Flock Hall).
- Slots com centro em múltiplos de 6. No mundo, A+3 e B+3 (o centro do slot fica a 3 un da borda do tile).
- Rotação no mundo = rotação na planta + 45.

| Prédio | Prefab | (A, B) | Rotação (frente) |
|---|---|---|---|
| Loja (storeId `store_general`) | 24 StoreBuilding | (0, 12) | 315 (−B) |
| Armazém | 14 StorageBuilding | (12, 12) | 315 |
| Pilot's Pub | 23 Tavern | (−12, 12) | 315 |
| Oficina | 26 Crafter | (24, 12) | 315 |
| Flock Hall | 28 FlockHall | (6, 24) | 315 |
| Hangar | 12 Hangar | (12, 0) | 45 (−A) |
| Pilot School | 27 PilotSchoolBuilding | (24, 0) | 45 |
| Combat Sim | 29 CombatSimBuilding | (−12, 0) | 225 (+A) |
| Porta-avioes das missões | 30 MegaCarrierBuilding | (39, 6) | 45 |

- Pista principal em B = 6 (A −27 a 33), cruzamento em (6, 6).
- Pier do Flock Hall em A 3..9 (B 15 a 27, com o Flock Hall no último slot).
- Águia (Insignia, rotação 0: cabeça para +B) em (0, 0), na praça. O avião nasce em cima dela.
- Enfeites (scatter com precisão total: posição/rotação no mundo, z 0,5): TavernStore (−6, 12), StorageCrafting (18, 12), Bunker (−12, −6), GunTowers (−30, −3) e (−30, 15), FlagTrio (−21, 13), TallFlag (27, −1), casas no penhasco, FreeRock embaixo do chão (ilha).
- Heliponto: removido da praça na 4.4 (o vídeo mostra só pista em 13:04–13:12). Até a 4.3 ficava em (6, −6).
- Caps nas pontas (4.3): (−30, 6) rot 225 e (36, 6) rot 45, no centro do slot seguinte à pista. A origem da malha do Cap fica na ponta estreita; o lado largo fica 3 u para −x local.
- Cidade (4.4, todos com rotação 45). Posições calculadas por `encostar.py`: a parte sólida chega a ~0,25 u do chão sem cobrir; penhasco que desce mais de 0,3 u pode passar por baixo.
  - Esquerda do pier (as mais baixas, ficam entre a câmera e o Flock Hall): BackWall09 (−0,25, 19,5), BackWall08 (−1,5, 28).
  - Atrás do Pub e da Store: BackWall04 (−11, 18,5), BackWall03 (−22, 20), BackWall01 (−31, 21,5).
  - Direita do pier: BackWall02 (13, 21), BackWall06 (12,75, 28), BackWall7 (23, 19,25) atrás do Armazém e da Oficina.
  - Logo atrás do Flock Hall: BackWall05 (6, 32,75).
  - Morros: LowerRock (−10, 33), (8, 44), (34, 24,5).

## Protocolo
- Chão: ClientReceiveTerrainTiles com peças por slot e scatter. Sem muros desenhados; só as `collisions` do mapa (22/11, **não testado no jogo**: fundos da cidade, morros, torres, bunker e adereços entre os prédios, nunca os prédios, para dar para atracar). Risco: a arte do muro vem do território do Nexus (`wallTag nexus`); sem arte só aparece o erro "No walls found" no log do jogo e os colisores continuam existindo.
  - Peças por slot com bandeira 8 (rotação). Sem ela, o cliente sorteia giro e espelho, e o MeshCollider fica com escala negativa.
  - Scatter com bandeiras 8|4: posição float x, y, z; rotação obrigatória.
  - Desde a v5 a mensagem é escrita por `Maps.Map.WriteFloor`.
- Prédios: SpawnUnitsBatch com o prefab, shortIds 70001+ (na ordem do arquivo), time 0, autoridade do servidor.
  - unitOptions nulo (só a loja manda a string storeId); posição + velocidade 0 + rotação.
  - Enviados uma vez por conexão, quando o avião nasce.

## Atracar nos prédios
- Pub (QuestGiverBuilding, giverId `tavern`) e Oficina (Workshop, `crafter`):
  - Ao atracar, o cliente manda `ServerEnterQuestGiver` e só abre a janela quando chega `ClientReceiveGiverQuests` desse giverId (54192: string giverId + Int32 quantidade + entradas).
  - O `Game.Rpc.cs` responde `ServerRequestGiverQuests` (54191) e `ServerEnterQuestGiver` (54195) com o dicionário vazio (quests ainda não existem; `CHECKLIST.md`, seção 9). Fechar a janela solta o avião.
- Armazém:
  - `UGUIStorage` sempre lê a vaga 0 do cargo do avião e do armazém (`InventoryPosition.FirstIn(Cargo)` é sempre válida).
  - Desde a 4.1 o welcome (`PlayerData.cs`) manda as vagas do armazém: base 16 (`GlobalConfig.baseNumStorageSlots`) mais as compradas na loja `store_storage`. O cargo do avião tem 12 (mais as vagas compradas).
- Loja, Pilot School, Flock Hall, Combat Sim e porta-aviões abrem a janela direto; fechar a janela solta o avião. A Loja e as lojas do Hangar, do Armazém e da Pilot School vendem de verdade desde 22/11 (`docs/stores.md`).
- Hangar usa `StartHangarPlaneSelection`.
- Flock Hall: as flocks existem desde 23/11 (`Server\Social`, `docs/social.md`), incluindo `ServerFindFlockSuggestion`; falta abri-lo no jogo.
- Porta-aviões gigante pede `ServerListMissions` (12000). Desde 22/11 o `Dungeons\MissionTransport.cs` responde com uma missão por dungeon aberta e `ServerMissionJoin` (12001) leva à dungeon (`docs/dungeon-tuning.md`); a resposta vazia da 4.2 (`MissionsAndLookAhead`: bool true + varint 0 + varint 0, sem marcador de nulo) vale quando `entryMode` é `portals`.

## Envios do cliente e diagnóstico
- Quando algo quebra, o jogo manda o log sozinho:
  1. `POST /ozy/user_uploads/upload` (path `errorlog`) devolve `UploadSlot {upload_url, finish_url, download_url, uploadid}`.
  2. PUT do arquivo no upload_url.
  3. POST no finish_url.
- Desde a 4.1, o servidor (`Program.Http.cs`) devolve URLs em `/ozy/user_uploads/put/<id>` e grava os arquivos em `Server\client-uploads`.
- Plugin Steambirds Quieter:
  - 1.17.0: desligou a correção das placas do chão e criou `BepInEx\predios_log.txt` (postfix em `Building.Init`).
  - 1.18.0: liga a arte do Flock Hall.
  - 1.19.0: removeu o código das placas (avisos CS0169/CS0414 na compilação) e apaga o `placas_log.txt` velho.
  - 1.20.0 a 1.22.0: correção da tela de morte, retratos, modo de exportação (`GameFilesExport.cs`) e nomes em inglês nas seções do `.cfg` e nos arquivos gravados (`buildings_log.txt`, `terrain_dump.txt`, `ascii_maps.txt`). O plugin atual é o **1.22.0**.

## Pendências
- Confirmar no jogo: pontas da pista (4.3), Flock Hall sem os letreiros "HANGAR" (plugin 1.19.0), a cidade em volta do Flock Hall (4.4) e a v5 (mapa em arquivo) igual à 4.4.
- Talvez: heliponto com "H" no prédio elevado perto da Store, como no vídeo (14:40).
- Colisões do Nexus (22/11) e Mission Transport: sem teste no jogo.
- Falta no servidor: quests do Pub e da Oficina (listas vazias), missões de verdade no porta-aviões (hoje só dungeons) e o Combat Sim. Lojas, armazém, Pilot School e Flock Hall existem no servidor e faltam testes no jogo (`CHECKLIST.md`, seções 6 e 10).

- O arquivo do mapa é escolhido pelo `nexusMap` do `dungeons.json` (padrão `nexus`; 24/11, tela "Maps and dungeons" do launcher). Nome sem arquivo volta ao `nexus`.
