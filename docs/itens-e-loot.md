# Itens, inventário e loot (v9, 15/11/2026)

Primeiro teste no jogo (15/11, 15:42): o sorteio funciona (`[LOOT] ... soltou N saco(s)`) e o cliente recebe os sacos (`spawn_log`: prefab 8, shortId 5000000), mas nenhum aparecia nem era coletado. Teste em dois PCs (16/11, host sem o jogo): nenhum loot. Causas e correções na seção "Correções". Código em `Server\Items`, tabelas em `loot_tables\` (doc: `loot_tables\README.md`), opções do host em `items.json`, dados da conta em `inventory.json`, catálogo em `catalog\item_catalog.md`/`.json`.

## Arquivos

- `ItemCatalog.cs`: lê do configBinary `Items/**`, `ingredients` (gera os `<id>_ingpickup`), `playerclasses` (tags permitidas, itens iniciais, variantes), `mulching`, `reforging`, `global` (vagas, `lootbagLifetime`, tabela de desgaste) e o `loot`/`lootGlobal` de `Units/**`. Baldes por slot e tier como `SBConfig.GetBucketedItemConfigs` (sem obsoletos e pré-lançamento). **Sem configBinary não há catálogo: loot, reforja e reciclagem ficam desligados.**
- `InventoryModel.cs` / `InventoryStore.cs`: inventário autoritativo (equipados, arma alternativa, cargo, `Permanents_`, `_accumulatedItems`, armazém com characterId próprio, moedas, ingredientes), salvo em `inventory.json`. Itens iniciais com o mesmo id de antes (SHA-256 de `item|characterId|slot`).
- `InventoryWire.cs`: bytes de `InventoryItem`, `InventoryPosition` (byte 0x80|loc<<5|slot + índice), `StandardInventory` e das mensagens de inventário e respostas de RPC (52428; -1 = recusado).
- `InventoryHandler.cs`: RPCs `17407` troca, `60901` remover (soltar = saco no mundo; lixo = reciclagem), `60902` consumir, `60903` ordenar (estável + `ClientReplaceInventory`), `60904` acumular melhoria, `60905` favoritar; `21818` coleta, `4422`, `61999`, `12157` (Oficina) e `25386` só para sacos. Ganchos: `PrepareForWelcome` (itens vencidos), `PlayerIdentified` (carteira de ingredientes), `PlaneCreated`, `PlaneChanged`, `PlaneDestroyed`. Comandos `/itens`, `/item`, `/loot`, `/inventario`, `/limpar_cargo`, `/moeda`, `/ingrediente`, `/sacos`, `/limpar_sacos`, `/exportar_itens`.
- `ItemActions.cs`: o cliente executa as ações do item no próprio avião antes de pedir o consumo; o servidor entrega desbloqueio, variante, moeda, buff de conta e ingrediente. `deco`, `dungeon` e `planexpboost` ainda são recusados (o item fica).
- `LootTables.cs`: tabelas em arquivo no formato `LootTableDef` do jogo; sorteio igual a `SteambirdsLootTable.DrawLoot` (tier relativo ao inimigo, `usableChance`, `ensureUsableSlots`, condições). Ordem: `units` → loot do jogo da unidade (embutido + `lootTableId` pelo `replaces` mais específico) → `normal`/`boss`.
- `LootDrops.cs`: sacos (prefab 8 `lootbag`, um item por saco, shortIds 5.000.000+), um por jogador; `KillUnitsBatch` `LootBagDestroyed` na coleta e `Fizzled` no fim do tempo. Chamado em `InimigosEsboco` na morte (time da IA) e na abertura de baús; vale no tutorial e nas dungeons.
- `Workshop.cs`: reforja (condição < 0 → 0) e supercarga (tier 9+, condição 1 e stats de `SuperchargeNewStatProbabilities`), custos pelas chaves de `InventoryItem.GenerateItemConditionLookupKeys`.
- `ItemCatalogExport.cs`: grava o catálogo quando a build muda.

## Correções

- 15/11 (v9.1, `LootDrops.cs`): as opções do saco (`LootBagScript.GenerateEntOptions`) têm dois bools de 1 bit, então o buffer não fecha em byte inteiro. `m.Write(NetBuffer)` do Lidgren descarta os bits sobrando do último byte, mas o cliente (`NetBufferCopperSerializer.Read`) sempre lê `LengthBytes` bytes inteiros: o `FieldUpdate` depois das opções era lido 6 bits deslocado e o saco nascia numa posição lixo. Agora grava `m.Write(options.Data, 0, options.LengthBytes)`, igual ao `NetBufferCopperSerializer.Write` original. **Regra geral:** para embutir unitOptions, nunca usar `Write(NetBuffer)` quando o buffer pode ter bits soltos (bools). As outras opções do esboço (avião, portal, prédios, nuvens) fecham em byte e não são afetadas.
- 16/11 (`Build launcher.ps1`, função `CopyGameDataToServer`): o servidor lê o configBinary direto do jogo instalado (`DadosDoJogo.LugaresDoConfigBinary`) e o pacote `dist` não levava o arquivo. Num host sem o jogo (teste com o servidor em outro PC) ficava sem catálogo de itens (nenhum `[LOOT]`), com tabelas de XP embutidas (190828), skills sem validação, só o walker desbloqueado e modelo de voo "?". Agora o script copia `Steambirds_Data\StreamingAssets\configBinary` e `BepInEx\portraits_config.txt` do jogo para `dist\server\dados` (o `dist` está no `.gitignore`, então nada disso vai para o GitHub). Ideia futura: o launcher do cliente enviar o configBinary ao host que não tem o jogo.

## Decisões

- Cargo compartilhado entre os aviões (`isUsingSharedCargoForAll = true` no global; dica de lore "one cargo pod for all your planes"). Opção `sharedCargoForAllPlanes`.
- Morte permanente: cargo e equipados se perdem; `deathSalvageRandomEquipment` (padrão 0, porque `death = {}`) manda equipados sorteados ao armazém, gastos.
- `named_loot_tables` vem vazio nas builds conferidas: tabelas `normal`, `boss` e `tutorial_walker_chest` são reconstruções. A caixa do tutorial do walker solta os 4 motores tier 1 (a placa 10 manda escolher o motor antes do chefe).
- O catálogo entregue foi gerado da build 190911 (1242 itens, `VersoesAntigas`); o servidor regrava com a 201219 (1593 itens).

## Falta

Confirmar no jogo a coleta (`21818` → `ClientInvAdd`, `4422`, `25386`); consumíveis `deco`/`dungeon`/`planexpboost`; recompensas de carreira do tipo item; loot com vários jogadores; `upgradeCaps`.
