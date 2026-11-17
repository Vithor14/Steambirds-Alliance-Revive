using System.Globalization;
using System.Text;
using Lidgren.Network;

public static partial class Game
{
    // ------------------------------------------------------------------ client units

    // SpawnUnitsBatch coming from the client: the units it has authority over (its own plane).
    // IncrementalMessagePacker: a byte with (items + 1) and the whole items.
    private static void HandleSpawnBatch(Player j, NetBuffer msg, Action<string, string> log)
    {
        uint countPlusOne = msg.ReadVariableUInt32();
        for (uint i = 0; i + 1 < countPlusOne && CopperReader.BitsLeft(msg) > 0; i++)
        {
            CopperReader.Spawn n = CopperReader.ReadSpawn(msg);

            var u = new Unit { ShortId = n.ShortId, Prefab = n.Prefab, Template = n.Template, Team = n.Team };
            ApplyFields(u, n.Fields);
            j.Units[n.ShortId] = u;

            bool isThePlane = n.Prefab == PlaneSpawn.PlayerPlanePrefabId && n.Authority == j.Peer;
            if (isThePlane)
            {
                j.PlaneShortId = n.ShortId;
                j.PlaneAlive = true;
                ApplyToPlayer(j, n.Fields);
            }

            log("UNIT", j.Role + " client confirmed the unit " + n.ShortId + " (prefab " + n.Prefab + " " +
                PrefabName(n.Prefab) + ", template " + n.Template + ", time " + n.Team + ", options " +
                n.OptionBytes + "B)" + (isThePlane ? " = player's plane" : "") + ": " + CopperReader.FieldsText(n.Fields));
        }
    }

    // UpdateUnitsBatch coming from the client: position, health and energy of its units, several times per second.
    // Each item: shortId, a "has its own time" byte (+ float), FieldUpdate. At the end of the message comes the batch time.
    private static void HandleUpdateBatch(Player j, NetBuffer msg)
    {
        uint countPlusOne = msg.ReadVariableUInt32();
        for (uint i = 0; i + 1 < countPlusOne && CopperReader.BitsLeft(msg) > 0; i++)
        {
            int shortId = msg.ReadVariableInt32();
            if (msg.ReadByte() > 0) msg.ReadSingle();
            CopperReader.Fields fields = CopperReader.ReadFields(msg);

            if (!j.Units.TryGetValue(shortId, out var u))
            {
                u = new Unit { ShortId = shortId, Prefab = -1 };
                j.Units[shortId] = u;
            }
            ApplyFields(u, fields);
            if (shortId == j.PlaneShortId) ApplyToPlayer(j, fields);
        }
    }

    // KillUnitsBatch coming from the client: its units that died. Each item: float time, shortId, KillInfo.
    private static void HandleDeathBatch(NetServer server, Player j, NetIncomingMessage msg, Action<string, string> log)
    {
        uint countPlusOne = msg.ReadVariableUInt32();
        for (uint i = 0; i + 1 < countPlusOne && CopperReader.BitsLeft(msg) > 0; i++)
        {
            msg.ReadSingle(); // time
            int shortId = msg.ReadVariableInt32();
            CopperReader.Death death = CopperReader.ReadDeath(msg);
            RecordDeath(server, msg.SenderConnection, j, shortId, death.Reason, CopperReader.DeathText(death), "KillUnitsBatch", log);
        }
    }

    // ServerCreateUnit: the client created a unit by itself and tells the server.
    private static void HandleUnitCreation(Player j, NetBuffer msg, Action<string, string> log)
    {
        int prefab = msg.ReadInt32();
        short template = msg.ReadInt16();
        int shortId = msg.ReadInt32();
        msg.ReadInt64(); // playerAuthority
        int team = msg.ReadInt32();
        int optionBytes = CopperReader.SkipBuffer(msg);
        CopperReader.Fields fields = CopperReader.ReadFields(msg);

        var u = new Unit { ShortId = shortId, Prefab = prefab, Template = template, Team = team };
        ApplyFields(u, fields);
        j.Units[shortId] = u;

        log("UNIT", j.Role + " client created the unit " + shortId + " (prefab " + prefab + " " + PrefabName(prefab) +
            ", template " + template + ", time " + team + ", options " + optionBytes + "B): " + CopperReader.FieldsText(fields));
    }

    // ServerRequestKill(int shortId, int reason, float posX, float posY): the client asks for the death of a unit.
    // For its own plane it already did the local death; here the server only updates what it knows.
    private static void HandleDeathRequest(NetServer server, Player j, NetIncomingMessage msg, Action<string, string> log)
    {
        int shortId = msg.ReadInt32();
        int reason = msg.ReadInt32();
        float x = msg.ReadSingle();
        float y = msg.ReadSingle();
        string text = CopperReader.Name(CopperReader.DeathReasonNames, reason) + " em (" +
            CopperReader.Num(x) + ", " + CopperReader.Num(y) + ")";
        RecordDeath(server, msg.SenderConnection, j, shortId, reason, text, "ServerRequestKill", log);
    }

