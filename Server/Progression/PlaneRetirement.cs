using System.Globalization;
using Lidgren.Network;

// Hangar "Retire plane" (ServerRetirePlane): the pilot gives a plane up on purpose.
//
// What the client shows (SuicideSubmenu.SetTexts): the plane's pilot score (PilotXpFromPlaneXp at the current rank) times
// (1 - realTimePlaneXPProportion) is the pilot XP the retirement pays, and that XP / pilotXpPer1Currency becomes currency
// (pilotXpCurrencyId), only from pilotXpCurrencyAwardThreshold on. The other part (realTimePlaneXPProportion) was already
// counting in real time: when the plane leaves the hangar it goes into the baseline so the pilot XP does not drop.
// The client then waits for ClientRemovePlane to take the plane out of its hangar (GameClient.ClientRemovePlane).
public static class PlaneRetirement
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed class Result
    {
        public Accounts.SavedPlane Plane = null!;
        public int Paid;                     // pilot XP of the retirement (the part that was not counting yet)
        public int Currency;                 // currency paid for that XP
        public int Shown;                    // the pilot XP the currency is counted on (what the retire window shows)
        public int PilotLevelBefore;
        public int PilotLevelAfter;
        public int BaselineXp;
        public List<string> Unlocks = new List<string>();
    }

    public static void Retire(NetServer server, NetConnection connection, string role, string characterId)
    {
        using Accounts.Scope accountScope = Accounts.UseFor(connection);
        lock (Game.Lock)
        {
            ProgressionSettings settings = ProgressionSettingsFile.Current;
            Accounts.SavedPlane? plane = Accounts.GetPlane(characterId);
            if (plane == null)
            {
                InventoryHandler.Log("PLANE", "[" + role + "] ServerRetirePlane: '" + characterId + "' is not in the account (ignored)");
                return;
            }

            // The client still has the plane's inventory in the record: send the removal before the inventory goes away.
            byte[] account = Accounts.AccountId;
            Accounts.SavedPlane sent = plane.Copy();
            ProgressionMessages.Send(server, connection, m => PlayerData.WriteClientRemovePlane(m, account, sent.CharacterId, sent.Class,
                sent.Variant, sent.Level, sent.Xp));

            Result result = Accounts.Change(d => Apply(d, characterId, settings));

            string summary = InventoryHandler.PlaneRetired(server, connection, role, characterId);

            PayCurrencyForPilotXp(server, connection, result.Currency, result.Shown);

            ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientUpdatePlayer(m, "", 0, 0,
                result.BaselineXp, result.PilotLevelAfter, result.Unlocks, false));

            InventoryHandler.Log("PLANE", "[" + role + "] " + result.Plane.Class + " level " + result.Plane.Level + " (" + result.Plane.Xp +
                " XP) retired: +" + result.Paid.ToString(Inv) + " pilot XP, +" + result.Currency.ToString(Inv) + " " + ProgressionData.PilotXpCurrencyId +
                ", rank " + result.PilotLevelBefore + " -> " + result.PilotLevelAfter + "; items: " + summary);

            PilotExtras.PlaneBuffsEnded(server, connection, role, characterId);

            if (result.PilotLevelAfter > result.PilotLevelBefore)
                PilotExtras.DeliverCareerRewards(server, connection, role);
        }
    }

    // Pilot XP turned into currency (global.pilotXpPer1Currency, pilotXpCurrencyId, pilotXpCurrencyAwardThreshold of the game's config):
    // the currency goes into the account, the client gets the new totals and ClientNotificationGiveCurrencyForPilotXp `54391`,
    // which shows "you got N <currency> for X pilot XP". Used when a plane is retired and when it is destroyed.
    public static void PayCurrencyForPilotXp(NetServer server, NetConnection connection, int currency, int pilotXp)
    {
        if (currency <= 0) return;
        string currencyId = ProgressionData.PilotXpCurrencyId;
        Dictionary<string, int> currencies = InventoryStore.Change(d =>
        {
            InventoryStore.AddCurrency(d, currencyId, currency);
            return new Dictionary<string, int>(d.Currencies);
        });
        InventoryHandler.SendCurrencies(server, connection, currencies);
        ProgressionMessages.Send(server, connection, m => ProgressionMessages.WriteClientNotificationGiveCurrencyForPilotXp(m, currency, currencyId, pilotXp));
    }

    private static Result Apply(Accounts.AccountData d, string characterId, ProgressionSettings settings)
    {
        PilotSave pilot = d.Pilot;
        Accounts.SavedPlane plane = d.Planes.First(a => a.CharacterId == characterId);
        var result = new Result { Plane = plane.Copy(), PilotLevelBefore = pilot.Level };

        float proportion = ProgressionData.RealTimePlaneXpProportion;
        float multiplier = Math.Max(0f, settings.PilotXpMultiplier);
        int planeScore = ProgressionData.PilotXpFromPlaneXp(pilot.Level, plane.Xp);

        // The real-time share stops counting with the plane: it goes into the baseline. The rest is what the retirement pays
        // (what a fall already paid as bonus, with permadeath off, is not paid twice).
        float kept = proportion * planeScore;
        int unpaid = Math.Max(0, planeScore - plane.BankedPilotXp);
        float paid = (1f - proportion) * unpaid * multiplier;

        d.Planes.Remove(plane);
        pilot.BaselineXp = (int)Math.Clamp((long)pilot.BaselineXp + ProgressionData.RoundToInt(kept + paid), 0L, int.MaxValue);
        PilotProgression.UpdatePilotRank(d);

        // The currency uses the same figure the client shows in the retire window (without the host's XP multiplier).
        int shown = (int)Math.Floor((1f - proportion) * planeScore);
        int currency = ProgressionData.PilotXpPer1Currency > 0 ? shown / ProgressionData.PilotXpPer1Currency : 0;
        if (currency < ProgressionData.PilotXpCurrencyAwardThreshold) currency = 0;

        PilotProgression.RefreshUnlocks(d);
        result.Paid = Math.Max(0, ProgressionData.RoundToInt(paid));
        result.Currency = currency;
        result.Shown = shown;
        result.PilotLevelAfter = pilot.Level;
        result.BaselineXp = pilot.BaselineXp;
        result.Unlocks = new List<string>(pilot.Unlocks);
        return result;
    }
}
