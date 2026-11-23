using System.Text.Json;

// The flocks of this server, saved in flocks.json next to the server (the accounts are in accounts\). Every method works under one
// lock and returns copies (snapshots), never the live objects. The error texts are the "fault" of the RPC that asked: the client
// shows the ones it has a text for (guild_name_invalid, joined_recently, limit_reached) and the key itself for the others.
public static class FlockStore
{
    public const string FileName = "flocks.json";

    // GeneralIDType.Guild.
    private const int GuildType = 13;

    private static readonly object gate = new object();
    private static readonly Random random = new Random();
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };
    private static List<Flock> flocks = new List<Flock>();
    private static Action<string, string> log = (_, _) => { };

    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), FileName);

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static void Start(Action<string, string> logger)
    {
        lock (gate)
        {
            log = logger;
            flocks = new List<Flock>();
            if (File.Exists(FilePath))
            {
                try
                {
                    flocks = JsonSerializer.Deserialize<List<Flock>>(File.ReadAllText(FilePath), Options) ?? new List<Flock>();
                }
                catch (Exception e)
                {
                    string copy = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss");
                    try { File.Copy(FilePath, copy, true); } catch { }
                    log("FLOCK", "could not read " + FilePath + " (" + e.Message + "): copy in " + copy + ", starting without flocks");
                    flocks = new List<Flock>();
                }
            }
            flocks.RemoveAll(f => f == null || f.Id.Length == 0);
            foreach (Flock f in flocks)
            {
                f.Members ??= new List<FlockMember>();
                f.Members.RemoveAll(m => m == null || m.UserId.Length == 0);
                foreach (FlockMember m in f.Members)
                {
                    m.MutedBy ??= new List<string>();
                    m.HasMuted ??= new List<string>();
                }
            }
            log("FLOCK", FilePath + ": " + flocks.Count + " flock(s)" +
                (flocks.Count > 0 ? " (" + string.Join(", ", flocks.Take(8).Select(f => f.Name + " " + f.CountMembers)) + (flocks.Count > 8 ? ", ..." : "") + ")" : ""));
        }
    }

    // ------------------------------------------------------------------ queries

    public static Flock? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        lock (gate) return flocks.FirstOrDefault(f => f.Id == id)?.Clone();
    }

    // The flocks this player is a member of (not waiting, not banned).
    public static List<Flock> MembershipsOf(string userId)
    {
        lock (gate) return flocks.Where(f => f.ActiveMember(userId) != null).Select(f => f.Clone()).ToList();
    }

    public static int CountFlocksOf(string userId)
    {
        lock (gate) return CountOf(userId);
    }

    // The flocks whose name has this text (ignoring case), the best matches first.
    public static List<Flock> Search(string? text, int max)
    {
        text = (text ?? "").Trim();
        lock (gate)
        {
            IEnumerable<Flock> found = flocks.Where(f => f.CountMembers > 0);
            if (text.Length > 0) found = found.Where(f => f.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
            return found.OrderByDescending(f => string.Equals(f.Name, text, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(f => f.CountMembers)
                .Take(Math.Max(1, max)).Select(f => f.Clone()).ToList();
        }
    }

    // A flock this player could join (it has room and they are not in it, waiting for it or banned from it). One of the healthiest few.
    public static Flock? Suggest(string userId, long now)
    {
        lock (gate)
        {
            if (CountOf(userId) >= FlockRules.MaxFlocksPerPlayer) return null;
            List<Flock> candidates = Candidates(userId);
            if (candidates.Count == 0) return null;
            var ranked = candidates.OrderByDescending(f => FlockRules.Health(f, now)).ThenByDescending(f => f.CountMembers).ToList();
            int top = Math.Min(ranked.Count, 3);
            return ranked[random.Next(top)].Clone();
        }
    }

    public static bool CanJoinAny(string userId)
    {
        lock (gate) return CountOf(userId) < FlockRules.MaxFlocksPerPlayer && Candidates(userId).Count > 0;
    }

    private static List<Flock> Candidates(string userId)
    {
        int max = FlockRules.MaxMembers;
        List<Flock> open = flocks.Where(f => f.CountMembers > 0 && f.CountMembers < max && f.Member(userId) == null).ToList();
        List<Flock> easy = open.Where(f => f.InviteMode != 2).ToList();
        return easy.Count > 0 ? easy : open;
    }

    // ------------------------------------------------------------------ changes

    public static string? Create(string userId, string userName, string name, string locale, int timezone, out Flock? created)
    {
        created = null;
        name = (name ?? "").Trim();
        if (!FlockRules.ValidName(name)) return "guild_name_invalid";
        lock (gate)
        {
            if (flocks.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) return "guild_name_invalid";
            if (CountOf(userId) >= FlockRules.MaxFlocksPerPlayer) return "limit_reached";
            long now = Now;
            var flock = new Flock
            {
                Id = PlayerData.ToBase64Url(PlayerData.NewId(GuildType)),
                Name = name,
                Locale = string.IsNullOrWhiteSpace(locale) ? "en-US" : locale,
                Timezone = Math.Clamp(timezone, -12, 14),
                Created = now
            };
            flock.Members.Add(new FlockMember
            {
                UserId = userId,
                Name = userName,
                Role = FlockRoles.Leader,
                Founder = true,
                JoinedAt = now,
                ActiveAt = now
            });
            flocks.Add(flock);
            Save();
            created = flock.Clone();
            log("FLOCK", "'" + name + "' created by " + userName);
            return null;
        }
    }

    // pending = true: the player asked and has to wait for the answer of an officer.
    public static string? Join(string userId, string userName, int rank, string flockId, out bool pending, out Flock? after)
    {
        pending = false;
        after = null;
        lock (gate)
        {
            Flock? flock = flocks.FirstOrDefault(f => f.Id == flockId);
            if (flock == null) return "flock_not_found";
            FlockMember? known = flock.Member(userId);
            if (known != null)
            {
                if (known.Banned) return "banned_from_flock";
                pending = known.Pending;
                after = flock.Clone();
                return null;
            }
            if (CountOf(userId) >= FlockRules.MaxFlocksPerPlayer) return "limit_reached";
            if (flock.CountMembers >= FlockRules.MaxMembers) return "limit_reached";

            bool direct = flock.InviteMode == 0 || (flock.InviteMode == 1 && rank >= 100);
            long now = Now;
            flock.Members.Add(new FlockMember { UserId = userId, Name = userName, Pending = !direct, JoinedAt = now, ActiveAt = now });
            flock.Version++;
            Save();
            pending = !direct;
            after = flock.Clone();
            log("FLOCK", userName + (direct ? " joined '" : " asked to join '") + flock.Name + "'");
            return null;
        }
    }

    // The player leaves. A founder who leaves hands the flock to the member who has been in it longest; the last one out ends it.
    public static string? Leave(string userId, string flockId, out Flock? after, out FlockMember? newFounder)
    {
        after = null;
        newFounder = null;
        lock (gate)
        {
            Flock? flock = flocks.FirstOrDefault(f => f.Id == flockId);
            FlockMember? member = flock?.Member(userId);
            if (flock == null || member == null) return "flock_not_found";
            if (member.Banned) return "banned_from_flock";

            bool wasFounder = member.Founder;
            RemoveMember(flock, member);
            if (flock.CountMembers == 0)
            {
                flocks.Remove(flock);
                Save();
                log("FLOCK", "'" + flock.Name + "' ended: the last member left");
                return null;
            }
            if (wasFounder)
            {
                FlockMember next = flock.Members.Where(m => m.IsMember).OrderBy(m => m.JoinedAt <= 0 ? long.MaxValue : m.JoinedAt).First();
                next.Founder = true;
                next.Role = FlockRoles.Leader;
                newFounder = next.Clone();
            }
            flock.Version++;
            Save();
            after = flock.Clone();
            return null;
        }
    }

    // An officer answers a join request: accepted (a member now) or turned down (the request goes away).
    public static string? Resolve(string actorId, string flockId, string userId, bool accept, out Flock? after, out string userName)
    {
        after = null;
        userName = "";
        lock (gate)
        {
            Flock? flock = flocks.FirstOrDefault(f => f.Id == flockId);
            if (flock == null) return "flock_not_found";
            FlockMember? asked = flock.Member(userId);
            if (asked == null || !asked.Pending || asked.Banned) return "no_such_request";
            if (!FlockRoles.CanDo(flock, actorId, userId, FlockPermission.HandleInvites)) return "no_permission";
            userName = asked.Name;
            if (accept)
            {
                if (flock.CountMembers >= FlockRules.MaxMembers) return "limit_reached";
                if (CountOf(userId) >= FlockRules.MaxFlocksPerPlayer) return "limit_reached";
                asked.Pending = false;
                asked.JoinedAt = Now;
                asked.ActiveAt = Now;
            }
            else
            {
                RemoveMember(flock, asked);
            }
            flock.Version++;
            Save();
            after = flock.Clone();
            return null;
        }
    }

    // Kick (the player may come back) or ban (they may not). banned = false lifts a ban.
    public static string? Remove(string actorId, string flockId, string userId, bool ban, bool banned, out Flock? after, out string userName)
    {
        after = null;
        userName = "";
        lock (gate)
        {
            Flock? flock = flocks.FirstOrDefault(f => f.Id == flockId);
            FlockMember? target = flock?.Member(userId);
            if (flock == null || target == null) return "flock_not_found";
            userName = target.Name;
            FlockPermission permission = ban ? FlockPermission.Ban : FlockPermission.Kick;
            if (!FlockRoles.CanDo(flock, actorId, userId, permission)) return "no_permission";
            if (ban && banned)
            {
                target.Banned = true;
                target.Pending = false;
                target.Founder = false;
            }
            else
            {
                RemoveMember(flock, target);
            }
            flock.Version++;
            Save();
            after = flock.Clone();
            return null;
        }
    }

    // A member mutes (or unmutes) another one for themselves. A member muted by enough people is banned.
    public static string? Mute(string actorId, string flockId, string userId, bool muted, out Flock? after, out bool autoBanned)
    {
        after = null;
        autoBanned = false;
        lock (gate)
        {
            Flock? flock = flocks.FirstOrDefault(f => f.Id == flockId);
            if (flock == null) return "flock_not_found";
            if (!FlockRoles.CanDo(flock, actorId, userId, FlockPermission.MuteAndUnMute)) return "no_permission";
            FlockMember actor = flock.ActiveMember(actorId)!;
            FlockMember? target = flock.ActiveMember(userId);
            if (target == null) return "flock_not_found";

            if (muted)
            {
                if (!actor.HasMuted.Contains(userId)) actor.HasMuted.Add(userId);
                if (!target.MutedBy.Contains(actorId)) target.MutedBy.Add(actorId);
                int limit = FlockRules.AutoBanMutes;
                if (limit > 0 && target.MutedBy.Count >= limit)
                {
                    target.Banned = true;
                    target.Founder = false;
                    autoBanned = true;
                }
            }
            else
            {
                actor.HasMuted.Remove(userId);
                target.MutedBy.Remove(actorId);
            }
            flock.Version++;
            Save();
            after = flock.Clone();
            return null;
        }
    }

    // The founder gives a member a role (GuildPermissions.Role: 0 member, 1 officer, 2 leader, 3 elder).
    public static string? SetRole(string actorId, string flockId, string userId, int role, out Flock? after)
    {
        after = null;
        lock (gate)
        {
            Flock? flock = flocks.FirstOrDefault(f => f.Id == flockId);
            if (flock == null) return "flock_not_found";
            if (role < 0 || role > 3) return "invalid_role";
            if (!FlockRoles.CanDo(flock, actorId, userId, FlockPermission.SetRole)) return "no_permission";
            FlockMember? target = flock.ActiveMember(userId);
            if (target == null) return "flock_not_found";
            target.Role = role;
            flock.Version++;
            Save();
            after = flock.Clone();
            return null;
        }
    }

    // New name, description and way of joining (the founder and the leaders).
    public static string? SetInfo(string actorId, string flockId, string name, string desc, int inviteMode, out Flock? after)
    {
        after = null;
        name = (name ?? "").Trim();
        desc = (desc ?? "").Trim();
        lock (gate)
        {
            Flock? flock = flocks.FirstOrDefault(f => f.Id == flockId);
            if (flock == null) return "flock_not_found";
            if (!FlockRoles.CanDo(flock, actorId, null, FlockPermission.ChangeSettings)) return "no_permission";
            if (!FlockRules.ValidName(name)) return "guild_name_invalid";
            if (flocks.Any(f => f != flock && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))) return "guild_name_invalid";
            if (desc.Length > 1024) desc = desc.Substring(0, 1024);
            flock.Name = name;
            flock.Desc = desc;
            flock.InviteMode = Math.Clamp(inviteMode, 0, 2);
            flock.Version++;
            Save();
            after = flock.Clone();
            return null;
        }
    }

    // A player showed up: their name and "last seen" are refreshed in every flock they belong to.
    public static void Touch(string userId, string name)
    {
        lock (gate)
        {
            long now = Now;
            bool changed = false;
            foreach (Flock flock in flocks)
            {
                FlockMember? member = flock.Member(userId);
                if (member == null) continue;
                if (member.Name != name)
                {
                    member.Name = name;
                    changed = true;
                }
                if (now - member.ActiveAt > 120)
                {
                    member.ActiveAt = now;
                    changed = true;
                }
            }
            if (changed) Save();
        }
    }

    // ------------------------------------------------------------------ helpers (inside the lock)

    private static int CountOf(string userId) => flocks.Count(f => f.ActiveMember(userId) != null);

    private static void RemoveMember(Flock flock, FlockMember member)
    {
        flock.Members.Remove(member);
        foreach (FlockMember other in flock.Members)
        {
            other.MutedBy.Remove(member.UserId);
            other.HasMuted.Remove(member.UserId);
        }
    }

    private static void Save()
    {
        try
        {
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(flocks, Options));
            File.Move(temp, FilePath, true);
        }
        catch (Exception e)
        {
            log("FLOCK", "could not write " + FilePath + ": " + e.Message);
        }
    }
}
