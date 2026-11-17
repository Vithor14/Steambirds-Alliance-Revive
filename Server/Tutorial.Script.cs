using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

public static partial class Tutorial
{
    // ================================================================== script

    // Start: floor signs, crate walls, the instructor waiting at the hangar entrance and all the
    // enemies already in their places, waiting.
    private static void StartTutorial(Session sess, double now, Action<string, string> log)
    {
        sess.Started = true;
        sess.Stage = 0;
        Maps.Map map = sess.Map;
        Ground.Rect hangar = map.GetArea("hangar");
        Enemies.SetDefaultArea(sess.Server, sess.Connection, Role, hangar, now);

        // The floor signs come in the ClientMapInfo (BuildMapInfo): the client creates them by itself.

        // Crate walls (the anteroom one is already destructible; the gates unlock at the right moment).
        int crates = 0;
        foreach (Gate gate in sess.Gates)
        {
            Maps.Gate def = map.Gates[gate.Index];
            gate.Damageable = def.Damageable;
            foreach (Piece piece in gate.Pieces)
            {
                piece.ShortId = Enemies.CreateFromScript(sess.Server, sess.Connection, Role, gate.Type,
                    piece.X, piece.Y, CrateRotation, GateGroup(gate.Index), null, !def.Damageable, now, log);
                if (piece.ShortId != 0) crates++;
            }
        }

        // Drill Sergeant Davis waits at the hangar entrance, looking at the player.
        Maps.Point instructor = map.GetPoint("instructor");
        sess.Instructor = Create(sess, "tutorial_ally", instructor.A, instructor.B, instructor.Rotation, "ally", null, null, now, log);
        instructor.World(out sess.GuideX, out sess.GuideY);
        sess.WaitingAtEntrance = true;

        // Training Turrets on the hangar's left wall, facing the front of the corridor (+b): the gun
        // (tutorialcannon) fires 2 bullets with multiSpread 180, one to each side (+a and -a), so the bursts
        // cross the room from wall to wall and the player passes between them ("Try dodging these bullets!").
        // They stay quiet until the player enters the hangar.
        sess.Towers.Clear();
        List<Maps.Point> towers = map.GetGroup("towers");
        for (int i = 0; i < towers.Count; i++)
        {
            int tower = Create(sess, "tutorial_cannon", towers[i].A, towers[i].B, towers[i].Rotation, "turret" + i.ToString(Inv),
                hangar, null, now, log);
            if (tower == 0) continue;
            WaitFor(sess, tower, 0f);
            sess.Towers.Add(tower);
        }

        // Training drones: two in the anteroom and three in the corridor (this group wakes up when the wall falls).
        int drones = 0;
        float drones1Radius = map.GetValue("drones1Radius");
        foreach (Maps.Point p in map.GetGroup("drones1"))
            drones += CreateWaiting(sess, "tutorial_hunter", p.A, p.B, p.Rotation, "drones1", map.GetArea("anteroom"),
                drones1Radius, now, log);
        foreach (Maps.Point p in map.GetGroup("drones2"))
            drones += CreateWaiting(sess, "tutorial_hunter", p.A, p.B, p.Rotation, "drones2", map.GetArea("drones"), 0f, now, log);

        // Ring of gas drones waiting in a circle in the middle of their room, facing the center.
        int ring = CreateRing(sess, now, log);

        // Supply crate beside the corridor. The Large Training Drone enters the arena (idle, waiting)
        // when the boss gate unlocks, out of sight: that way the boss counter only shows up at the end.
        Maps.Point crate = map.GetPoint("crate");
        sess.Crate = Create(sess, "tutorial_chest", crate.A, crate.B, crate.Rotation, "crate", null, null, now, log);

        WorldObjects.SendChat(sess.Server, sess.Connection, "You unlocked Quad");
        log("TUTORIAL", "[tutorial] step 0 (hangar): " + crates + " crate(s) in " + sess.Gates.Count + " wall(s), instructor " +
            Id(sess.Instructor) + ", " + sess.Towers.Count + " tower(s) in the hangar, " + drones + " training drone(s) and " + ring +
            " gas drone(s) waiting, crate " + Id(sess.Crate) + ", " + map.Signs.Count + " floor sign(s) (through the ClientMapInfo)");
    }

