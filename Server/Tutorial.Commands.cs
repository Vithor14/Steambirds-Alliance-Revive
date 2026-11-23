using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

public static partial class Tutorial
{
    // ================================================================== commands

    // /step N: the step of the script ("steps" of the map) marks what was left behind as done, removes it, opens the walls on the
    // way and takes the plane to the step's checkpoint.
    private static void JumpToStage(Session sess, int number, double now, Action<string, string> log)
    {
        if (!sess.Started)
        {
            Chat(sess, "Wait for the plane to spawn to jump steps.");
            return;
        }

        string? checkpointName = sess.Script.Jump(number, now);
        if (checkpointName == null)
        {
            Chat(sess, "That step does not exist in the map's script.");
            return;
        }

        Maps.Point destination = sess.Map.GetCheckpoint(checkpointName).Point;
        float destB = destination.B;
        UpdateCheckpoint(sess, destB, now, log);
        destination.World(out float x, out float y);
        WorldObjects.SendTeleport(sess.Server, sess.Connection, x, y, destination.Rotation, 0.2f);
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