    // KillReasons that really destroy (KillInfo.reasonIsDestroyed): Destroyed (2) up to DestroyedFromDamage (10)
    // and NoDamage (15). OutOfView, Fizzled, Disconnected, OffMap, Stale and ChangePlane do not count.
    private static bool ReasonDestroy(int reason)
    {
        return (reason >= 2 && reason <= 10) || reason == 15;
    }

    private static void RecordDeath(NetServer server, NetConnection connection, Player j, int shortId, int code, string reason,
        string origin, Action<string, string> log)
    {
        j.Units.Remove(shortId);
        if (shortId == j.PlaneShortId)
        {
            // KillUnitsBatch and ServerRequestKill usually both arrive for the same death: only the first counts.
            bool wasAlive = j.PlaneAlive;
            j.PlaneAlive = false;
            log("PLANE", j.Role + " player's plane (unit " + shortId + ") destroyed: " + reason + " [" + origin + "]" +
                (wasAlive ? "" : " (was already dead)"));

            // A real death: plane XP becomes pilot XP, the death screen goes out and the plane leaves the account
            // (GameClient.OnPlayerPlaneDied also takes the plane out of the PlayerData). Plane swaps and disconnects do not count.
            if (wasAlive && ReasonDestroy(code))
            {
                PilotProgression.PlaneDestroyed(server, connection, j.Role.Trim('[', ']'), j.CharacterId, j.Class);
                // Items of the plane: with permadeath the cargo and the equipped items are lost (items.json says what is saved).
                InventoryHandler.PlaneDestroyed(server, connection, j.Role.Trim('[', ']'), j.CharacterId);
            }
        }
        else
        {
            log("UNIT", j.Role + " unit " + shortId + " died: " + reason + " [" + origin + "]");
        }
    }

    private static void ApplyFields(Unit u, CopperReader.Fields c)
    {
        if (c.Has(1u))
        {
            u.X = c.X;
            u.Y = c.Y;
        }
        if (c.Has(4u)) u.Rotation = c.Rotation;
        if (c.Has(8u))
        {
            u.Health = c.Health;
            u.HasHealth = true;
        }
        if (c.Has(16u))
        {
            u.Energy = c.Energy;
            u.HasEnergy = true;
        }
    }

    private static void ApplyToPlayer(Player j, CopperReader.Fields c)
    {
        if (c.Has(1u))
        {
            j.HasPosition = true;
            j.X = c.X;
            j.Y = c.Y;
        }
        if (c.Has(4u)) j.Rotation = c.Rotation;
        if (c.Has(64u)) j.Aim = c.Aim;
        if (c.Has(8u))
        {
            j.Health = c.Health;
            j.HasHealth = true;
        }
        if (c.Has(16u))
        {
            j.Energy = c.Energy;
            j.HasEnergy = true;
        }
    }

    // ------------------------------------------------------------------ summary

    // One line per minute per player: what arrived the most and how the plane is.
    private static void Summarize(Player j, double now, Action<string, string> log)
    {
        if (now < j.NextSummary) return;
        j.NextSummary = now + 60;
        log("SUMMARY", j.Role + " " + SummaryText(j));
    }

    private static string SummaryText(Player j)
    {
        var sb = new StringBuilder();

        sb.Append("messages: ");
        bool first = true;
        foreach (var pair in j.Count.OrderByDescending(p => p.Value).Take(8))
        {
            if (!first) sb.Append(", ");
            first = false;
            string name = MessageCatalog.ById.TryGetValue(pair.Key, out var info) ? info.Name : pair.Key.ToString(Inv);
            sb.Append(name).Append(" x").Append(pair.Value);
        }
        if (first) sb.Append("none");

        sb.Append(" | ");
        if (j.PlaneShortId == 0)
        {
            sb.Append("no plane");
        }
        else
        {
            sb.Append("plane ").Append(j.PlaneShortId).Append(j.PlaneAlive ? "" : " (destroyed)");
            if (j.HasPosition) sb.Append(" em (").Append(CopperReader.Num(j.X)).Append(", ").Append(CopperReader.Num(j.Y)).Append(')');
            if (j.HasHealth) sb.Append(" health ").Append(CopperReader.Num(j.Health));
            if (j.HasEnergy) sb.Append(" energy ").Append(CopperReader.Num(j.Energy));
        }

        sb.Append(" | ").Append(j.Shots).Append(" shot(s), ");
        sb.Append(j.Hits).Append(" hit(s), ");
        sb.Append(j.Maneuvers).Append(" maneuver(s), ");
        sb.Append(j.Clouds).Append(" cloud(s), ");
        sb.Append(j.Stores).Append(" store(s), ");
        sb.Append(j.Rpcs).Append(" RPC(s)");
        return sb.ToString();
    }

}
