using System;
using Lidgren.Network;

public static class PlaneSpawn
{
    // ID of the SpawnUnitsBatch message (GeneratedDispatcher.Pack_SpawnUnitsBatch).
    public const ushort MsgSpawnUnitsBatch = 45862;

    // Index of the "playerPlane" prefab (prefab_table.txt: playerPlanePrefabIndex=0).
    public const int PlayerPlanePrefabId = 0;

    // GetTemplateHash("playerPlane") on the client (runtime_values.txt).
    // Default only: each class flies with its own template (planeName), see Planes\PlayerPlaneTemplates.cs.
    public const short PlayerPlaneTemplateHash = 32607;

    // playerTeam.teamIndex on the client (runtime_values.txt).
    public const int PlayerTeamIndex = 0;

    // Id of the unit in the world. Any positive integer that is not in use.
    public const int PlayerPlaneShortId = 1000;

    // Ids the player's plane can use (1000..1999). Changing planes in the hangar uses the next id:
    // the old plane and the new one arrive together and the client discards a spawn whose shortId is still alive
    // (EntityHandler.SpawnFromQueue).
    public const int LastPlayerPlaneShortId = 1999;

    // KillUnitsBatch (GeneratedDispatcher.Pack_KillUnitsBatch) e KillReason.ChangePlane.
    public const ushort MsgKillUnitsBatch = 45863;
    public const byte ReasonPlaneSwap = 16;

    // Next id for the new plane of a hangar swap.
    public static int NextPlaneShortId(int current)
    {
        return current >= PlayerPlaneShortId && current < LastPlayerPlaneShortId ? current + 1 : PlayerPlaneShortId;
    }

    // KillUnitsBatch with one item: the player's plane leaves the world because they swapped planes in the hangar
    // (KillReason.ChangePlane). On the client this is NOT a death: GameClient.OnPlayerPlaneDied does not take the plane
    // out of the hangar or open the death screen.
    // Order: ListEntityKillStructCopperSerializer (Count + 1), EntityKillStructSerializer (float time,
    // varint shortId) and KillInfoSerializer (reason, x and y in 16 bits, rotation in 8 bits, BulletHitResult).
    public static void WritePlaneSwap(NetBuffer buf, float time, int shortId, float x, float y, float rotation)
    {
        buf.Write(MsgKillUnitsBatch);
        buf.WriteVariableUInt32(2u);            // 1 item + 1
        buf.Write(time);
        buf.WriteVariableInt32(shortId);
        buf.Write(ReasonPlaneSwap);
        buf.WriteRangedSingle(Math.Clamp(x, -2000f, 2000f), -2000f, 2000f, 16);
        buf.WriteRangedSingle(Math.Clamp(y, -2000f, 2000f), -2000f, 2000f, 16);
        float rot = rotation;
        while (rot > 180f) rot -= 360f;
        while (rot < -180f) rot += 360f;
        buf.WriteRangedSingle(rot, -180f, 180f, 8);
        buf.Write((byte)0);                     // BulletHitResult.None
    }

    // Writes the SpawnUnitsBatch message with a single item: the player's plane.
    // options: buffer of PlayerData.WritePlaneOptions (unitOptions). Null = no options.
    // shortId: PlayerPlaneShortId, or the id of NextPlaneShortId on a plane swap.
    // templateHash: UnitConfig of the plane (PlayerPlaneTemplates.ForClass). It decides how the plane flies and aims:
    // walkerClassPlane = 8 directions + guns follow the mouse; playerPlane = always forward + guns forward.
    // Writing order equal to ListEntitySpawnStructCopperSerializer.Write, EntitySpawnStructSerializer.Write,
    // NetBufferCopperSerializer.Write and FieldUpdateSerializer.Write.
    //
    // WARNING: two counts of this message follow the Copper "value + 1" rule (0 = null):
    //  - the list sends Count + 1. Sending 1 becomes an EMPTY list: the client logs "items=0",
    //    the plane does not spawn and the camera has no target (black screen).
    //  - unitOptions sends LengthBytes + 1. Sending only the size makes the client read one byte less
    //    (it loses the decoration byte and the rest of the message comes out misaligned).
    public static void WritePlaneSpawn(NetBuffer buf, long playerAuthority, float spawnTime, float x, float y, float rotation,
        NetBuffer? options = null, int shortId = PlayerPlaneShortId, short templateHash = PlayerPlaneTemplateHash)
    {
        const int itemCount = 1;

        buf.Write(MsgSpawnUnitsBatch);          // ushort: message ID
        buf.WriteVariableUInt32((uint)(itemCount + 1)); // List<T>: Count + 1 (0 = null list)

        buf.Write(spawnTime);                   // float spawnTime
        buf.WriteVariableInt32(PlayerPlanePrefabId);  // int prefabId
        buf.WriteVariableInt32(shortId);        // int shortId
        buf.Write(templateHash);                // short templateHash (planeName of the class)
        buf.Write((short)0);                    // short familyRootTemplateHash
        buf.WriteVariableInt32(0);              // int parentShortId (0 = none)
        buf.WriteVariableInt32(PlayerTeamIndex);        // int teamIndex
        buf.Write((short)0);                    // short artReplacement
        buf.Write(playerAuthority);             // PeerId: Lidgren long (8 bytes)

        // unitOptions (NetBufferCopperSerializer): LengthBytes + 1, then the bytes. 0 = null.
        if (options == null)
        {
            buf.WriteVariableInt32(0);
        }
        else
        {
            buf.WriteVariableInt32(options.LengthBytes + 1);
            buf.Write(options);                  // the LengthBytes of the options buffer
        }

        // FieldUpdate: POS (1) + ROT (4) mask. Order: position, then rotation.
        buf.WriteVariableUInt32(1u | 4u);
        WriteScaled(buf, x, 1024f);
        WriteScaled(buf, y, 1024f);
        WriteScaled(buf, rotation, 32f);
    }

    // The client's ScaledHelper.WriteScaled: variable integer of floor(value * scale).
    private static void WriteScaled(NetBuffer buf, float val, float scale)
    {
        buf.WriteVariableInt32((int)Math.Floor((double)val * scale));
    }
}
