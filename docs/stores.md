# Stores, purchases and slots

Written 22/11/2026 (client build 201219.1413). Code in `Server\Stores\`. Settings in `stores.json`.
This replaces the old "stores open empty, purchase answers an error" state. The server side was exercised with a test client that speaks the same messages (see "Tests"). In the game on 23/11, paint and the paladin EpicA variant were bought; the Variant menu did not send `ServerRequestChangePlaneVariant` `15910`, so the plane stayed on `default`. The other shelves are still unplayed.

## What the client already has, and what the server had to rebuild

The `store` file of the `configBinary` (`SBConfig.storeConfig`, class `StoreConfig`) is complete in every build we
have (190426, 190609, 190709, 190828, 190911 and 201219):

| In the client (no work) | Rebuilt on the server |
|---|---|
| 4 stores: `store_general`, `store_hangar`, `store_storage`, `store_skill_loadouts` | which products each section shows to the player (`StoreShelves.cs`; the original `StoreAlgorithms.UpdateStores` runs on the original server only) |
| their sections (type NORMAL / BANNER / SPECIAL, `product_selection`, `max_product_count`) | the purchase: `GiveProductServer` is an empty method in the client (`StorePurchase.cs`) |
| 294 products, with price tags, limits (`max_per_account`) and what they give (`items`, `unlocks`, `account_items`) | what each account bought, and the slot counts (`StoreAccounts.cs`) |
| currencies `gold`, `iron`, `newscrap` (and the hidden `keys`) | server stock and the host's rules (`StoreStock.cs`, `StoreSettings.cs`) |
| texts: product names, purchase errors | rotating specials: no build has any `fillers`, so `general_sales` is empty unless the host fills it |
| 5 gold packages `dummy_*` (`currency_packages`) | the Steam payment: **not rebuilt on purpose** |

Builds before 201219 use the currency `scrap` instead of `iron` and `newscrap`; the products are the same.

## Messages

All ids are in `MessageCatalog.cs`. `Game.cs` / `Game.Rpc.cs` hand them to `Stores\StoreHandler.cs`.

| id | name | direction | what the server does |
|---|---|---|---|
| 54295 | `ServerEnterStore(string storeId)` | client -> server | answers `ClientReceiveStoreContent` of that store |
| 54290 | `ClientReceiveStoreContent(string storeId, List<StoreShelve> shelves)` | server -> client | the shelves; the client throws away what it had of that store and uses these |
| 54296 | `ServerPurchaseInStore(storeId, sectionId, productId, priceId, postPurchaseAction)` RPC `string` | client -> server | the purchase |
| 54390 | `ClientNotificationGiveProduct(GeneralID accountId, string planeId, storeId, sectionId, productId)` | server -> client | after a product with account items or unlocks, so the client counts them |
| 48914 | `ClientUpdatePlayerDataSlots(int hangar, int additionalStorage, int additionalCargo, int loadouts)` | server -> client | after the notification above: the real slot counts |
| 54297 | `ServerGetBuyCurrencyPackages(string payment)` RPC list | client -> server | empty list (no Steam packages) |
| 54298 | `ServerRequestBuyCurrencyPackage(string id, string extra)` RPC `string` | client -> server | failure `purchase_error_payment_failed` and a chat note |

The client opens a store by sending 54295 when the player docks at the General Store, opens the hangar plane list
(`store_hangar`), the Pilot School loadouts (`store_skill_loadouts`) or the Storage window (`store_storage`).

### Bytes of `ClientReceiveStoreContent` (54290)

Lidgren `NetBuffer`, in this order (`StoreWire.WriteStoreContent`; client: `StoreShelveSerializer`,
`StoreShelveItemSerializer`, `ListStoreShelveCopperSerializer`):

```
ushort  54290
string  storeId
varuint shelves.Count + 1            (0 would be a null list: the client does a foreach on it)
  per shelf (StoreShelve):
    string  store_id
    string  section_id               must exist in the store config: an unknown section is a NullReferenceException in the client
    string  active_filler            "" (no filler)
    int32   filler_end_timestamp     0 (no timer)
    int32   items.Count
    per item (StoreShelveItem):
      int32   amount                 stock left (only meaningful when has_limited_amount)
      int32   expires_timestamp      0 = never
      int32   sort_order             position in the config file, the window sorts by it
      bool    has_limited_amount     ONE BIT, not a byte
      string  product                starts in the middle of a byte after the bool; write it with NetBuffer.Write(bool) like the client
      int32   discount               percent off, 0-100
