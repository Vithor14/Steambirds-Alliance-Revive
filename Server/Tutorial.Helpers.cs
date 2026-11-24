using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

public static partial class Tutorial
{
    // ================================================================== helpers

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

    // Checks what the tutorial needs from the map: a floor, the crate walls, a spawn checkpoint and a script. (The names the
    // script uses are checked by MapScript.Validate when the file is read.) Returns the first problem or null.
    private static string? Validate(Maps.Map map)
    {
        if (map.DrawingTiles.Count == 0) return "the floor drawing is empty";
        if (map.Checkpoints.Count == 0) return "needs at least 1 checkpoint (the first one is where the plane spawns)";
        if (map.Script == null || (map.Script.Rules?.Count ?? 0) == 0) return "has no \"script\" with rules (the tutorial is all in the script)";
        return null;
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
            Name = "Training Drone", Template = "tutorialattacker", Ai = "generic_chase",
            PreferredDistance = 7f, AggroRadius = 26f, FirstShotDelay = 1.5f
        });
        Enemies.RegisterDefaultType("tutorial_drone", new Enemies.EnemyType
        {
            Name = "Gas Drone", Template = "tutorialattacker2minion_walker", Ai = "generic_charge",
            PreferredDistance = 6f, AggroRadius = 40f
        });
        Enemies.RegisterDefaultType("tutorial_cannon", new Enemies.EnemyType
        {
            Name = "Training Turret", Template = "tutorialcannon", Ai = "generic_fixed", Invulnerable = true,
            AggroRadius = 0f, ShotRange = 40f, FirstShotDelay = 0.3f
        });
        Enemies.RegisterDefaultType("tutorial_boss", new Enemies.EnemyType
        {
            Name = "Large Training Drone", Template = "tutorialattacker4", Boss = true,
            PreferredDistance = 9f, AggroRadius = 60f, FirstShotDelay = 2.0f,
            // Two phases that summon the escorts: ai\brains\tutorial_boss.json
            Ai = "tutorial_boss"
        });
        Enemies.RegisterDefaultType("tutorial_minion", new Enemies.EnemyType
        {
            Name = "Large Drone Escort", Template = "tutorialattacker4minion", Ai = "generic_chase",
            PreferredDistance = 7f, AggroRadius = 60f
        });
        Enemies.RegisterDefaultType("tutorial_boss_death", new Enemies.EnemyType
        {
            Name = "Large Drone Wreck", Template = "tutorialattacker4DEATH", Ai = "generic_idle", Invulnerable = true
        });
        Enemies.RegisterDefaultType("tutorial_wall0", new Enemies.EnemyType
        {
            Name = "Crate Wall", Template = "tutorialwall0", Ai = "generic_idle", HealthMultiplier = 0.3f
        });
        Enemies.RegisterDefaultType("tutorial_wall", new Enemies.EnemyType
        {
            Name = "Crate Gate", Template = "tutorialwall", Ai = "generic_idle", HealthMultiplier = 0.3f
        });
        Enemies.RegisterDefaultType("tutorial_wall_boss", new Enemies.EnemyType
        {
            Name = "Boss Gate", Template = "tutorialwallboss", Ai = "generic_idle", HealthMultiplier = 0.3f
        });
        Enemies.RegisterDefaultType("tutorial_chest", new Enemies.EnemyType
        {
            Name = "Supply Crate", Template = "tutorialwalkerdungeontreasure", Ai = "generic_idle", OpensAfterHitSeconds = 1.5f
        });
        Enemies.RegisterDefaultType("tutorial_ally", new Enemies.EnemyType
        {
            Name = "Drill Sergeant Davis", Template = "tutorialwalkerally", Ai = "generic_escort", Team = 1, FreeRoam = true,
            Invulnerable = true, PreferredDistance = 5f
        });
    }
}
