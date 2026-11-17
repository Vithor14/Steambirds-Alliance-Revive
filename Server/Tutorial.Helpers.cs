using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

public static partial class Tutorial
{
    // ================================================================== helpers

    private static int Create(Session sess, string type, float a, float b, float rotation, string group,
        Ground.Rect? area, bool? invulnerable, double now, Action<string, string> log)
    {
        Ground.ToWorld(a, b, out float x, out float y);
        return Enemies.CreateFromScript(sess.Server, sess.Connection, Role, type, x, y, rotation, group, area,
            invulnerable, now, log);
    }

    // Pre-placed enemy: spawns idle, with no target. radius > 0: wakes up (with the group) when the player gets
    // within that distance; 0: only by the script or with a hit. Returns 1 if it spawned.
    private static int CreateWaiting(Session sess, string type, float a, float b, float rotation, string group,
        Ground.Rect? area, float radius, double now, Action<string, string> log)
    {
        int id = Create(sess, type, a, b, rotation, group, area, null, now, log);
        if (id == 0) return 0;
        WaitFor(sess, id, radius);
        return 1;
    }

    private static void WaitFor(Session sess, int shortId, float radius)
    {
        if (shortId != 0) Enemies.SetAsleep(sess.Connection, shortId, true, radius);
    }

    // Hangar turrets back to silence (the player left the hangar or jumped a step).
    private static void QuietTowers(Session sess)
    {
        foreach (int tower in sess.Towers) WaitFor(sess, tower, 0f);
    }

    // Line of Drill Sergeant Davis (portrait "sergeant", a bubble over him and a chat line).
    private static void Say(Session sess, string key)
    {
        WorldObjects.SendSpeech(sess.Server, sess.Connection, sess.Instructor, key, 4f, WorldObjects.SpeechDialog,
            InstructorPortrait, Rng());
    }

    // Marks a script step as done. True only the first time.
    private static bool Once(Session sess, string step)
    {
        return sess.Finished.Add(step);
    }

    private static float Rng()
    {
        lock (rng) return (float)rng.NextDouble();
    }

    private static void Chat(Session sess, string text)
    {
        WorldObjects.SendChat(sess.Server, sess.Connection, text);
    }

    private static void Schedule(Session sess, double when, string name, Action<double> action)
    {
        sess.Pending.Add(new PendingAction { When = when, Name = name, Action = action });
    }

    // Runs what is due. An action can schedule another (it goes into the list for the next round).
    private static void RunPending(Session sess, double now)
    {
        if (sess.Pending.Count == 0) return;
        List<PendingAction> cleared = sess.Pending.Where(p => p.When <= now).OrderBy(p => p.When).ToList();
        if (cleared.Count == 0) return;
        sess.Pending.RemoveAll(p => p.When <= now);
        foreach (PendingAction p in cleared) p.Action(now);
    }

    private static string GateGroup(int k)
    {
        return "gate" + k.ToString(Inv);
    }

    private static string StageName(int stage)
    {
        switch (stage)
        {
            case 0: return "hangar";
            case 1: return "anteroom drones";
            case 2: return "corridor drones";
            case 3: return "gas drone ring";
            case 4: return "supply crate";
            case 5: return "boss gate";
            case 6: return "Large Training Drone";
            case 7: return "exit to the Nexus";
            default: return "?";
        }
    }

    private static string Id(int shortId)
    {
        return shortId != 0 ? shortId.ToString(Inv) : "NOT created";
    }

    private static string Num(float val)
    {
        return val.ToString("0.##", Inv);
    }

    // ClientMapInfo of the tutorial: the client's original one (opening conversation, inventory hints,
    // "Basic training: COMPLETE!" when the portal appears) plus one event per floor sign of the map
    // (Maps.AddSigns: the client creates each text when the plane gets close).
    private static Map.MapInfoData BuildMapInfo(Map.MapInfoData? original)
    {
        Map.MapInfoData info = original ?? OriginalTutorialInfo();
        lock (Game.Lock) Maps.AddSigns(info, MapForSession());
        return info;
    }

