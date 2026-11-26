using System.Text.Json;
using System.Text.Json.Serialization;

namespace SteambirdsAi;

// One formation (ai\formations\<id>.json): who is born with the leader, where each one sits, and the links between them
// (a shield that lasts while someone lives, a death that takes the rest, a minion whose health opens the next shield).
// Each piece still flies and shoots with its own AI. This file only says how they belong together.
// Reconstruction: the original servers had no file like this.
public sealed class FormationFile
{
    public int Format { get; set; } = 1;
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    // When true, a unit with this leader name and only a generic AI still brings the formation (the train, the Pup, the Five Star).
    // When false, only an AI that names this file in its "formation" field brings it (the Ambassador body, the spirit-vessel example).
    public bool BindToUnit { get; set; } = true;
    public FormationWho Leader { get; set; } = new FormationWho();
    // Chain pieces sit this many units behind the one in front (a train car is about 1.2 units wide).
    public float Spacing { get; set; } = 1.7f;
    public List<FormationMember> Members { get; set; } = new List<FormationMember>();
    public List<FormationLink> Links { get; set; } = new List<FormationLink>();

    public string LeaderAi => (Leader?.Ai ?? "").Trim();

    // One entry per piece that is actually spawned (a ring member with count 5 becomes five slots).
    public List<FormationSlot> Expand()
    {
        var slots = new List<FormationSlot>();
        int chain = 0;
        int n = 0;
        foreach (FormationMember member in Members ?? new List<FormationMember>())
        {
            if (member == null || string.IsNullOrWhiteSpace(member.Unit)) continue;
            FormationPlace place = member.Place ?? new FormationPlace();
            string kind = (place.Kind ?? "slot").Trim().ToLowerInvariant();
            if (kind != "chain" && kind != "slot" && kind != "ring" && kind != "free") kind = "slot";
            int count = kind == "ring" ? Math.Clamp(place.Count <= 0 ? 1 : place.Count, 1, 30) : Math.Clamp(place.Count <= 0 ? 1 : place.Count, 1, 30);
            string role = string.IsNullOrWhiteSpace(member.Role) ? (member.Id ?? "").Trim() : member.Role.Trim();
            if (role.Length == 0) role = (member.Unit ?? "").Trim();
            for (int i = 0; i < count; i++)
            {
                n++;
                string id = (member.Id ?? "").Trim();
                if (id.Length == 0) id = "m" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (count > 1) id = id + "#" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                slots.Add(new FormationSlot
                {
                    Id = id,
                    Unit = (member.Unit ?? "").Trim(),
                    Ai = (member.Ai ?? "").Trim(),
                    Role = role,
                    Kind = kind,
                    ChainIndex = kind == "chain" ? chain++ : -1,
                    X = place.X,
                    Y = place.Y,
                    Radius = place.Radius <= 0f ? 8f : place.Radius,
                    Spin = place.Spin,
                    Angle = place.Angle + (count > 1 ? 360f * i / count : 0f),
                    RingIndex = i,
                    RingCount = count
                });
            }
        }
        return slots;
    }
}

public sealed class FormationWho
{
    public string Unit { get; set; } = "";
    public string Ai { get; set; } = "";
}