```

Details that matter:

- The bool is a single bit. The next string is not byte-aligned. Writing the whole buffer with the same Lidgren calls
  as the client keeps the alignment (same rule as the unit options in `CLAUDE.md`).
- The shelves of a store are replaced as a whole, so the server sends the store again after every purchase (stock and
  limits change).
- The window shows the price itself, from `product.normal_price` / `additional_price_tags` and `discount`. The server
  cannot show a different price; the only legitimate knob is `discount`. The server charges with the same integer
  formula (`StorePrice.AmountWith`): `rest = amount - amount_subs[owned]` (when `owned < amount_subs.Length`), then
  `rest - rest * discount / 100`.
- Escalating prices count `owned` as the account items named by `amount_sub_item_tag` (`hangar_slot`...). The client
  reads them from `PlayerData.accumulatedItems`, so the welcome message now sends them (before, the dictionary was empty).

### Purchase (`ServerPurchaseInStore`, 54296)

Request after the message id: `string storeId, string sectionId, string productId, string priceId, string postPurchaseAction`,
then the `ushort rpcId`. `priceId` is the id of the price the player chose (`normal` is the gold one, `iron` the second
price of dyes, emotes, variants and consumables). `postPurchaseAction` is `""` for products without choices.

Answer (message 52428): `ushort rpcId`, `bool ok` (one bit), then the `string`:

- success: `ok = true`, string = product id. The client prints "You purchased: X" itself.
- failure: `ok = false`, string = a key of the game's store texts. The client prints `TextMachine.GetStore(key)` in red:

| key | when |
|---|---|
| `purchase_error_no_currency` | the account lacks the currency (**the client then opens its "Buy currency" page, which is empty here**) |
| `purchase_error_limit_exceeded` | game limit `max_per_account`, the host's `maxPerAccount`, or the top of the hangar (20) / skill loadouts (10) |
| `purchase_error_no_inventory_space` | not enough free slots in the plane cargo plus the storage for the items of the product |
| `purchase_error_unknown` | not on the shelf, no such product, purchases off, store off, sold out |
| `purchase_error_payment_failed` | a currency package was requested |

After a refusal the server also writes one chat line with the exact reason ("needs 750 gold, the account has 0").

What the server checks, in order (`StorePurchase.Buy`): purchases on, config loaded, product exists, store on, product
not blocked for this account (sold out, host limit, top of the hangar / loadouts), product on that shelf right now, the
game's `max_per_account`, price id exists, then, under the inventory lock, the currency and the free slots. Only then
anything changes: currency taken, items placed, purchase and stock recorded, unlocks added, messages sent.

Order of the messages after a success, so the window ends in a consistent state: currencies (`ClientUpdatePlayerCurrencies`),
`ClientInvAdd` per item, unlocks, `ClientNotificationGiveProduct`, `ClientUpdatePlayerDataSlots`, `ClientReceiveStoreContent`
of the store, and last the RPC answer.

Why 54390 and then 48914: when the client gets 54390 it adds temporary account items to its own `PlayerData` and
recomputes its slots from ITS base values (hangar 2, loadouts 1). That also makes the next price right at once, because
the temporary items count for the escalating price. 48914 then puts the real numbers (host base + bought).

### What a product gives

| Field of the product | Effect on the server |
|---|---|
| `items` | the inventory items go to the plane cargo, then the storage (or storage first for the actions `storage` and `use_and_storage`) |
| `unlocks` | added to the pilot unlocks (emotes, plane classes), like the unlock items |
| `account_items` | counters of the account in `accounts\<key>\store.json`: `hangar_slot`, `storage_slot`, `inventory_slot`, `skill_loadout_slot` and the markers `store_storage_slot`, `inventory_slot_buy`, `store_bundle_starter` |

`postPurchaseAction` (products with items): `nothing`, `storage`, `use_and_inventory`, `use_and_storage`, `use_instantly`.
`use_instantly` is done by the server when it can deliver the effect (unlocks and plane variants: the 163 variant
products, via `ItemActions`); otherwise the item goes to the inventory and the log says so. Dyes and trails (`deco` actions)
run only on the client: with `use_and_*` the server delivers the item and then sends `ClientConsumedItemOnPickup`, which makes the
client perform the item's actions (repaint the plane). The dye is unlimited, so it stays in the inventory or the storage.
The paint is not saved by the server (the plane decoration is still sent empty), so it lasts until the plane is lost or the
player leaves, as with a dye used from the inventory.

## Slots

The slots of an account are the host's base value plus what the account bought:

| Slot | Base (host) | Bought | Where it is used |
|---|---|---|---|
| hangar (planes) | `progression.json` `hangarSlots` (3) | `hangar_slot` | `PlayerData.HangarSlots` (welcome, `Accounts.AddPlane`) |
| skill loadouts | `progression.json` `skillLoadoutSlots` (3) | `skill_loadout_slot` | `PilotProgression.SkillLoadoutSlots()` |
| storage | game base + `items.json` `extraStorageSlots` | `storage_slot` (6 per purchase) | `InventoryStore.StorageCapacity` |
| plane cargo | game base + `items.json` `extraCargoSlots` | `inventory_slot` (4 per purchase) | `InventoryStore.CargoCapacity` |

The original had 2 hangar slots and 1 loadout as the base (`global.baseNumHangarSlots`, `baseSkillLoadoutSlots`) and
sold the rest. The host's base replaces them; the store still sells more (and the price of the first purchase is
the first step of the escalating price, whatever the base is).

Hard caps: 20 planes and 10 loadouts in total (the clamps the server already had). Reaching them takes the product off the shelf.

## Currency rule (no Steam packages)

Gold used to come from Steam microtransactions (packages `dummy_*`, messages 54254 and 54297-54300), iron from pilot XP
(`global.pilotXpPer1Currency` = 10, id `iron`; **not implemented on the server yet**) and `newscrap` from recycling items.
The packages are not coming back. The rule is the simplest one, and configurable in `stores.json`:

- `"priceMode": "currency"` (default): the account pays with its own `gold` / `iron`. The host gives gold with `/currency gold N`,
  `startingCurrencies` in `items.json`, or the account editor of the launcher (one field per currency).
- `"priceMode": "free"`: every product costs 0 (the server sends `discount` 100, the window shows "100% off").
- `"discountPercent"` and the per-product `"discount"`: any value from 0 to 100.

## `stores.json`

Created with comments the first time; the launcher builds its "Stores" page from it (one field per top-level key, the
comment above is the explanation). The server reloads it by itself.

| Key | Meaning |
|---|---|
| `purchasesEnabled` | `false`: stores open, every purchase refused |
| `priceMode` | `currency` or `free` |
| `discountPercent` | percent off every price |
| `showPreRelease` | also sell the products the game marks `preRelease` (some trails and emotes) |
| `stores` | per store: `enabled`, `sections` -> per section: `enabled`, `include`, `exclude` (product ids or `tag:<tag>`) |
| `products` | per product: `enabled`, `discount`, `stock`, `maxPerAccount`, `items` (replaces what the product gives; the window keeps the original name, icon and price) |

Notes:

- `include` adds to what the game's rule already puts in the section. `general_sales` (type SPECIAL, filled by fillers on the original
  server) is empty until the host adds products to its `include`; pair it with a `discount` in `products`. Do not add
  products without a `banner` image to a BANNER section (`general_banner`).
- `stock` is for the whole server; the sold units are in `data\store_stock.json` (`/store restock` refills).
- `maxPerAccount` counts purchases of that product (`accounts\<key>\store.json`, `Purchases`).
- A section keeps the game's `max_product_count`. When there are more products than that, the server shows the first ones by
  `sort_order` (the original picked at random and changed the pick over time).

## Files

| File | Role |
|---|---|
| `Stores\StoreCatalog.cs` | the `store` config of the configBinary: currencies, products (with the automatic tags of `StoreManager.ExtendProductWithDefaults`), stores, sections, price formula |
| `Stores\StoreSettings.cs` | `stores.json` and its default text |
| `Stores\StoreShelves.cs` | the shelves of a store for the current account |
| `Stores\StoreRules.cs` | limits (game and host) and the price of a product |
| `Stores\StorePurchase.cs` | the purchase and the messages after it |
| `Stores\StoreAccounts.cs` | `accounts\<key>\store.json`: account items, purchases, history |
| `Stores\StoreStock.cs` | server stock (`data\store_stock.json`) |
| `Stores\StoreWire.cs` | the bytes |
| `Stores\StoreHandler.cs` | entry points (open store, RPCs) and the `/store` chat commands |

Changed in existing files: `Game.Rpc.cs` (store open and RPC hand-over), `Game.Shots.cs` (command), `Program.cs` (start),
`PlayerData.cs` (accumulatedItems, slot counts), `Progression\PilotProgression.cs` (loadouts), `Items\InventoryStore.cs` (capacities),
launcher `ServerTab.cs`, `SettingLabels.cs` and `lang\*.json`.

## Chat commands (`/store`, `/loja`, `/shop`)

`/store info` (state, slots, currencies), `/store list [store]`, `/store buy PRODUCT [PRICE]` (first shelf that has it,
price `normal` or `iron`), `/store reset` (forgets this account's purchases), `/store restock` (refills the server stock).
Gold for tests: `/currency gold 20000`. They follow `network.json` `chatCommands`.

## Launcher

- **Server > Store** (`UI\StorePage.cs`, `StoreProductsView.cs`, `StoreShelvesView.cs`, model in `Core\StoreRules.cs`): edits every key of
  `stores.json`. Products, prices and sections are read from the game's `configBinary` (`Core\GameData\GameConfig.Store.cs`).
  Saving replaces only the six top-level values and keeps the comments (`JsoncFile.ApplyChanges`).
  - *General*: purchases on/off, price mode, discount on every price, pre-release products.
  - *Prices and products*: one row per product: sold, discount %, stock, max per account, and read-only game price and
    the price the player pays now. Filter by text or category and apply discount, stock, limit or sold/not sold to every row shown.
  - *Shelves*: stores open/closed; sections on/off with include and exclude (product picker).
  - The price can only go down: the game window draws prices from its own config, so the discount is the only dial.
- **Server > Accounts** (`UI\MoneyPanels.cs`): *Starting money of new accounts* writes `startingCurrencies` of `items.json`
  (one field per currency of the game); *Money of the selected account* writes `Currencies` of its `inventory.json`
  (server stopped; a copy goes to `server\_backup`).

## Not done

- Iron from pilot XP (the original "currency for pilot XP", message 54391).
- Rotating sales and timers (`fillers`, `filler_end_timestamp`): no build has any filler; the host fills `general_sales` by hand.
- The "Buy currency" page of the client stays in the window, empty.
- A dye applied by a purchase is not persisted by the server (no decoration saved per plane).
- `min_pilot_level` of sections is only a client-side hide; the server does not refuse a purchase below it.

## Tests

Server side, done with a test client against a running server (not part of the repository): open the 4 stores,
refuse without gold, buy hangar slots (750 then 1750 gold), a loadout (500), storage (750), the beginner bundle (7500,
second time refused), a key, `maxPerAccount`, `stock`, disabled product, disabled section, sale shelf, discount (10% and 50%),
`use_instantly` of a plane variant, dye to storage, free mode, purchases off, `/store` commands. The slot counts and
the gold left matched the formulas above. The checklist in `CHECKLIST.md` has the in-game tests.
