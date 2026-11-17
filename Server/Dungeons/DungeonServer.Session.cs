using System.Globalization;
using System.Net;
using Lidgren.Network;
using Info = MapInfo;

public static partial class DungeonServer
{
    // ================================================================== chat commands

    public static bool Command(NetServer server, NetConnection? connection, string role, List<string> pieces, double now,
        Action<string, string> log)
    {
        if (connection == null || pieces.Count == 0) return false;
        string command = pieces[0].Trim().ToLowerInvariant();
        string? argument = pieces.Count > 1 ? string.Join(" ", pieces.Skip(1)).Trim() : null;
        bool inDungeon = string.Equals(role, Role, StringComparison.OrdinalIgnoreCase);

        switch (command)
        {
            case "/dungeons":
            {
                foreach (DungeonCatalog.Entry d in DungeonCatalog.All())
                    WorldObjects.SendChat(server, connection, DungeonCatalog.Describe(d));
                WorldObjects.SendChat(server, connection, "Use /dungeon NAME (e.g. /dungeon bomb) or enter a dungeon carrier in the Nexus.");
                log("COMMAND", "[" + role + "] /dungeons");
                return true;
            }

            case "/dungeon":
            {
                DungeonCatalog.Entry? entry = DungeonCatalog.Find(argument);
                if (entry == null)
                {
                    WorldObjects.SendChat(server, connection, string.IsNullOrEmpty(argument)
                        ? "Use /dungeon NAME. List: /dungeons"
                        : "Dungeon '" + argument + "' is not in the " + DungeonCatalog.FileName + ". List: /dungeons");
                }
                else if (!entry.IsOpen(DateTime.Now, out string reason))
                {
                    WorldObjects.SendChat(server, connection, entry.Label + " is closed: " + reason + ".");
                }
                else if (SwitchToDungeon == null)
                {
                    WorldObjects.SendChat(server, connection, "No link to the dungeon server.");
                }
                else
                {
                    WorldObjects.SendChat(server, connection, "Going to " + entry.Label + "...");
                    SwitchToDungeon(server, connection, entry.Type, "/dungeon no chat (" + role + ")");
                }
                log("COMMAND", "[" + role + "] " + string.Join(" ", pieces));
                return true;
            }

            case "/dungeonhelp":
                WorldObjects.SendChat(server, connection, "/dungeons: list. /dungeon NAME: go to the dungeon.");
                WorldObjects.SendChat(server, connection, "Inside the dungeon: /room [N] (list or jump to room N), /boss (boss room), " +
                    "/restart (from scratch), /leave (back to the Nexus).");
                if (inDungeon)
                {
                    lock (Game.Lock)
                    {
                        if (sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s))
                            WorldObjects.SendChat(server, connection, "Now: " + StateText(s));
                    }
                }
                return true;

            case "/room":
            case "/boss":
            case "/leave":
            case "/restart":
                if (!inDungeon) return false;   // the tutorial and the enemies have commands with these names
                break;

            default:
                return false;
        }