    // 14 gas drones in an ellipse in the middle of the ring room (longer along the corridor), with an
    // opening on the side of whoever arrives: the player enters the middle of the ring and it closes around them.
    private static int CreateRing(Session sess, double now, Action<string, string> log)
    {
        Maps.Map map = sess.Map;
        Maps.Point center = map.GetPoint("ring");
        int count = Math.Max(1, (int)map.GetValue("ringCount"));
        float opening = map.GetValue("ringOpening");          // degrees without a drone, centered on the entrance side
        float radiusA = map.GetValue("ringRadiusU") / Ground.CellSize;
        float radiusB = map.GetValue("ringRadiusV") / Ground.CellSize;
        Ground.Rect room = map.GetArea("ring");
        center.World(out float cx, out float cy);
        int created = 0;
        for (int i = 0; i < count; i++)
        {
            // 270 degrees = entrance side (smaller b). The drones take up the rest of the circle.
            float degrees = 270f + opening / 2f + i * (360f - opening) / Math.Max(1, count - 1);
            float angle = degrees * MathF.PI / 180f;
            float a = center.A + radiusA * MathF.Cos(angle);
            float b = center.B + radiusB * MathF.Sin(angle);
            Ground.ToWorld(a, b, out float x, out float y);
            float toCenter = MathF.Atan2(cx - x, cy - y) * 180f / MathF.PI;
            created += CreateWaiting(sess, "tutorial_drone", a, b, toCenter, "ring", room, 0f, now, log);
        }
        return created;
    }

    private static void UpdateSession(Session sess, double now, Action<string, string> log)
    {
        if (!sess.Started) return;

        RunPending(sess, now);
        CheckGates(sess, now, log);

        if (Game.GetPlane(sess.Peer, out float px, out float py, out bool alive, out _) && alive)
        {
            Ground.ToGrid(px, py, out float pa, out float pb);
            UpdateCheckpoint(sess, pb, now, log);
            Triggers(sess, pa, pb, now, log);
        }

        CheckDrones(sess);
        CheckRing(sess, now, log);
        CheckCrate(sess, now, log);
        CheckBoss(sess, now, log);
        GuideInstructor(sess, now);
    }

