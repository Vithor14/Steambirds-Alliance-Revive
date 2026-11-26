using System.Text.Json;
using Lidgren.Network;

// Open world: things the host asks for from the launcher's world map (right click on the map, "Spawn dungeon" button).
//
// The launcher writes one small file per request in the folder world-requests\ of the server; the world server reads it, does it and
// deletes it, and writes the answer in world-request-result.json (the launcher shows it). Files, not a connection, so it works with
// the server started by the launcher or by hand, and with no player online the answer says why nothing happened.
//
//   { "id": "...", "action": "boss",    "x": 120.5, "y": -300 }     the boss of the region under that point, awake, at that point
//   { "id": "...", "action": "boss",    "x": 120.5, "y": -300, "boss": "gargantua" }   ... that boss of the region instead
//                                                                        (a boss comes with its parts and minions)
//   { "id": "...", "action": "carrier", "dungeon": "DungeonBomb" }   a dungeon carrier at a random free place
//   { "id": "...", "action": "carrier", "dungeon": "DungeonBomb", "x": 120.5, "y": -300 }   ... next to that point
//
// Needs a player in the world: the server creates enemies and carriers for the connections that are in it (a world built in
// advance has none yet).
public static partial class WorldServer
{
    public const string RequestFolder = "world-requests";
    public const string ResultFileName = "world-request-result.json";

    private const int MaxRequestsPerCheck = 8;

    private static double nextRequestCheck;

    private sealed class Request
    {
        public string Id { get; set; } = "";
        public string Action { get; set; } = "";
        public float? X { get; set; }
        public float? Y { get; set; }
        public string Dungeon { get; set; } = "";
        public string Boss { get; set; } = "";
    }

    private static readonly JsonSerializerOptions RequestOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static string RequestPath => Path.Combine(Directory.GetCurrentDirectory(), RequestFolder);

    // Called by Update on the thread of the world server (inside Game.Lock), about twice per second.
    private static void ProcessRequests(NetServer server, double now, Action<string, string> log)
    {
        if (now < nextRequestCheck) return;
        nextRequestCheck = now + 0.5;

        string folder = RequestPath;
        string[] files;
        try
        {
            if (!Directory.Exists(folder)) return;
            files = Directory.GetFiles(folder, "*.json");
        }
        catch (Exception)
        {
            return;
        }
        if (files.Length == 0) return;

        foreach (string file in files.OrderBy(f => f, StringComparer.Ordinal).Take(MaxRequestsPerCheck))
        {
            Request? request = null;
            try
            {
                request = JsonSerializer.Deserialize<Request>(File.ReadAllText(file), RequestOptions);
            }
            catch (IOException)
            {
                continue;   // the launcher is still writing it: next time
            }
            catch (Exception e)
            {
                log("WORLD", "[world] request " + Path.GetFileName(file) + " could not be read: " + e.Message);
            }
            try
            {
                File.Delete(file);
            }
            catch (Exception)
            {
                // Left there: it is answered again next time at worst.
            }
            if (request == null) continue;

            string message;
            bool ok;
            try
            {
                ok = Run(server, request, now, log, out message);
            }
            catch (Exception e)
            {
                ok = false;
                message = "the request failed: " + e.GetType().Name + ": " + e.Message;
            }
            log("WORLD", "[world] request " + request.Action + " (" + request.Id + "): " + (ok ? "done" : "refused") + ": " + message);
            WriteResult(request.Id, ok, message, log);
        }
    }

    private static bool Run(NetServer server, Request request, double now, Action<string, string> log, out string message)
    {
        Session? s = session;
        NetConnection? handle = s == null ? null : Connected(s).FirstOrDefault();
        if (s == null || s.Map == null || s.Pregenerated || s.Server != server || handle == null)
        {
            message = "Nobody is in the open world. Enemies and carriers are created for the players who are in it: go in first.";
            return false;
        }

        switch ((request.Action ?? "").Trim().ToLowerInvariant())
        {
            case "boss":
                return SpawnBoss(s, handle, request, now, log, out message);
            case "carrier":
                return SpawnCarrier(s, request, now, log, out message);
            default:
                message = "unknown request '" + request.Action + "'";
                return false;
        }
    }

