using System.Globalization;
using Lidgren.Network;

// A death wreck: the unit that died is removed as usual, and a still, invulnerable stand-in plays instant effects
// (ClientInstantEffect) until its time runs out, then it is destroyed so the client plays the explosion.
public static partial class Enemies
{
    private const float DefaultWreckSeconds = 6f;
    private const ushort MsgClientInstantEffect = 58535;

    private static readonly string[] DefaultDeathSequence =
    {
        "0 explosionBossInitial", "0 explosionBossDebris", "0 deathAnimation",
        "0.3 epicDeath1", "2.1 epicDeath2", "3.9 epicDeath4", "5.1 epicDeath3"
    };

    private static void LeaveWreck(Instance inst, Enemy dead, double now, Action<string, string> log)
    {
        string unit = (dead.Type.DeathUnit ?? "").Trim();
        if (unit.Length == 0) return;
        EnemyType? type = FindSummonType(dead, unit, out string key);
        if (type == null)
        {
            key = "wreck:" + unit;
            type = GetEnemyType(key);
            if (type == null)
            {
                type = new EnemyType { Name = unit, Template = unit, Behavior = "idle", Invulnerable = true, Ai = "" };
                RegisterDefaultType(key, type);
            }
        }
        Enemy? wreck = CreateEnemy(inst, key, type, dead.X, dead.Y, false, now, log);
        if (wreck == null) return;
        wreck.Rot = dead.Rot;
        wreck.Vel = 0f;
        wreck.Wreck = true;
        wreck.Invulnerable = true;
        wreck.Aggro = true;
        float seconds = dead.Type.DeathSeconds > 0f ? dead.Type.DeathSeconds : DefaultWreckSeconds;
        wreck.ExpiresAt = now + seconds;
        if (dead.Type.DeathState != 0) wreck.State = dead.Type.DeathState;
        string[] lines = dead.Type.DeathSequence != null && dead.Type.DeathSequence.Length > 0
            ? dead.Type.DeathSequence : DefaultDeathSequence;
        QueueEffects(wreck, lines, now);
        if (!string.IsNullOrWhiteSpace(dead.Type.DeathEffect)) QueueEffects(wreck, new[] { "0 " + dead.Type.DeathEffect.Trim() }, now);
        SendSpawns(inst, new List<Enemy> { wreck }, now);
    }