    // What happens as the player advances along the corridor (a and b in tiles).
    private static void Triggers(Session sess, float pa, float pb, double now, Action<string, string> log)
    {
        Maps.Map map = sess.Map;

        // Meeting the instructor at the hangar entrance.
        if (pb >= map.GetLineB("instructor") && Once(sess, "talk_together"))
        {
            sess.WaitingAtEntrance = false;
            Say(sess, "newtutorialally1");                     // We are in this together, recruit!
            log("TUTORIAL", "[tutorial] player reached the instructor");
        }

        // Inside the hangar: the turrets start firing, one after another (out-of-sync bursts).
        if (pb >= map.GetLineB("towers") && Once(sess, "turret"))
        {
            int towerCount = map.GetGroup("towers").Count;
            for (int i = 0; i < towerCount; i++)
            {
                string group = "turret" + i.ToString(Inv);
                Schedule(sess, now + 0.35 * i, "tower " + i, t =>
                {
                    if (!sess.Finished.Contains("towers_quiet")) Enemies.WakeGroup(sess.Connection, group, t);
                });
            }
        }
        if (pb >= map.GetLineB("bulletsTalk") && Once(sess, "talk_bullets"))
            Say(sess, "newtutorialally1_2");                   // Try dodging these bullets!

        // Hangar exit: the turret goes quiet again and the instructor shows the training drones.
        if (pb >= map.GetLineB("hangarExit") && Once(sess, "drones1"))
        {
            sess.Stage = 1;
            Enemies.SetDefaultArea(sess.Server, sess.Connection, Role, map.GetArea("anteroom"), now);
            Say(sess, "newtutorialally2_0");                   // Not bad!
            Schedule(sess, now + 2.5, "talk drones", _ => Say(sess, "newtutorialally2_0_1"));
            Maps.Point guide = map.GetPoint("dronesGuide");
            Guide(sess, guide.A, guide.B, now, 6.0);
            log("TUTORIAL", "[tutorial] step 1 (training drones): hangar exit");
        }
        if (pb >= map.GetLineB("towersQuiet") && Once(sess, "towers_quiet"))
            QuietTowers(sess);

        // Drone corridor (after the crate wall).
        float droneCorridor = map.GetLineB("droneCorridor");
        if (pb >= droneCorridor && Once(sess, "drone_corridor"))
        {
            Enemies.SetDefaultArea(sess.Server, sess.Connection, Role, map.GetArea("drones"), now);
            WakeDrones2(sess, now);
        }

        // Gas drone ring: closes around the player when they reach the middle of the room.
        if (!sess.Finished.Contains("ring"))
        {
            Maps.Point ring = map.GetPoint("ring");
            float middleRadius = map.GetValue("ringMiddleRadius");
            ring.World(out float cx, out float cy);
            Ground.ToWorld(pa, pb, out float jx, out float jy);
            float dx = jx - cx;
            float dy = jy - cy;
            bool inMiddle = dx * dx + dy * dy <= middleRadius * middleRadius;
            // A shot from far away also wakes the ring (the whole group); it only counts with the player already in the corridor.
            bool provoked = pb >= droneCorridor && Enemies.GroupAwake(sess.Connection, "ring");
            if ((pb >= ring.B - 0.15f || inMiddle || provoked) && Once(sess, "ring"))
                WakeRing(sess, now, log);
        }

        // Final corridor: after the middle gate.
        if (pb >= map.GetLineB("finalCorridor") && sess.Gates[MiddleGate].Open && Once(sess, "prepare"))
        {
            sess.Stage = 5;
            Say(sess, "newtutorialally4_0");                   // Prepare yourself!
            ReleaseGate(sess, BossGate, now, log);
            CreateBoss(sess, now, log);
        }

        // Crate warning ("Shoot this to proceed!") when the player gets close to a damageable wall.
        foreach (Gate gate in sess.Gates)
        {
            if (!gate.Damageable || gate.Open || gate.Warned) continue;
            float b = gate.Sj / (float)Ground.WallsPerCell;
            if (pb < b - 1.1f) continue;
            gate.Warned = true;
            List<int> aliveCount = gate.Pieces.Where(p => p.ShortId != 0 && Enemies.IsAlive(sess.Connection, p.ShortId))
                .Select(p => p.ShortId).ToList();
            int who = aliveCount.Count > 0 ? aliveCount[aliveCount.Count / 2] : 0;
            if (who != 0)
                WorldObjects.SendSpeech(sess.Server, sess.Connection, who, "newtutorialwall", 4f, WorldObjects.SpeechNoChat, "",
                    Rng());
            if (gate.Index == WallGate) Guide(sess, 0.5f, b - 0.5f, now, 6.0);
        }
    }

    // The three corridor drones: they wake up when the crate wall falls (or when the player passes it).
    private static void WakeDrones2(Session sess, double now)
    {
        if (!Once(sess, "drones2")) return;
        sess.Stage = 2;
        Enemies.WakeGroup(sess.Connection, "drones2", now);
        Say(sess, "newtutorialally2");                         // Imagine these drones are a feline patrol.
        Schedule(sess, now + 4.5, "talk focus", _ => Say(sess, "newtutorialally2_2"));
    }

    // "These cats destroyed your home!" when one corridor drone is left (or none).
    private static void CheckDrones(Session sess)
    {
        if (!sess.Finished.Contains("drones2") || sess.Finished.Contains("talk_cats")) return;
        if (Enemies.CountAlive(sess.Connection, "drones2") > 1) return;
        sess.Finished.Add("talk_cats");
        Say(sess, "newtutorialally2_3");                       // These cats destroyed your home!
    }

