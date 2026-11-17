// String hashes of the game client (Unity Mono), reproduced on the server.
//
// The client uses string.GetHashCode() from Mono, which is the "legacy non-randomized" hash of the .NET
// reference source (64-bit variant). .NET 8 randomizes string hashes per process, so the server cannot just
// call GetHashCode(): it needs this copy.
//
// Checked against the plugin export (data\units_config.txt): TemplateHash(name) matches the templateHash of
// 1585 of the 1641 units of client 201219.1413; the other 56 use a "hashOverride" in their UnitConfig.
//
// Uses on the client side:
//  - StatefulEffectHelpers.IDFromName: (short)name.GetHashCode()      -> StatefulEffectId
//  - UnitConfig templateHash:          (short)(h ^ (h >> 16))          -> TemplateHash
public static class MonoHash
{
    // string.GetHashCode() of Mono (reference source GetLegacyNonRandomizedHashCode, 64-bit branch).
    public static int StringHash(string text)
    {
        unchecked
        {
            int hash1 = 5381;
            int hash2 = hash1;
            int i = 0;
            while (i < text.Length)
            {
                hash1 = ((hash1 << 5) + hash1) ^ text[i];
                if (i + 1 >= text.Length) break;
                hash2 = ((hash2 << 5) + hash2) ^ text[i + 1];
                i += 2;
            }
            return hash1 + hash2 * 1566083941;
        }
    }

    // Id of a StatefulEffectConfig (configBinary "statefulEffects"), as sent in FieldUpdate EFFECTSTATE1/2.
    // 0 means "no effect" for the client (SBConfig.FindStatefulEffect returns null for 0).
    public static short StatefulEffectId(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0;
        unchecked
        {
            return (short)StringHash(name.Trim());
        }
    }

    // templateHash of a UnitConfig without hashOverride.
    public static short TemplateHash(string templateName)
    {
        unchecked
        {
            int h = StringHash(templateName);
            return (short)(h ^ (h >> 16));
        }
    }
}
