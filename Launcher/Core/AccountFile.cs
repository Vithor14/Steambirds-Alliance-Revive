using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SteambirdsLauncher.Editor;

namespace SteambirdsLauncher.Core;

// Read-only view of a account.json (Server\Accounts.cs): one per player in server\accounts\<key>\, plus the
// old single account server\account.json while it waits for the host's first login. AccountStore edits them.
public static class AccountFile
{
    public const string FileName = "account.json";

    public sealed class Account
    {
        public string AccountId { get; set; } = "";
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string SteamId { get; set; } = "";
        public DateTime? CreatedAt { get; set; }
        public DateTime? LastAccess { get; set; }
        public List<Plane> Planes { get; set; } = new List<Plane>();
        public List<string> FinishedDungeons { get; set; } = new List<string>();
        public Pilot? Pilot { get; set; }
    }

    public sealed class Plane
    {
        public string CharacterId { get; set; } = "";
        public string Class { get; set; } = "";
        public string Variant { get; set; } = "";
        public double Xp { get; set; }
        public double Level { get; set; }
    }

    public sealed class Pilot
    {
        public double Level { get; set; }
        public double Kills { get; set; }
        public double BossKills { get; set; }
        public double PlanesLost { get; set; }
        public double HighestPlaneLevel { get; set; }
        public List<string> Unlocks { get; set; } = new List<string>();
    }

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    // null when the file does not exist.
    public static Account? Load(string path)
    {
        if (!File.Exists(path)) return null;
        JsonNode? node = JsonNode.Parse(TextFiles.ReadShared(path), null, JsoncFile.ParseOptions);
        Account account = (node == null ? null : LegacyNames.Translate(node).Deserialize<Account>(Options)) ?? new Account();
        account.Planes ??= new List<Plane>();
        account.FinishedDungeons ??= new List<string>();
        account.Name ??= "";
        account.SteamId ??= "";
        account.Key ??= "";
        if (account.Pilot != null) account.Pilot.Unlocks ??= new List<string>();
        return account;
    }
}
