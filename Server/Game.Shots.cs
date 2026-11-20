using System.Globalization;
using System.Text;
using Lidgren.Network;

public static partial class Game
{
    // ------------------------------------------------------------------ shots

    // GunFire(GunFireSpec spec): a shot of the player's plane. The client already created the bullet;
    // the server only needs to know it (and, in the future, pass it on to the other players).
    private static void HandleShot(Player j, NetBuffer msg, double now, Action<string, string> log)
    {
        CopperReader.Shot d = CopperReader.ReadShot(msg);

        j.Shots++;
        if (d.IsPlayer && j.PlaneShortId == 0) j.PlaneShortId = d.Owner;

        // The other players of the server see the shot (only the shots of the player's own plane).
        if (d.IsPlayer && d.Owner == j.PlaneShortId) PlayerRelay.PlaneShot(j.Peer, d, now);

        if (!j.BurstOpen)
        {
            j.BurstOpen = true;
            j.BurstStart = now;
            j.ShotsInBurst = 0;
            j.HitsInBurst = 0;
            j.DamageInBurst = 0;
            log("SHOT", j.Role + " burst started: " + CopperReader.ShotText(d));
        }

        j.ShotsInBurst++;
        j.LastShot = now;
    }

    // ServerReportBulletHit: the client reports that one of its bullets hit something.
    // If the target is a server enemy, Enemies takes its health off (the client only showed the number).
    // Damage applied = (damage + effectDamage) * multiple, as in BulletScript.ExplodeBulletOn.
    private static void HandleHit(Player j, NetIncomingMessage msg, double now, Action<string, string> log)
    {
        ulong bullet = msg.ReadUInt64();
        int target = msg.ReadInt32();
        int multiple = msg.ReadByte();
        float damage = msg.ReadSingle();
        int effect = msg.ReadByte();
        msg.ReadSingle(); // effectAmount
        msg.ReadSingle(); // effectTime
        float elementalDamage = msg.ReadSingle(); // effectDamage
        int owner = msg.ReadInt32();
        CopperReader.Death death = CopperReader.ReadDeath(msg);

        float totalDamage = (damage + Math.Max(0f, elementalDamage)) * Math.Max(1, multiple);

        // Damage boost of damage clouds/blasts (walker Attack Area): kept by the server, see Skills\DamageBoost.cs.
        //         // Only hits of the player's own plane (owner = shortId of the plane), like the main gun boost of the client.
        float bonus = owner == j.PlaneShortId ? DamageBoost.Multiplier(j.Peer, j.Role, j.HasPosition, j.X, j.Y, now) : 1f;
        float damageWithoutBonus = totalDamage;
        totalDamage *= bonus;

        string? onEnemy = Enemies.TakeDamage(msg.SenderConnection, target, totalDamage, now, log);

        j.Hits++;
        j.Damage += totalDamage;
        if (j.BurstOpen)
        {
            j.HitsInBurst++;
            j.DamageInBurst += totalDamage;
        }

        if (j.Hits <= 3 || j.Hits % 25 == 0)
        {
            ulong bulletOwner = bullet >> 32;
            ulong bulletNumber = bullet & 0xFFFFFFFFUL;
            string text = j.Role + " hit #" + j.Hits + ": bullet " + bulletOwner.ToString(Inv) + "#" +
                bulletNumber.ToString(Inv) + " of the unit " + owner + " on target " + target +
                ", damage " + CopperReader.Num(damage) + " x" + multiple;
            if (bonus > 1f)
                text += ", damage bonus x" + CopperReader.Num(bonus) + " (" + CopperReader.Num(damageWithoutBonus) + " -> " +
                    CopperReader.Num(totalDamage) + ")";
            if (effect != 0) text += ", effect " + CopperReader.EffectName(effect) + " " + CopperReader.Num(elementalDamage);
            text += ", result: " + CopperReader.DeathText(death);
            if (onEnemy != null) text += " | " + onEnemy;
            log("SHOT", text);
        }
    }

    // ServerReportDamageBoost(float boostFactor, float boostClock, float clientFixedTime, float clientChosenServerTime):
    //     // the client applied a damage cloud boost on its own plane, so its hit damage is already boosted.
    //     // Rewinds the message for the generic log, like HandleManeuverDamage.
    private static void HandleClientBonus(Player j, NetIncomingMessage msg, double now)
    {
        var start = msg.Position;
        try
        {
            float factor = msg.ReadSingle();
            float duration = msg.ReadSingle();
            if (factor > 1f) DamageBoost.ClientReported(j.Peer, factor, duration, now);
        }
        finally
        {
            msg.Position = start;
        }
    }

