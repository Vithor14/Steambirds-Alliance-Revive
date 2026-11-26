// Death wrecks: a unit type with "deathUnit" leaves that unit where it died (the game's "*DEATH" models play the boss's death
// animation, e.g. ufoDEATH for the Ambassador). The wreck does not move or take damage, fires its guns, and is destroyed after
// "deathSeconds" (which plays its explosion). The dungeon server has its own "onDeath" for its maps (DungeonServer.Units.cs); this one
// is for every other place (the open world, enemies.json types).
//
// The boss death of the original game is a sequence of the client's INSTANT EFFECTS (configBinary "instantEffects", sent by the server
// with ClientInstantEffect on a unit): explosionBossInitial (the first big explosion and its sound), explosionBossDebris, deathAnimation
// (fires the animator trigger "death" of the death model: it shakes and throws its beams of white light) and epicDeath1-4 (small and
// medium explosions with their sounds around the model). The server then destroys the death model, which plays its explosionPrefab
// (ExplosionB_Final). Found 25/11/2026 after trying the gun, long lives, stateful effects and unit states (none of them did it).
// The timing of the original is lost: DefaultDeathSequence spreads the effects over the 6 s of the wreck (deathSequence changes it).
public static partial class Enemies
{
    private const float DefaultWreckSeconds = 6f;
    private const ushort MsgClientInstantEffect = 58535;

    // Played on a "*DEATH" wreck when its type does not say otherwise ("SECONDS EFFECT").
    private static readonly string[] DefaultDeathSequence =
    {
        "0 explosionBossInitial", "0 explosionBossDebris", "0 deathAnimation", "0.3 epicDeath1", "2.1 epicDeath2", "3.9 epicDeath4",
        "5.1 epicDeath3"
    };

    private static void LeaveWreck(Instance inst, Enemy dead, double now, Action<string, string> log)
    {
        string name = (dead.Type.DeathUnit ?? "").Trim();
        if (name.Length == 0) return;
        float seconds = dead.Type.DeathSeconds > 0f ? dead.Type.DeathSeconds : DefaultWreckSeconds;
        Enemy? wreck = SpawnWreck(inst, dead, name, dead.X, dead.Y, dead.Rot, seconds, dead.Type.DeathEffect,
            ParseUnitState(dead.Type.DeathState), now, log);
        if (wreck == null) return;
        QueueEffects(wreck, dead.Type.DeathSequence, now);
        log("ENEMY", inst.Role + " " + Describe(dead) + " left its wreck " + wreck.TypeKey + " for " + Num(seconds) + "s");
    }

    // A death model (or any unit) as a wreck at (x, y): still, invulnerable, firing its guns, showing "effect" (empty or "none" =
    // nothing), in the given unit state, and destroyed after "seconds", which plays its explosion. "near" lends its map (where a type
    // is looked up), area and group.
    private static Enemy? SpawnWreck(Instance inst, Enemy near, string name, float x, float y, float rot, float seconds, string? effectName,
        byte state, double now, Action<string, string> log)
    {
        // A unit type (enemies.json or the dead unit's map), or else a game unit made into a wreck type here.
        EnemyType? type = FindSummonType(near, name, out string key);
        if (type == null)
        {
            if (FindUnit(name) == null)
            {
                if (reportedBrainFailures.Add("wreck|" + name))
                    log("ENEMY", "the type '" + near.TypeKey + "' leaves the wreck '" + name + "', which is neither a unit type nor a game unit");
                return null;
            }
            key = "wreck/" + name;
            type = GetEnemyType(key);
            if (type == null)
            {
                RegisterDefaultType(key, new EnemyType { Name = name, Template = name, Ai = "generic_idle", Invulnerable = true });
                type = GetEnemyType(key);
            }
            if (type == null) return null;
        }

        Enemy? wreck = CreateEnemy(inst, key, type, x, y, false, now, log);
        if (wreck == null) return null;
        wreck.Rot = rot;
        wreck.Vel = 0f;
        wreck.FreeRoam = near.FreeRoam;
        wreck.Area = near.Area;
        wreck.Group = near.Group;
        wreck.Invulnerable = true;
        wreck.Wreck = true;
        wreck.State = state;
        // An effect on the wreck (deathEffect; a unit type with its own visualEffect keeps it).
        string effect = (effectName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(type.VisualEffect) && effect.Length > 0 && !effect.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            wreck.BaseEffect = MonoHash.StatefulEffectId(effect);
            wreck.EffectId = wreck.BaseEffect;
            wreck.EffectChanged = true;
        }
        wreck.ExpiresAt = now + Math.Max(0.5f, seconds);
        SendSpawns(inst, new List<Enemy> { wreck }, now);
        return wreck;
    }

