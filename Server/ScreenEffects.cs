using Lidgren.Network;

// Small effects the server asks the client to show: sparks where something took damage, camera shake, interface effects and the
// end of a bullet that already hit something.
//
//  - ClientShowDamage (29463): the client plays its "spark" particle at the place (KillInfo position and rotation).
//  - ClientCameraShake (502): CameraScript.Shake(level, seconds).
//  - ClientShowUIEffect (46628): only "mission_accomplished" does something in this client (the mission complete banner).
//  - ClientKillBullet (8267): ends a bullet of the client (BulletId) as if it had hit, at the given place.
//
// The host turns each one on or off, and tunes the shake, in enemies.json ("effects").
public static class ScreenEffects
{
    public const ushort MsgClientCameraShake = 502;
    public const ushort MsgClientKillBullet = 8267;
    public const ushort MsgClientShowDamage = 29463;
    public const ushort MsgClientShowUIEffect = 46628;

    private const byte KillReasonBulletDestroyed = 3;   // KillReason.BulletDestroyed

    public sealed class Settings
    {
        // Sparks at the enemy hit by a turret or mine of a player (those bullets are not simulated by a client).
        public bool DamageSparks { get; set; } = true;
        // The bullet of another player ends where it hit, on every client that sees it (they do not know the enemies of the server as targets).
        public bool RemoteBulletKill { get; set; } = true;
        // The mission complete banner when a dungeon is cleared.
        public bool MissionAccomplished { get; set; } = true;
        // The camera shakes when a boss dies and when a dungeon is cleared. 0 turns it off.
        public float BossDeathShakeLevel { get; set; } = 0.8f;
        public float BossDeathShakeSeconds { get; set; } = 0.9f;
    }

    private static Settings Current => Enemies.Effects;

    public static void CameraShake(NetServer server, NetConnection? connection, float level, float seconds)
    {
        if (level <= 0f || seconds <= 0f || connection == null || connection.Status != NetConnectionStatus.Connected) return;
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientCameraShake);
        m.Write(level);
        m.Write(seconds);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    public static void BossDeathShake(NetServer server, NetConnection? connection)
    {
        CameraShake(server, connection, Current.BossDeathShakeLevel, Current.BossDeathShakeSeconds);
    }

    public static void UiEffect(NetServer server, NetConnection? connection, string name)
    {
        if (connection == null || connection.Status != NetConnectionStatus.Connected) return;
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientShowUIEffect);
        m.Write(name);
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    public static void MissionAccomplished(NetServer server, NetConnection? connection)
    {
        if (Current.MissionAccomplished) UiEffect(server, connection, "mission_accomplished");
    }

    // Spark at (x, y). rotation: degrees, the way the spark points.
    public static void ShowDamage(NetServer server, NetConnection? connection, float x, float y, float rotation = 0f)
    {
        if (!Current.DamageSparks || connection == null || connection.Status != NetConnectionStatus.Connected) return;
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientShowDamage);
        WriteKillInfo(m, KillReasonBulletDestroyed, x, y, rotation, 1);
        m.Write(0f);                                   // time: now
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // The bullet ends at (x, y) on this client. result: BulletHitResult (1 = damage, 2 = shield...).
    public static void KillBullet(NetServer server, NetConnection connection, ulong bulletId, float x, float y, float rotation, int result)
    {
        if (!Current.RemoteBulletKill || connection.Status != NetConnectionStatus.Connected) return;
        NetOutgoingMessage m = server.CreateMessage();
        m.Write(MsgClientKillBullet);
        m.Write(bulletId);
        WriteKillInfo(m, KillReasonBulletDestroyed, x, y, rotation, result);
        m.Write(0f);                                   // time: now (the client uses its own server clock)
        server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
    }

    // KillInfoSerializer: reason, then x and y in 16 bits (+-2000), rotation in 8 bits (+-180) and the hit result.
    private static void WriteKillInfo(NetBuffer m, byte reason, float x, float y, float rotation, int result)
    {
        m.Write(reason);
        m.WriteRangedSingle(Math.Clamp(x, -2000f, 2000f), -2000f, 2000f, 16);
        m.WriteRangedSingle(Math.Clamp(y, -2000f, 2000f), -2000f, 2000f, 16);
        float r = rotation % 360f;
        if (r > 180f) r -= 360f;
        if (r < -180f) r += 360f;
        m.WriteRangedSingle(Math.Clamp(r, -180f, 180f), -180f, 180f, 8);
        m.Write((byte)result);
    }
}
