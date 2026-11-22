using Lidgren.Network;

// Plane XP boost: the "planexpboost" consumables (PlaneXpBoostAction: additionalPercentage for duration seconds).
//
// The boost lives in the account (PilotSave.PlaneXpBoostPercent / PlaneXpBoostUntil, unix seconds). The client shows it in its buff
// list (ClientUpdatePlayerDataPlaneXpBoost) and the server multiplies the XP of every kill while it lasts, like
// GameServer.GetXpActuallyAwarded: xp += xp * percentage / 100 (before the per-kill limit).
public static class PlaneXpBoost
{
    public const ushort MsgClientUpdatePlayerDataPlaneXpBoost = 48944;   // int percentage, int untilUnixTime

    public static int UnixNow() => (int)Math.Min(int.MaxValue, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

    public static bool IsActive(PilotSave pilot) => pilot.PlaneXpBoostPercent > 0 && pilot.PlaneXpBoostUntil >= UnixNow();

    // Percentage in force right now (0 = none).
    public static int ActivePercent(PilotSave pilot) => IsActive(pilot) ? pilot.PlaneXpBoostPercent : 0;

    // Boosted amount of a kill's XP.
    public static int Boost(PilotSave pilot, int amount)
    {
        int percent = ActivePercent(pilot);
        return percent <= 0 ? amount : amount + (int)((long)amount * percent / 100);
    }

    // Starts (or lengthens) the boost. PlaneXpBoostAction.IsPossibleToConsume refuses a weaker boost while a stronger one lasts;
    // the same percentage adds the time, a stronger one replaces the weaker.
    public static string Grant(NetServer server, NetConnection connection, string role, int percent, int durationSeconds)
    {
        if (percent <= 0 || durationSeconds <= 0) return "boost without percentage or duration";
        (int Percent, int Until, string Text) now = Accounts.Change(d =>
        {
            PilotSave p = d.Pilot;
            int unix = UnixNow();
            bool active = IsActive(p);
            string text;
            if (active && percent < p.PlaneXpBoostPercent)
            {
                text = "a stronger boost (+" + p.PlaneXpBoostPercent + "%) is running: kept";
            }
            else if (active && percent == p.PlaneXpBoostPercent)
            {
                p.PlaneXpBoostUntil = (int)Math.Min(int.MaxValue, (long)p.PlaneXpBoostUntil + durationSeconds);
                text = "+" + percent + "% plane XP: " + durationSeconds + "s added";
            }
            else
            {
                p.PlaneXpBoostPercent = percent;
                p.PlaneXpBoostUntil = (int)Math.Min(int.MaxValue, (long)unix + durationSeconds);
                text = "+" + percent + "% plane XP for " + durationSeconds + "s";
            }
            return (p.PlaneXpBoostPercent, p.PlaneXpBoostUntil, text);
        });
        Send(server, connection, now.Percent, now.Until);
        return now.Text;
    }

    public static void Send(NetServer server, NetConnection connection, int percent, int until)
    {
        ProgressionMessages.Send(server, connection, m => Write(m, percent, until));
    }

    public static void Write(NetBuffer buf, int percent, int until)
    {
        buf.Write(MsgClientUpdatePlayerDataPlaneXpBoost);
        buf.Write(percent);
        buf.Write(until);
    }
}