        lock (Game.Lock)
        {
            if (!sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? s) || s.Map == null)
            {
                WorldObjects.SendChat(server, connection, "No dungeon session on this connection.");
                return true;
            }
            List<Maps.Checkpoint> points = s.Map.Checkpoints;
            switch (command)
            {
                case "/room":
                {
                    if (string.IsNullOrEmpty(argument))
                    {
                        for (int i = 0; i < points.Count; i++)
                            Chat(s, (i + 1).ToString(Inv) + ": " + points[i].Name + (i == s.Checkpoint ? " (current checkpoint)" : ""));
                        Chat(s, "Use /room N to jump to room N.");
                        break;
                    }
                    int index = -1;
                    if (int.TryParse(argument, NumberStyles.Integer, Inv, out int n)) index = n - 1;
                    else index = points.FindIndex(p => p.Name.Contains(argument, StringComparison.OrdinalIgnoreCase));
                    if (index < 0 || index >= points.Count)
                    {
                        Chat(s, "Room '" + argument + "' does not exist. /room shows the list.");
                        break;
                    }
                    SkipTo(s, connection, index, now, log);
                    break;
                }

                case "/boss":
                    if (points.Count == 0) break;
                    SkipTo(s, connection, points.Count - 1, now, log);
                    break;

                case "/leave":
                    SendMemberToNexus(s, connection, "/leave in the chat", s.Finished);
                    break;

                case "/restart":
                    Restart(s, now, log);
                    break;
            }
            log("COMMAND", "[" + role + "] " + string.Join(" ", pieces));
            return true;
        }
    }

    // ================================================================== session

    private static Session NewSession(NetServer server, NetConnection connection, DungeonCatalog.Entry entry, double now,
        Action<string, string> log)
    {
        var s = new Session
        {
            Id = "dungeon:" + (nextSessionNumber++).ToString(Inv),
            Peer = connection.RemoteUniqueIdentifier,
            Server = server,
            Connection = connection,
            Entry = entry,
            NextTick = now
        };
        s.Members.Add(connection);
        try
        {
            s.Map = Maps.Get(entry.Map, log);
            s.Data = DungeonMapData.Get(entry.Map, entry.HealthMultiplier, log);
            s.Plant = s.Map.CreatePlan();
        }
        catch (Exception e)
        {
            s.Map = null;
            s.Data = null;
            s.Plant = null;
            s.LoadError = e.Message;
            log("DUNGEON", "[dungeon] ERROR: " + entry.Label + " (" + entry.Map + ") did not load: " + e.Message);
            return s;
        }

        // Rooms: one per encounter whose area exists in the map.
        foreach (DungeonMapData.EncounterDef enc in s.Data.Encounters)
        {
            string areaName = string.IsNullOrWhiteSpace(enc.Area) ? enc.Room : enc.Area;
            if (!s.Map.Areas.TryGetValue(areaName, out Ground.Rect area))
            {
                log("DUNGEON", "[dungeon] warning: room '" + enc.Room + "' of " + entry.Map + " has no area \"" + areaName + "\": ignored");
                continue;
            }
            if (s.RoomsByName.ContainsKey(enc.Room)) continue;
            var room = new RoomState { Name = enc.Room, Index = s.Rooms.Count, Area = area, Encounter = enc };
            s.Rooms.Add(room);
            s.RoomsByName[room.Name] = room;
        }

        // Gates: one wall piece per free sub-cell of the row (or column), at its center, like the tutorial.
        int index = 0;
        foreach (DungeonMapData.GateDef def in s.Data.File.Gates ?? new List<DungeonMapData.GateDef>())
        {
            var gate = new GateState { Def = def, Index = index++, Side = false };
            for (int si = Math.Min(def.SiFrom, def.SiTo); si <= Math.Max(def.SiFrom, def.SiTo); si++)
                AddPiece(s, gate, si, def.Sj);
            s.Gates.Add(gate);
        }
        foreach (DungeonMapData.GateDef def in s.Data.File.SideGates ?? new List<DungeonMapData.GateDef>())
        {
            var gate = new GateState { Def = def, Index = index++, Side = true };
            for (int sj = Math.Min(def.SjFrom, def.SjTo); sj <= Math.Max(def.SjFrom, def.SjTo); sj++)
                AddPiece(s, gate, def.Si, sj);
            s.Gates.Add(gate);
        }

        log("DUNGEON", "[dungeon] new session: " + entry.Label + " (" + s.Map.FilePath + "): " + s.Rooms.Count + " room(s), " +
            s.Gates.Count + " gate(s), " + s.Map.Checkpoints.Count + " checkpoint(s)");
        return s;
    }

    private static void AddPiece(Session s, GateState gate, int si, int sj)
    {
        if (s.Plant == null || !s.Plant.SubIsFree(si, sj)) return;
        float a = (si + 0.5f) / Ground.WallsPerCell;
        float b = (sj + 0.5f) / Ground.WallsPerCell;
        Ground.ToWorld(a, b, out float x, out float y);
        gate.Pieces.Add((x, y, 0));
    }

    // The plane is in the dungeon for the first time: walls of the gates and the welcome text.
    private static void StartDungeon(Session s, double now, Action<string, string> log)
    {
        s.Started = true;
        s.StartedAt = now;
        if (s.Rooms.Count > 0) Enemies.SetDefaultArea(s.Server, s.Connection, Role, s.Rooms[0].Area, now);

        int pieces = 0;
        foreach (GateState gate in s.Gates)
        {
            // Barricades ("damageable") take damage; locked doors wait for their condition.
            pieces += SpawnGate(s, gate, !gate.Def.Damageable, now, log);
        }

        float tier = 0f;
        if (s.Data?.File.Values != null) s.Data.File.Values.TryGetValue("tier", out tier);
        Chat(s, s.Entry.Label + (tier > 0f ? " (tier " + tier.ToString("0", Inv) + ")" : "") + ". Commands: /dungeonhelp");
        log("DUNGEON", "[dungeon] " + s.Entry.Label + " started: " + pieces + " wall piece(s) in " + s.Gates.Count + " gate(s)");
        Tick(s, now, log);
    }

    private static int SpawnGate(Session s, GateState gate, bool invulnerable, double now, Action<string, string> log)
    {
        if (s.Data == null) return 0;
        int created = 0;
        for (int i = 0; i < gate.Pieces.Count; i++)
        {
            (float x, float y, _) = gate.Pieces[i];
            int id = Enemies.CreateFromScript(s.Server, s.Connection, Role, s.Data.Key(gate.Def.Type), x, y, 0f, gate.Group, null,
                invulnerable, now, log);
            gate.Pieces[i] = (x, y, id);
            if (id != 0) created++;
        }
        gate.Open = false;
        return created;
    }

}
