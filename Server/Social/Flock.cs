// A flock (guild): the data the host keeps in flocks.json. The client's GuildInfo is made from this (SocialWire).
//
// Roles and permissions are the client's own (GuildPermissions): the founder can do everything; an Officer can mute, kick, ban and
// handle the invites; a Leader can also change the settings; Members and Elders (ceremonial) can only mute. Setting a role is the
// founder's.
public sealed class FlockMember
{
    // accountId (base64url) and the name the player had the last time we saw them.
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";

    // GuildPermissions.Role: 0 Member, 1 Officer, 2 Leader, 3 Elder.
    public int Role { get; set; }
    public bool Founder { get; set; }

    // Asked to join and waiting for somebody to accept (the "Pending Invites" page).
    public bool Pending { get; set; }
    public bool Banned { get; set; }

    // Unix seconds.
    public long JoinedAt { get; set; }
    public long ActiveAt { get; set; }

    // Personal mutes: the accounts that muted this member, and the accounts this member muted.
    public List<string> MutedBy { get; set; } = new List<string>();
    public List<string> HasMuted { get; set; } = new List<string>();

    // Counts as a member (not waiting, not banned).
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMember => !Pending && !Banned;

    public FlockMember Clone()
    {
        return new FlockMember
        {
            UserId = UserId,
            Name = Name,
            Role = Role,
            Founder = Founder,
            Pending = Pending,
            Banned = Banned,
            JoinedAt = JoinedAt,
            ActiveAt = ActiveAt,
            MutedBy = new List<string>(MutedBy),
            HasMuted = new List<string>(HasMuted)
        };
    }
}

public sealed class Flock
{
    // accountId-style GeneralID (type Guild) in base64url.
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";

    // GuildInviteMode: 0 open for all, 1 open for rank 100+, 2 invite only.
    public int InviteMode { get; set; }
    public string Locale { get; set; } = "en-US";
    public int Timezone { get; set; }
    public long Created { get; set; }

    // Goes up with every change, so the client can tell a newer copy from an older one.
    public int Version { get; set; } = 1;
    public List<FlockMember> Members { get; set; } = new List<FlockMember>();

    public FlockMember? Member(string? userId)
    {
        return userId == null ? null : Members.FirstOrDefault(m => m.UserId == userId);
    }

    // A member that really belongs to the flock.
    public FlockMember? ActiveMember(string? userId)
    {
        FlockMember? member = Member(userId);
        return member != null && member.IsMember ? member : null;
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public FlockMember? FounderMember => Members.FirstOrDefault(m => m.Founder && m.IsMember);

    [System.Text.Json.Serialization.JsonIgnore]
    public int CountMembers => Members.Count(m => m.IsMember);

    [System.Text.Json.Serialization.JsonIgnore]
    public int CountPending => Members.Count(m => m.Pending && !m.Banned);

    // Members seen in the last "seconds".
    public int CountActive(long now, int seconds) => Members.Count(m => m.IsMember && now - m.ActiveAt <= seconds);

    public Flock Clone()
    {
        return new Flock
        {
            Id = Id,
            Name = Name,
            Desc = Desc,
            InviteMode = InviteMode,
            Locale = Locale,
            Timezone = Timezone,
            Created = Created,
            Version = Version,
            Members = Members.Select(m => m.Clone()).ToList()
        };
    }
}

// What each permission of the client's GuildPermissions.Permission lets one do.
public enum FlockPermission
{
    Kick,
    Ban,
    MuteAndUnMute,
    HandleInvites,
    ChangeSettings,
    SetRole
}

public static class FlockRoles
{
    public const int Member = 0;
    public const int Officer = 1;
    public const int Leader = 2;
    public const int Elder = 3;

    // GuildPermissions.Has.
    public static bool Has(int role, bool founder, FlockPermission permission)
    {
        if (founder) return true;
        switch (role)
        {
            case Member:
            case Elder:
                return permission == FlockPermission.MuteAndUnMute;
            case Officer:
                return permission == FlockPermission.MuteAndUnMute || permission == FlockPermission.Kick ||
                       permission == FlockPermission.Ban || permission == FlockPermission.HandleInvites;
            case Leader:
                return permission == FlockPermission.MuteAndUnMute || permission == FlockPermission.Kick ||
                       permission == FlockPermission.Ban || permission == FlockPermission.HandleInvites ||
                       permission == FlockPermission.ChangeSettings;
            default:
                return false;
        }
    }

    // GuildPermissions.CanDo: "actor" does it to "target" (null = to the flock itself).
    public static bool CanDo(Flock flock, string actor, string? target, FlockPermission permission)
    {
        if (target != null && target == actor) return false;
        FlockMember? who = flock.ActiveMember(actor);
        if (who == null) return false;
        if (target != null)
        {
            FlockMember? other = flock.Member(target);
            if (other == null || other.Founder) return false;
        }
        return Has(who.Role, who.Founder, permission);
    }
}
