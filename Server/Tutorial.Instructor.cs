using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

public static partial class Tutorial
{
    // ================================================================== instructor and checkpoints

    private static void Guide(Session sess, float a, float b, double now, double seconds)
    {
        Ground.ToWorld(a, b, out sess.GuideX, out sess.GuideY);
        sess.GuideUntil = now + seconds;
    }

    // The instructor waits at the hangar entrance, then follows the player and goes ahead when guiding.
    private static void GuideInstructor(Session sess, double now)
    {
        if (sess.Instructor == 0) return;
        bool destination = sess.WaitingAtEntrance || now < sess.GuideUntil;
        Enemies.SetDestination(sess.Connection, sess.Instructor, sess.GuideX, sess.GuideY, destination);
    }

    private static void UpdateCheckpoint(Session sess, float pb, double now, Action<string, string> log)
    {
        List<Maps.Checkpoint> points = sess.Map.Checkpoints;
        int fresh = sess.Checkpoint;
        for (int i = sess.Checkpoint + 1; i < points.Count; i++)
        {
            if (pb >= points[i].ActiveAtB) fresh = i;
        }
        if (fresh == sess.Checkpoint) return;
        sess.Checkpoint = fresh;
        log("TUTORIAL", "[tutorial] checkpoint: " + points[fresh].Name);
    }

}