    // ------------------------------------------------------------------ boss

    private static bool SpawnBoss(Session s, NetConnection handle, Request request, double now, Action<string, string> log, out string message)
    {
        if (!request.X.HasValue || !request.Y.HasValue)
        {
            message = "the request has no position";
            return false;
        }
        WorldMap map = s.Map!;
        if (!FindOpenNear(map, request.X.Value, request.Y.Value, 6f, 36f, out float x, out float y))
        {
            message = "No free land near that point (water, a canyon or walls). Pick another place.";
            return false;
        }
        WorldData.Region? region = map.RegionOfPoint(x, y);
        if (region == null)
        {
            message = "That point is not over land.";
            return false;
        }
        string? template = string.IsNullOrWhiteSpace(request.Boss) ? WorldEnemyPools.BossFor(region) : request.Boss.Trim();
        Enemies.UnitSummary? unit = template == null ? null : Enemies.FindUnit(template);
        if (template == null || unit == null || unit.Wall || unit.Player)
        {
            message = template == null
                ? "The game has no boss for " + region.ZoneName + ". Pick one in the launcher (Enemies tab, Boss of the biome)."
                : "The unit '" + template + "' is not in the game's catalog.";
            return false;
        }

        // The boss and everything that comes with it (parts and minions of its group of the game), around it.
        WorldEnemyPools.BossGroup bossGroup = WorldEnemyPools.BossGroupOf(region, unit.Name);
        WorldSettings.EnemySettings settings = WorldSettingsFile.Current.Enemies;
        string group = "boss:" + request.Id;
        float rotation = (float)(random.NextDouble() * 360.0 - 180.0);
        int id = Enemies.CreateFromScript(s.Server, handle, Role, EnsureBossType(bossGroup.Name, settings, true, region.Name), x, y, rotation, group, null, null, now, log);
        if (id == 0)
        {
            message = "The server could not create " + bossGroup.Name + " (enemy limit or a unit that is not in the catalog).";
            return false;
        }
        int created = Enemies.SpawnFormationMembers(handle, id, now, log);
        if (created == 0)
        {
        var attached = new List<int>();
        double offset = random.NextDouble() * Math.PI * 2.0;
        // Parts the boss's AI summons itself (the Ambassador's cats and diplomats) are not created up front.
        HashSet<string> summonedByAi = Enemies.TemplatesSummonedBy(BossAi(region.Name, bossGroup.Name, true));
        for (int i = 0; i < bossGroup.Parts.Count; i++)
        {
            if (summonedByAi.Contains(bossGroup.Parts[i])) continue;
            // Spread on a ring around the boss (a second ring when there are many), on free land when there is some.
            // A boss with a formation file is created above, not here.
            double angle = offset + i * Math.PI * 2.0 / Math.Max(1, Math.Min(bossGroup.Parts.Count, 12));
            float distance = 9f + 6f * (i / 12);
            float px = x + (float)Math.Sin(angle) * distance;
            float py = y + (float)Math.Cos(angle) * distance;
            if (!map.IsFreeLand(px, py))
            {
                px = x;
                py = y;
            }
            float partRotation = (float)(random.NextDouble() * 360.0 - 180.0);
            int partId = Enemies.CreateFromScript(s.Server, handle, Role, EnsureBossType(bossGroup.Parts[i], settings, false, region.Name), px, py, partRotation, group, null, null, now, log);
            if (partId == 0) continue;
            created++;
            attached.Add(partId);
        }
        }

        message = bossGroup.Name + (created > 0 ? " with " + created + " part(s)/minion(s)" : "") + " appeared in " + region.ZoneName + " at " +
                  x.ToString("0") + ", " + y.ToString("0") + ".";
        Chat(s, "A boss appeared in " + region.ZoneName + ": " + bossGroup.Name + ". " + Direction(x, y, Players(s)));
        return true;
    }

