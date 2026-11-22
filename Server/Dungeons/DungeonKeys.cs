using Lidgren.Network;

// Dungeon keys: consumables with the "dungeon" action (key_sneezy, key_bomb... "Mission Plans: Iron Lair").
// The game's text says "Activate to summon a drop ship": using the key takes the player to that dungeon.
//
// Here the key is checked before the item is spent (Check: it only works in the Nexus, for a dungeon this server offers
// and that is open in dungeons.json) and then Use sends the player to the dungeon server with the plane, like a carrier does.
// A key the server cannot use (closed dungeon, wrong server) is refused and stays in the inventory.
public static class DungeonKeys
{
    // null = the key can be used now; otherwise the client's error text key (shown when the use is refused).
    public static string? Check(string role, string dungeonType, out string why)
    {
        why = "";
        if (!string.Equals(role, "nexus", StringComparison.OrdinalIgnoreCase))
        {
            why = "keys only summon the drop ship in the Nexus";
            return "consumable_item_error_not_supported";
        }
        DungeonCatalog.Entry? entry = DungeonCatalog.Find(dungeonType);
        if (entry == null)
        {
            why = "this server does not offer " + dungeonType + " (dungeons.json)";
            return "consumable_item_error_no_effect";
        }
        if (!entry.IsOpen(DateTime.Now, out string reason))
        {
            why = entry.Label + " is closed: " + reason;
            return "consumable_item_error_no_effect";
        }
        if (DungeonServer.SwitchToDungeon == null)
        {
            why = "no link to the dungeon server";
            return "consumable_item_error_no_effect";
        }
        return null;
    }

    // The key was spent: the drop ship takes the player to the dungeon.
    public static string Use(NetServer server, NetConnection connection, string role, string dungeonType)
    {
        DungeonCatalog.Entry? entry = DungeonCatalog.Find(dungeonType);
        if (entry == null || DungeonServer.SwitchToDungeon == null) return "dungeon " + dungeonType + " not available";
        WorldObjects.SendChat(server, connection, "Drop ship summoned: going to " + entry.Label + "...");
        DungeonServer.SwitchToDungeon(server, connection, entry.Type, "[" + role + "] dungeon key of " + entry.Label);
        return "drop ship to " + entry.Label;
    }
}
