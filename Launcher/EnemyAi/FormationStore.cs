using System.Text.Json;
using SteambirdsAi;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.EnemyAi;

// The formation files of server\ai\formations. One file is one formation. Delete moves the file into the launcher backup folder.
internal sealed class FormationStore
{
    private static readonly JsonSerializerOptions Write = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public FormationStore(string aiFolder)
    {
        Folder = Path.Combine(aiFolder, AiCatalog.FormationsFolder);
    }

    public string Folder { get; }
    public List<FormationFile> Files { get; } = new List<FormationFile>();
    public List<string> Problems { get; } = new List<string>();

    public void Reload()
    {
        Files.Clear();
        Problems.Clear();
        FormationLibrary library = FormationLibrary.Load(Path.GetDirectoryName(Folder) ?? Folder);
        Problems.AddRange(library.Problems);
        Files.AddRange(library.ById.Values.OrderBy(f => f.Id, StringComparer.OrdinalIgnoreCase));
    }

    public FormationFile Add(string wantedId)
    {
        string id = Unique(wantedId.Length == 0 ? "formation" : wantedId);
        var file = new FormationFile
        {
            Id = id,
            Name = id,
            Origin = AiCatalog.GenericFolder,
            BindToUnit = true,
            Leader = new FormationWho { Unit = "", Ai = "" }
        };
        Files.Add(file);
        Save(file);
        return file;
    }

    public FormationFile Duplicate(FormationFile source)
    {
        string json = JsonSerializer.Serialize(source, Write);
        FormationFile copy = JsonSerializer.Deserialize<FormationFile>(json, Write) ?? new FormationFile();
        copy.Id = Unique(source.Id + "_copy");
        copy.Name = (source.Name.Length > 0 ? source.Name : source.Id) + " (copy)";
        copy.Origin = source.Origin;
        Files.Add(copy);
        Save(copy);
        return copy;
    }

    public void Rename(FormationFile file, string wantedId)
    {
        string next = AiCatalog.CleanId(wantedId);
        if (next.Length == 0 || next.Equals(file.Id, StringComparison.OrdinalIgnoreCase)) return;
        if (Files.Any(f => !ReferenceEquals(f, file) && f.Id.Equals(next, StringComparison.OrdinalIgnoreCase)))
            throw new IOException(L.F("There is already a formation called \"{0}\".", next));
        string oldPath = PathOf(file);
        file.Id = next;
        Save(file);
        if (File.Exists(oldPath) && !string.Equals(oldPath, PathOf(file), StringComparison.OrdinalIgnoreCase))
            File.Delete(oldPath);
    }

    public void Delete(FormationFile file)
    {
        string path = PathOf(file);
        if (File.Exists(path))
        {
            string backup = LauncherPaths.NewBackupFolder("formations");
            File.Move(path, Path.Combine(backup, Path.GetFileName(path)));
        }
        Files.Remove(file);
    }

    public void Save(FormationFile file)
    {
        if (file.Id.Length == 0) file.Id = Unique("formation");
        string previous = File.Exists(PathOf(file)) ? PathOf(file) : "";
        string home = FormationOrigins.ForUnit(file.Leader?.Unit ?? "");
        if (!string.Equals(home, file.Origin, StringComparison.OrdinalIgnoreCase)) file.Origin = home;
        Directory.CreateDirectory(Path.GetDirectoryName(PathOf(file))!);
        string json = JsonSerializer.Serialize(file, Write);
        File.WriteAllText(PathOf(file), json);
        if (previous.Length > 0 && !string.Equals(previous, PathOf(file), StringComparison.OrdinalIgnoreCase) && File.Exists(previous))
            File.Delete(previous);
    }

    public string Unique(string wanted)
    {
        string id = AiCatalog.CleanId(wanted);
        if (id.Length == 0) id = "formation";
        string candidate = id;
        int n = 2;
        while (Files.Any(f => f.Id.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
            candidate = id + "_" + n++;
        return candidate;
    }

    private string PathOf(FormationFile file)
    {
        string origin = string.IsNullOrWhiteSpace(file.Origin) ? "" : file.Origin.Replace('/', Path.DirectorySeparatorChar);
        string folder = origin.Length == 0 ? Folder : Path.Combine(Folder, origin);
        return Path.Combine(folder, file.Id + ".json");
    }
}
