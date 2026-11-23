using System.Text.Json;

namespace SteambirdsLauncher.Core;

// flocks.json of the server (Server\Social\FlockStore.cs): the flocks of the host and their members. The launcher only reads it
// (the players manage their flocks in the game's Flock Hall); the server writes the file at every change.
public sealed class FlockMemberData
{
    public string UserId { get; set; } = "";
    public string Name { get; set; } = "";
    public int Role { get; set; }
    public bool Founder { get; set; }
    public bool Pending { get; set; }
    public bool Banned { get; set; }
    public long JoinedAt { get; set; }
    public long ActiveAt { get; set; }

    public string RoleName => Founder ? "Founder" : Role switch { 1 => "Officer", 2 => "Leader", 3 => "Elder", _ => "Member" };
}

public sealed class FlockData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Desc { get; set; } = "";
    public int InviteMode { get; set; }
    public long Created { get; set; }
    public List<FlockMemberData> Members { get; set; } = new List<FlockMemberData>();

    public int MemberCount => Members.Count(m => !m.Pending && !m.Banned);
    public int PendingCount => Members.Count(m => m.Pending && !m.Banned);
    public string FounderName => Members.FirstOrDefault(m => m.Founder && !m.Banned)?.Name ?? "";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string FilePath => LauncherPaths.ServerFile("flocks.json");

    public static DateTime Stamp() => File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : DateTime.MinValue;

    // The flocks of the file; an empty list when it does not exist yet or cannot be read (the server may be writing it).
    public static List<FlockData> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new List<FlockData>();
            return JsonSerializer.Deserialize<List<FlockData>>(File.ReadAllText(FilePath), Options) ?? new List<FlockData>();
        }
        catch
        {
            return new List<FlockData>();
        }
    }
}
