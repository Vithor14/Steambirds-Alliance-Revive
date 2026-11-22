using System.Globalization;
using Lidgren.Network;

// What a plane looks like beyond its variant: the three paint colors, the livery and the two trails (Decoration of the client).
// Dyes and trails (consumables with the "deco" action) change them. The client already repaints its plane when it uses the item
// (DecorationAction.Perform); the server keeps the result in the account so the next login and the other players get it.
//
// Colors are saved as "rrggbbaa" hex text, trails by their id in the game's config (the network id is the Mono hash of the id, like
// every NetIdentifiedConfig without hashOverride).
public sealed class SavedDecoration
{
    public string? Color1 { get; set; }
    public string? Color2 { get; set; }
    public string? Color3 { get; set; }
    public string? Livery { get; set; }
    public string? NormalTrail { get; set; }
    public string? SuperTrail { get; set; }

    public SavedDecoration Copy() => (SavedDecoration)MemberwiseClone();

    public bool IsEmpty => Color1 == null && Color2 == null && Color3 == null && string.IsNullOrEmpty(Livery) &&
                           NormalTrail == null && SuperTrail == null;
}

public static class PlaneDecoration
{
    // DecorationSerializer: a byte of flags (1/2/4 colors, 8 livery, 16/32 trails) followed by what is present.
    public static void Write(NetBuffer buf, SavedDecoration? deco)
    {
        if (deco == null || deco.IsEmpty)
        {
            buf.Write((byte)0);
            return;
        }
        uint? c1 = Parse(deco.Color1), c2 = Parse(deco.Color2), c3 = Parse(deco.Color3);
        string livery = deco.Livery ?? "";
        bool normal = !string.IsNullOrEmpty(deco.NormalTrail);
        bool super = !string.IsNullOrEmpty(deco.SuperTrail);
        byte flags = (byte)((c1.HasValue ? 1 : 0) | (c2.HasValue ? 2 : 0) | (c3.HasValue ? 4 : 0) | (livery.Length > 0 ? 8 : 0) |
                            (normal ? 16 : 0) | (super ? 32 : 0));
        buf.Write(flags);
        if (c1.HasValue) buf.Write(c1.Value);
        if (c2.HasValue) buf.Write(c2.Value);
        if (c3.HasValue) buf.Write(c3.Value);
        if (livery.Length > 0) buf.Write(livery);
        if (normal) buf.Write(MonoHash.TemplateHash(deco.NormalTrail!));
        if (super) buf.Write(MonoHash.TemplateHash(deco.SuperTrail!));
    }

    // ColorSerializer: r, g, b, a in one uint (r in the high byte). Accepts "rrggbb", "#rrggbb" and "rrggbbaa".
    private static uint? Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        string h = hex.Trim().TrimStart('#');
        if (h.Length == 6) h += "ff";
        if (h.Length != 8) return null;
        return uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v : null;
    }

    private static string? Normalize(string? hex)
    {
        uint? v = Parse(hex);
        return v.HasValue ? v.Value.ToString("x8", CultureInfo.InvariantCulture) : null;
    }

    // The decoration a plane has before any dye: the class colors of the game ("unitColors" config) under the variant's own.
    public static SavedDecoration Default(string planeClass, string variant)
    {
        ConfigNode colors = ConfigBinary.Get("unitColors");
        var deco = new SavedDecoration
        {
            Color1 = Normalize(colors["defaultColor1"].AsString("")),
            Color2 = Normalize(colors["defaultColor2"].AsString("")),
            Color3 = Normalize(colors["defaultColor3"].AsString(""))
        };
        PlaneClassInfo? cls = ItemCatalog.GetClass(planeClass);
        if (cls != null && cls.VariantDecorations.TryGetValue(variant, out ConfigNode? own)) ApplyFrom(deco, own);
        return deco;
    }

    // DecorationAction.ApplyToDecoration + Decoration.ApplyFrom: the item's cleaning flags, then what it paints.
    // Returns a text for the log.
    public static string Apply(Accounts.SavedPlane plane, ConfigNode action)
    {
        SavedDecoration deco = (plane.Decoration ?? Default(plane.Class, plane.Variant)).Copy();
        SavedDecoration variantDeco = Default(plane.Class, plane.Variant);

        if (action["cleanTrails"].AsBool(false))
        {
            deco.NormalTrail = variantDeco.NormalTrail;
            deco.SuperTrail = variantDeco.SuperTrail;
        }
        if (action["cleanColors"].AsBool(false))
        {
            deco.Color1 = variantDeco.Color1;
            deco.Color2 = variantDeco.Color2;
            deco.Color3 = variantDeco.Color3;
        }
        if (action["cleanLivery"].AsBool(false)) deco.Livery = variantDeco.Livery;
        ApplyFrom(deco, action);

        plane.Decoration = deco;
        return Describe(deco);
    }

    // Decoration.ApplyFrom: only what the config sets replaces what the plane has.
    private static void ApplyFrom(SavedDecoration deco, ConfigNode config)
    {
        string? c1 = Normalize(Pick(config, "hexColor1", "extraColor1"));
        string? c2 = Normalize(Pick(config, "hexColor2", "extraColor2"));
        string? c3 = Normalize(Pick(config, "hexColor3", "extraColor3"));
        if (c1 != null) deco.Color1 = c1;
        if (c2 != null) deco.Color2 = c2;
        if (c3 != null) deco.Color3 = c3;
        string livery = config["livery"].AsString("");
        if (livery.Length > 0) deco.Livery = livery;
        string normal = config["normalTrail"].AsString("");
        if (normal.Length > 0) deco.NormalTrail = normal;
        string super = config["superTrail"].AsString("");
        if (super.Length > 0) deco.SuperTrail = super;
    }

    private static string? Pick(ConfigNode config, string first, string second)
    {
        string a = config[first].AsString("");
        return a.Length > 0 ? a : config[second].AsString("");
    }

    public static string Describe(SavedDecoration? d)
    {
        if (d == null || d.IsEmpty) return "default look";
        var parts = new List<string>();
        if (d.Color1 != null) parts.Add("color1 " + d.Color1);
        if (d.Color2 != null) parts.Add("color2 " + d.Color2);
        if (d.Color3 != null) parts.Add("color3 " + d.Color3);
        if (!string.IsNullOrEmpty(d.Livery)) parts.Add("livery " + d.Livery);
        if (d.NormalTrail != null) parts.Add("trail " + d.NormalTrail);
        if (d.SuperTrail != null) parts.Add("super trail " + d.SuperTrail);
        return string.Join(", ", parts);
    }
}
