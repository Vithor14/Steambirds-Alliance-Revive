using Lidgren.Network;
using SteambirdsAi;

// Units that belong to a leader (ai\formations). Glued pieces (chain, slot, ring) do not fly: each tick they are placed.
// Free pieces fly with their own AI. Links (shield, death, health, heal) are FormationRules, the same code as the simulator.
public static partial class Enemies
{
    private sealed class FormationState
    {
        public FormationFile Def = null!;
        public Enemy Leader = null!;
        public readonly List<Enemy> Parts = new List<Enemy>();
        public readonly List<(float X, float Y)> Trail = new List<(float X, float Y)>();
        public readonly HashSet<string> SeenAlive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<string> Fired = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public double BornAt;
    }

    // After the leader exists: creates the pieces its AI (or its unit, when the AI is generic) asks for.
    // Returns how many were created. Safe to call when the leader has no formation.
    public static int SpawnFormationMembers(NetConnection connection, int leaderId, double now, Action<string, string> log)
    {
        FormationFile? file;
        Enemy? leader;
        float x, y, rot;
        string group;
        Ground.Rect? area;
        bool freeRoam;
        float healthMult;
        NetServer server;
        string role;
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return 0;
            if (!inst.Enemies.TryGetValue(leaderId, out leader) || leader.Formation != null) return 0;
            file = ResolveFormation(leader);
            if (file == null) return 0;
            x = leader.X;
            y = leader.Y;
            rot = leader.Rot;
            group = leader.Group;
            area = leader.Area;
            freeRoam = leader.FreeRoam;
            healthMult = leader.Type.HealthMultiplier;
            server = inst.Server;
            role = inst.Role;
        }

