# Itens, inventário e loot (v9, 15/11/2026; revisado em 23/11/2026)

Situação em 23/11: o inventário, o loot, a oficina, a reciclagem e os consumíveis (variantes, tintas, chaves, bônus de XP) estão no servidor; o que foi testado no jogo e o que falta está em `CHECKLIST.md`, seção 5. Primeiro teste no jogo (15/11, 15:42): o sorteio funciona (`[LOOT] ... soltou N saco(s)`) e o cliente recebe os sacos (`spawn_log`: prefab 8, shortId 5000000), mas nenhum aparecia nem era coletado. Teste em dois PCs (16/11, host sem o jogo): nenhum loot. Causas e correções na seção "Correções". Código em `Server\Items`, tabelas em `loot_tables\` (doc: `loot_tables\README.md`), opções do host em `items.json`, dados da conta em `accounts\<chave>\inventory.json`, catálogo em `catalog\item_catalog.md`/`.json` (gravado pelo servidor; a pasta `catalog\` fica fora do git).

## Arquivos

- `ItemCatalog.cs`: lê do configBinary `Items/**`, `ingredients` (gera os `<id>_ingpickup`), `playerclasses` (tags permitidas, itens iniciais, variantes), `mulching`, `reforging`, `global` (vagas, `lootbagLifetime`, tabela de desgaste) e o `loot`/`lootGlobal` de `Units/**`. Baldes por slot e tier como `SBConfig.GetBucketedItemConfigs` (sem obsoletos e pré-lançamento). **Sem configBinary não há catálogo: loot, reforja e reciclagem ficam desligados.**
- `InventoryModel.cs` / `InventoryStore.cs`: inventário autoritativo (equipados, arma alternativa, cargo, `Permanents_`, `_accumulatedItems`, armazém com characterId próprio, moedas, ingredientes), salvo em `inventory.json`. Itens iniciais com o mesmo id de antes (SHA-256 de `item|characterId|slot`).
- `InventoryWire.cs`: bytes de `InventoryItem`, `InventoryPosition` (byte 0x80|loc<<5|slot + índice), `StandardInventory` e das mensagens de inventário e respostas de RPC (52428; -1 = recusado).
- `InventoryHandler*.cs` (`.cs`, `.Moves`, `.Use`, `.Commands`, `.Send`, `.Retire`): RPCs `17407` troca, `60901` remover (soltar = saco no mundo; lixo = reciclagem), `60902` consumir, `60903` ordenar (estável + `ClientReplaceInventory`), `60904` acumular melhoria, `60905` favoritar; `21818` coleta, `4422`, `61999`, `12157` (Oficina) e `25386` só para sacos. Ganchos: `PrepareForWelcome` (itens vencidos), `PlayerIdentified` (carteira de ingredientes), `PlaneCreated`, `PlaneChanged`, `PlaneDestroyed`. Comandos `/items`, `/item`, `/loot`, `/inventory`, `/clear_cargo`, `/currency`, `/ingredient`, `/bags`, `/clear_bags`, `/export_items` (`Chat commands.md`).
- `ItemActions.cs`: o cliente executa as ações do item no próprio avião antes de pedir o consumo; o servidor entrega desbloqueio, variante, moeda, buff de conta e ingrediente. `deco`, `dungeon` e `planexpboost` passaram a ser tratados em 22/11 (ver "Consumíveis novos"); se não puderem ser usados, o servidor recusa e o item fica.
- `LootTables.cs`: tabelas em arquivo no formato `LootTableDef` do jogo; sorteio igual a `SteambirdsLootTable.DrawLoot` (tier relativo ao inimigo, `usableChance`, `ensureUsableSlots`, condições). Ordem: `units` → loot do jogo da unidade (embutido + `lootTableId` pelo `replaces` mais específico) → `normal`/`boss`.
- `LootDrops.cs`: sacos (prefab 8 `lootbag`, um item por saco, shortIds 5.000.000+), um por jogador; `KillUnitsBatch` `LootBagDestroyed` na coleta e `Fizzled` no fim do tempo. Chamado em `Enemies` na morte (time da IA) e na abertura de baús; vale no tutorial e nas dungeons.
- `Workshop.cs`: reforja (condição < 0 → 0) e supercarga (tier 9+, condição 1 e stats de `SuperchargeNewStatProbabilities`), custos pelas chaves de `InventoryItem.GenerateItemConditionLookupKeys`.
- `ItemCatalogExport.cs`: grava o catálogo quando a build muda.

## Correções

- 15/11 (v9.1, `LootDrops.cs`): as opções do saco (`LootBagScript.GenerateEntOptions`) têm dois bools de 1 bit, então o buffer não fecha em byte inteiro. `m.Write(NetBuffer)` do Lidgren descarta os bits sobrando do último byte, mas o cliente (`NetBufferCopperSerializer.Read`) sempre lê `LengthBytes` bytes inteiros: o `FieldUpdate` depois das opções era lido 6 bits deslocado e o saco nascia numa posição lixo. Agora grava `m.Write(options.Data, 0, options.LengthBytes)`, igual ao `NetBufferCopperSerializer.Write` original. **Regra geral:** para embutir unitOptions, nunca usar `Write(NetBuffer)` quando o buffer pode ter bits soltos (bools). As outras opções do esboço (avião, portal, prédios, nuvens) fecham em byte e não são afetadas.
- 16/11 (`Build launcher.ps1`, função `CopyGameDataToServer`): o servidor lê o configBinary direto do jogo instalado (`GameDataFiles`) e o pacote `dist` não levava o arquivo. Num host sem o jogo ficava sem catálogo de itens (nenhum `[LOOT]`), com tabelas de XP embutidas (190828), skills sem validação, só o walker desbloqueado e modelo de voo "?". O script copia `configBinary` e `portraits_config.txt` do jogo para `dist\server\data` (o `dist` está no `.gitignore`). **Para o GitHub** (20/11 em diante): o zip do launcher **não** leva nenhum arquivo do jogo; quem recebe o zip roda o `SteambirdsGameFiles.exe`, que copia o `configBinary` e os dados do plugin da cópia do jogo de cada jogador para `server\data` (`GameFiles\README.md`).

## Decisões

- Cargo compartilhado entre os aviões (`isUsingSharedCargoForAll = true` no global; dica de lore "one cargo pod for all your planes"). Opção `sharedCargoForAllPlanes`.
- Morte permanente: cargo e equipados se perdem; `deathSalvageRandomEquipment` (padrão 0, porque `death = {}`) manda equipados sorteados ao armazém, gastos.
- `named_loot_tables` vem vazio nas builds conferidas: tabelas `normal`, `boss` e `tutorial_walker_chest` são reconstruções. A caixa do tutorial do walker solta os 4 motores tier 1 (a placa 10 manda escolher o motor antes do chefe).
- O catálogo vem da build 201219 (1593 itens): o servidor o regrava em `catalog\` quando a build muda. (O catálogo antigo, da 190911, tinha 1242 itens.)

## Falta

Confirmar no jogo a coleta (`21818` → `ClientInvAdd`, `4422`, `25386`) (a coleta e o loot funcionaram no tutorial e na dungeon com dois PCs em 22/11); consumíveis `deco`/`dungeon`/`planexpboost`; recompensas de carreira do tipo item; `upgradeCaps`.

## Consumíveis novos e recompensas de item (22/11/2026, sem teste no jogo)

- **Pinturas e rastros (`deco`):** o cliente repinta o avião na hora (`DecorationAction.Perform`) e só depois pede o `ServerInvConsumeItem`; o servidor guarda o resultado no avião da conta (`SavedPlane.Decoration`, `Planes\PlaneDecoration.cs`) e o escreve no `PlaneRecord`, nas opções do spawn e nas do relay. Cores base = config `unitColors` + decoração da variante (campos `extraColor1..3`, `livery`, `normalTrail` direto no nó de `playerclasses` `variants[]`); `cleanColors`/`cleanLivery`/`cleanTrails` voltam ao da variante. Id de rede do rastro = hash Mono do id (como `TemplateHash`). Trocar de variante apaga a pintura salva (o cliente faz o mesmo).
- **Chaves de dungeon (`dungeon`, `key_*`):** "summon a drop ship". `DungeonKeys.Check` só deixa usar no Nexus, para uma dungeon que o `dungeons.json` oferece e está aberta; senão o RPC falha e o item fica. Ao gastar, `DungeonKeys.Use` manda o jogador para a dungeon com o avião, como um carrier.
- **Bônus de XP (`planexpboost`):** ver `Progression\PlaneXpBoost.cs` (`PilotSave.PlaneXpBoostPercent/Until`). O XP de abate sobe o % antes do limite por abate.
- **Recompensas de carreira do tipo Item:** `Items\ItemDelivery.Give` põe no cargo do avião vivo ou no armazém; o que não couber fica em `PilotSave.PendingRewardItems` e é tentado de novo no login, ao subir de patente e no pedido de entrega.
- Na loja, "usar na hora" de tinta e chave deixa o item no inventário (precisam do jogador); o "usar e guardar" de tinta manda o `ClientConsumedItemOnPickup` e o servidor também guarda a pintura.