    private static void WakeRing(Session sess, double now, Action<string, string> log)
    {
        sess.Stage = 3;
        sess.RingAt = now;
        Enemies.SetDefaultArea(sess.Server, sess.Connection, Role, sess.Map.GetArea("ring"), now);
        int awake = Enemies.WakeGroup(sess.Connection, "ring", now);
        int aliveOnes = Enemies.CountAlive(sess.Connection, "ring");
        Say(sess, "newtutorialwalkerally3");                   // Use your damage boost to deal extra damage.
        log("TUTORIAL", "[tutorial] step 3 (gas drone ring): " + aliveOnes + " drone(s) around the player" +
            (awake < aliveOnes ? " (the group had already woken up from a shot)" : ""));
        if (aliveOnes == 0) sess.RingAt = now - 60.0;              // no drones: the ring counts as cleared
    }

    // Ring cleared (or 55 s later, with the leftovers exploding): "Well done!" and the supply crate.
    private static void CheckRing(Session sess, double now, Action<string, string> log)
    {
        if (!sess.Finished.Contains("ring") || sess.Finished.Contains("ring_cleared")) return;
        int aliveOnes = Enemies.CountAlive(sess.Connection, "ring");
        if (aliveOnes > 0 && now < sess.RingAt + 55.0) return;

        sess.Finished.Add("ring_cleared");
        if (aliveOnes > 0)
        {
            Enemies.RemoveGroup(sess.Connection, "ring", true, now);
            log("TUTORIAL", "[tutorial] ring: " + aliveOnes + " remaining drone(s) exploded (time limit)");
        }
        else
        {
            log("TUTORIAL", "[tutorial] ring cleared");
        }

        Say(sess, "newtutorialally3_2");                       // Well done!
        Schedule(sess, now + 2.5, "crate", t => RequestCrate(sess, t, log));
    }

    // After the ring: the instructor tells the player to shoot the crate (if it is still closed).
    private static void RequestCrate(Session sess, double now, Action<string, string> log)
    {
        if (!Once(sess, "crate")) return;
        sess.Stage = 4;
        sess.CrateAt = now;
        if (CrateOpened(sess))
        {
            log("TUTORIAL", "[tutorial] step 4 (supply crate): the crate had already been opened");
            return;
        }
        Say(sess, "newtutorialallyx_1");                       // Shoot that storage crate!
        Maps.Point crate = sess.Map.GetPoint("crate");
        Guide(sess, crate.A + 0.25f, crate.B - 0.45f, now, 8.0);
        log("TUTORIAL", "[tutorial] step 4 (supply crate): crate " + Id(sess.Crate));
    }

    private static bool CrateOpened(Session sess)
    {
        return sess.Crate == 0 || !Enemies.IsAlive(sess.Connection, sess.Crate);
    }

    // Crate open (or 30 s after the request): "Better equipment gets auto-equipped." and the gate unlocks.
    // If the player opens the crate before the ring, the gate only unlocks after the "Well done!".
    private static void CheckCrate(Session sess, double now, Action<string, string> log)
    {
        if (!sess.Finished.Contains("crate") || sess.Finished.Contains("crate_open")) return;
        bool isOpen = CrateOpened(sess);
        if (!isOpen && now < sess.CrateAt + 30.0) return;

        sess.Finished.Add("crate_open");
        if (!isOpen) Enemies.RemoveGroup(sess.Connection, "crate", true, now);
        Say(sess, "newtutorialallyx_2");                       // Better equipment gets auto-equipped.
        ReleaseGate(sess, MiddleGate, now, log);
        log("TUTORIAL", "[tutorial] crate " + (isOpen ? "open" : "opened by the time limit") + ": gate unlocked");
    }

    // Large Training Drone at the far end of the arena, idle and with no target until the boss gate falls.
    private static void CreateBoss(Session sess, double now, Action<string, string> log)
    {
        if (sess.Boss != 0 || sess.BossDefeated) return;
        Maps.Point boss = sess.Map.GetPoint("boss");
        sess.Boss = Create(sess, "tutorial_boss", boss.A, boss.B, boss.Rotation, "boss", sess.Map.GetArea("arena"), null, now, log);
        if (sess.Boss == 0) return;
        WaitFor(sess, sess.Boss, 0f);
        Enemies.GetPosition(sess.Connection, sess.Boss, out sess.BossX, out sess.BossY);
        log("TUTORIAL", "[tutorial] boss " + sess.Boss + " waiting at the far end of the arena");
    }