    // The game's unit as a boss ("world/boss/<unit>") or as a part or minion of one ("world/part/<unit>") of the open world: the default
    // AI (chase), free to roam. A type with the same key in enemies.json replaces it, so the host or the Enemy AI tab can give it its
    // own AI.
    private static string EnsureBossType(string template, WorldSettings.EnemySettings settings, bool boss, string regionName)
    {
        string ai = BossAi(regionName, template, boss);
        string key = (boss ? "world/boss/" : "world/part/") + template + (ai.Equals(WorldEnemyLists.DefaultAi, StringComparison.OrdinalIgnoreCase) ? "" : "~" + ai);
        Enemies.RegisterDefaultType(key, new Enemies.EnemyType
        {
            Name = template,
            Template = template,
            Ai = ai,
            FreeRoam = true,
            Boss = boss,
            DeathUnit = boss && Enemies.FindUnit(template + "DEATH") != null ? template + "DEATH" : "",
            HealthMultiplier = settings.HealthMultiplier
        });
        return key;
    }

    // Host's AI, else the formation bound to the unit, else an AI in ai\brains\world made for it (the Ambassador), else chase.
    private static string BossAi(string regionName, string template, bool boss)
    {
        string? ai = WorldEnemyLists.HostAi(regionName, template);
        if (!string.IsNullOrWhiteSpace(ai)) return ai;
        if (boss)
        {
            ai = Formations.DefaultAi(template) ?? Enemies.WorldAiMadeFor(template);
            if (!string.IsNullOrWhiteSpace(ai)) return ai;
        }
        return WorldEnemyLists.DefaultAi;
    }

    // ------------------------------------------------------------------ dungeon carrier

    private static bool SpawnCarrier(Session s, Request request, double now, Action<string, string> log, out string message)
    {
        DungeonCatalog.Entry? entry = string.IsNullOrWhiteSpace(request.Dungeon) ? null : DungeonCatalog.Find(request.Dungeon);
        if (!string.IsNullOrWhiteSpace(request.Dungeon) && entry == null)
        {
            message = "There is no dungeon '" + request.Dungeon + "' in dungeons.json.";
            return false;
        }
        if (entry != null && !entry.IsOpen(DateTime.Now, out string closed))
        {
            message = entry.Label + " is closed (" + closed + "). Open it in dungeons.json first.";
            return false;
        }

        (float X, float Y)? at = request.X.HasValue && request.Y.HasValue ? (request.X.Value, request.Y.Value) : null;
        Carrier? made = CreateCarrier(s, WorldSettingsFile.Current.Carriers, entry, now, log, at);
        if (made == null)
        {
            message = at.HasValue
                ? "No free land near that point for a carrier. Pick another place."
                : "No dungeon is open or there is no free place for a carrier.";
            return false;
        }

        message = made.Entry.Label + " carrier at " + made.X.ToString("0") + ", " + made.Y.ToString("0") + " for " +
                  WorldSettingsFile.Current.Carriers.LifetimeSeconds.ToString("0") + " s.";
        Chat(s, "A dungeon carrier appeared in the open world: " + made.Entry.Label + ". " + Direction(made.X, made.Y, Players(s)));
        return true;
    }

    // Free land near a point: the point itself, or the closest place around it (up to "radius" units) with room for "reach".
    private static bool FindOpenNear(WorldMap map, float x, float y, float reach, float radius, out float foundX, out float foundY)
    {
        if (map.IsOpenLand(x, y, reach))
        {
            foundX = x;
            foundY = y;
            return true;
        }
        for (float distance = 6f; distance <= radius; distance += 6f)
        {
            int steps = Math.Max(8, (int)(distance * 0.9f));
            for (int i = 0; i < steps; i++)
            {
                double angle = i * Math.PI * 2.0 / steps;
                float tx = x + (float)Math.Sin(angle) * distance;
                float ty = y + (float)Math.Cos(angle) * distance;
                if (!map.IsOpenLand(tx, ty, reach)) continue;
                foundX = tx;
                foundY = ty;
                return true;
            }
        }
        foundX = 0f;
        foundY = 0f;
        return false;
    }

    // ------------------------------------------------------------------ answer

    private static void WriteResult(string id, bool ok, string message, Action<string, string> log)
    {
        try
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), ResultFileName);
            string text = JsonSerializer.Serialize(new { id, ok, message, at = DateTime.Now.ToString("o") },
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception e)
        {
            log("WORLD", "could not write " + ResultFileName + ": " + e.Message);
        }
    }
}
