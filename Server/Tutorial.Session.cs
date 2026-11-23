using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

public static partial class Tutorial
{
    // ================================================================== session and plan

    private static Session NewSession(NetServer server, NetConnection connection, double now)
    {
        var sess = new Session
        {
            Peer = connection.RemoteUniqueIdentifier,
            Server = server,
            Connection = connection,
            StartAt = now,
            NextTick = now,
            Map = MapForSession()
        };
        BuildPlan(sess);
        sess.Script = new MapScriptRunner(new Host(sess));
        sessions[sess.Peer] = sess;
        return sess;
    }

    // Floor and walls of the map's drawing and the crate walls of the gates.
    private static void BuildPlan(Session sess)
    {
        Maps.Map map = sess.Map;
        Ground.FloorPlan plan = map.CreatePlan();

        // Crate walls: one crate per free sub-cell of the row, at its center. The floor stays free
        // underneath (the wall collider comes from the unit itself); without that the wall pillar would swallow the crate.
        sess.Gates.Clear();
        for (int k = 0; k < map.Gates.Count; k++)
        {
            Maps.Gate def = map.Gates[k];
            var gate = new Gate { Index = k, Name = def.Name, Type = def.Type, Sj = def.Sj };
            for (int si = def.SiFrom; si <= def.SiTo; si++)
            {
                if (!plan.SubIsFree(si, def.Sj)) continue;
                float a = (si + 0.5f) / Ground.WallsPerCell;
                float b = (def.Sj + 0.5f) / Ground.WallsPerCell;
                Ground.ToWorld(a, b, out float x, out float y);
                gate.Pieces.Add(new Piece { Si = si, Sj = def.Sj, X = x, Y = y });
            }
            sess.Gates.Add(gate);
        }
        sess.FloorPlan = plan;
    }

    private static void SendPlan(Session sess, Action<string, string> log)
    {
        NetOutgoingMessage m = sess.Server.CreateMessage();
        sess.Map.WriteFloor(m, sess.FloorPlan, out string summary);
        int bytes = m.LengthBytes;
        sess.Server.SendMessage(m, sess.Connection, NetDeliveryMethod.ReliableOrdered, 0);
        log("FLOOR", "[tutorial] ClientReceiveTerrainTiles sent (" + bytes + " bytes, " + sess.Map.Name + ": " + summary + ")");
    }

}