    // ServerReportUnitDamage(int damageSourceId, int targetId, int maneuverID, EffectType effect,
    // float effectAmount, float effectTime, float effectDamage, Vector3 pos): maneuver/skill damage.
    // If the target is a server enemy, the effectDamage comes off its health. At the end the read goes back to the start
    // of the arguments, so the generic log decodes the whole message.
    private static void HandleManeuverDamage(NetIncomingMessage msg, double now, Action<string, string> log)
    {
        var start = msg.Position;
        try
        {
            msg.ReadInt32();                 // damageSourceId
            int target = msg.ReadInt32();
            msg.ReadInt32();                 // maneuverID
            msg.ReadByte();                  // effect
            msg.ReadSingle();                // effectAmount
            msg.ReadSingle();                // effectTime
            float damage = msg.ReadSingle();   // effectDamage
            if (damage > 0f)
            {
                string? onEnemy = Enemies.TakeDamage(msg.SenderConnection, target, damage, now, log);
                if (onEnemy != null) log("SKILL", "maneuver damage " + CopperReader.Num(damage) + " | " + onEnemy);
            }
        }
        finally
        {
            msg.Position = start;
        }
    }

    // SlashCommand(List<string> pieces): a chat command the client does not know and sends to the server
    // (ChatManager). The enemy ones go to Enemies; the others only show in the log.
    private static void HandleCommand(NetServer server, Player j, NetIncomingMessage msg, string role, double now,
        Action<string, string> log)
    {
        var pieces = new List<string>();
        uint countPlusOne = msg.ReadVariableUInt32();
        for (uint i = 0; i + 1 < countPlusOne && i < 32 && CopperReader.BitsLeft(msg) > 0; i++)
        {
            pieces.Add(msg.ReadString() ?? "");
        }

        if (ProgressionHandler.Command(server, msg.SenderConnection, role, pieces, now, log)) return;
        if (InventoryHandler.Command(server, msg.SenderConnection, role, pieces, now, log)) return;
        // Dungeons (/dungeons, /dungeon X; inside a dungeon /room, /boss, /leave, /restart) before the tutorial,
        // which also uses /leave and /restart: DungeonServer.Command only takes those inside a dungeon.
        if (DungeonServer.Command(server, msg.SenderConnection, role, pieces, now, log)) return;
        if (Tutorial.Command(server, msg.SenderConnection, role, pieces, now, log)) return;
        if (!Enemies.Command(server, msg.SenderConnection, role, pieces, now, log))
            log("MSG", j.Role + " chat command not handled by the server: " + string.Join(" ", pieces));
    }

    // ------------------------------------------------------------------ skills

    // ServerUnitManeuver(int shortId, int maneuverHash, EquipmentSlot slot, int target, float, float).
    // The client already ran the maneuver and spent the energy. The hash is the Mono ActionID.GetHashCode().
    private static void HandleManeuver(Player j, NetBuffer msg, Action<string, string> log)
    {
        int shortId = msg.ReadInt32();
        int hash = msg.ReadInt32();
        int slot = msg.ReadByte();
        int target = msg.ReadInt32();

        j.Maneuvers++;
        LoadActions(log);
        string name = maneuvers.TryGetValue(hash, out var found) ? "'" + found + "'" : "no known name";

        string text = j.Role + " maneuver " + name + " (hash " + hash + ") of the unit " + shortId +
            " by the slot " + SlotName(slot);
        if (target != 0) text += ", target " + target;
        log("SKILL", text + Where(j));
    }

    // ServerUnitAction(int shortId, int action, Vector3 position, float rotation, int flags, string param).
    private static void HandleUnitAction(NetServer server, Player j, NetIncomingMessage msg, double now,
        Action<string, string> log)
    {
        int shortId = msg.ReadInt32();
        int action = msg.ReadInt32();
        float x = msg.ReadSingle();
        float y = msg.ReadSingle();
        msg.ReadSingle(); // z
        float rotation = msg.ReadSingle();
        int flags = msg.ReadInt32();
        string parameter = msg.ReadString() ?? "";

        if (action == ActionSuperSpeed)
        {
            // Arrives every frame while super speed is on.
            if (!j.SuperOpen)
            {
                j.SuperOpen = true;
                j.SuperStart = now;
                log("SKILL", j.Role + " super speed on for the unit " + shortId + " at (" +
                    CopperReader.Num(x) + ", " + CopperReader.Num(y) + ")");
            }
            j.LastSuper = now;
            return;
        }

        if (action == ActionDropCloud)
        {
            CreateCloud(server, j, msg.SenderConnection, shortId, parameter, x, y, rotation, now, log);
            return;
        }

        log("SKILL", j.Role + " action " + action + " of the unit " + shortId + " at (" + CopperReader.Num(x) + ", " +
            CopperReader.Num(y) + ") flags " + flags + " parameter '" + parameter + "' (action with no handling of its own)");
    }

