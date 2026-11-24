using Lidgren.Network;

// Plane variants (skins): the "Variant" entry of the options menu.
//
// The client lists the variants of the class of the current plane (playerclasses variants[]) and sends
// ServerRequestChangePlaneVariant(PeerId playerId, string variantId) for the one the player picked. It is not an RPC: the client
// waits for ClientUpdatePlaneVariant(variantId), which repaints the plane (PlaneRecord.planeVariant, art replacement, decoration).
//
// The server checks that the variant exists for the class and that the account owns it (the variant's unlockItemId is in the
// pilot's unlocks, which the "variant" consumables and the stores deliver), saves it on the plane and answers. The variant is kept
// in the account, so the next spawn (and the other players, who see the plane through the relay) use it.
public static class PlaneVariants
{
    public const ushort MsgServerRequestChangePlaneVariant = 15910;   // PeerId playerId, string variantId
    public const ushort MsgClientUpdatePlaneVariant = 15909;          // string variantId

    private sealed class Result
    {
        public string Reason = "";
        public string Class = "";
        public string Previous = "";
    }

    // The message is positioned after the message id.
    public static void Handle(NetServer server, NetConnection connection, string role, NetIncomingMessage msg, Action<string, string> log)
    {
        msg.ReadInt64();                                  // PeerId of the player (the connection already says who it is)
        string variant = msg.ReadString() ?? "";
        string tag = "[" + role + "]";

        lock (Game.Lock)
        {
            string? characterId = Game.GetPlaneCharacterId(connection);
            if (characterId == null)
            {
                log("PLANE", tag + " ServerRequestChangePlaneVariant '" + variant + "' without a live plane (ignored)");
                return;
            }

            Result result = Accounts.Change(d =>
            {
                Accounts.SavedPlane? plane = d.Planes.FirstOrDefault(a => a.CharacterId == characterId);
                if (plane == null) return new Result { Reason = "the plane is not in the account" };
                var r = new Result { Class = plane.Class, Previous = plane.Variant };

                PlaneClassInfo? cls = ItemCatalog.GetClass(plane.Class);
                if (string.IsNullOrEmpty(variant)) { r.Reason = "empty variant"; return r; }
                if (!PlayerData.VariantNetIdShort.ContainsKey(variant)) { r.Reason = "the server has no network id for the variant"; return r; }
                if (cls != null && cls.VariantUnlocks.Count > 0)
                {
                    if (!cls.VariantUnlocks.TryGetValue(variant, out string? unlock)) { r.Reason = "the class has no such variant"; return r; }
                    if (!string.IsNullOrEmpty(unlock) && !d.Pilot.Unlocks.Contains(unlock)) { r.Reason = "variant locked (needs " + unlock + ")"; return r; }
                }
                if (string.Equals(plane.Variant, variant, StringComparison.Ordinal)) { r.Reason = "already using it"; return r; }

                plane.Variant = variant;
                plane.Decoration = null;   // GameClient.ClientUpdatePlaneVariant: the plane takes the look of the new variant
                return r;
            });

            if (result.Reason.Length > 0)
            {
                log("PLANE", tag + " ServerRequestChangePlaneVariant '" + variant + "' refused: " + result.Reason);
                // The client already painted the variant it picked? No: it waits for this message. Answer with the variant in force
                // so a refused request never leaves the menu out of date.
                string current = Accounts.GetPlane(characterId)?.Variant ?? "default";
                ProgressionMessages.Send(server, connection, m => WriteClientUpdatePlaneVariant(m, current));
                return;
            }

            ProgressionMessages.Send(server, connection, m => WriteClientUpdatePlaneVariant(m, variant));
            PlayerRelay.VariantChanged(connection, PlayerData.VariantNetIdShort[variant], variant);
            log("PLANE", tag + " " + result.Class + " " + result.Previous + " -> " + variant + " (ClientUpdatePlaneVariant sent)");
        }
    }

    // Network id (netIdShort) of the art replacement a variant brings, 0 when the variant has none (or the id is unknown).
    //
    // The client repaints a cloud's effect through the "art replacement" of the unit that creates it (CloudScript.Init ->
    // ApplyArtReplacement(prefabName); the art_replacements config maps, for example, "poisonCloudFX" to "poisonCloudFXPremium"
    // for the Epic variants). The id travels in the artReplacement field of the cloud's spawn, so the server must send the one of the
    // variant that the plane that used the skill wears. It changes only the picture: radius, duration and effects stay the same.
    //
    // NetIdentifiedConfig.netIdShort: the config's hashOverride when set, else the Mono hash of its id (the file key).
    public static short ArtReplacementId(string planeClass, string variant)
    {
        if (string.IsNullOrEmpty(planeClass) || string.IsNullOrEmpty(variant)) return 0;
        PlaneClassInfo? cls = ItemCatalog.GetClass(planeClass);
        if (cls == null || !cls.VariantDecorations.TryGetValue(variant, out ConfigNode? node)) return 0;

        string art = node["artReplacement"].AsString("");
        if (art.Length == 0) return 0;

        int hashOverride = ConfigBinary.Get("art_replacements")[art]["hashOverride"].AsInt(0);
        return hashOverride != 0 ? unchecked((short)hashOverride) : MonoHash.TemplateHash(art);
    }

    public static void WriteClientUpdatePlaneVariant(NetBuffer buf, string variantId)
    {
        buf.Write(MsgClientUpdatePlaneVariant);
        buf.Write(variantId);
    }
}
