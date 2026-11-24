// Rules of the safe trade between two players: the numbers of the game's own "trade" config (the configBinary has the whole TradeConfig:
// slot limit, timeout, the currency of the fee, the item values and the tax percentages) and the host's switches in social.json.
//
// What the original server did with those numbers is not in the client, so the tax is a reconstruction:
//   value of the trade = the value of every offered item (the first "itemTradeValues" entry that fits its tier, tags and soulbound
//                        state, or "defaultValue"), plus the gold offered when the fee is paid in the main currency;
//   fee                = value x marketTaxPercentage100 / 10000, never below minTaxPerTransactionInCurrency (0 when nothing is offered);
//   two flockmates     = marketTaxPercentageSameGuild100 instead (hasSpecialSameGuildTax).
// The fee is paid in gold or iron (the players pick) by one of them or half each, and disappears (a tax). A host who does not want
// it turns it off with "tradeTaxes": false in social.json.
public static class SecureTradeRules
{
    // FeeMode and FeeCurrencyMode of the client.
    public const byte FeeSplit = 0;
    public const byte FeeInitiator = 1;
    public const byte FeeTarget = 2;
    public const byte CurrencyMain = 0;
    public const byte CurrencyExtra = 1;

    private static ConfigNode Config => ConfigBinary.Get("trade");
    private static SocialSettings Host => SocialSettingsFile.Current;

    public static bool Enabled => Host.TradeEnabled;
    public static bool TaxesOn => Host.TradeTaxes;

    public static string MainCurrency => Config["feeCurrencyKey"].AsString("gold");
    public static string ExtraCurrency => Config["feeExtraCurrencyKey"].AsString("iron");
    public static string FeeCurrencyKey(byte mode) => mode == CurrencyExtra ? ExtraCurrency : MainCurrency;

    // Most items each player can put in the offer.
    public static int SlotLimit => Math.Max(1, Config["itemTradeSlotLimit"].AsInt(4));

    // Seconds a request waits for the answer.
    public static double RequestSeconds => Math.Max(5f, Config["timeout"].AsFloat(60f));

    // A "decline" that arrives this soon after the request is ignored. The client's TradeTrigger (the area spawned at the requester's plane
    // that the target flies into to accept) declines by itself the moment the requester's plane leaves that area, about 1.3 s after the
    // request in the 24/11 test, and then the target could no longer answer. A person needs longer than this to type /trade_reject.
    public const double EarlyDeclineSeconds = 4;

    // Seconds an open trade may stay without changes before the server closes it.
    public const double IdleSeconds = 600;

    // Manager.IsPossibleToTrade of the client, with the current time.
    public static bool IsTradable(StoredItem item, out string reason)
    {
        reason = "";
        ItemDef? def = ItemCatalog.Get(item.DefId);
        if (def == null) { reason = "unknown item"; return false; }
        if (item.Salvaged) { reason = "salvaged"; return false; }
        if (!def.Tradable) { reason = "not tradable"; return false; }
        if (item.Uses < def.Uses) { reason = "already used"; return false; }
        if (item.HasDecayed(DateTimeOffset.UtcNow.ToUnixTimeSeconds())) { reason = "decayed"; return false; }
        if (def.Node["notTradableIfUsed"].AsBool(false) && item.Used) { reason = "used"; return false; }
        return true;
    }

    // The fee of the whole trade, in the currency of the offer (0 when taxes are off or nothing is offered).
    public static int Fee(IReadOnlyList<StoredItem> items, int mainCurrencyOffered, byte feeCurrency, bool sameFlock)
    {
        if (!TaxesOn) return 0;
        if (items.Count == 0 && mainCurrencyOffered <= 0) return 0;

        bool extra = feeCurrency == CurrencyExtra;
        long value = 0;
        foreach (StoredItem item in items) value += ValueOf(item, extra);
        if (!extra) value += Math.Max(0, mainCurrencyOffered);

        bool special = sameFlock && Config["hasSpecialSameGuildTax"].AsBool(true);
        int percent100 = extra
            ? (special ? Config["marketTaxExtraPercentageSameGuild100"].AsInt(0) : Config["marketTaxExtraPercentage100"].AsInt(0))
            : (special ? Config["marketTaxPercentageSameGuild100"].AsInt(0) : Config["marketTaxPercentage100"].AsInt(0));
        int minimum = extra ? Config["minTaxPerTransactionInCurrencyExtra"].AsInt(0) : Config["minTaxPerTransactionInCurrency"].AsInt(0);

        long fee = (value * Math.Max(0, percent100) + 9999) / 10000;
        return (int)Math.Clamp(Math.Max(fee, Math.Max(0, minimum)), 0, 1_000_000_000);
    }

    // One line with the numbers of the game's config that this server uses (logged at the first trade request).
    public static string Describe()
    {
        ConfigNode c = Config;
        if (c.IsMissing) return "the configBinary has no 'trade' config: defaults (4 slots, 60 s, no tax)";
        return "slots " + SlotLimit + ", request " + RequestSeconds + " s, fee in " + MainCurrency + " or " + ExtraCurrency + ", tax " +
               c["marketTaxPercentage100"].AsInt(0) / 100.0 + "% (min " + c["minTaxPerTransactionInCurrency"].AsInt(0) + ") / extra " +
               c["marketTaxExtraPercentage100"].AsInt(0) / 100.0 + "% (min " + c["minTaxPerTransactionInCurrencyExtra"].AsInt(0) + "), flockmates " +
               c["marketTaxPercentageSameGuild100"].AsInt(0) / 100.0 + "%, " + c["itemTradeValues"].Count + " item value rule(s), default value " +
               c["defaultValue"].AsInt(0) + ", taxes " + (TaxesOn ? "on" : "OFF");
    }

    // Is the guild tax rate the one in force (two members of the same flock)? The client shows a note when so.
    public static bool GuildRateApplies(bool sameFlock) => TaxesOn && sameFlock && Config["hasSpecialSameGuildTax"].AsBool(true);

    private static int ValueOf(StoredItem item, bool extra)
    {
        ItemDef? def = ItemCatalog.Get(item.DefId);
        foreach (ConfigNode entry in Config["itemTradeValues"].Items)
        {
            int tier = entry["tier"].AsInt(-1);
            if (tier >= 0 && (def == null || def.Tier != tier)) continue;
            if (entry["filterOnSoulbound"].AsBool(false) && entry["soulbound"].AsBool(false) != item.Soulbound) continue;
            bool tagsOk = true;
            foreach (ConfigNode tag in entry["extraTags"].Items)
            {
                if (def == null || !def.HasTag(tag.AsString("") )) { tagsOk = false; break; }
            }
            if (!tagsOk) continue;
            return extra ? entry["valueExtra"].AsInt(0) : entry["value"].AsInt(0);
        }
        return extra ? Config["defaultValueExtra"].AsInt(0) : Config["defaultValue"].AsInt(0);
    }
}
