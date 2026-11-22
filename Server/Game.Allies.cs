using Lidgren.Network;

public static partial class Game
{
    // ServerSpawnGreenTeamAlly(int ownerShortID, string allyTemplate, int allyCount, Vector3 spawnOrigin, float ownerHeading,
    // float spawnAngle, float spawnDist, bool isSpawnRelativeToOwnerHeading): the engineer's turret, the raptor's mines...
    // The client already spent the energy and started the cooldown; the unit itself comes from the server (Enemies.Allies.cs).
    private static void HandleSpawnAlly(Player j, NetIncomingMessage msg, double now, Action<string, string> log)
    {
        int ownerShortId = msg.ReadInt32();
        string template = msg.ReadString() ?? "";
        int count = msg.ReadInt32();
        float x = msg.ReadSingle();
        float y = msg.ReadSingle();
        msg.ReadSingle(); // z
        float heading = msg.ReadSingle();
        float angle = msg.ReadSingle();
        float distance = msg.ReadSingle();
        bool relative = msg.ReadBoolean();

        int created = Enemies.SpawnAllies(msg.SenderConnection, ownerShortId, template, count, x, y, heading, angle, distance, relative, now, log);
        log("SKILL", j.Role + " ally '" + template + "' x" + count + " requested by the unit " + ownerShortId + " at (" +
            CopperReader.Num(x) + ", " + CopperReader.Num(y) + ") -> " + created + " created");
    }
}