        List<FormationSlot> slots = file.Expand();
        var made = new List<(int Id, FormationSlot Slot)>();
        foreach (FormationSlot slot in slots)
        {
            FormationMotion.Pose pose = FormationMotion.Start(slot, x, y, rot, file.Spacing);
            string key = EnsureMemberType(leader, slot, freeRoam, healthMult);
            int id = CreateFromScript(server, connection, role, key, pose.X, pose.Y, pose.Rot, group, area, null, now, log);
            if (id == 0) continue;
            made.Add((id, slot));
        }
        if (made.Count == 0) return 0;
        Attach(connection, leaderId, made, file, now);
        return made.Count;
    }

    private static FormationFile? ResolveFormation(Enemy leader)
    {
        string ai = leader.Ai?.Brain.Id ?? leader.Type.Ai ?? "";
        string named = leader.Ai?.Brain.Formation ?? "";
        bool generic = ai.Length == 0 || ai.StartsWith("generic_", StringComparison.OrdinalIgnoreCase);
        return Formations.Resolve(ai, named, leader.Type.Name, generic);
    }

    private static string EnsureMemberType(Enemy leader, FormationSlot slot, bool freeRoam, float healthMult)
    {
        string wantedAi = slot.Ai.Length > 0 ? slot.Ai : "world_formation_turret";
        int slash = leader.TypeKey.LastIndexOf('/');
        if (slash >= 0)
        {
            string local = leader.TypeKey.Substring(0, slash + 1) + slot.Unit;
            EnemyType? onMap = GetEnemyType(local);
            if (onMap != null) return Variant(local, onMap, wantedAi, freeRoam, healthMult);
        }
        EnemyType? plain = GetEnemyType(slot.Unit);
        if (plain != null) return Variant(slot.Unit, plain, wantedAi, freeRoam, healthMult);
        string key = "formation/" + slot.Unit + "~" + wantedAi;
        if (GetEnemyType(key) == null)
        {
            RegisterDefaultType(key, new EnemyType
            {
                Name = slot.Unit,
                Template = slot.Unit,
                Ai = wantedAi,
                FreeRoam = freeRoam,
                HealthMultiplier = healthMult
            });
        }
        return key;
    }

    private static string Variant(string key, EnemyType type, string ai, bool freeRoam, float healthMult)
    {
        if (ai.Length == 0 || string.Equals(type.Ai, ai, StringComparison.OrdinalIgnoreCase)) return key;
        string alt = key + "~" + ai;
        if (GetEnemyType(alt) == null)
        {
            RegisterDefaultType(alt, new EnemyType
            {
                Name = type.Name.Length > 0 ? type.Name : key,
                Template = string.IsNullOrWhiteSpace(type.Template) ? type.Name : type.Template,
                Ai = ai,
                FreeRoam = freeRoam,
                HealthMultiplier = healthMult,
                Boss = false
            });
        }
        return alt;
    }

    private static void Attach(NetConnection connection, int leaderId, List<(int Id, FormationSlot Slot)> made, FormationFile def, double now)
    {
        lock (Game.Lock)
        {
            if (!instances.TryGetValue(connection.RemoteUniqueIdentifier, out Instance? inst)) return;
            if (!inst.Enemies.TryGetValue(leaderId, out Enemy? leader)) return;
            var state = new FormationState { Def = def, Leader = leader, BornAt = now };
            int glued = 0;
            foreach ((int id, FormationSlot slot) in made)
            {
                if (!inst.Enemies.TryGetValue(id, out Enemy? part)) continue;
                part.Formation = state;
                part.PartIndex = slot.Glued ? glued++ : -1;
                part.Summoner = leader;
                part.Leader = leader;
                part.SummonUnit = slot.Unit;
                part.SummonRole = slot.Role;
                part.FormationMemberId = slot.Id;
                part.FormationSlot = slot;
                state.Parts.Add(part);
            }
            if (state.Parts.Count == 0) return;
            leader.Formation = state;
            leader.PartIndex = -1;
            leader.FormationMemberId = "leader";
            if (state.Parts.Any(p => p.FormationSlot != null && p.FormationSlot.Kind == "chain"))
            {
                float rad = leader.Rot * Rad;
                float step = Math.Max(0.4f, def.Spacing) * 0.5f;
                int samples = (state.Parts.Count + 1) * 2 + 2;
                for (int i = samples; i >= 1; i--)
                    state.Trail.Add((leader.X - MathF.Sin(rad) * step * i, leader.Y - MathF.Cos(rad) * step * i));
            }
        }
    }

    private static void UpdateFormations(Instance inst, double now)
    {
        HashSet<FormationState>? states = null;
        foreach (Enemy enemy in inst.Enemies.Values)
        {
            if (enemy.Formation != null) (states ??= new HashSet<FormationState>()).Add(enemy.Formation);
        }
        if (states == null) return;

        foreach (FormationState f in states)
        {
            bool leaderAlive = inst.Enemies.ContainsKey(f.Leader.ShortId);
            if (!leaderAlive)
            {
                foreach (Enemy part in f.Parts)
                {
                    if (!inst.Enemies.ContainsKey(part.ShortId)) continue;
                    part.Formation = null;
                    part.PartIndex = -1;
                    part.Summoner = null;
                }
                continue;
            }

            var actors = new List<FormationActor>
            {
                new FormationActor
                {
                    Id = "leader", IsLeader = true, Alive = true,
                    HealthFraction = f.Leader.MaxHp > 0f ? f.Leader.Health / f.Leader.MaxHp : 1f,
                    ShieldSuppressed = f.Leader.ShieldSuppressed
                }
            };
            var byId = new Dictionary<string, Enemy>(StringComparer.OrdinalIgnoreCase) { ["leader"] = f.Leader };
            var roleCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (Enemy part in f.Parts)
            {
                bool alive = inst.Enemies.ContainsKey(part.ShortId);
                string role = part.SummonRole ?? "";
                roleCount.TryGetValue(role, out int n);
                if (alive) roleCount[role] = n + 1;
                byId[part.FormationMemberId] = part;
                actors.Add(new FormationActor
                {
                    Id = part.FormationMemberId, Role = role, RoleOrder = n, Alive = alive,
                    HealthFraction = alive && part.MaxHp > 0f ? part.Health / part.MaxHp : 0f,
                    ShieldSuppressed = part.ShieldSuppressed
                });
            }

            var orders = new List<FormationOrder>();
            FormationRules.Evaluate(f.Def, actors, f.SeenAlive, f.Fired, orders);
            var kill = new List<Enemy>();
            foreach (FormationOrder order in orders)
            {
                if (!byId.TryGetValue(order.ActorId, out Enemy? who)) continue;
                if (!inst.Enemies.ContainsKey(who.ShortId)) continue;
                if (order.ClearShield) ClearShield(who);
                if (order.HealFraction > 0f && who.MaxHp > 0f)
                {
                    who.Health = Math.Min(who.MaxHp, who.Health + who.MaxHp * order.HealFraction);
                    who.HealthChanged = true;
                }
                if (order.Kill) kill.Add(who);
                if (order.Shield) ShieldLook(who, now, order.Effect);
            }
            PlaceParts(f, inst);
            if (kill.Count == 0) continue;
            foreach (Enemy who in kill)
            {
                ClearShield(who);
                who.Invulnerable = false;
                TakeDamage(inst.Members.FirstOrDefault(), who.ShortId, who.Health + 1f, now, (_, _) => { });
            }
        }
    }

    private static void PlaceParts(FormationState f, Instance inst)
    {
        Enemy leader = f.Leader;
        FormationFile def = f.Def;
        bool anyChain = f.Parts.Any(p => p.FormationSlot != null && p.FormationSlot.Kind == "chain");
        if (anyChain)
        {
            (float X, float Y) last = f.Trail.Count > 0 ? f.Trail[^1] : (leader.X + 1000f, leader.Y);
            if (Distance(last.X, last.Y, leader.X, leader.Y) >= 0.5f) f.Trail.Add((leader.X, leader.Y));
            float needed = (f.Parts.Count + 1) * Math.Max(0.4f, def.Spacing) + 6f;
            float length = f.Trail.Count == 0 ? 0f : Distance(f.Trail[^1].X, f.Trail[^1].Y, leader.X, leader.Y);
            int keepFrom = f.Trail.Count - 1;
            while (keepFrom > 0 && length < needed)
            {
                length += Distance(f.Trail[keepFrom].X, f.Trail[keepFrom].Y, f.Trail[keepFrom - 1].X, f.Trail[keepFrom - 1].Y);
                keepFrom--;
            }
            if (keepFrom > 0) f.Trail.RemoveRange(0, keepFrom);
        }

        foreach (Enemy part in f.Parts)
        {
            if (!inst.Enemies.ContainsKey(part.ShortId) || part.FormationSlot == null || !part.FormationSlot.Glued) continue;
            FormationSlot slot = part.FormationSlot;
            FormationMotion.Pose pose;
            if (slot.Kind == "chain")
            {
                float behind = (slot.ChainIndex + 1) * Math.Max(0.4f, def.Spacing);
                pose = FormationMotion.AlongTrail(leader.X, leader.Y, leader.Rot, leader.Vel, f.Trail, behind);
            }
            else if (slot.Kind == "ring")
            {
                double age = Math.Max(0, NowFor(inst) - f.BornAt);
                pose = FormationMotion.Ring(leader.X, leader.Y, leader.Rot, leader.Vel, slot.Radius, slot.Angle, slot.Spin, (float)age);
            }
            else pose = FormationMotion.Slot(leader.X, leader.Y, leader.Rot, leader.Vel, slot.X, slot.Y);
            part.X = pose.X;
            part.Y = pose.Y;
            part.Rot = pose.Rot;
            part.Vel = pose.Speed;
        }
    }

    private static void ShieldLook(Enemy enemy, double now, string effect)
    {
        if (enemy.ShieldSuppressed) return;
        enemy.ShieldUntil = now + 0.35;
        if (effect.Length == 0) return;
        short id = MonoHash.StatefulEffectId(effect);
        if (id != enemy.EffectId)
        {
            enemy.EffectId = id;
            enemy.EffectChanged = true;
        }
    }

    private static void ClearShield(Enemy enemy)
    {
        enemy.ShieldSuppressed = true;
        enemy.ShieldUntil = 0;
        if (enemy.EffectId != enemy.BaseEffect)
        {
            enemy.EffectId = enemy.BaseEffect;
            enemy.EffectChanged = true;
        }
    }

    // Kept so a kill still runs the links on the next tick (the "died" rule). The old tip-kills-all behavior is a link now.
    private static void FormationMemberKilled(Instance inst, Enemy dead, NetConnection connection, double now, Action<string, string> log)
    {
    }
}
