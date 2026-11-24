using System.Globalization;
using Lidgren.Network;

// Plane XP, plane level, pilot XP and pilot rank (checklist section 3).
//
// How the client expects it (decompiled GameClient, Pilot, DeathWrapupStateMachine, PilotSchoolUI):
//  - every kill: ClientUpdatePlayer with the plane XP and level (the HUD bar, level-up effects and the
//    "class unlocked" effect come from it). A kill also fires the OnKill pilot skills, which the client never
//    fires by itself: TriggerPlayerSkill(OnKill).
//  - pilot XP is earned in real time: the client shows baselineXP + realTimePlaneXPProportion (0.9 in build
//    201219, 65% in older builds) x the pilot XP every plane of the hangar is worth at the current rank
//    (Pilot.computedXP, conversion table pilotScore, best ratio 250 plane XP : 1 pilot XP). The rank only comes
//    from the server (ClientUpdatePlayer), so the server raises it as soon as that computed XP reaches the next
//    target, and moves the baseline so the computed XP does not jump (the table depends on the rank).
//    meowzaDB's pilot XP calculator (meowzadb.com/xp) describes the same rule: 65% in real time, the other 35%
//    only when the plane was retired (hangar "Retire plane": ServerRetirePlane, handled in PlaneRetirement.cs).
//  - when the plane is destroyed the client waits, staring at the crash site, for ClientDeathSummaryInformation
//    (DeathWrapupStateMachine.WaitingForStats). The real-time share of that plane goes into the baseline (the
//    client stops counting the plane), plus an optional bonus (progression.json deathPilotXpBonus), and the plane
//    leaves the account (permadeath).
//  - highestLevels (highest level of each class) unlocks classes and some skill groups.
//
// Everything that touches the account goes through Accounts (its own lock). Session state of this file is
// protected by Game.Lock, the same lock used by the enemies; the methods below take it themselves.
public static class PilotProgression
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const byte TriggerOnKill = 0;   // SkillTriggerType.OnKill

    // One connected player.
    private sealed class Session
    {
        public long Peer;
        public NetServer Server = null!;
        public NetConnection Connection = null!;
        public string Role = "";
        public string? CharacterId;
        public bool PlaneAlive;
        public double NextNearbyStat;
        public int LastNearbyCount = -1;
    }

    private static readonly Dictionary<long, Session> sessions = new Dictionary<long, Session>();
    private static Action<string, string> log = (_, _) => { };

    // ------------------------------------------------------------------ startup and login

    // Called once at server start, after Accounts.Load. The accounts are prepared at the login of each player
    // (PrepareAccount).
    public static void Start(Action<string, string> log)
    {
        PilotProgression.log = log;
        ProgressionSettingsFile.Start(log);
        ProgressionData.Load(log);
        PilotExtras.Start(log);
    }

    // Once per account loaded (Program.cs, at the login, with the account in scope): starting rank of a new pilot,
    // plane levels from the XP, rank from the real-time pilot XP and unlocks.
    public static void PrepareAccount()
    {
        lock (Game.Lock)
        {
            PrepareAccountLocked();
        }
    }

    private static void PrepareAccountLocked()
    {
        ProgressionSettings settings = ProgressionSettingsFile.Current;
        string who = Accounts.Name;
        if (Accounts.NewPilot)
        {
            int startLevel = Math.Clamp(settings.StartingPilotLevel, 1, ProgressionData.PilotLevels.TopLevel);
            Accounts.Change(d =>
            {
                if (d.Pilot.Level < startLevel)
                {
                    d.Pilot.Level = startLevel;
                    d.Pilot.BaselineXp = BaselineForRankStart(d, startLevel);
                }
                d.Pilot.LastLevelupShown = d.Pilot.Level;
                return 0;
            });
            log("PROGRESSION", who + ": new pilot, starting rank " + startLevel);
        }

        // Planes from before the progression: the level follows the XP (never down). The rank follows the pilot XP
        // the planes are worth in real time (accounts from before this rule go up here).
        (int rankBefore, int rankAfter, int pilotXp) = Accounts.Change(d =>
        {
            foreach (Accounts.SavedPlane plane in d.Planes) plane.Level = Math.Max(plane.Level, PlaneLevelFromXp(plane.Xp));
            RefreshHighestLevels(d);
            int before = d.Pilot.Level;
            UpdatePilotRank(d);
            RefreshUnlocks(d);
            return (before, d.Pilot.Level, ComputedPilotXp(d));
        });
        log("PROGRESSION", who + ": pilot rank " + rankAfter + ", " + pilotXp + " pilot XP (" +
            (ProgressionData.RealTimePlaneXpProportion * 100f).ToString("0.#", Inv) + "% of the hangar planes' value counts in real time)" +
            (rankAfter > rankBefore ? "; rose from rank " + rankBefore + " from the planes' XP" : ""));
    }

    // Before every ClientWelcomeMessage: buffs that ended, portrait, unlocks.
    public static void PrepareForWelcome()
    {
        lock (Game.Lock)
        {
            PilotExtras.PrepareForWelcome();
            Accounts.Change(d =>
            {
                RefreshHighestLevels(d);
                RefreshUnlocks(d);
                return 0;
            });
        }
    }

    // After the ClientWelcomeMessage of a connection.
    public static void PlayerIdentified(NetServer server, NetConnection connection, string role, double now)
    {
        lock (Game.Lock)
        {
            Session session = GetSession(server, connection, role);
            session.PlaneAlive = false;
            session.CharacterId = null;
            PilotExtras.AfterWelcome(server, connection, now);
        }
    }

    // After the SpawnUnitsBatch of the player's plane.
    public static void PlaneSpawned(NetServer server, NetConnection connection, string role, string characterId, double now)
    {
        lock (Game.Lock)
        {
            Session session = GetSession(server, connection, role);
            session.CharacterId = characterId;
            session.PlaneAlive = true;
            session.NextNearbyStat = now;
            session.LastNearbyCount = -1;
        }
    }

    // After ServerRequestCreateCharacter: a new plane also counts in the real-time pilot XP.
    public static void PlaneCreated(NetServer server, NetConnection connection, string role)
    {
        lock (Game.Lock)
        {
            (bool rankUp, int level, int pilotXp) = Accounts.Change(d =>
            {
                bool up = UpdatePilotRank(d);
                return (up, d.Pilot.Level, ComputedPilotXp(d));
            });
            if (!rankUp) return;
            SendPilotUpdate(server, connection);
            log("PROGRESSION", "[" + role + "] new plane in the hangar: the pilot rose to rank " + level + " (" + pilotXp + " XP)");
            PilotExtras.DeliverCareerRewards(server, connection, role);
        }
    }

    public static void Disconnected(NetConnection? connection)
    {
        if (connection == null) return;
        lock (Game.Lock)
        {
            // The same client keeps its peer id when it changes server: only the session of this connection goes.
            if (sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? session) && session.Connection == connection)
                sessions.Remove(connection.RemoteUniqueIdentifier);
        }
    }

    private static Session GetSession(NetServer server, NetConnection connection, string role)
    {
        long peer = connection.RemoteUniqueIdentifier;
        if (!sessions.TryGetValue(peer, out Session? session))
        {
            session = new Session { Peer = peer };
            sessions[peer] = session;
        }
        session.Server = server;
        session.Connection = connection;
        session.Role = role;
        return session;
    }

    // ------------------------------------------------------------------ periodic

    // Called by the UDP loop of each server (about 200 times per second): sends the "nearby players" stat
    // once per second to every live plane (pilot skills with nearbyPlayers conditions read it).
    public static void Update(NetServer server, double now)
    {
        lock (Game.Lock)
        {
            if (sessions.Count == 0) return;
            float radius = ProgressionData.NearbyPlayersRadius;
            foreach (Session session in sessions.Values)
            {
                if (session.Server != server || !session.PlaneAlive || now < session.NextNearbyStat) continue;
                session.NextNearbyStat = now + 1.0;
                if (!Game.GetPlane(session.Peer, out float x, out float y, out bool alive, out _) || !alive) continue;

                int nearby = 1;
                foreach (Session other in sessions.Values)
                {
                    if (other == session || other.Server != server || !other.PlaneAlive) continue;
                    if (!Game.GetPlane(other.Peer, out float ox, out float oy, out bool otherAlive, out _) || !otherAlive) continue;
                    float dx = ox - x;
                    float dy = oy - y;
                    if (dx * dx + dy * dy <= radius * radius) nearby++;
                }

                int count = nearby;
                ProgressionMessages.Send(server, session.Connection,
                    m => ProgressionMessages.WriteClientReceiveReliableStats(m, 0, (float)now, count));
                if (count != session.LastNearbyCount)
                {
                    if (session.LastNearbyCount >= 0 || count > 1)
                        log("PROGRESSION", "[" + session.Role + "] players near the plane: " + count);
                    session.LastNearbyCount = count;
                }
            }
        }
    }

    // ------------------------------------------------------------------ kills

    // An enemy of the server was destroyed by this player (Enemies.TakeDamage).
    // templateName/tier/maxHitPoints come from the server's unit catalog (units_config.txt).
    // damageShare: part (0..1) of the enemy's damage that this player dealt. Returns false when the loot of this enemy must be
    // withheld from this player (the after-fight text is shown and the share is under the unit's damage threshold).
    public static bool EnemyKilled(NetServer server, NetConnection connection, string role, string templateName, short templateHash,
        int tier, float maxHitPoints, bool boss, int shortId, float x, float y, float damageShare, double now)
    {
        // The XP goes to the account of the player of this connection (also when the update loop calls this).
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        lock (Game.Lock)
        {
            ProgressionSettings settings = ProgressionSettingsFile.Current;

            if (settings.KillSkillTrigger)
                ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteTriggerPlayerSkill(m, TriggerOnKill, 0f));

            Accounts.Change(d =>
            {
                d.Pilot.Kills++;
                if (boss) d.Pilot.BossKills++;
                return 0;
            });

            string? characterId = Game.GetPlaneCharacterId(connection);
            float rawXp = ProgressionData.EnemyXp(templateName, tier, maxHitPoints) * Math.Max(0f, settings.PlaneXpMultiplier);
            int xp = ProgressionData.RoundToInt(rawXp);

            int awarded = 0;
            if (characterId == null)
            {
                log("XP", "[" + role + "] " + templateName + " killed without a live plane: no XP");
            }
            else if (xp > 0)
            {
                awarded = AwardPlaneXp(server, connection, role, characterId, xp, settings.CapXpPerKill, "kill of " + templateName +
                    " (tier " + tier + ")", boosted: true);
            }

            ProgressionData.UnitXpInfo? unit = ProgressionData.GetUnit(templateName);
            bool showDetails = unit != null ? unit.ShowPostFightDetails : boss;
            bool lootAllowed = true;
            if (settings.PostFightDetails && showDetails)
            {
                float threshold = unit != null && unit.DamageThreshold >= 0f ? unit.DamageThreshold : ProgressionData.DefaultDamageThreshold;
                float share = Math.Clamp(damageShare, 0f, 1f);
                ProgressionMessages.Send(server, connection,
                    m => ProgressionMessages.WriteClientPostFightDetails(m, templateHash, shortId, x, y, share, threshold, awarded));
                lootAllowed = share >= threshold;
                log("XP", "[" + role + "] after-fight of " + templateName + ": " + (share * 100f).ToString("0.##", Inv) + "% of the damage (minimum " +
                    (threshold * 100f).ToString("0.##", Inv) + "%), " + awarded + " XP" + (lootAllowed ? "" : ", no loot"));
            }
            return lootAllowed;
        }
    }

    // Adds plane XP to a plane of the account and tells the client. Returns the XP actually added.
    // cap: GameServer.GetXpActuallyAwarded (half of the current level, or xpLimitForMaxLevel at max level).
    // boosted: the account's XP boost (PlaneXpBoost) applies, as it does to kills in GameServer.GetXpActuallyAwarded.
    public static int AwardPlaneXp(NetServer server, NetConnection connection, string role, string characterId, int amount, bool cap, string reason,
        bool boosted = false)
    {
        lock (Game.Lock)
        {
            PlaneXpResult? result = Accounts.Change(d => AddPlaneXp(d, characterId, boosted ? PlaneXpBoost.Boost(d.Pilot, amount) : amount, cap));
            if (result == null)
            {
                log("XP", "[" + role + "] " + reason + ": plane " + characterId + " is not in the account, XP lost");
                return 0;
            }

            SendPlayerUpdate(server, connection, result.Plane, result.PilotLevel, result.PilotBaselineXp, result.Unlocks, false);
            if (result.HighestLevelsChanged)
                ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientUpdateHighestLevels(m, result.HighestLevels));

            string text = "[" + role + "] " + reason + ": +" + result.Awarded + " XP" +
                (result.Awarded < amount ? " (of " + amount + ", per-kill limit)" : "") +
                " -> " + result.Plane.Class + " level " + result.Plane.Level + " (" + result.Plane.Xp + " XP)";
            if (result.LevelBefore != result.Plane.Level) text += " LEVELED UP (was " + result.LevelBefore + ")";
            text += " | pilot rank " + result.PilotLevel + " (" + result.PilotXp + " XP)";
            if (result.PilotLevel > result.PilotLevelBefore) text += " RANKED UP (was " + result.PilotLevelBefore + ")";
            if (result.NewUnlocks.Count > 0) text += " | unlocked: " + string.Join(", ", result.NewUnlocks);
            log("XP", text);
            if (result.PilotLevel > result.PilotLevelBefore) PilotExtras.DeliverCareerRewards(server, connection, role);
            return result.Awarded;
        }
    }

    private sealed class PlaneXpResult
    {
        public Accounts.SavedPlane Plane = null!;
        public int Awarded;
        public int LevelBefore;
        public int PilotLevelBefore;
        public int PilotLevel;
        public int PilotBaselineXp;
        public int PilotXp;
        public List<string> Unlocks = new List<string>();
        public List<string> NewUnlocks = new List<string>();
        public bool HighestLevelsChanged;
        public Dictionary<string, int> HighestLevels = new Dictionary<string, int>();
    }

    private static PlaneXpResult? AddPlaneXp(Accounts.AccountData d, string characterId, int amount, bool cap)
    {
        Accounts.SavedPlane? plane = d.Planes.FirstOrDefault(a => a.CharacterId == characterId);
        if (plane == null) return null;

        ProgressionData.XpTable levels = ProgressionData.PlaneLevels;
        int awarded = Math.Max(0, amount);
        if (cap)
        {
            if (!levels.IsMaxLevel(plane.Level))
            {
                int half = levels.XpFromCurLevelStartToFinish(plane.Xp) / 2;
                if (awarded > half) awarded = Math.Max(1, half);
            }
            else if (ProgressionData.XpLimitForMaxLevel > 0 && awarded > ProgressionData.XpLimitForMaxLevel)
            {
                awarded = ProgressionData.XpLimitForMaxLevel;
            }
        }

        var result = new PlaneXpResult { LevelBefore = plane.Level, Awarded = awarded, PilotLevelBefore = d.Pilot.Level };
        int scoreBefore = ProgressionData.PilotXpFromPlaneXp(d.Pilot.Level, plane.Xp);
        plane.Xp = (int)Math.Min(int.MaxValue, (long)plane.Xp + awarded);
        plane.Level = Math.Max(plane.Level, PlaneLevelFromXp(plane.Xp));

        // The client adds realTimePlaneXPProportion x the new pilot value by itself. With pilotXpMultiplier != 1 the
        // difference goes into the baseline, so the pilot still gets "multiplier" times that.
        float multiplier = Math.Max(0f, ProgressionSettingsFile.Current.PilotXpMultiplier);
        if (multiplier != 1f)
        {
            int scoreAfter = ProgressionData.PilotXpFromPlaneXp(d.Pilot.Level, plane.Xp);
            float extra = (multiplier - 1f) * ProgressionData.RealTimePlaneXpProportion * (scoreAfter - scoreBefore);
            d.Pilot.BaselineXp = (int)Math.Clamp((long)d.Pilot.BaselineXp + ProgressionData.RoundToInt(extra), 0L, int.MaxValue);
        }
        UpdatePilotRank(d);

        List<string> unlocksBefore = new List<string>(d.Pilot.Unlocks);
        result.HighestLevelsChanged = RefreshHighestLevels(d);
        RefreshUnlocks(d);

        result.Plane = plane.Copy();
        result.PilotLevel = d.Pilot.Level;
        result.PilotBaselineXp = d.Pilot.BaselineXp;
        result.PilotXp = ComputedPilotXp(d);
        result.Unlocks = new List<string>(d.Pilot.Unlocks);
        result.NewUnlocks = d.Pilot.Unlocks.Where(u => !unlocksBefore.Contains(u)).ToList();
        result.HighestLevels = new Dictionary<string, int>(d.Pilot.HighestLevels);
        return result;
    }

    // Sets the XP of a plane so it is at the start of "level" (admin command /level).
    public static bool SetPlaneLevel(NetServer server, NetConnection connection, string role, string characterId, int level)
    {
        lock (Game.Lock)
        {
            int target = Math.Clamp(level, 1, ProgressionData.PlaneLevels.TopLevel);
            PlaneXpResult? result = Accounts.Change(d =>
            {
                Accounts.SavedPlane? plane = d.Planes.FirstOrDefault(a => a.CharacterId == characterId);
                if (plane == null) return null;
                var r = new PlaneXpResult { LevelBefore = plane.Level, PilotLevelBefore = d.Pilot.Level };
                plane.Xp = ProgressionData.PlaneLevels.XpFromLevel(target);
                plane.Level = target;
                UpdatePilotRank(d);
                List<string> unlocksBefore = new List<string>(d.Pilot.Unlocks);
                r.HighestLevelsChanged = RefreshHighestLevels(d);
                RefreshUnlocks(d);
                r.Plane = plane.Copy();
                r.PilotLevel = d.Pilot.Level;
                r.PilotBaselineXp = d.Pilot.BaselineXp;
                r.PilotXp = ComputedPilotXp(d);
                r.Unlocks = new List<string>(d.Pilot.Unlocks);
                r.NewUnlocks = d.Pilot.Unlocks.Where(u => !unlocksBefore.Contains(u)).ToList();
                r.HighestLevels = new Dictionary<string, int>(d.Pilot.HighestLevels);
                return r;
            });
            if (result == null) return false;

            SendPlayerUpdate(server, connection, result.Plane, result.PilotLevel, result.PilotBaselineXp, result.Unlocks, false);
            if (result.HighestLevelsChanged)
                ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientUpdateHighestLevels(m, result.HighestLevels));
            log("XP", "[" + role + "] /level: " + result.Plane.Class + " level " + result.LevelBefore + " -> " + result.Plane.Level +
                " (" + result.Plane.Xp + " XP) | pilot rank " + result.PilotLevel + " (" + result.PilotXp + " XP)" +
                (result.NewUnlocks.Count > 0 ? " | unlocked: " + string.Join(", ", result.NewUnlocks) : ""));
            if (result.PilotLevel > result.PilotLevelBefore) PilotExtras.DeliverCareerRewards(server, connection, role);
            return true;
        }
    }

    // ------------------------------------------------------------------ pilot

    // ------------------------------------------------------------------ pilot XP in real time

    // Pilot XP every plane of the hangar is worth on the client's screen: realTimePlaneXPProportion x
    // PilotXpFromPlaneXp(rank, plane XP), summed in float like Pilot.computedXP (no plane is combat-sim tagged here).
    private static float RealtimePilotXp(Accounts.AccountData d, int pilotLevel)
    {
        float proportion = ProgressionData.RealTimePlaneXpProportion;
        float sum = 0f;
        foreach (Accounts.SavedPlane plane in d.Planes)
            sum += proportion * ProgressionData.PilotXpFromPlaneXp(pilotLevel, plane.Xp);
        return sum;
    }

    // Pilot.computedXP: the pilot XP the client shows (and the one the rank follows).
    public static int ComputedPilotXp(Accounts.AccountData d)
    {
        return ProgressionData.RoundToInt(d.Pilot.BaselineXp + RealtimePilotXp(d, d.Pilot.Level));
    }

    // Baseline that puts the computed XP at the start of a rank (new pilot, /rank, wipe). Never negative:
    // if the planes are already worth more, the computed XP starts above the rank (and may rank up later).
    private static int BaselineForRankStart(Accounts.AccountData d, int level)
    {
        float baseline = ProgressionData.PilotLevels.XpFromLevel(level) - RealtimePilotXp(d, level);
        return Math.Max(0, ProgressionData.RoundToInt(baseline));
    }

    // Raises the rank while the computed XP reaches the next target. The conversion table depends on the rank, so
    // after each rank the baseline moves to keep the computed XP where it was. Returns true if the rank went up.
    internal static bool UpdatePilotRank(Accounts.AccountData d)
    {
        PilotSave pilot = d.Pilot;
        int top = ProgressionData.PilotLevels.TopLevel;
        int before = pilot.Level;
        for (int guard = 0; guard < 1000 && pilot.Level < top; guard++)
        {
            float total = pilot.BaselineXp + RealtimePilotXp(d, pilot.Level);
            int target = Math.Min(top, PilotLevelFromXp(ProgressionData.RoundToInt(total)));
            if (target <= pilot.Level) break;
            pilot.Level = target;
            pilot.BaselineXp = Math.Max(0, ProgressionData.RoundToInt(total - RealtimePilotXp(d, target)));
        }
        return pilot.Level > before;
    }

    // Adds pilot XP (career rewards, admin command). Returns the new rank.
    public static int AddPilotXp(Accounts.AccountData d, int amount)
    {
        PilotSave pilot = d.Pilot;
        pilot.BaselineXp = (int)Math.Clamp((long)pilot.BaselineXp + amount, 0L, int.MaxValue);
        UpdatePilotRank(d);
        return pilot.Level;
    }

    // Sets the pilot rank (admin command /rank): XP at the start of the rank.
    public static void SetPilotLevel(NetServer server, NetConnection connection, string role, int level)
    {
        lock (Game.Lock)
        {
            int target = Math.Clamp(level, 1, ProgressionData.PilotLevels.TopLevel);
            (int levelNow, int xpNow) = Accounts.Change(d =>
            {
                d.Pilot.Level = target;
                d.Pilot.BaselineXp = BaselineForRankStart(d, target);
                return (d.Pilot.Level, ComputedPilotXp(d));
            });
            SendPilotUpdate(server, connection);
            log("PROGRESSION", "[" + role + "] /rank: pilot at rank " + levelNow + " (" + xpNow + " XP)");
        }
    }

    // ClientUpdatePlayer with an empty plane: updates only the pilot rank and baseline XP on the client.
    public static void SendPilotUpdate(NetServer server, NetConnection connection)
    {
        PilotSave pilot = Accounts.Pilot();
        ProgressionMessages.Send(server, connection,
            m => ProgressionMessages.WriteClientUpdatePlayer(m, "", 0, 0, pilot.BaselineXp, pilot.Level, pilot.Unlocks, false));
    }

    private static void SendPlayerUpdate(NetServer server, NetConnection connection, Accounts.SavedPlane plane, int pilotLevel,
        int pilotBaselineXp, List<string> unlocks, bool fromPersistence)
    {
        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientUpdatePlayer(m, plane.CharacterId, plane.Xp, plane.Level,
            pilotBaselineXp, pilotLevel, unlocks, fromPersistence));
    }

    // ------------------------------------------------------------------ death

    // The player's plane was really destroyed (KillReason Destroyed..DestroyedFromDamage or NoDamage).
    // Always sends ClientDeathSummaryInformation: without it the client stays on the crash site forever.
    // A plane change in the hangar (KillReason.ChangePlane) never gets here.
    public static void PlaneDestroyed(NetServer server, NetConnection connection, string role, string? characterId, string? planeClass)
    {
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        lock (Game.Lock)
        {
            if (sessions.TryGetValue(connection.RemoteUniqueIdentifier, out Session? session)) session.PlaneAlive = false;

            ProgressionSettings settings = ProgressionSettingsFile.Current;
            DeathResult result = Accounts.Change(d => ApplyDeath(d, characterId, settings));

            string className = ProgressionData.ClassDisplayName(result.PlaneClass ?? planeClass ?? "walker", result.PlaneVariant ?? "default",
                Game.GetLanguage(connection));
            ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientDeathSummaryInformation(m, result.PlaneXp,
                result.PlaneLevel, className, result.PilotLevelBefore, result.PilotLevelAfter, result.PilotXpDelta, result.PilotXpTotal, false));
            ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientUpdatePlayer(m, "", 0, 0,
                result.PilotBaselineXp, result.PilotLevelAfter, result.Unlocks, false));

            // Buffs the plane had started end with it: the next plane must not inherit them.
            PilotExtras.PlaneBuffsEnded(server, connection, role, characterId);

            if (result.PlaneKept != null)
            {
                // Permadeath off: the client already took the plane out of its list (OnPlayerPlaneDied); it comes back.
                Accounts.SavedPlane kept = result.PlaneKept;
                byte[] account = Accounts.AccountId;
                ProgressionMessages.Send(server, connection, m => PlayerData.WriteClientAddPlane(m, account, kept.CharacterId, kept.Class,
                    kept.Variant, kept.Level, kept.Xp));
            }

            if (result.Missing)
            {
                log("DEATH", "[" + role + "] plane " + (characterId ?? "?") + " destroyed, but it was not in the account: death screen without XP");
            }
            else
            {
                log("DEATH", "[" + role + "] " + result.PlaneClass + " level " + result.PlaneLevel + " (" + result.PlaneXp + " XP) destroyed: +" +
                    result.PilotXpDelta + " pilot XP" + (result.PilotXpBonus > 0 ? " (" + result.PilotXpBonus + " of fall bonus)" : "") +
                    ", rank " + result.PilotLevelBefore + " -> " + result.PilotLevelAfter +
                    " (" + result.PilotXpTotal + " XP)" + (result.PlaneKept != null ? "; the plane stays in the hangar (permadeath off)" :
                    "; the plane left the account"));
            }

            if (result.PilotLevelAfter > result.PilotLevelBefore)
                PilotExtras.DeliverCareerRewards(server, connection, role);
        }
    }

    private sealed class DeathResult
    {
        public bool Missing;
        public string? PlaneClass;
        public string? PlaneVariant;
        public int PlaneXp;
        public int PlaneLevel = 1;
        public int PilotLevelBefore;
        public int PilotLevelAfter;
        // The death screen line "+X pilot XP": the real-time share the plane had already given (it stays with the
        // pilot) plus the bonus of deathPilotXpBonus.
        public int PilotXpDelta;
        public int PilotXpBonus;
        public int PilotXpTotal;
        public int PilotBaselineXp;
        public List<string> Unlocks = new List<string>();
        public Accounts.SavedPlane? PlaneKept;
    }

    private static DeathResult ApplyDeath(Accounts.AccountData d, string? characterId, ProgressionSettings settings)
    {
        PilotSave pilot = d.Pilot;
        var result = new DeathResult { PilotLevelBefore = pilot.Level };
        Accounts.SavedPlane? plane = string.IsNullOrEmpty(characterId) ? null : d.Planes.FirstOrDefault(a => a.CharacterId == characterId);
        if (plane == null)
        {
            result.Missing = true;
            result.PilotLevelAfter = pilot.Level;
            result.PilotBaselineXp = pilot.BaselineXp;
            result.PilotXpTotal = ComputedPilotXp(d);
            result.Unlocks = new List<string>(pilot.Unlocks);
            return result;
        }

        // GameServer.PilotXpFromPlaneXp: the pilot rank before the death picks the conversion table.
        float proportion = ProgressionData.RealTimePlaneXpProportion;
        float multiplier = Math.Max(0f, settings.PilotXpMultiplier);
        int planeScore = ProgressionData.PilotXpFromPlaneXp(pilot.Level, plane.Xp);

        // Bonus: part of what does not show in real time (1 - proportion). With permadeath off the plane can fall many
        // times, so BankedPilotXp keeps the value already paid as bonus.
        int unpaid = Math.Max(0, planeScore - plane.BankedPilotXp);
        float bonus = (1f - proportion) * unpaid * multiplier * Math.Clamp(settings.DeathPilotXpBonus, 0f, 10f);

        float kept;
        if (settings.Permadeath)
        {
            // The client stops counting this plane (OnPlayerPlaneDied removes it): its real-time share goes into the baseline.
            kept = proportion * planeScore;
            d.Planes.Remove(plane);
        }
        else
        {
            // The plane comes back to the hangar and keeps counting in real time.
            kept = 0f;
            plane.BankedPilotXp = planeScore;
        }
        pilot.BaselineXp = (int)Math.Clamp((long)pilot.BaselineXp + ProgressionData.RoundToInt(kept + bonus), 0L, int.MaxValue);
        UpdatePilotRank(d);
        pilot.PlanesLost++;

        result.PlaneClass = plane.Class;
        result.PlaneVariant = plane.Variant;
        result.PlaneXp = plane.Xp;
        result.PlaneLevel = plane.Level;
        result.PilotXpDelta = Math.Max(0, ProgressionData.RoundToInt(kept + bonus));
        result.PilotXpBonus = Math.Max(0, ProgressionData.RoundToInt(bonus));
        result.PilotLevelAfter = pilot.Level;
        result.PilotBaselineXp = pilot.BaselineXp;
        result.PilotXpTotal = ComputedPilotXp(d);
        if (!settings.Permadeath) result.PlaneKept = plane.Copy();

        RefreshUnlocks(d);
        result.Unlocks = new List<string>(pilot.Unlocks);
        return result;
    }

    // ServerRecordLevelupUIShown: what the client already showed.
    public static void LevelupUiShown(string role, int lastXpLevelupShown, int lastLevelupShown)
    {
        Accounts.Change(d =>
        {
            d.Pilot.LastXpLevelupShown = lastXpLevelupShown;
            d.Pilot.LastLevelupShown = lastLevelupShown;
            return 0;
        });
        log("PROGRESSION", "[" + role + "] ServerRecordLevelupUIShown: lastXPLevelupShown=" + lastXpLevelupShown +
            ", lastLevelupShown=" + lastLevelupShown);
    }

    // Wipes the pilot (rank, XP and skills; portrait and unlocks stay). Admin command /reset_pilot.
    public static void WipePilot(NetServer server, NetConnection connection, string role)
    {
        lock (Game.Lock)
        {
            int startLevel = Math.Clamp(ProgressionSettingsFile.Current.StartingPilotLevel, 1, ProgressionData.PilotLevels.TopLevel);
            Accounts.Change(d =>
            {
                PilotSave pilot = d.Pilot;
                pilot.Level = startLevel;
                pilot.BaselineXp = BaselineForRankStart(d, startLevel);
                pilot.LastXpLevelupShown = 0;
                pilot.LastLevelupShown = startLevel;
                pilot.SkillLoadouts = new List<Dictionary<string, int>> { new Dictionary<string, int>() };
                pilot.SkillLoadoutNames = new List<string> { "" };
                pilot.SkillLoadoutIndex = 0;
                pilot.CareerRewardsDelivered.Clear();
                foreach (Accounts.SavedPlane plane in d.Planes) plane.BankedPilotXp = 0;
                return 0;
            });
            ProgressionMessages.Send(server, connection, ProgressionMessages.WriteClientWipePilot);
            SendPilotUpdate(server, connection);
            PilotSkills.SendSkills(server, connection);
            log("PROGRESSION", "[" + role + "] pilot reset (rank " + startLevel + ", skills erased)");
        }
    }

    // ------------------------------------------------------------------ levels, highest levels and unlocks

    public static int PlaneLevelFromXp(int xp)
    {
        return Math.Clamp(ProgressionData.PlaneLevels.LevelFromXp(xp), 1, ProgressionData.PlaneLevels.MaxLevel);
    }

    public static int PilotLevelFromXp(int xp)
    {
        return Math.Clamp(ProgressionData.PilotLevels.LevelFromXp(xp), 1, ProgressionData.PilotLevels.MaxLevel);
    }

    // PlayerData.highestLevels: highest level of each class among the account's planes (never goes down).
    // Returns true if something changed.
    private static bool RefreshHighestLevels(Accounts.AccountData d)
    {
        bool changed = false;
        foreach (Accounts.SavedPlane plane in d.Planes)
        {
            if (!d.Pilot.HighestLevels.TryGetValue(plane.Class, out int highest) || highest < plane.Level)
            {
                d.Pilot.HighestLevels[plane.Class] = plane.Level;
                changed = true;
            }
            if (plane.Level > d.Pilot.HighestPlaneLevel) d.Pilot.HighestPlaneLevel = plane.Level;
        }
        return changed;
    }

    // PlayerData.unlocks: what the account has (only grows). Sources: the walker (always), startingClassUnlocks,
    // the "unlocks" rules of the game (always / classLevelGreater, checked with highestLevels) and progression.json.
    internal static void RefreshUnlocks(Accounts.AccountData d)
    {
        ProgressionSettings settings = ProgressionSettingsFile.Current;
        List<string> unlocks = d.Pilot.Unlocks;

        void Add(string item)
        {
            if (string.IsNullOrWhiteSpace(item)) return;
            // A class id becomes its unlock item ("medic" -> "playerclass_medic").
            string value = ProgressionData.ClassUnlockItems.ContainsKey(item) ? ProgressionData.UnlockItemOf(item) : item;
            if (!unlocks.Contains(value)) unlocks.Add(value);
        }

        foreach (string item in ProgressionData.StartingClassUnlocks) Add(item);
        foreach (string item in settings.StartingUnlocks) Add(item);

        if (settings.UnlockAllClasses)
        {
            foreach (string playerClass in ProgressionData.ClassUnlockItems.Keys) Add(playerClass);
        }

        if (ProgressionData.ClassUnlocks.Count == 0)
        {
            Add("walker");
        }
        else
        {
            foreach (ProgressionData.ClassUnlock rule in ProgressionData.ClassUnlocks)
            {
                if (rule.Conditions.Count == 0) continue;
                bool always = rule.Conditions.Any(c => c.Always);
                bool byLevel = settings.UnlockClassesByLevel && rule.Conditions.All(c =>
                    c.Always || (d.Pilot.HighestLevels.TryGetValue(c.ClassName, out int level) && level >= c.Amount));
                if (always || byLevel) Add(rule.ClassToUnlock);
            }
        }
    }

    // Unlocks of the account right now (for the welcome and the remote player data).
    public static List<string> CurrentUnlocks()
    {
        return Accounts.Read(d => new List<string>(d.Pilot.Unlocks));
    }

    // Skill loadouts the pilot has: the host's base value (progression.json, or the game's base value) plus the ones
    // the account bought in the store (Stores\StoreAccounts).
    public static int SkillLoadoutSlots()
    {
        int slots = ProgressionSettingsFile.Current.SkillLoadoutSlots;
        if (slots <= 0) slots = ProgressionData.BaseSkillLoadoutSlots;
        slots = Math.Clamp(slots, 1, StoreRules.MaxSkillLoadouts) + StoreAccounts.Count(StoreAccounts.SkillLoadoutSlot);
        return Math.Clamp(slots, 1, StoreRules.MaxSkillLoadouts);
    }

    // Text for the /pilot command.
    public static string StatusText(NetConnection connection)
    {
        PilotSave pilot = Accounts.Pilot();
        int pilotXp = Accounts.Read(d => ComputedPilotXp(d));
        ProgressionData.XpTable pilotLevels = ProgressionData.PilotLevels;
        string text = "Pilot: rank " + pilot.Level + ", " + pilotXp + " XP (" + pilot.BaselineXp + " stored + " +
            (ProgressionData.RealTimePlaneXpProportion * 100f).ToString("0.#", Inv) + "% of the planes)";
        if (pilot.Level < pilotLevels.TopLevel)
            text += ", missing " + Math.Max(0, pilotLevels.XpFromLevel(pilot.Level + 1) - pilotXp) + " to the " + (pilot.Level + 1);
        text += ", free skill points " + PilotSkills.PointsAvailable(pilot) + ", loadout " + (pilot.SkillLoadoutIndex + 1) + "/" + SkillLoadoutSlots() +
            ", kills " + pilot.Kills + ", planes lost " + pilot.PlanesLost + ".";

        string? characterId = Game.GetPlaneCharacterId(connection);
        Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
        if (plane != null)
        {
            ProgressionData.XpTable planeLevels = ProgressionData.PlaneLevels;
            int planeScore = ProgressionData.PilotXpFromPlaneXp(pilot.Level, plane.Xp);
            text += " Plane: " + plane.Class + " level " + plane.Level + ", " + plane.Xp + " XP";
            if (!planeLevels.IsMaxLevel(plane.Level))
                text += " (missing " + Math.Max(0, planeLevels.XpFromLevel(plane.Level + 1) - plane.Xp) + ")";
            text += ", worth " + planeScore + " pilot XP (" +
                ProgressionData.RoundToInt(ProgressionData.RealTimePlaneXpProportion * planeScore) + " already count).";
        }
        return text;
    }
}