public sealed class FormationMember
{
    public string Id { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Ai { get; set; } = "";
    public string Role { get; set; } = "";
    public FormationPlace Place { get; set; } = new FormationPlace();
}

public sealed class FormationPlace
{
    // chain: behind the leader along the path it flew. slot: fixed x (right) and y (front), turns with the leader.
    // ring: count pieces on a circle (radius, spin degrees per second, angle where the first sits). free: born in the
    // group and flies with its own AI (not glued).
    public string Kind { get; set; } = "slot";
    public float X { get; set; }
    public float Y { get; set; }
    public float Radius { get; set; } = 8f;
    public float Spin { get; set; }
    public float Angle { get; set; }
    public int Count { get; set; } = 1;
}

// shield: who is protected, and while whom. when + do: something that happens once (a death, a health line).
public sealed class FormationLink
{
    public List<string>? Shield { get; set; }
    public string? WhileAlive { get; set; }
    public List<string>? WhileAnyAlive { get; set; }
    // Any living piece with this role keeps the shield up.
    public string? WhileRole { get; set; }
    public string? Effect { get; set; }
    public FormationWhen? When { get; set; }
    [JsonConverter(typeof(FormationDoConverter))]
    public FormationDo? Do { get; set; }
}

public sealed class FormationWhen
{
    public string? Died { get; set; }
    public string? Role { get; set; }
    public string? Which { get; set; }
    // 33 means 33 percent. A value from 0 to 1 is a fraction.
    public float? HealthBelow { get; set; }
    public string? Member { get; set; }
}

public sealed class FormationDo
{
    // killAll, unshield, heal.
    public string Kind { get; set; } = "";
    // A member id, "leader", "next" (the next living piece of the same role), or "role:<name>".
    public string Target { get; set; } = "";
    public float Amount { get; set; }
}

public sealed class FormationDoConverter : JsonConverter<FormationDo>
{
    public override FormationDo? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;
        if (reader.TokenType == JsonTokenType.String) return new FormationDo { Kind = reader.GetString() ?? "" };
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException("formation do");
        var result = new FormationDo();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString() ?? "";
            reader.Read();
            switch (name.ToLowerInvariant())
            {
                case "unshield":
                    result.Kind = "unshield";
                    result.Target = reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : "";
                    break;
                case "heal":
                    result.Kind = "heal";
                    result.Target = reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : "";
                    break;
                case "killall":
                    result.Kind = "killAll";
                    if (reader.TokenType == JsonTokenType.String) result.Target = reader.GetString() ?? "";
                    break;
                case "amount":
                    result.Amount = reader.TokenType == JsonTokenType.Number ? (float)reader.GetDouble() : 0f;
                    break;
                case "target":
                    result.Target = reader.TokenType == JsonTokenType.String ? reader.GetString() ?? "" : "";
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, FormationDo value, JsonSerializerOptions options)
    {
        if (value.Target.Length == 0 && value.Amount == 0f)
        {
            writer.WriteStringValue(value.Kind);
            return;
        }
        writer.WriteStartObject();
        if (value.Kind == "unshield") writer.WriteString("unshield", value.Target);
        else if (value.Kind == "heal") writer.WriteString("heal", value.Target);
        else writer.WriteString("kind", value.Kind.Length == 0 ? "killAll" : value.Kind);
        if (value.Kind == "heal" && value.Amount > 0f) writer.WriteNumber("amount", value.Amount);
        if (value.Kind != "unshield" && value.Kind != "heal" && value.Target.Length > 0) writer.WriteString("target", value.Target);
        writer.WriteEndObject();
    }
}

// One spawned piece (Expand). Not stored in the file.
public sealed class FormationSlot
{
    public string Id = "";
    public string Unit = "";
    public string Ai = "";
    public string Role = "";
    public string Kind = "slot";
    public int ChainIndex = -1;
    public float X, Y, Radius = 8f, Spin, Angle;
    public int RingIndex, RingCount = 1;
    public bool Glued => Kind == "chain" || Kind == "slot" || Kind == "ring";
}

// The formation files of ai\formations, plus the old formations.json (one object keyed by the boss unit) so a server that
// already wrote that file keeps working. A file in ai\formations wins over the old file for the same leader unit.
public sealed class FormationLibrary
{
    public readonly Dictionary<string, FormationFile> ById = new Dictionary<string, FormationFile>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, FormationFile> ByLeaderUnit = new Dictionary<string, FormationFile>(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, string> AiToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public readonly List<string> Problems = new List<string>();

    public FormationFile? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return ById.TryGetValue(id.Trim(), out FormationFile? file) ? file : null;
    }

    public FormationFile? ForAi(string? aiId)
    {
        if (string.IsNullOrWhiteSpace(aiId)) return null;
        return AiToId.TryGetValue(aiId.Trim(), out string? id) ? Get(id) : null;
    }

    public FormationFile? ForUnit(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return null;
        return ByLeaderUnit.TryGetValue(unit.Trim(), out FormationFile? file) ? file : null;
    }

    public void NoteBrain(string aiId, string formationId)
    {
        if (aiId.Length == 0 || formationId.Length == 0 || !ById.ContainsKey(formationId)) return;
        AiToId[aiId] = formationId;
    }

    public static FormationLibrary Load(string aiFolder)
    {
        var library = new FormationLibrary();
        string dir = Path.Combine(aiFolder ?? "", AiCatalog.FormationsFolder);
        if (Directory.Exists(dir))
        {
            foreach (string path in Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly))
            {
                string id = AiCatalog.CleanId(Path.GetFileNameWithoutExtension(path));
                try
                {
                    FormationFile? file = JsonSerializer.Deserialize<FormationFile>(File.ReadAllText(path), AiJson.Options);
                    if (file == null) continue;
                    if (string.IsNullOrWhiteSpace(file.Id)) file.Id = id;
                    library.Add(file);
                }
                catch (Exception e) when (e is JsonException || e is IOException)
                {
                    library.Problems.Add(path + ": " + e.Message);
                }
            }
        }
        return library;
    }

