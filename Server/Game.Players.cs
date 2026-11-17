using System.Globalization;
using System.Text;
using Lidgren.Network;

public static partial class Game
{
    // ------------------------------------------------------------------ state per player

    private static Player GetPlayer(NetConnection connection, string role, double now)
    {
        long peer = connection.RemoteUniqueIdentifier;
        if (!players.TryGetValue(peer, out var j))
        {
            j = new Player { Peer = peer, Role = "[" + role + "]", NextSummary = now + 60 };
            players[peer] = j;
        }
        return j;
    }

    private static int Tally(Player j, ushort id)
    {
        j.Count.TryGetValue(id, out int current);
        current++;
        j.Count[id] = current;
        return current;
    }

    private static string SlotName(int slot)
    {
        return CopperReader.Name(CopperReader.SlotNames, slot);
    }

    private static string PrefabName(int prefab)
    {
        return CopperReader.Name(PrefabNames, prefab);
    }

    private static string Where(Player j)
    {
        if (!j.HasPosition) return "";
        return " em (" + CopperReader.Num(j.X) + ", " + CopperReader.Num(j.Y) + ")";
    }

    // ------------------------------------------------------------------ player input

    // ServerReceivePlayerInput(ALInputAction input, float clientFixedTime, float clientChosenServerTime).
    //     // Arrives ~16 times per second. Only the changes become a log line.
    private static void HandleInput(Player j, NetBuffer msg, Action<string, string> log)
    {
        CopperReader.Entry e = CopperReader.ReadInput(msg);

        if (e.HasCheck)
        {
            j.HasPosition = true;
            j.X = e.X;
            j.Y = e.Y;
            j.Rotation = e.Rotation;
            j.Aim = e.Aim;
        }

        // Equipment buttons: one bit per EquipmentSlot. MainGun and Engine already have their own log
        // (burst and super speed), so the others show here: SecondaryAttack, Armor...
        int changed = e.SlotMask ^ j.SlotMask;
        if (changed != 0)
        {
            for (int slot = 0; slot < 16; slot++)
            {
                int bit = 1 << slot;
                if ((changed & bit) == 0) continue;
                if ((e.SlotMask & bit) == 0) continue;
                if (slot == SlotMainGun || slot == SlotEngine) continue;
                log("SKILL", j.Role + " button " + SlotName(slot) + " pressed" + Where(j));
            }
            j.SlotMask = e.SlotMask;
        }

        if (e.State != j.State)
        {
            log("SKILL", j.Role + " plane state: " + CopperReader.Name(CopperReader.StateNames, j.State) +
                " -> " + CopperReader.Name(CopperReader.StateNames, e.State) + Where(j));
            j.State = e.State;
        }
    }

}