    // Current map of the file, if it has everything the script uses; otherwise, the last one it had.
    private static Maps.Map MapForSession()
    {
        Maps.Map map = Maps.Get(MapId, mapLog);
        if (ReferenceEquals(map, mapValid)) return map;
        string? error = Validate(map);
        if (error == null)
        {
            mapValid = map;
            return map;
        }
        mapLog("MAP", "ERROR in the tutorial map (" + map.FilePath + "): " + error +
            (mapValid != null ? " (the previous version stays)" : ""));
        if (mapValid != null) return mapValid;
        throw new InvalidOperationException("invalid tutorial map (" + map.FilePath + "): " + error);
    }

    // Checks everything the script asks for by name. Returns the first problem or null.
    private static string? Validate(Maps.Map map)
    {
        try
        {
            if (map.DrawingTiles.Count == 0) return "the floor drawing is empty";
            if (map.Gates.Count < 3) return "needs 3 gates (crate wall, gate, boss gate)";
            if (map.Checkpoints.Count == 0) return "needs at least 1 checkpoint (the first one is where the plane spawns)";
            foreach (string name in new[] { "instructor", "ring", "crate", "boss", "portal", "dronesGuide", "finalCorridorGuide" })
                map.GetPoint(name);
            foreach (string name in new[] { "towers", "drones1", "drones2" }) map.GetGroup(name);
            foreach (string name in new[] { "hangar", "anteroom", "drones", "ring", "arena" }) map.GetArea(name);
            foreach (string name in new[] { "instructor", "towers", "bulletsTalk", "hangarExit", "towersQuiet",
                         "droneCorridor", "finalCorridor" })
                map.GetLineB(name);
            foreach (string name in new[] { "ringCount", "ringOpening", "ringRadiusU", "ringRadiusV", "ringMiddleRadius",
                         "drones1Radius" })
                map.GetValue(name);
            foreach (string name in new[] { "anteroom", "ring room", "crate corridor", "final corridor", "arena" })
                map.GetCheckpoint(name);
            return null;
        }
        catch (InvalidOperationException e)
        {
            return e.Message;
        }
    }

    // The ClientMapInfo of the client 201219.1413's DungeonTutorialWalker (mapinfo_config.txt), for when the
    // file is not in the data folder.
    private static Map.MapInfoData OriginalTutorialInfo()
    {
        var info = new Map.MapInfoData
        {
            RespawnWithClass = "walker",
            StartsAtKeyScreen = true,
            HomeTeleportOff = true,
            LocalTeleportOff = false
        };
        info.Events.Add(Map.Ev("on_player_gets_plane", "init1", null, Map.Reac("conversation", "", "intro")));
        info.Events.Add(Map.Ev("close_inventory", "init2", null, Map.Reac("show_inventory")));
        info.Events.Add(Map.Ev("pickup_item", "tutorial_pickup", null, Map.Reac("lore_text_hint", "tutorial_pickup", "", 3f, 5f, 8f)));
        info.Events.Add(Map.Ev("pickup_item", "inventory_hint",
            Map.Cond(Map.ConditionNot, "", 0f, 0f, 0f, Map.Cond(Map.ConditionInventoryOpen)),
            Map.Reac("show_hint", "inventory_hint")));
        info.Events.Add(Map.Ev("open_inventory", "", null, Map.Reac("hide_hint", "inventory_hint")));
        info.Events.Add(Map.Ev("open_inventory", "",
            Map.Cond(Map.ConditionNot, "", 0f, 0f, 0f, Map.Cond(Map.ConditionKeyNotSet, "init2")),
            Map.Reac("show_hint", "inventory_hint_close")));
        info.Events.Add(Map.Ev("close_inventory", "", null, Map.Reac("hide_hint", "inventory_hint_close")));
        info.Events.Add(Map.Ev("on_player_gets_plane", "", null, Map.Reac("disable_item_pickup_panel")));
        info.Events.Add(Map.Ev("open_inventory", "", Map.Cond(Map.ConditionKeySet, "inventory_hint"),
            Map.Reac("enable_item_pickup_panel")));
        info.Events.Add(Map.Ev("portal", "tut_end_text",
            Map.Cond(Map.ConditionAnd, "", 0f, 0f, 0f, Map.Cond(Map.ConditionWindowFocused)),
            Map.Reac("lore_text_hint", "newtutorialgroundsign11", "", 2f, 8f, 5f)));
        info.Events.Add(Map.Ev("repeatEvery_5", "tutorial_health",
            Map.Cond(Map.ConditionAnd, "", 0f, 0f, 0f, Map.Cond(Map.ConditionLowHealth, "", 0.6f),
                Map.Cond(Map.ConditionKeyNotSet, "tutorial_health")),
            Map.Reac("lore_text_hint", "tutorial_health", "", 0f, 0f, 8f)));
        return info;
    }