    // Boss gate open: the Large Training Drone wakes up at the far end of the arena.
    private static void WakeBoss(Session sess, double now, Action<string, string> log)
    {
        if (sess.BossDefeated || !Once(sess, "boss_awake")) return;
        sess.Stage = 6;
        CreateBoss(sess, now, log);
        Enemies.SetDefaultArea(sess.Server, sess.Connection, Role, sess.Map.GetArea("arena"), now);
        if (sess.Boss == 0 || !Enemies.IsAlive(sess.Connection, sess.Boss))
        {
            // Without the boss (missing catalog?) the tutorial cannot get stuck: it goes straight to the portal.
            log("TUTORIAL", "[tutorial] the boss does not exist: skipping to the portal");
            sess.Map.GetPoint("boss").World(out sess.BossX, out sess.BossY);
            sess.BossDefeated = true;
            OnBossDefeated(sess, now, log);
            return;
        }
        Enemies.WakeGroup(sess.Connection, "boss", now);
        int boss = sess.Boss;
        WorldObjects.SendSpeech(sess.Server, sess.Connection, boss, "newtutorialenemy1", 4f, WorldObjects.SpeechDialog,
            BossPortrait, Rng());                          // The birds shall fall!
        log("TUTORIAL", "[tutorial] step 6 (Large Training Drone): boss " + sess.Boss + " woke up in the arena");
    }

    private static void CheckBoss(Session sess, double now, Action<string, string> log)
    {
        if (sess.Boss == 0 || sess.BossDefeated) return;
        if (Enemies.GetPosition(sess.Connection, sess.Boss, out float x, out float y))
        {
            sess.BossX = x;
            sess.BossY = y;
            return;
        }
        sess.BossDefeated = true;
        OnBossDefeated(sess, now, log);
    }

    // Boss defeated: the escort explodes, wreck with the last line, then the carrier (portal) and the instructor.
    private static void OnBossDefeated(Session sess, double now, Action<string, string> log)
    {
        sess.Stage = 7;
        int escort = Enemies.RemoveGroup(sess.Connection, "boss", true, now);
        int wreck = Enemies.CreateFromScript(sess.Server, sess.Connection, Role, "tutorial_boss_death",
            sess.BossX, sess.BossY, float.NaN, "wreck", null, true, now, log);
        if (wreck != 0)
        {
            WorldObjects.SendSpeech(sess.Server, sess.Connection, wreck, "tutorialBossDEATH", 4f, WorldObjects.SpeechDialog,
                BossPortrait, Rng());                      // You have defeated me! / Your test is complete!
            Schedule(sess, now + 6.0, "wreck", t => Enemies.RemoveGroup(sess.Connection, "wreck", false, t));
        }

        Maps.Point portal = sess.Map.GetPoint("portal");
        Schedule(sess, now + 2.0, "portal", t =>
        {
            CreatePortal(sess, portal.A, portal.B, t, log);
            Guide(sess, portal.A - 0.6f, portal.B - 0.3f, t, 30.0);
        });
        Schedule(sess, now + 3.5, "final talk 1", _ => Say(sess, "newtutorialally5"));  // Excellent work, recruit!...
        Schedule(sess, now + 10.0, "final talk 2", _ => Say(sess, "newtutorialally6")); // Fly aboard this carrier...

        log("TUTORIAL", "[tutorial] boss defeated at (" + Num(sess.BossX) + ", " + Num(sess.BossY) + "): " +
            escort + " of the escort removed, wreck " + (wreck != 0 ? "created" : "NOT created"));
    }

