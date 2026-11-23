using Lidgren.Network;

// Going to another player: "Join Server" in the friend list and in the flock roster, and /tp NAME for a friend who is on another server.
//
// On the same server the client teleports by itself (radar, friend list "Teleport", /tp NAME): it finds the other plane among the
// units it already sees and moves its own plane there; the server only hears ServerNotifyCompleteTeleport. Only the way between
// servers goes through here, because the client cannot see a plane that is not on its server:
//  - ServerSendMeToPlayerServer(GeneralID target): the target must be online, joinable (the Nexus, or the open world while it has room)
//    and a friend or flockmate of the player. On the same server the plane is moved next to the target (ClientTeleportToCoordinatesAfterTime);
//    on another one the client is switched there (Nexus <-> open world) and the answer to its spawn request puts the new plane
//    next to the target (ArrivalPlace).
//  - A refusal is ClientSendMeToPlayerServerFailed with a text of the client: "Unable to join NAME: Server is currently not open"
//    or "...: No server found."
public static partial class SocialHandler
{
    private const string NotFound = "error_message_unable_to_join_other_player_server_not_found";
    private const string NotJoinable = "error_message_unable_to_join_other_player_server_not_joinable";

    // ServerNotifyCompleteTeleport(GeneralID teleportTargetPlayer, int teleportTargetId, TeleportTargetType type): the client finished a
    // teleport (it moved itself). Only for the log: the position the client reports goes on as usual.
    private static void CompleteTeleport(NetIncomingMessage msg, NetConnection connection, string role)
    {
        byte[] player = CopperReader.ReadGeneralId(msg);
        int unit = msg.ReadInt32();
        byte type = msg.ReadByte();
        string[] types = { "player on this server", "squad leader", "quest target", "portal", "Nexus", "player on another server" };
        string target = type == 0 ? PlayerRelay.NameOfRelayId(unit) : type == 5 ? AccountDirectory.NameOf(PlayerData.ToBase64Url(player)) : "";
        log("TELEPORT", "[" + role + "] " + PlayerSessions.NameOf(connection) + " teleported to " + (type < types.Length ? types[type] : "type " + type) +
            (target.Length > 0 ? " " + target : "") + (unit != 0 ? " (unit " + unit + ")" : ""));
    }

    // ServerSendMeToPlayerServer(GeneralID targetPlayer).
    private static void SendMeToPlayer(NetServer server, NetIncomingMessage msg, NetConnection connection, string role)
    {
        byte[] target = CopperReader.ReadGeneralId(msg);
        Accounts.Account me = Accounts.Current;
        string targetId = PlayerData.ToBase64Url(target);
        string targetName = AccountDirectory.NameOf(targetId);
        Presence.Here? mine = Presence.Of(connection);
        Presence.Here? there = Presence.Find(targetId);

        string refusal = "";
        if (mine == null || !SocialSettingsFile.Current.JoinPlayerServers) refusal = NotJoinable;
        else if (targetId == me.IdText || there == null) refusal = NotFound;
        else if (!HasBond(me, targetId) || IsBlockedBy(targetId, me)) refusal = NotFound;
        else if (!Presence.IsJoinable(there)) refusal = NotJoinable;
        if (refusal.Length > 0)
        {
            SocialWire.SendJoinFailed(server, connection, target, refusal);
            log("TELEPORT", "[" + role + "] " + me.Name + " tried to join " + targetName + ": " + (refusal == NotFound ? "no server found" : "not joinable"));
            return;
        }

        if (mine!.Role == there!.Role)
        {
            // The same server: the plane just flies next to the player.
            if (!PlayerRelay.PositionOfAccount(target, out float x, out float y, out float rotation))
            {
                SocialWire.SendJoinFailed(server, connection, target, NotFound);
                log("TELEPORT", "[" + role + "] " + me.Name + " tried to join " + targetName + ": the plane is not in the world yet");
                return;
            }
            (float nx, float ny) = PlaceNear(there.Role, connection, x, y);
            WorldObjects.SendTeleport(server, connection, nx, ny, rotation, 0.2f);
            log("TELEPORT", "[" + role + "] " + me.Name + " joined " + targetName + " on the same server (" + nx.ToString("0") + ", " + ny.ToString("0") + ")");
            return;
        }

        string why = "joining " + targetName + " (" + Presence.PlaceName(there) + ")";
        if (there.Role == Presence.RoleWorld) SwitchToWorld?.Invoke(server, connection, why);
        else SwitchToNexus?.Invoke(server, connection, why);
    }

    // ServerRequestSpawnPlayerPlane carries the account the player was going to join (the "teleportOnJoinTargetAccountId" of the client).
    // Where the new plane is born: next to that player, when it is on this server and the player may join them. Null = the spawn point.
    public static (float X, float Y, float Rotation)? ArrivalPlace(NetConnection connection, byte[]? target)
    {
        if (target == null || target.Length < 16 || IsNone(target)) return null;
        SocialSettings settings = SocialSettingsFile.Current;
        if (!settings.SpawnNearTarget || !settings.JoinPlayerServers) return null;

        Accounts.Account me = Accounts.Current;
        string targetId = PlayerData.ToBase64Url(target);
        Presence.Here? mine = Presence.Of(connection);
        Presence.Here? there = Presence.Find(targetId);
        if (mine == null || there == null || there.Role != mine.Role || targetId == me.IdText) return null;
        if (!HasBond(me, targetId) || IsBlockedBy(targetId, me)) return null;
        if (!PlayerRelay.PositionOfAccount(target, out float x, out float y, out float rotation)) return null;

        (float nx, float ny) = PlaceNear(there.Role, connection, x, y);
        log("TELEPORT", me.Name + " is born next to " + AccountDirectory.NameOf(targetId) + " (" + nx.ToString("0") + ", " + ny.ToString("0") + ")");
        return (nx, ny, rotation);
    }

    // A spot ArrivalDistance units from (x, y): free land in the open world (a few bearings are tried), simply beside the player elsewhere.
    private static (float X, float Y) PlaceNear(byte role, NetConnection connection, float x, float y)
    {
        float distance = SocialSettingsFile.Current.ArrivalDistance;
        var random = new Random();
        double start = random.NextDouble() * Math.PI * 2.0;
        for (int i = 0; i < 8; i++)
        {
            double angle = start + i * Math.PI / 4.0;
            float nx = x + (float)Math.Sin(angle) * distance;
            float ny = y + (float)Math.Cos(angle) * distance;
            if (role != Presence.RoleWorld || WorldServer.IsFreeLand(connection, nx, ny)) return (nx, ny);
        }
        return (x, y);
    }
}
