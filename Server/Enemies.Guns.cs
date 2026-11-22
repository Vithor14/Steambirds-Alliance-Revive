using Lidgren.Network;
using SteambirdsAi;

// Visual rotation of the turrets of the enemies (FieldUpdate FIELDMASK_GUNS = 4096).
//
// The client moves the barrel of a gun mount toward the target angle it receives (GunMountInfo.SyncTargetAngle: the mount's own
// angle, relative to the front of the unit; the client slews it at the mount's slewRate). The server's AI only fired the bullets, so
// the turrets of enemies stayed still. Now the update carries, for each mount whose angle changed, the data of UnitScript.SyncGuns:
//   VLI mount index, VLI reload clock x64, VLI angle x32, ... and VLI -1 at the end.
// (VLI: sign bit and 6 bits in the first byte, 7 bits and a continuation bit in the next ones, EncodingWriter.WriteVLI.)
// Aimed guns follow the player while the unit is awake; the others show the angle of their last shot and go back to rest after a second.
public static partial class Enemies
{
    private const float GunAimTolerance = 2f;       // degrees: smaller changes are not sent
    private const double GunAimHold = 1.0;          // seconds a fired angle stays before the gun goes back to rest

    // The bytes of FIELDMASK_GUNS for this enemy now, or null when no gun needs a new angle.
    private static byte[]? BuildGunSignal(Instance inst, Enemy enemy, double now)
    {
        if (enemy.Team != EnemyTeam || enemy.Guns.Count == 0) return null;

        MemoryStream? stream = null;
        foreach (GunState gun in enemy.Guns)
        {
            GameGun m = gun.Model;
            if (m.Damage <= 0f || !GunAllowed(enemy, m.Index)) continue;

            float desired = DesiredGunAngle(inst, enemy, gun, now);
            float sent = float.IsNaN(gun.SentAim) ? m.Angle : gun.SentAim;
            if (Math.Abs(AngleDifference(sent, desired)) < GunAimTolerance) continue;

            stream ??= new MemoryStream();
            WriteVli(stream, m.Index);
            WriteVli(stream, (int)MathF.Floor((float)Math.Max(0.0, Math.Max(gun.NextShot, gun.ReloadUntil) - now) * 64f));
            WriteVli(stream, (int)MathF.Floor(NormalizeAngle(desired) * 32f));
            gun.SentAim = desired;
        }
        if (stream == null) return null;
        WriteVli(stream, -1);
        return stream.ToArray();
    }

    private static float DesiredGunAngle(Instance inst, Enemy enemy, GunState gun, double now)
    {
        GameGun m = gun.Model;
        bool awake = enemy.Aggro && inst.HasPlayer && inst.PlayerAlive && enemy.Behavior != "idle";

        // Guns that aim: follow the player while inside the arc.
        if (m.Mode == 0 && awake && !(enemy.Behavior == "fixed" && enemy.Ai == null))
        {
            float relative = AngleDifference(enemy.Rot, Heading(enemy.X, enemy.Y, inst.PlayerX, inst.PlayerY));
            float turn = gun.Turn;
            if (AiGunAim.TryAim(0, m.Angle, m.Arc, m.Burst, gun.Ammo, ref turn, enemy.Type.FreeAim, relative, now, out float rotation))
                return rotation;
            return m.Angle;
        }

        // Patterns (arcs, hoses, sprinklers): the angle of the last shot, for a moment.
        if (!float.IsNaN(gun.Aim) && now - gun.AimAt < GunAimHold) return gun.Aim;
        return m.Angle;
    }

    // EncodingWriter.WriteVLI.
    private static void WriteVli(MemoryStream stream, int value)
    {
        byte b = 0;
        byte mask = 63;
        int shift = 6;
        uint magnitude;
        if (value < 0)
        {
            b = 64;
            magnitude = (uint)(-value);
        }
        else
        {
            magnitude = (uint)value;
        }
        do
        {
            b |= (byte)(magnitude & mask);
            magnitude >>= shift;
            mask = 127;
            shift = 7;
            if (magnitude != 0) b |= 0x80;
            stream.WriteByte(b);
            b = 0;
        }
        while (magnitude != 0);
    }
}
