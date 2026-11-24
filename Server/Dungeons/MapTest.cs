using Lidgren.Network;

// Map test mode: SteambirdsServer.exe --test-map "<path of a map .json>"
//
// Started by the launcher's map editor ("Play test") to try one map in the game, with nothing else around it:
//   - no Nexus visit: right after the login (and the welcome) the client is sent to the dungeon server with this map;
//   - only this PC (HTTP and UDP listen on localhost), ports of their own (below) so the normal server can run at the same time;
//   - the tutorial and the open world servers and the server search are not started;
//   - the accounts live in "maptest\accounts" (never in "accounts"): a new one gets a walker with complete tier 10 equipment,
//     the EULA and the privacy policy already accepted, and the tutorial counts as done, so there is nothing to set up; a plane that dies is replaced by a new one at the next arrival;
//   - dungeons.json is ignored: the only dungeon is this map, whatever its terrain (the "terrain" of the map file).
// The map file is read from the path given (not from the maps folder), and read again when it changes: save in the editor and
// type /restart in the game to see the new version.
public static class MapTest
{
    public const string Argument = "--test-map";
    public const string FolderName = "maptest";

    // Ports of the test server (the launcher uses the same numbers: Launcher\Core\MapTestPorts.cs).
    public static readonly NetworkPorts Ports = new NetworkPorts
    {
        Http = 6543, Nexus = 1511, Tutorial = 1512, Dungeon = 1513, World = 1514, Search = 6544
    };

    public static bool Active { get; private set; }
    public static string MapId { get; private set; } = "";
    public static DungeonCatalog.Entry Entry { get; private set; } = new DungeonCatalog.Entry();

    // Reads the arguments. False = the server must not go on (the reason was logged).
    public static bool Start(string[] args, Action<string, string> log)
    {
        int at = Array.FindIndex(args, a => string.Equals(a, Argument, StringComparison.OrdinalIgnoreCase));
        if (at < 0) return true;

        string path = at + 1 < args.Length ? args[at + 1] : "";
        if (path.Length == 0 || !File.Exists(path))
        {
            log("MAPTEST", "ERROR: " + Argument + " needs the path of a map file (got '" + path + "')");
            return false;
        }
        path = Path.GetFullPath(path);
        MapId = Path.GetFileNameWithoutExtension(path);

        try
        {
            Maps.UseFile(MapId, path);
            Maps.Map map = Maps.Get(MapId, log);
            if (string.IsNullOrWhiteSpace(map.Terrain))
            {
                log("MAPTEST", "ERROR: the map has no \"terrain\" (the client terrain of the dungeon, e.g. DungeonBomb)");
                return false;
            }
            Entry = new DungeonCatalog.Entry
            {
                Type = map.Terrain,
                Map = MapId,
                Name = map.Name,
                Enabled = true,
                InNexus = false,
                HealthMultiplier = 1f,
                Note = "map test"
            };
        }
        catch (Exception e)
        {
            log("MAPTEST", "ERROR: the map did not load: " + e.Message);
            return false;
        }

        Accounts.UseBaseFolder(Path.Combine(Directory.GetCurrentDirectory(), FolderName));
        DungeonCatalog.UseOnly(Entry);
        Active = true;
        log("MAPTEST", "map test: '" + Entry.Name + "' (" + path + "), terrain " + Entry.Type + "; accounts in " + Accounts.AccountsFolder +
            "; only this PC; " + Ports);
        return true;
    }

    // Before the welcome at the Nexus: the account has a plane and the tutorial counts as done, so the client asks for nothing.
    public static void PrepareAccount(NetServer server, NetConnection connection, Action<string, string> log)
    {
        if (Accounts.Planes().Count == 0)
        {
            string characterId = PlayerData.ToBase64Url(PlayerData.NewCharacterId());
            Accounts.AddPlane(characterId, "walker", "default");
            InventoryHandler.PlaneCreated(characterId, "walker");
            PilotProgression.PlaneCreated(server, connection, "nexus");
            log("MAPTEST", "new plane for " + PlayerSessions.NameOf(connection) + ": walker " + characterId);
        }
        Accounts.MarkDungeonFinished(Tutorial.DungeonName);
        AcceptLegalNotices(log);
        foreach (Accounts.SavedPlane plane in Accounts.Planes()) EquipTierTen(plane, log);
    }

    // What the game shows as texts to accept (the EULA and the privacy policy, "legal" of the configBinary): the test pilot has
    // already signed them, so nothing covers the screen the first time. Same record the client's own acceptance makes.
    private static readonly Dictionary<string, int> KnownTextHashes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["eula"] = -751434507,
        ["privacy_policy"] = 1877113321
    };

    private static void AcceptLegalNotices(Action<string, string> log)
    {
        var ids = new List<string>();
        foreach (ConfigNode notice in ConfigBinary.Get("legal").Items)
        {
            string id = notice["id"].AsString("");
            if (id.Length > 0) ids.Add(id);
        }
        if (ids.Count == 0) ids.AddRange(KnownTextHashes.Keys);

        Dictionary<string, LegalAcceptance> done = Accounts.Acceptances();
        foreach (string id in ids)
        {
            if (done.ContainsKey(id)) continue;
            Accounts.RecordAcceptance(id, new LegalAcceptance
            {
                Ticks = DateTime.UtcNow.Ticks,
                TextHash = KnownTextHashes.TryGetValue(id, out int hash) ? hash : 0,
                Locale = "en-US",
                BuildVersion = DungeonCatalog.GameVersion,
                BuildCommit = "9ffa6596f98f018b069d06ed359c5f8202a7a61d"
            });
            log("MAPTEST", "'" + id + "' accepted for " + Accounts.Name + " (the test account never asks for it)");
        }
    }

    // Complete tier 10 equipment: main weapon, secondary attack, engine and armor, whatever the class can use. Items already at
    // tier 10 stay; the rest is replaced (the hangar's starting items, or an older test set). The first engine whose id says "power"
    // wins when the class has several (walker: engine_destruction_10, engine_power_10, engine_repair_10).
    private const int EquipmentTier = 10;

    private static void EquipTierTen(Accounts.SavedPlane plane, Action<string, string> log)
    {
        PlaneClassInfo? info = ItemCatalog.GetClass(plane.Class);
        if (info == null) return;

        var chosen = new List<string>();
        foreach (int slot in ItemCatalog.EquipSlotsInWireOrder)
        {
            List<ItemDef> options = ItemCatalog.Bucket(slot, EquipmentTier)
                .Where(d => !d.Obsolete && info.AllowsTag(d.Tag)).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            ItemDef? pick = options.FirstOrDefault(d => d.Id.Contains("power", StringComparison.OrdinalIgnoreCase)) ?? options.FirstOrDefault();
            if (pick != null) chosen.Add(pick.Id);
        }

        int changed = InventoryStore.Change(d =>
        {
            InventoryStore.EnsurePlane(d, plane.CharacterId, plane.Class);
            StoredInventory inv = d.Planes[plane.CharacterId];
            int count = 0;
            foreach (string defId in chosen)
            {
                string slotName = ItemCatalog.Get(defId)!.Slot;
                if (inv.Equipped.TryGetValue(slotName, out StoredItem? current) && current.DefId == defId) continue;
                StoredItem item = InventoryStore.NewItem(defId);
                item.Used = true;
                inv.Equipped[slotName] = item;
                count++;
            }
            return count;
        });
        if (changed > 0) log("MAPTEST", plane.Class + " " + plane.CharacterId + ": tier " + EquipmentTier + " equipment (" + string.Join(", ", chosen) + ")");
    }
}
