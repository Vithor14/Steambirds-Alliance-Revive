using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.UI;

// Names and descriptions of the known keys of the server config files, in English (translated through lang\*.json).
// In English the description below is shown; in another language its translation, or else the comment above the key
// in the file. Keys that are not listed show their own name and the
// comment of the file, so new server settings appear in the launcher without changing it.
internal static class SettingLabels
{
    public sealed record SettingInfo(string Title, string Description = "", string[]? Choices = null);

    private static readonly Dictionary<string, SettingInfo> Known = new Dictionary<string, SettingInfo>(StringComparer.OrdinalIgnoreCase)
    {
        // ---------------------------------------------------------------- progression.json
        ["planeXpMultiplier"] = new SettingInfo("Plane XP multiplier",
            "XP of each kill (1 = rebuilt formula: base health x xpTiers of the tier x xpMultiplier of the unit)."),
        ["pilotXpMultiplier"] = new SettingInfo("Pilot XP multiplier",
            "Multiplies the pilot XP the planes give (1 = original)."),
        ["capXpPerKill"] = new SettingInfo("Limit the XP per kill (original rule)",
            "Original rule: one kill gives at most half of the current level; at the top level, at most xpLimitForMaxLevel."),
        ["permadeath"] = new SettingInfo("Permadeath",
            "On = original: a destroyed plane leaves the account (the pilot keeps the XP the plane already gave in real time). Off = the plane goes back to the hangar and keeps counting for the pilot."),
        ["deathPilotXpBonus"] = new SettingInfo("Extra pilot XP when a plane goes down",
            "Pilot XP comes in real time: part of the value of each hangar plane (90% in this game version, 65% in older ones). The rest only came from retiring the plane, and this client has no button for it. How much of that rest the pilot gets when the plane goes down: 0 = original, 1 = all of it."),
        ["skillLoadoutSlots"] = new SettingInfo("Skill loadouts per pilot",
            "0 = the game's base value; the original sold more in the store."),
        ["hangarSlots"] = new SettingInfo("Hangar slots (planes per account)",
            "The original had 2 and sold more."),
        ["startingPilotLevel"] = new SettingInfo("Starting pilot rank", "Rank of a new pilot."),
        ["startingUnlocks"] = new SettingInfo("Starting unlocks",
            "Unlocks every new account gets (plane classes and other unlock items)."),
        ["unlockAllClasses"] = new SettingInfo("All classes unlocked", "Every plane class is unlocked from the start."),
        ["unlockClassesByLevel"] = new SettingInfo("Unlock classes by plane level",
            "Classes are unlocked by the level of the planes, following the game's \"unlocks\" rules."),
        ["postFightDetails"] = new SettingInfo("Show the damage done after bosses", "The \"you did X% of the damage\" text after bosses."),
        ["killSkillTrigger"] = new SettingInfo("\"On kill\" pilot skills", "OnKill pilot skills fire when the player kills an enemy."),
        ["randomPortrait"] = new SettingInfo("Random portrait for pilots without one",
            "Needs portraits_config.txt from plugin 1.20.0 or newer."),
        ["validateSkills"] = new SettingInfo("Check pilot skills on the server",
            "The server checks the points, requirements and top level of the pilot skills."),
        ["careerRewards"] = new SettingInfo("Career rewards",
            "Career rewards (Pilot School timeline). Example: { \"id\": \"career_reward_rank5\", \"pilotLevel\": 5, \"rewards\": [ { \"type\": \"PilotXp\", \"amount\": 10 } ] }. Types delivered today: PilotXp, PlaneXp and Unlock (id = unlock item)."),

        // ---------------------------------------------------------------- items.json
        ["lootEnabled"] = new SettingInfo("Enemies drop loot", "Enemies drop loot bags."),
        ["lootRolls"] = new SettingInfo("Loot rolls per kill", "How many times the loot of each kill is rolled (1 = original)."),
        ["normalLootTable"] = new SettingInfo("Loot table of common enemies",
            "Table used when the unit has no loot of its own (file in loot_tables, without .json)."),
        ["bossLootTable"] = new SettingInfo("Loot table of bosses and chests",
            "File in loot_tables (without .json) used by bosses and chests that have no loot of their own."),
        ["useGameUnitLoot"] = new SettingInfo("Use the loot written in the game's units",
            "Also rolls the loot written in the game's units (built-in tables, like the dungeon key some enemies drop). Named tables (lootTableId) only existed on the original server: a file in loot_tables with that id (or a pattern in \"replaces\") takes their place."),
        ["dungeonDifficulty"] = new SettingInfo("Dungeon loot difficulty",
            "Difficulty of the dungeons for \"..._normal\", \"..._difficult\" and \"..._lethal\" loot entries.",
            new[] { "normal", "difficult", "lethal" }),
        ["lootBagLifetime"] = new SettingInfo("Time a loot bag stays in the world (seconds)", "0 = the game's value, 30."),
        ["sharedCargoForAllPlanes"] = new SettingInfo("Cargo shared by all planes", "Original rule: one cargo pod for every plane of the account."),
        ["extraCargoSlots"] = new SettingInfo("Extra cargo slots", "Extra plane cargo slots for every account."),
        ["extraStorageSlots"] = new SettingInfo("Extra storage slots", "Extra account storage slots for every account."),
        ["keepCargoOnDeath"] = new SettingInfo("Keep the cargo when the plane goes down", "Original: a destroyed plane loses its cargo."),
        ["keepEquippedOnDeath"] = new SettingInfo("Keep the equipped items when the plane goes down",
            "Original: a destroyed plane loses its equipped items."),
        ["deathSalvageRandomEquipment"] = new SettingInfo("Equipped items saved when the plane goes down",
            "Equipped items that go to the storage when the plane goes down (DeathLootConfig.salvageRandomEquipment). The \"death\" config is empty in this game version, so the original here is 0."),
        ["degradeSalvagedItems"] = new SettingInfo("Saved items lose condition",
            "Items saved when the plane goes down lose condition (wear), and supercharged ones lose the bonus."),
        ["mulchOnTrash"] = new SettingInfo("Recycle items thrown in the trash",
            "\"Trash\" in the inventory recycles the item into the recycling currency (the game's \"mulching\" config)."),
        ["reforgeEnabled"] = new SettingInfo("Reforge and supercharge in the Workshop", "The game's \"reforging\" config."),
        ["startingCurrencies"] = new SettingInfo("Starting currencies", "Currencies of a new account."),
        ["startingIngredients"] = new SettingInfo("Starting ingredients",
            "Ingredients of a new account, for example { \"ingredient_copper\": 10 }."),
        ["startingStorageItems"] = new SettingInfo("Starting storage items",
            "Items a new account gets in the storage, for example [\"armor_2\", \"engine_speed_2\"] (ids in catalog\\item_catalog.md)."),
        ["exportItemCatalog"] = new SettingInfo("Export the item catalog",
            "Writes catalog\\item_catalog.md and .json when the server starts and the game version changed."),

        // ---------------------------------------------------------------- stores.json
        ["purchasesEnabled"] = new SettingInfo("Purchases enabled",
            "Off: the stores open, but every purchase is refused."),
        ["priceMode"] = new SettingInfo("How prices are charged",
            "currency = the account pays with its own gold and iron (like the original, without the Steam packages; give gold with /currency gold N, items.json startingCurrencies or the Accounts tab). free = every product costs 0 and the game shows \"100% off\".",
            new[] { "currency", "free" }),
        ["discountPercent"] = new SettingInfo("Discount on every price (%)",
            "Percent taken off every price, from 0 to 100. The game shows it as \"x% off\". A product can have its own value in the product rules."),
        ["showPreRelease"] = new SettingInfo("Sell pre-release products",
            "The game marks a few trails and emotes as pre-release and does not show them. On = they are sold too."),
        ["stores"] = new SettingInfo("Rules per store",
            "Keys: store_general, store_hangar, store_storage, store_skill_loadouts. Each store has \"enabled\" and \"sections\" (section id, for example general_account, general_keys, general_sales) with \"enabled\", \"include\" and \"exclude\": lists of product ids or \"tag:<tag>\". \"include\" adds products to what the game's rule puts in the section. general_sales is the sale shelf: empty until you add products to its \"include\". Example: { \"store_general\": { \"sections\": { \"general_keys\": { \"enabled\": false } } } }"),
        ["products"] = new SettingInfo("Rules per product",
            "By product id (for example hangar_slot, store_skill_loadout_slot, store_key_eye). \"enabled\": false hides it everywhere. \"discount\": percent off (0-100). \"stock\": units the whole server can sell (0 = no limit). \"maxPerAccount\": times each account can buy it (0 = the game's own limit). Example: { \"hangar_slot\": { \"discount\": 50, \"maxPerAccount\": 5 } }"),

        // ---------------------------------------------------------------- network.json
        ["serverName"] = new SettingInfo("Server name", "Name shown in the server lists of the launchers."),
        ["allowOtherPlayers"] = new SettingInfo("Players of other PCs can join",
            "On: players of other PCs (local network, Radmin, Hamachi, internet) can join. Off: only the game of this PC. Changes when the server restarts."),
        ["showInServerSearch"] = new SettingInfo("Show up in the server search",
            "Answers the \"find servers\" of the launchers on the local network and on VPNs. Off: players type the address in Connect by IP."),
        ["maxPlayers"] = new SettingInfo("Maximum players", "Players at the same time. Whoever comes when it is full gets \"server full\"."),
        ["chatCommands"] = new SettingInfo("Who can use chat commands",
            "Commands like /xp, /dungeon and /items: everyone, host (only the game of this PC) or none. /leave and /players always work.",
            new[] { "everyone", "host", "none" }),
        ["welcomeMessage"] = new SettingInfo("Welcome message", "Extra chat message for every player who arrives at the Nexus (empty = none)."),
        ["httpPort"] = new SettingInfo("Login port (TCP)",
            "Port of the login (the launcher opens the game with OZY_URL=http://<ip>:<port>/ozy). Players of other PCs type it after the IP when it is not 6443. Changes when the server restarts."),
        ["nexusPort"] = new SettingInfo("Nexus port (UDP)", "Changes when the server restarts."),
        ["tutorialPort"] = new SettingInfo("Tutorial port (UDP)", "Changes when the server restarts."),
        ["dungeonPort"] = new SettingInfo("Dungeon port (UDP)", "Changes when the server restarts."),
        ["searchPort"] = new SettingInfo("Server search port (UDP)",
            "Port that answers the server search of the launchers (they search on 6444 unless set otherwise in their Settings tab). Changes when the server restarts."),

        // ---------------------------------------------------------------- enemies.json
        ["chatWarnings"] = new SettingInfo("Wave and boss messages in the chat",
            "Server messages in the game chat when a wave starts or a boss shows up."),
        ["globalHealthMultiplier"] = new SettingInfo("Health multiplier of all enemies",
            "Multiplies the health of every enemy of the server (tutorial, Nexus and dungeons). 1 = the game's health; in the dungeons it is multiplied by the health multiplier of each dungeon (Maps and dungeons tab)."),
    };

    public static SettingInfo For(string key)
    {
        return Known.TryGetValue(key, out SettingInfo? info) ? info : new SettingInfo(key);
    }

    // Title in the launcher language (keys the launcher does not know show their own name).
    public static string Title(string key) => Known.TryGetValue(key, out SettingInfo? info) ? L.T(info.Title) : key;

    // Description in the launcher language (see the comment at the top).
    public static string Description(string key, string fileComment)
    {
        SettingInfo info = For(key);
        if (L.IsEnglish) return info.Description.Length > 0 ? info.Description : fileComment;
        if (L.Has(info.Description)) return L.T(info.Description);
        return fileComment.Length > 0 ? fileComment : info.Description;
    }
}
