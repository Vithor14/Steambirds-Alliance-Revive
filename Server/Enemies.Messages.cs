using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Lidgren.Network;

// Messages the server sends to the client about enemies: spawns, updates, shots and kills.
public static partial class Enemies
{
    // ================================================================== messages to the client

    // SpawnUnitsBatch: same order as EntitySpawnStructSerializer.Write (see PlaneSpawn).
    // only: a single player that joined later (the others already have these units). resetFlags: false for that case.
    private static void SendSpawns(Instance inst, List<Enemy> added2, double now, NetConnection? only = null)
    {
        if (added2.Count == 0) return;

        for (int start = 0; start < added2.Count; start += 20)
        {
            List<Enemy> part = added2.Skip(start).Take(20).ToList();
            NetOutgoingMessage m = inst.Server.CreateMessage();
            m.Write(MsgSpawnUnitsBatch);
            m.WriteVariableUInt32((uint)(part.Count + 1));             // List: count + 1 (0 = null)
            foreach (Enemy enemy in part)
            {
                m.Write((float)now);                                  // spawnTime
                m.WriteVariableInt32(PrefabGenericUnit);                // prefabId
                m.WriteVariableInt32(enemy.ShortId);                      // shortId
                m.Write(enemy.Model.Hash);                               // templateHash
                m.Write((short)0);                                      // familyRootTemplateHash
                m.WriteVariableInt32(enemy.OwnerShortId);               // parentShortId (owner of an ally, else 0)
                m.WriteVariableInt32(enemy.Team);                        // teamIndex: 2 = aiTeam, 1 = ally
                m.Write((short)0);                                      // artReplacement
                m.Write(inst.Server.UniqueIdentifier);                // authority = server
                m.WriteVariableInt32(0);                                // null unitOptions (GenericUnit does not read options)
                WriteFields(m, enemy, true, enemy.EffectId != 0);
                if (only == null)
                {
                    enemy.EffectChanged = false;
                    enemy.HealthChanged = false;
                    enemy.LastHealthSent = now;
                }
            }
            Broadcast(inst, m, only);
        }

        if (only == null && added2.Any(i => i.Boss)) SendBossCount(inst);
    }