    // The death sequence of a wreck: the type's own list, or the game's boss death for a "*DEATH" model.
    private static void QueueEffects(Enemy wreck, List<string>? sequence, double now)
    {
        IEnumerable<string> steps = sequence != null ? sequence
            : wreck.Model.Name.EndsWith("DEATH", StringComparison.OrdinalIgnoreCase) ? DefaultDeathSequence : Array.Empty<string>();
        foreach (string step in steps)
        {
            string[] parts = (step ?? "").Trim().Split(new[] { ' ', '\t', ':' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            double at = 0;
            string effect = parts[0];
            if (parts.Length == 2 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out at))
                effect = parts[1].Trim();
            (wreck.PendingEffects ??= new List<(double, string)>()).Add((now + Math.Max(0, at), effect));
        }
    }

    // The death sequence of a "*DEATH" unit the dungeon server left (its "onDeath" wrecks).
    public static void StartDeathSequence(Lidgren.Network.NetConnection? connection, int shortId, double now)
    {
        if (connection == null) return;
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (inst.Enemies.TryGetValue(shortId, out Enemy? wreck)) QueueEffects(wreck, wreck.Type.DeathSequence, now);
        }
    }

    // Sends the instant effects that are due, to the players whose client knows the unit (after UpdateInterest, so a wreck that
    // just appeared is already known). An effect that nobody knows the unit for yet waits a second at most.
    private static void SendDueEffects(Instance inst, double now)
    {
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            List<(double At, string Effect)>? pending = enemy.PendingEffects;
            if (pending == null || pending.Count == 0) continue;
            var members = inst.Members.Where(c => c.Status == Lidgren.Network.NetConnectionStatus.Connected && IsKnownTo(inst, c, enemy)).ToList();
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (now < pending[i].At) continue;
                if (members.Count == 0 && now < pending[i].At + 1.0) continue;
                foreach (Lidgren.Network.NetConnection member in members) SendInstantEffect(inst, member, enemy, pending[i].Effect);
                pending.RemoveAt(i);
            }
        }
    }

    // ClientInstantEffect (58535): InstantEffectRequest = shortID (varint), fxID (short: the name's Mono hash, like the stateful
    // effects), offset x, y, z (x20) and scale (x10). No time: the client plays it at once.
    private static void SendInstantEffect(Instance inst, Lidgren.Network.NetConnection member, Enemy enemy, string effect, float scale = 1f)
    {
        Lidgren.Network.NetOutgoingMessage m = inst.Server.CreateMessage();
        m.Write(MsgClientInstantEffect);
        m.WriteVariableInt32(enemy.ShortId);
        m.Write(MonoHash.StatefulEffectId(effect));
        WriteScaled(m, 0f, 20f);
        WriteScaled(m, 0f, 20f);
        WriteScaled(m, 0f, 20f);
        WriteScaled(m, scale, 10f);
        inst.Server.SendMessage(m, member, Lidgren.Network.NetDeliveryMethod.ReliableOrdered, 0);
    }

    // A unit state by number (0-255) or by name of the game's UnitScript.States ("Siege", "StopNSpin_Stopped"...). 0 = Default.
    private static byte ParseUnitState(string? text)
    {
        string s = (text ?? "").Trim();
        if (s.Length == 0) return 0;
        if (int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int n))
            return (byte)Math.Clamp(n, 0, 255);
        int index = Array.FindIndex(CopperReader.StateNames, x => x.Equals(s, StringComparison.OrdinalIgnoreCase));
        return index >= 0 ? (byte)index : (byte)0;
    }

    // /wreck UNIT [seconds] [effect|none] [state]: a death model 12 units in front of the player, as a boss would leave it, to try out
    // what brings its beams of light (default: 20 s, no effect, state 0).
    private static void WreckCommand(Instance inst, List<string> pieces, double now, Action<string, string> log)
    {
        if (pieces.Count < 2)
        {
            Reply(inst, "Use /wreck UNIT [seconds] [effect|none] [state], for example /wreck ufoDEATH 20 none 1");
            return;
        }
        float seconds = 20f;
        if (pieces.Count > 2) float.TryParse(pieces[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds);
        seconds = Math.Clamp(seconds <= 0f ? 20f : seconds, 0.5f, 120f);
        string effect = pieces.Count > 3 ? pieces[3] : "";
        byte state = ParseUnitState(pieces.Count > 4 ? pieces[4] : "");
        PositionNearPlayer(inst, 0f, 12f, out float x, out float y);
        // Any unit of the instance only lends its map and area; with none, a plain free-roaming stand-in.
        Enemy near = inst.Enemies.Values.FirstOrDefault() ?? new Enemy { TypeKey = "", FreeRoam = true };
        Enemy? wreck = SpawnWreck(inst, near, pieces[1], x, y, 0f, seconds, effect, state, now, log);
        if (wreck != null) QueueEffects(wreck, null, now);
        string stateName = state < CopperReader.StateNames.Length ? CopperReader.StateNames[state] : state.ToString();
        Reply(inst, wreck == null
            ? "'" + pieces[1] + "' is not a unit type or a game unit (try ufoDEATH, gargantuaDEATH...)."
            : pieces[1] + " for " + Num(seconds) + " s, effect " + (effect.Length == 0 ? "none" : effect) + ", state " + state + " (" +
              stateName + "). It explodes at the end.");
    }

    // A wreck fires its guns the whole time it lasts, whatever the player does: the game's boss death models have one gun
    // (gargantuaDEATHgun: a spinning burst of harmless, still bullets 7 times a second).
    private static void FireWreck(Instance inst, Enemy wreck, double now)
    {
        if (now < wreck.ActiveAt) return;
        foreach (GunState gun in wreck.Guns)
        {
            if (now < gun.NextShot) continue;
            GameGun m = gun.Model;
            if (!AimGun(gun, true, 0f, now, out float rotation)) rotation = m.Angle;
            SendShot(inst, wreck, m, rotation, now);
            gun.NextShot = now + 1.0 / Math.Max(0.5f, m.FireRate > 0.05f ? m.FireRate : 2f);
        }
    }

    // Units whose time is up (not the players' allies: UpdateAllies). A wreck is DESTROYED, not faded out: the client plays the
    // unit's explosionPrefab only for a destruction, and the death models carry the big final explosion of the boss
    // (ufoDEATH: ExplosionB_Final; the boss itself has planeExplosionEmpty). Any other unit just vanishes.
    private static void RemoveExpired(Instance inst, double now)
    {
        List<Enemy>? blown = null, gone = null;
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (enemy.AllyKind != 0 || enemy.ExpiresAt <= 0 || now < enemy.ExpiresAt) continue;
            if (enemy.Wreck) (blown ??= new List<Enemy>()).Add(enemy);
            else (gone ??= new List<Enemy>()).Add(enemy);
        }
        if (blown != null) Remove(inst, blown, ReasonDestroyedByDamage, ResultDamage, now);
        if (gone != null) Remove(inst, gone, ReasonVanished, ResultNone, now);
    }
}