    public void Add(FormationFile file)
    {
        if (file == null || string.IsNullOrWhiteSpace(file.Id)) return;
        file.Spacing = Math.Clamp(file.Spacing <= 0f ? 1.7f : file.Spacing, 0.4f, 30f);
        file.Leader ??= new FormationWho();
        file.Members ??= new List<FormationMember>();
        file.Links ??= new List<FormationLink>();
        ById[file.Id] = file;
        string unit = (file.Leader.Unit ?? "").Trim();
        if (file.BindToUnit && unit.Length > 0) ByLeaderUnit[unit] = file;
        string ai = (file.Leader.Ai ?? "").Trim();
        if (ai.Length > 0) AiToId[ai] = file.Id;
    }

    // The old formations.json ("trainA01": { kind, parts, slots, ... }). Skips a leader the new folder already has.
    public void ImportLegacy(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (!doc.RootElement.TryGetProperty("formations", out JsonElement map)) return;
        foreach (JsonProperty prop in map.EnumerateObject())
        {
            string unit = prop.Name.Trim();
            if (unit.Length == 0 || ByLeaderUnit.ContainsKey(unit)) continue;
            FormationFile? file = FromLegacy(unit, prop.Value);
            if (file != null) Add(file);
        }
    }

    private static FormationFile? FromLegacy(string unit, JsonElement body)
    {
        string kind = Str(body, "kind");
        bool chain = kind.Equals("chain", StringComparison.OrdinalIgnoreCase);
        var parts = new List<string>();
        if (body.TryGetProperty("parts", out JsonElement partEl) && partEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement one in partEl.EnumerateArray())
                if (one.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(one.GetString())) parts.Add(one.GetString()!.Trim());
        }
        if (parts.Count == 0) return null;
        var slots = new List<List<float>>();
        if (body.TryGetProperty("slots", out JsonElement slotEl) && slotEl.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement one in slotEl.EnumerateArray())
            {
                var pair = new List<float>();
                if (one.ValueKind == JsonValueKind.Array)
                    foreach (JsonElement n in one.EnumerateArray())
                        if (n.TryGetSingle(out float v)) pair.Add(v);
                slots.Add(pair);
            }
        }
        bool shield = !body.TryGetProperty("shield", out JsonElement sh) || sh.ValueKind != JsonValueKind.False;
        bool tipKills = body.TryGetProperty("tipKillsAll", out JsonElement tip) && tip.ValueKind == JsonValueKind.True;
        string leaderAi = Str(body, "leaderAi");
        float spacing = body.TryGetProperty("spacing", out JsonElement sp) && sp.TryGetSingle(out float s) ? s : 1.7f;
        string id = unit.Equals("trainA01", StringComparison.OrdinalIgnoreCase) ? "shield_convoy"
            : unit.Equals("podleader", StringComparison.OrdinalIgnoreCase) ? "sergeant_pup"
            : AiCatalog.CleanId(unit);
        var file = new FormationFile
        {
            Id = id,
            Name = unit,
            BindToUnit = true,
            Spacing = spacing,
            Leader = new FormationWho { Unit = unit, Ai = leaderAi },
            Description = "Read from the old formations.json."
        };
        for (int i = 0; i < parts.Count; i++)
        {
            var place = new FormationPlace { Kind = chain ? "chain" : "slot", Count = 1 };
            if (!chain)
            {
                List<float> slot = i < slots.Count ? slots[i] : new List<float>();
                place.X = slot.Count > 0 ? slot[0] : 0f;
                place.Y = slot.Count > 1 ? slot[1] : -6f;
            }
            file.Members.Add(new FormationMember
            {
                Id = chain && i == parts.Count - 1 ? "tip" : "p" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
                Unit = parts[i],
                Role = chain && i == parts.Count - 1 ? "tip" : "part",
                Place = place
            });
        }
        if (shield)
        {
            if (chain)
            {
                var who = new List<string> { "leader" };
                for (int i = 0; i < parts.Count - 1; i++) who.Add("p" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                file.Links.Add(new FormationLink { Shield = who, WhileAlive = "tip" });
            }
            else
            {
                file.Links.Add(new FormationLink
                {
                    Shield = new List<string> { "leader" },
                    WhileRole = "part",
                    Effect = "tankShield"
                });
            }
        }
        if (chain && tipKills)
            file.Links.Add(new FormationLink { When = new FormationWhen { Died = "tip" }, Do = new FormationDo { Kind = "killAll" } });
        return file;
    }

    private static string Str(JsonElement body, string name)
    {
        return body.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String ? (el.GetString() ?? "").Trim() : "";
    }
}