    // Sends the message to every connected player of the instance (or only to "only").
    private static void Broadcast(Instance inst, NetOutgoingMessage m, NetConnection? only = null)
    {
        if (only != null)
        {
            if (only.Status == NetConnectionStatus.Connected) inst.Server.SendMessage(m, only, NetDeliveryMethod.ReliableOrdered, 0);
            return;
        }
        List<NetConnection> to = inst.Members.Where(c => c.Status == NetConnectionStatus.Connected).ToList();
        if (to.Count == 0) return;
        if (to.Count == 1) inst.Server.SendMessage(m, to[0], NetDeliveryMethod.ReliableOrdered, 0);
        else inst.Server.SendMessage(m, to, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // UpdateUnitsBatch: list of (shortId, byte 0 = no own time, FieldUpdate) and the batch time at the end.
    private static void SendUpdates(Instance inst, double now)
    {
        NetOutgoingMessage m = inst.Server.CreateMessage();
        m.Write(MsgUpdateUnitsBatch);
        m.WriteVariableUInt32((uint)(inst.Enemies.Count + 1));
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            m.WriteVariableInt32(enemy.ShortId);
            m.Write((byte)0);
            bool withHealth = enemy.HealthChanged || now - enemy.LastHealthSent > 2.0;
            bool withEffect = enemy.EffectChanged;
            WriteFields(m, enemy, withHealth, withEffect);
            enemy.EffectChanged = false;
            if (withHealth)
            {
                enemy.HealthChanged = false;
                enemy.LastHealthSent = now;
            }
        }
        m.Write((float)now);
        Broadcast(inst, m);
    }

    // FieldUpdateSerializer.Write: mask, then position (x1024), velocity (x1024), rotation (x32), health (x512, fraction).
    // withEffect: also the visual effect (EFFECTSTATE1 = 8192, short id; 0 turns it off). The client reads the fields in
    // the order of FieldUpdateSerializer: position, velocity, rotation, (aim, state), health, ..., effect state 1.
    private static void WriteFields(NetBuffer m, Enemy enemy, bool withHealth, bool withEffect = false)
    {
        uint mask = 1u | 2u | 4u | (withHealth ? 8u : 0u) | (withEffect ? 8192u : 0u);
        float vx = MathF.Sin(enemy.Rot * Rad) * enemy.Vel;
        float vy = MathF.Cos(enemy.Rot * Rad) * enemy.Vel;

        m.WriteVariableUInt32(mask);
        WriteScaled(m, enemy.X, 1024f);
        WriteScaled(m, enemy.Y, 1024f);
        WriteScaled(m, vx, 1024f);
        WriteScaled(m, vy, 1024f);
        WriteScaled(m, NormalizeAngle(enemy.Rot), 32f);
        if (withHealth) WriteScaled(m, Math.Clamp(enemy.Health / enemy.MaxHp, 0f, 1f), 512f);
        if (withEffect) m.Write(enemy.EffectId);
    }

    // GunFire (62326), in the order of GunFireSpecSerializer.Write, for a unit that is not a player:
    // byte (seed 0-31 | 64 if it has a target), ownerId, time, ownerPos (x64), ownerRotation (x256), mountIndex (byte),
    // mountRotation (x256), idCounter, multiple (byte) and, with a target, targetShortId.
    // With a target the bullet becomes homing (BulletScript follows the target in a curve): only with "homingBullets".
    private static void SendShot(Instance inst, Enemy enemy, GameGun gun, float gunRotation, double now)
    {
        bool homing = enemy.Type.HomingBullets && inst.PlayerShortId != 0;
        byte seed = (byte)rng.Next(32);

        // The target is a plane. Each client knows its own plane as unit 1000.. and the others by their relay id, so with
        // several players every client gets the id it knows.
        foreach (NetConnection member in inst.Members.Where(c => c.Status == NetConnectionStatus.Connected).ToList())
        {
            int target = 0;
            if (homing)
            {
                target = member.RemoteUniqueIdentifier == inst.FocusPeer
                    ? inst.PlayerShortId
                    : PlayerRelay.RelayIdOf(inst.FocusPeer);
            }

            NetOutgoingMessage m = inst.Server.CreateMessage();
            m.Write(MsgGunFire);
            m.Write((byte)(seed | (target != 0 ? 64 : 0)));
            m.WriteVariableInt32(enemy.ShortId);
            m.Write((float)now);
            WriteScaled(m, enemy.X, 64f);
            WriteScaled(m, enemy.Y, 64f);
            WriteScaled(m, NormalizeAngle(enemy.Rot), 256f);
            m.Write((byte)Math.Clamp(gun.Index, 0, 255));
            WriteScaled(m, NormalizeAngle(gunRotation), 256f);
            m.WriteVariableInt32(enemy.BulletCounter);
            m.Write((byte)Math.Clamp(enemy.Type.DamageMultiple, 1, 10));
            if (target != 0) m.WriteVariableInt32(target);
            inst.Server.SendMessage(m, member, NetDeliveryMethod.ReliableOrdered, 0);
        }

        // Each bullet of the shot uses an id (BulletId = owner << 32 | counter).
        enemy.BulletCounter += Math.Max(1, gun.Multi);
        enemy.Shots++;
    }

    // KillUnitsBatch: (float time, shortId, KillInfo) per item. KillInfoSerializer: reason, x and y in 16 bits
    // in the +-2000 range, rotation in 8 bits in the +-180 range and the bullet result.
    private static void Remove(Instance inst, List<Enemy> dead, byte reason, byte result, double now)
    {
        if (dead.Count == 0) return;
        bool hadBoss = false;

        for (int start = 0; start < dead.Count; start += 30)
        {
            List<Enemy> part = dead.Skip(start).Take(30).ToList();
            NetOutgoingMessage m = inst.Server.CreateMessage();
            m.Write(MsgKillUnitsBatch);
            m.WriteVariableUInt32((uint)(part.Count + 1));
            foreach (Enemy enemy in part)
            {
                m.Write((float)now);
                m.WriteVariableInt32(enemy.ShortId);
                m.Write(reason);
                m.WriteRangedSingle(Math.Clamp(enemy.X, -2000f, 2000f), -2000f, 2000f, 16);
                m.WriteRangedSingle(Math.Clamp(enemy.Y, -2000f, 2000f), -2000f, 2000f, 16);
                m.WriteRangedSingle(Math.Clamp(NormalizeAngle(enemy.Rot), -180f, 180f), -180f, 180f, 8);
                m.Write(result);
                inst.Enemies.Remove(enemy.ShortId);
                if (enemy.Boss) hadBoss = true;
            }
            Broadcast(inst, m);
        }

        if (hadBoss) SendBossCount(inst);
    }

    // ClientSetNumBossesRemaining: the HUD shows how many bosses are left.
    private static void SendBossCount(Instance inst)
    {
        int bosses = inst.Enemies.Values.Count(i => i.Boss);
        if (bosses == inst.BossesSent) return;
        inst.BossesSent = bosses;
        SendBossCountTo(inst, null);
    }

    private static void SendBossCountTo(Instance inst, NetConnection? only)
    {
        NetOutgoingMessage m = inst.Server.CreateMessage();
        m.Write(MsgClientSetNumBossesRemaining);
        m.Write(inst.Enemies.Values.Count(i => i.Boss));
        Broadcast(inst, m, only);
    }

    // Game notice (waves, bosses): turned off with "chatWarnings": false.
    private static void Warn(Instance inst, string text)
    {
        if (config.ChatWarnings) Reply(inst, text);
    }

    // Speech bubble over an enemy with a key of the game's texts (ClientShowUnitChat), like the dungeon barks.
    private static void Speak(Instance inst, Enemy enemy, string barkKey)
    {
        float pick = (float)Random.Shared.NextDouble();
        foreach (NetConnection member in inst.Members.ToList())
            WorldObjects.SendSpeech(inst.Server, member, enemy.ShortId, barkKey, 5f, WorldObjects.SpeechBig, "", pick);
    }

    // ClientReceiveText with type ServerSays: it shows in the game chat.
    private static void Reply(Instance inst, string text)
    {
        NetOutgoingMessage m = inst.Server.CreateMessage();
        m.Write(MsgClientReceiveText);
        m.Write(text);
        m.Write((byte)0);
        Broadcast(inst, m);
    }

}