    // The same types as the shipped enemies.json. They apply when the file does not have the key.
    private static void RegisterDefaultTypes()
    {
        Enemies.RegisterDefaultType("tutorial_hunter", new Enemies.EnemyType
        {
            Name = "Training Drone", Template = "tutorialattacker", Behavior = "chase",
            PreferredDistance = 7f, AggroRadius = 26f, FirstShotDelay = 1.5f
        });
        Enemies.RegisterDefaultType("tutorial_drone", new Enemies.EnemyType
        {
            Name = "Gas Drone", Template = "tutorialattacker2minion_walker", Behavior = "charge",
            PreferredDistance = 6f, AggroRadius = 40f
        });
        Enemies.RegisterDefaultType("tutorial_cannon", new Enemies.EnemyType
        {
            Name = "Training Turret", Template = "tutorialcannon", Behavior = "fixed", Invulnerable = true,
            AggroRadius = 0f, ShotRange = 40f, FirstShotDelay = 0.3f
        });
        Enemies.RegisterDefaultType("tutorial_boss", new Enemies.EnemyType
        {
            Name = "Large Training Drone", Template = "tutorialattacker4", Boss = true, Behavior = "circle",
            PreferredDistance = 9f, AggroRadius = 60f, FirstShotDelay = 2.0f,
            Phases = new List<Enemies.BossPhase>
            {
                new Enemies.BossPhase
                {
                    BelowHealth = 0.66f, Summon = "tutorial_minion", SummonCount = 2
                },
                new Enemies.BossPhase
                {
                    BelowHealth = 0.33f, Behavior = "charge", SpeedMultiplier = 1.2f,
                    Summon = "tutorial_minion", SummonCount = 2
                }
            }
        });
        Enemies.RegisterDefaultType("tutorial_minion", new Enemies.EnemyType
        {
            Name = "Large Drone Escort", Template = "tutorialattacker4minion", Behavior = "chase",
            PreferredDistance = 7f, AggroRadius = 60f
        });
        Enemies.RegisterDefaultType("tutorial_boss_death", new Enemies.EnemyType
        {
            Name = "Large Drone Wreck", Template = "tutorialattacker4DEATH", Behavior = "idle", Invulnerable = true
        });
        Enemies.RegisterDefaultType("tutorial_wall0", new Enemies.EnemyType
        {
            Name = "Crate Wall", Template = "tutorialwall0", Behavior = "idle", HealthMultiplier = 0.3f
        });
        Enemies.RegisterDefaultType("tutorial_wall", new Enemies.EnemyType
        {
            Name = "Crate Gate", Template = "tutorialwall", Behavior = "idle", HealthMultiplier = 0.3f
        });
        Enemies.RegisterDefaultType("tutorial_wall_boss", new Enemies.EnemyType
        {
            Name = "Boss Gate", Template = "tutorialwallboss", Behavior = "idle", HealthMultiplier = 0.3f
        });
        Enemies.RegisterDefaultType("tutorial_chest", new Enemies.EnemyType
        {
            Name = "Supply Crate", Template = "tutorialwalkerdungeontreasure", Behavior = "idle", OpensAfterHitSeconds = 1.5f
        });
        Enemies.RegisterDefaultType("tutorial_ally", new Enemies.EnemyType
        {
            Name = "Drill Sergeant Davis", Template = "tutorialwalkerally", Behavior = "escort", Team = 1, FreeRoam = true,
            Invulnerable = true, PreferredDistance = 5f
        });
    }
}