    // Creates the cloud/blast of a skill. In the original game the server creates it: the client only sends
    // the name of the cloud, plays the sound and spends the energy.
    //
    // The cloud spawns with the server as authority (on the client it stays "submissive" and does not try to
    // call server code) and already at its final radius. The KillUnitsBatch goes out when the duration ends.
    private static void CreateCloud(NetServer server, Player j, NetConnection connection, int ownerShortId, string name,
        float x, float y, float rotation, double now, Action<string, string> log)
    {
        j.Clouds++;
        string where = " at (" + CopperReader.Num(x) + ", " + CopperReader.Num(y) + ")";

        LoadActions(log);
        if (!clouds.TryGetValue(name, out var cloud))
        {
            log("SKILL", j.Role + " cloud skill '" + name + "' used by the unit " + ownerShortId + where +
                " -> NOT created: the cloud is not in " + ActionsPath() +
                " (the file comes in the server's data folder)");
            return;
        }

        if (cloud.PrefabIndex < 0 || !cloud.Resolvable)
        {
            log("SKILL", j.Role + " cloud skill '" + name + "' used by the unit " + ownerShortId + where +
                " -> NOT created: prefab '" + cloud.Prefab + "' index " + cloud.PrefabIndex +
                (cloud.Resolvable ? "" : ", and the client does not find this cloud by the hash " + cloud.Hash));
            return;
        }

        int shortId = nextShortId++;
        float start = (float)now;
        float duration = cloud.Duration > 0f ? cloud.Duration : 5f;

        // The cloud goes to the owner and to the other players of the room that see the owner's plane. The owner id inside the
        // cloud options is the plane id of the RECEIVER (own shortId for the owner, relay id for the others).
        SendCloudSpawn(server, connection, shortId, ownerShortId, cloud, start, x, y, rotation);
        pendingDeaths.Add(new PendingDeath
        {
            When = now + duration,
            Server = server,
            Connection = connection,
            ShortId = shortId,
            X = x,
            Y = y,
            Name = name
        });
        foreach (var (observer, relayOwnerId) in PlayerRelay.Observers(connection))
        {
            SendCloudSpawn(server, observer, shortId, relayOwnerId, cloud, start, x, y, rotation);
            pendingDeaths.Add(new PendingDeath
            {
                When = now + duration,
                Server = server,
                Connection = observer,
                ShortId = shortId,
                X = x,
                Y = y,
                Name = name
            });
        }

        j.Units[shortId] = new Unit { ShortId = shortId, Prefab = cloud.PrefabIndex, X = x, Y = y, Rotation = rotation };

        // Damage clouds/blasts: damage boost for the allies inside (Skills\DamageBoost.cs).
        DamageBoost.CloudCreated(server, connection, j.Peer, j.Role, ownerShortId, name, x, y, cloud.FinalRadius, duration, now, log);

        // Poison clouds hurt the server enemies (Skills\CloudDamage.cs): on the client the cloud never reaches them.
        CloudDamage.CloudCreated(connection, name, x, y, cloud.FinalRadius, duration, now, log);

        log("SKILL", j.Role + " cloud skill '" + name + "' used by the unit " + ownerShortId + where +
            " -> created the unit " + shortId + " (prefab " + cloud.PrefabIndex + " " + cloud.Prefab +
            ", radius " + CopperReader.Num(cloud.FinalRadius) + ", lasts " + CopperReader.Num(duration) + "s" +
            (cloud.AttachedToOwner ? ", attached to the owner" : "") + ")");
    }

