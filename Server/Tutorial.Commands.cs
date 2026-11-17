using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

public static partial class Tutorial
{
    // ================================================================== commands

    // /step N: opens the way there, marks what was left behind as done and takes the plane.
    private static void JumpToStage(Session sess, int number, double now, Action<string, string> log)
    {
        if (!sess.Started)
        {
            Chat(sess, "Wait for the plane to spawn to jump steps.");
            return;
        }

        // Destination of each step: the map's checkpoint with that name.
        var steps = new List<string> { "talk_together", "turret", "talk_bullets", "drones1", "towers_quiet" };
        string destPoint;
        switch (number)
        {
            case 1:
                destPoint = "anteroom";
                break;
            case 2:
                destPoint = "ring room";
                steps.AddRange(new[] { "drone_corridor", "drones2", "talk_cats" });
                break;
            case 3:
                destPoint = "crate corridor";
                steps.AddRange(new[] { "drone_corridor", "drones2", "talk_cats", "ring", "ring_cleared" });
                break;
            case 4:
                destPoint = "final corridor";
                steps.AddRange(new[] { "drone_corridor", "drones2", "talk_cats", "ring", "ring_cleared", "crate", "crate_open" });
                break;
            default:
                destPoint = "arena";
                steps.AddRange(new[] { "drone_corridor", "drones2", "talk_cats", "ring", "ring_cleared", "crate", "crate_open",
                    "prepare", "boss_awake" });
                break;
        }
        Maps.Point destination = sess.Map.GetCheckpoint(destPoint).Point;
        float destB = destination.B;
        foreach (string step in steps) sess.Finished.Add(step);
        sess.WaitingAtEntrance = false;
        QuietTowers(sess);

        // Whoever was left behind disappears (drones, ring, crate and boss of skipped steps) and the walls on the way fall.
        if (number >= 2)
        {
            Enemies.RemoveGroup(sess.Connection, "drones1", false, now);
            Enemies.RemoveGroup(sess.Connection, "drones2", false, now);
            if (!sess.Gates[WallGate].Open) OpenGate(sess, sess.Gates[WallGate], false, now);
        }
        if (number >= 3)
        {
            Enemies.RemoveGroup(sess.Connection, "ring", false, now);
            RequestCrate(sess, now, log);
        }
        if (number >= 4)
        {
            Enemies.RemoveGroup(sess.Connection, "crate", false, now);
            if (!sess.Gates[MiddleGate].Open) OpenGate(sess, sess.Gates[MiddleGate], false, now);
        }
        if (number >= 5)
        {
            // Straight to the exit: boss gate open, boss disappears and the portal appears.
            if (!sess.Gates[BossGate].Open) OpenGate(sess, sess.Gates[BossGate], false, now);
            Enemies.RemoveGroup(sess.Connection, "boss", false, now);
            if (!sess.BossDefeated)
            {
                sess.BossDefeated = true;
                sess.Map.GetPoint("boss").World(out sess.BossX, out sess.BossY);
                OnBossDefeated(sess, now, log);
            }
        }

        UpdateCheckpoint(sess, destB, now, log);
        destination.World(out float x, out float y);
        WorldObjects.SendTeleport(sess.Server, sess.Connection, x, y, destination.Rotation, 0.2f);
        Ground.ToWorld(0.25f, destB - 0.3f, out float ix, out float iy);
        if (sess.Instructor != 0) Enemies.SetDestination(sess.Connection, sess.Instructor, ix, iy, false);
        log("TUTORIAL", "[tutorial] /step " + number + ": plane taken to b = " + Num(destB) + " (" + Num(x) + ", " + Num(y) + ")");
    }

    // /restart: everything disappears, the floor goes back as at the start and the plane returns to the start room.
    private static void Restart(Session sess, double now, Action<string, string> log)
    {
        int unidades = Enemies.RemoveAll(sess.Connection, now);
        foreach ((int Id, float X, float Y) obj in sess.Objects)
            WorldObjects.SendRemoval(sess.Server, sess.Connection, obj.Id, obj.X, obj.Y, WorldObjects.ReasonFizzled, now);

        Session added = NewSession(sess.Server, sess.Connection, now);
        SendPlan(added, log);
        log("TUTORIAL", "[tutorial] /restart: " + unidades + " unit(s) and " + sess.Objects.Count + " object(s) removed");

        if (Game.GetPlane(added.Peer, out _, out _, out bool alive, out _) && alive)
        {
            Maps.Point start = added.Map.Checkpoints[0].Point;
            start.World(out float x, out float y);
            WorldObjects.SendTeleport(added.Server, added.Connection, x, y, start.Rotation, 0.2f);
            StartTutorial(added, now, log);
        }
    }

}
