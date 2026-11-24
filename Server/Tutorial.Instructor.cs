using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

public static partial class Tutorial
{
    // ================================================================== checkpoints

    // The checkpoint is the furthest one the player has gone past (where the plane is born again after a fall).
    private static void UpdateCheckpoint(Session sess, float pa, float pb, double now, Action<string, string> log)
    {
        List<Maps.Checkpoint> points = sess.Map.Checkpoints;
        int fresh = sess.Checkpoint;
        for (int i = sess.Checkpoint + 1; i < points.Count; i++)
        {
            if (points[i].Passed(pa, pb)) fresh = i;
        }
        if (fresh == sess.Checkpoint) return;
        sess.Checkpoint = fresh;
        log("TUTORIAL", "[tutorial] checkpoint: " + points[fresh].Name);
    }

}
