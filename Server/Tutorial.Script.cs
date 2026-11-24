using System.Globalization;
using System.Net;
using Lidgren.Network;
using Map = MapInfo;

public static partial class Tutorial
{
    // ================================================================== start and tick

    // Start: the crate walls, then the script of the map (maps\tutorial_walker.json, "script"): the instructor, turrets,
    // drones and everything else appear in their places, waiting, and the rules take over.
    private static void StartTutorial(Session sess, double now, Action<string, string> log)
    {
        sess.Started = true;
        Maps.Map map = sess.Map;

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

        // The floor signs come in the ClientMapInfo (BuildMapInfo): the client creates them by itself.
        sess.Script.Start(now);
        log("TUTORIAL", "[tutorial] step 0 (" + sess.Script.StageName() + "): " + crates + " crate(s) in " + sess.Gates.Count + " wall(s), " +
            map.Signs.Count + " floor sign(s) (through the ClientMapInfo)");
    }

    private static void UpdateSession(Session sess, double now, Action<string, string> log)
    {
        if (!sess.Started) return;

        RunPending(sess, now);
        CheckGates(sess, now, log);

        if (Game.GetPlane(sess.Peer, out float px, out float py, out bool alive, out _) && alive)
        {
            Ground.ToGrid(px, py, out float pa, out float pb);
            UpdateCheckpoint(sess, pa, pb, now, log);
        }

        sess.Script.Update(now);
    }

    // ================================================================== exit

    // Exit portal (prefab 11, default art: the carrier) to the Nexus, at (a, b).
    private static void CreatePortal(Session sess, float a, float b, double now, Action<string, string> log, double autoExitSeconds)
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
            Num(a) + ", " + Num(b) + ") -> Nexus " + portalDestination + "; automatic exit in " + Num((float)autoExitSeconds) + " s");
        Schedule(sess, now + autoExitSeconds, "automatic exit", _ => LeaveAlone(sess, "automatic exit (the player did not enter the carrier)", log));
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

    // ================================================================== gates

    private static Gate? FindGate(Session sess, string name)
    {
        return sess.Gates.FirstOrDefault(g => string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    // Unlocks a gate: the crates start taking damage.
    private static void UnlockGate(Session sess, Gate gate, double now, Action<string, string> log)
    {
        if (gate.Damageable) return;
        gate.Damageable = true;
        Enemies.SetInvulnerable(sess.Connection, GateGroup(gate.Index), false);
        log("TUTORIAL", "[tutorial] " + gate.Name + " unlocked");
    }

    // One destroyed crate brings the whole wall down (the others explode too).
    // Crates that did not spawn (no catalog) do not hold a damageable wall. What the opening starts is up to the script
    // ("gateOpen" conditions).
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
        }
    }

    private static void OpenGate(Session sess, Gate gate, bool explode, double now)
    {
        gate.Damageable = true;
        gate.Open = true;
        Enemies.RemoveGroup(sess.Connection, GateGroup(gate.Index), explode, now);
    }
}