    // A unit that is already in the room (a dungeon *DEATH script, or /wreck) plays the sequence in place.
    public static void StartDeathSequence(NetConnection connection, int shortId, double now)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (!inst.Enemies.TryGetValue(shortId, out Enemy? enemy)) return;
            enemy.Wreck = true;
            enemy.Vel = 0f;
            enemy.Invulnerable = true;
            float seconds = enemy.Type.DeathSeconds > 0f ? enemy.Type.DeathSeconds : DefaultWreckSeconds;
            if (enemy.ExpiresAt <= 0) enemy.ExpiresAt = now + seconds;
            string[] lines = enemy.Type.DeathSequence != null && enemy.Type.DeathSequence.Length > 0
                ? enemy.Type.DeathSequence : DefaultDeathSequence;
            QueueEffects(enemy, lines, now);
        }
    }

    private static void QueueEffects(Enemy enemy, IEnumerable<string> lines, double now)
    {
        foreach (string line in lines)
        {
            string text = (line ?? "").Trim();
            if (text.Length == 0) continue;
            int space = text.IndexOf(' ');
            string effect = space < 0 ? text : text.Substring(space + 1).Trim();
            float delay = 0f;
            if (space > 0) float.TryParse(text.Substring(0, space), NumberStyles.Float, CultureInfo.InvariantCulture, out delay);
            if (effect.Length == 0 || effect.Equals("none", StringComparison.OrdinalIgnoreCase)) continue;
            (enemy.PendingEffects ??= new List<(double At, string Effect)>()).Add((now + Math.Max(0f, delay), effect));
        }
    }

    private static void SendDueEffects(Instance inst, double now)
    {
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (enemy.PendingEffects == null || enemy.PendingEffects.Count == 0) continue;
            bool known = inst.Members.Any(m => IsKnownTo(inst, m, enemy));
            for (int i = enemy.PendingEffects.Count - 1; i >= 0; i--)
            {
                (double at, string effect) = enemy.PendingEffects[i];
                if (at > now) continue;
                // Wait a moment for someone to be told about the unit (open world interest) before dropping the effect.
                if (!known && now - at < 1.0) continue;
                enemy.PendingEffects.RemoveAt(i);
                if (known) SendInstantEffect(inst, enemy, effect);
            }
        }
    }

    private static void SendInstantEffect(Instance inst, Enemy enemy, string effect)
    {
        short id = MonoHash.StatefulEffectId(effect);
        NetOutgoingMessage m = inst.Server.CreateMessage();
        m.Write(MsgClientInstantEffect);
        m.WriteVariableInt32(enemy.ShortId);
        m.Write(id);
        WriteScaled(m, 0f, 20f);
        WriteScaled(m, 0f, 20f);
        WriteScaled(m, 0f, 20f);
        WriteScaled(m, 1f, 10f);
        Broadcast(inst, m);
    }

    private static void FireWreck(Instance inst, Enemy enemy, double now)
    {
        enemy.Aggro = true;
        Fire(inst, enemy, now);
    }

    private static void RemoveExpired(Instance inst, double now)
    {
        List<Enemy>? wrecks = null;
        List<Enemy>? gone = null;
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (enemy.ExpiresAt <= 0 || now < enemy.ExpiresAt) continue;
            if (enemy.Wreck) (wrecks ??= new List<Enemy>()).Add(enemy);
            else if (enemy.AllyKind == 0) (gone ??= new List<Enemy>()).Add(enemy);
        }
        if (wrecks != null) Remove(inst, wrecks, ReasonDestroyedByDamage, ResultDamage, now);
        if (gone != null) Remove(inst, gone, ReasonVanished, ResultNone, now);
    }

    private static void WreckCommand(Instance inst, List<string> pieces, double now, Action<string, string> log)
    {
        if (pieces.Count < 2)
        {
            Reply(inst, "Use /wreck UNIT [seconds] [effect|none] [state]. Spawns a still wreck in front of you.");
            return;
        }
        string unit = pieces[1].Trim();
        float seconds = 20f;
        if (pieces.Count > 2) float.TryParse(pieces[2], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
        seconds = Math.Clamp(seconds <= 0f ? 20f : seconds, 0.5f, 120f);
        string effect = pieces.Count > 3 ? pieces[3].Trim() : "";
        byte state = 0;
        if (pieces.Count > 4) state = ParseUnitState(pieces[4]);

        string key = "wreck:" + unit;
        EnemyType type = GetEnemyType(unit) ?? GetEnemyType(key) ?? new EnemyType { Name = unit, Template = unit, Behavior = "idle" };
        if (GetEnemyType(key) == null) RegisterDefaultType(key, type);
        float facing = MathF.Abs(inst.PlayerVX) + MathF.Abs(inst.PlayerVY) > 0.01f
            ? Heading(0f, 0f, inst.PlayerVX, inst.PlayerVY) : 0f;
        PositionNearPlayer(inst, facing, 12f, out float x, out float y);
        Enemy? wreck = CreateEnemy(inst, key, type, x, y, false, now, log);
        if (wreck == null)
        {
            Reply(inst, "Could not create a wreck of '" + unit + "'.");
            return;
        }
        wreck.Wreck = true;
        wreck.Invulnerable = true;
        wreck.Vel = 0f;
        wreck.Aggro = true;
        wreck.ExpiresAt = now + seconds;
        wreck.State = state;
        if (effect.Length > 0 && !effect.Equals("none", StringComparison.OrdinalIgnoreCase))
            QueueEffects(wreck, new[] { "0 " + effect }, now);
        else QueueEffects(wreck, DefaultDeathSequence, now);
        SendSpawns(inst, new List<Enemy> { wreck }, now);
        Reply(inst, "Wreck " + unit + " for " + seconds.ToString("0.#", CultureInfo.InvariantCulture) + "s.");
    }

    private static byte ParseUnitState(string text)
    {
        if (byte.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out byte n)) return n;
        for (int i = 0; i < CopperReader.StateNames.Length; i++)
        {
            if (string.Equals(CopperReader.StateNames[i], text, StringComparison.OrdinalIgnoreCase)) return (byte)i;
        }
        return 0;
    }
}