    // SpawnUnitsBatch of a cloud for one receiver. ownerId = the owner's plane as the receiver knows it.
    private static void SendCloudSpawn(NetServer server, NetConnection target, int shortId, int ownerId, Cloud cloud, float start,
        float x, float y, float rotation)
    {
        // CloudScript.CreateEntOptions: owner, cloud hash, start, radius modifier,
        // duration modifier and a byte that says whether it follows a unit.
        NetBuffer options = server.CreateMessage();
        options.WriteVariableInt32(ownerId);
        options.Write(cloud.Hash);
        options.Write(start);
        options.Write(1f);
        options.Write(1f);
        options.Write((byte)(cloud.AttachedToOwner ? 1 : 0));
        if (cloud.AttachedToOwner) options.WriteVariableInt32(ownerId);

        // Same order as PlaneSpawn.WritePlaneSpawn (EntitySpawnStructSerializer.Write).
        NetOutgoingMessage spawn = server.CreateMessage();
        spawn.Write(MsgSpawnUnitsBatch);
        spawn.WriteVariableUInt32(2u);                      // 1 item + 1 (0 = null list)
        spawn.Write(start);                                // spawnTime
        spawn.WriteVariableInt32(cloud.PrefabIndex);     // prefabId
        spawn.WriteVariableInt32(shortId);                  // shortId
        spawn.Write((short)0);                              // templateHash (a cloud has no UnitConfig)
        spawn.Write((short)0);                              // familyRootTemplateHash
        spawn.WriteVariableInt32(0);                        // parentShortId
        spawn.WriteVariableInt32(PlaneSpawn.PlayerTeamIndex); // the player's team
        spawn.Write((short)0);                              // artReplacement
        spawn.Write(server.UniqueIdentifier);               // authority = server
        spawn.WriteVariableInt32(options.LengthBytes + 1);   // unitOptions: bytes + 1
        spawn.Write(options.Data, 0, options.LengthBytes);   // never spawn.Write(options): loose bits would shift the rest
        spawn.WriteVariableUInt32(1u | 4u | 32768u);        // FieldUpdate: position, rotation and scale
        WriteScaled(spawn, x, 1024f);
        WriteScaled(spawn, y, 1024f);
        WriteScaled(spawn, rotation, 32f);
        WriteScaled(spawn, cloud.FinalRadius, 32f);
        server.SendMessage(spawn, target, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // The client's ScaledHelper.WriteScaled: variable integer of floor(value * scale).
    private static void WriteScaled(NetBuffer buf, float val, float scale)
    {
        buf.WriteVariableInt32((int)Math.Floor((double)val * scale));
    }

    // Sends the KillUnitsBatch of the clouds whose duration ended.
    // It cannot go with the spawn: on the client, a kill that arrives while the spawn is
    // still in the queue cancels the spawn (GameClient.ClientKillUnit -> CancelQueuedSpawnUnit).
    private static void SendDueDeaths(double now, Action<string, string> log)
    {
        for (int i = pendingDeaths.Count - 1; i >= 0; i--)
        {
            PendingDeath p = pendingDeaths[i];
            if (p.When > now) continue;

            pendingDeaths.RemoveAt(i);
            if (p.Connection.Status != NetConnectionStatus.Connected) continue;

            // ListEntityKillStructCopperSerializer + EntityKillStructSerializer + KillInfoSerializer.
            NetOutgoingMessage death = p.Server.CreateMessage();
            death.Write(MsgKillUnitsBatch);
            death.WriteVariableUInt32(2u);                       // 1 item + 1
            death.Write((float)now);                           // time
            death.WriteVariableInt32(p.ShortId);                 // shortId
            death.Write(ReasonFizzled);                          // killReason
            death.WriteRangedSingle(Math.Clamp(p.X, -2000f, 2000f), -2000f, 2000f, 16);
            death.WriteRangedSingle(Math.Clamp(p.Y, -2000f, 2000f), -2000f, 2000f, 16);
            death.WriteRangedSingle(0f, -180f, 180f, 8);         // rotation
            death.Write((byte)0);                                // bulletHitResult = None
            p.Server.SendMessage(death, p.Connection, NetDeliveryMethod.ReliableOrdered, 0);

            if (players.TryGetValue(p.Connection.RemoteUniqueIdentifier, out var owner)) owner.Units.Remove(p.ShortId);
            log("SKILL", "cloud '" + p.Name + "' (unit " + p.ShortId + ") reached the end: KillUnitsBatch sent");
        }
    }

    // Closes the burst of shots and the super speed when their messages stop arriving.
    private static void CloseSequences(Player j, double now, Action<string, string> log)
    {
        if (j.BurstOpen && now - j.LastShot > 1.0)
        {
            j.BurstOpen = false;
            double duration = j.LastShot - j.BurstStart;
            log("SHOT", j.Role + " burst ended: " + j.ShotsInBurst + " shot(s) in " +
                duration.ToString("0.0", Inv) + "s, " + j.HitsInBurst + " hit(s), damage " +
                j.DamageInBurst.ToString("0.#", Inv) + " | session: " + j.Shots + " shot(s), " + j.Hits + " hit(s)");
        }

        if (j.SuperOpen && now - j.LastSuper > 0.5)
        {
            j.SuperOpen = false;
            double duration = j.LastSuper - j.SuperStart;
            log("SKILL", j.Role + " super speed turned off after " + duration.ToString("0.0", Inv) + "s" + Where(j));
        }
    }

}
