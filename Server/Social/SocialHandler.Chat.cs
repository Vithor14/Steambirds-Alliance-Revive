using Lidgren.Network;

// Chat between players that is not "everybody around": whispers (/w NAME text) and the flock channel (/f text).
public static partial class SocialHandler
{
    private const int MaxChatLength = 200;

    // ServerWhisperChat(string text, string recipientName): the recipient gets ClientWhisperChat. The sender's own client already
    // showed the line (">>> name"), so only a failure comes back.
    private static void Whisper(NetServer server, NetIncomingMessage msg, NetConnection connection, string role)
    {
        string text = (msg.ReadString() ?? "").Trim();
        string recipientName = (msg.ReadString() ?? "").Trim();
        Accounts.Account me = Accounts.Current;
        if (text.Length == 0) return;
        if (text.Length > MaxChatLength) text = text.Substring(0, MaxChatLength);

        Presence.Here? to = Presence.FindByName(recipientName);
        if (to == null || to.AccountId == me.IdText)
        {
            string answer = SocialText.Ui(Game.GetLanguage(connection), "command_generic_player_not_found", "No player found named {0}.", recipientName);
            WorldObjects.SendChat(server, connection, answer);
            return;
        }
        if (IsBlockedBy(to.AccountId, me)) return;      // they blocked me: it goes nowhere

        DefaultFlock(me, out byte[]? flockId, out string flockName);
        NetOutgoingMessage m = to.Server.CreateMessage();
        m.Write(SocialWire.MsgWhisperChat);
        SocialWire.WriteId(m, me.Id);
        m.Write(me.Name);
        m.Write(text);
        SocialWire.WriteId(m, flockId);
        m.Write(flockName);
        m.Write(Accounts.Pilot().Level);
        to.Server.SendMessage(m, to.Connection, NetDeliveryMethod.ReliableOrdered, 0);
        log("CHAT", "[whisper] " + me.Name + " -> " + to.Name + ": " + text);
    }

    // ServerGuildChat(string text): everybody of the sender's default flock who is online gets ClientGuildChat, the sender too (the
    // client does not show its own /f line by itself). Members who muted the sender do not get it.
    private static void FlockChat(NetServer server, NetIncomingMessage msg, NetConnection connection, string role)
    {
        string text = (msg.ReadString() ?? "").Trim();
        Accounts.Account me = Accounts.Current;
        if (text.Length == 0) return;
        if (text.Length > MaxChatLength) text = text.Substring(0, MaxChatLength);

        Flock? flock = null;
        if (FlockRules.Enabled && DefaultFlock(me, out byte[]? id, out _) && id != null) flock = FlockStore.Get(PlayerData.ToBase64Url(id));
        FlockMember? sender = flock?.ActiveMember(me.IdText);
        if (flock == null || sender == null)
        {
            WorldObjects.SendChat(server, connection, SocialText.Ui(Game.GetLanguage(connection), "guild_chat_no_guild",
                "You can not send flock messages because you are not in any flock. You need to join a flock first."));
            return;
        }

        int rank = Accounts.Pilot().Level;
        int delivered = 0;
        foreach (FlockMember member in flock.Members.Where(m => m.IsMember))
        {
            if (member.UserId != me.IdText && (member.HasMuted.Contains(me.IdText) || sender.MutedBy.Contains(member.UserId))) continue;
            Presence.Here? there = Presence.Find(member.UserId);
            if (there == null) continue;

            NetOutgoingMessage m = there.Server.CreateMessage();
            m.Write(SocialWire.MsgGuildChat);
            SocialWire.WriteId(m, me.Id);
            m.Write(me.Name);
            m.Write(text);
            SocialWire.WriteIdText(m, flock.Id);
            m.Write(flock.Name);
            m.Write(rank);
            m.Write(sender.Founder);
            m.Write((byte)sender.Role);
            there.Server.SendMessage(m, there.Connection, NetDeliveryMethod.ReliableOrdered, 0);
            delivered++;
        }
        FlockStore.Touch(me.IdText, me.Name);
        log("CHAT", "[flock " + flock.Name + "] " + me.Name + ": " + text + " -> " + delivered + " player(s)");
    }
}