    // Exit portal (prefab 11, default art: the carrier) to the Nexus, at (a, b).
    private static void CreatePortal(Session sess, float a, float b, double now, Action<string, string> log)
    {
        if (sess.Portal != 0) return;
        Ground.ToWorld(a, b, out float x, out float y);
        int id = WorldObjects.NewShortId();
        WorldObjects.SendExitPortal(sess.Server, sess.Connection, id, x, y, portalDestination, nexusId, "Nexus",
            gameVersion, nexusTags, now);
        sess.Portal = id;
        sess.PortalX = x;
        sess.PortalY = y;
        sess.Objects.Add((id, x, y));
        log("TUTORIAL", "[tutorial] exit portal " + id + " (carrier) created at (" + Num(x) + ", " + Num(y) + "), tile (" +
            Num(a) + ", " + Num(b) + ") -> Nexus " + portalDestination + "; automatic exit in " + Num((float)AutoExitAt) + " s");
        Schedule(sess, now + AutoExitAt, "automatic exit", _ => LeaveAlone(sess, "automatic exit (the player did not enter the carrier)", log));
    }

    // End of the tutorial without going through the carrier: the server marks the tutorial as finished and switches the
    // client to the Nexus with the same plane (as the portal would).
    private static void LeaveAlone(Session sess, string reason, Action<string, string> log)
    {
        if (sess.Left || sess.Connection.Status != NetConnectionStatus.Connected) return;
        if (ExitToNexus == null)
        {
            log("TUTORIAL", "[tutorial] " + reason + ": no link to the Nexus (ExitToNexus is null)");
            return;
        }
        sess.Left = true;
        log("TUTORIAL", "[tutorial] " + reason + ": going to the Nexus");
        ExitToNexus(sess.Server, sess.Connection, "[tutorial] " + reason);
    }

    // Unlocks wall k: the crates start taking damage.
    private static void ReleaseGate(Session sess, int k, double now, Action<string, string> log)
    {
        if (k < 0 || k >= sess.Gates.Count) return;
        Gate gate = sess.Gates[k];
        if (gate.Damageable) return;
        gate.Damageable = true;
        Enemies.SetInvulnerable(sess.Connection, GateGroup(k), false);

        float b = gate.Sj / (float)Ground.WallsPerCell;
        Guide(sess, 0.5f, b - 0.9f, now, 6.0);
        log("TUTORIAL", "[tutorial] " + gate.Name + " unlocked");
    }

    // One destroyed crate brings the whole wall down (the others explode too).
    // Crates that did not spawn (no catalog) do not hold a damageable wall.
    private static void CheckGates(Session sess, double now, Action<string, string> log)
    {
        foreach (Gate gate in sess.Gates)
        {
            if (gate.Open) continue;
            bool anyFell = gate.Pieces.Any(p => p.ShortId != 0 && !Enemies.IsAlive(sess.Connection, p.ShortId));
            bool noCrates = gate.Pieces.All(p => p.ShortId == 0);
            if (!anyFell && !(noCrates && gate.Damageable)) continue;
            OpenGate(sess, gate, true, now);
            log("TUTORIAL", "[tutorial] " + gate.Name + " open");
            OnGateOpened(sess, gate, now, log);
        }
    }

    private static void OpenGate(Session sess, Gate gate, bool explode, double now)
    {
        gate.Damageable = true;
        gate.Open = true;
        gate.Warned = true;
        Enemies.RemoveGroup(sess.Connection, GateGroup(gate.Index), explode, now);
    }

    private static void OnGateOpened(Session sess, Gate gate, double now, Action<string, string> log)
    {
        switch (gate.Index)
        {
            case WallGate:
                // The wall fell: the corridor drones come up.
                WakeDrones2(sess, now);
                break;

            case MiddleGate:
            {
                Maps.Point guide = sess.Map.GetPoint("finalCorridorGuide");
                Guide(sess, guide.A, guide.B, now, 6.0);
                break;
            }

            case BossGate:
                // Boss gate: the instructor's line and the Large Training Drone wakes up.
                Say(sess, "newtutorialally4");     // Next up is the boss drone. You'll need to dodge and aim at the same time.
                Schedule(sess, now + 2.5, "boss", t => WakeBoss(sess, t, log));
                break;
        }
    }

}
