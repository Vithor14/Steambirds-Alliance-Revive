using System.Text.Json;
using System.Text.Json.Nodes;

namespace SteambirdsAi;

// Sharing: one file holding a brain and the behaviors it uses (kind "steambirds-ai-package"). A single brain file or a
// single behavior file is a valid thing to share as well; Read recognizes the three.
public static class AiPackage
{
    public sealed class Contents
    {
        public string Name = "";
        public List<(string Id, JsonObject Root)> Brains = new List<(string Id, JsonObject Root)>();
        public List<(string Id, JsonObject Root)> Behaviors = new List<(string Id, JsonObject Root)>();
        public bool IsEmpty => Brains.Count == 0 && Behaviors.Count == 0;
    }

    public static JsonObject Build(string name, IEnumerable<(string Id, JsonObject Root)> brains,
        IEnumerable<(string Id, JsonObject Root)> behaviors)
    {
        var package = new JsonObject
        {
            ["format"] = AiCatalog.FormatVersion,
            ["kind"] = AiCatalog.PackageKind,
            ["name"] = name
        };
        var brainList = new JsonObject();
        foreach ((string id, JsonObject root) in brains) brainList[id] = root.DeepClone();
        var behaviorList = new JsonObject();
        foreach ((string id, JsonObject root) in behaviors) behaviorList[id] = root.DeepClone();
        package["brains"] = brainList;
        package["behaviors"] = behaviorList;
        return package;
    }

    // Ids of the behavior files the states of a brain name.
    public static IEnumerable<string> UsedBehaviors(JsonObject brain)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (brain["states"] is not JsonArray states) yield break;
        foreach (JsonNode? node in states)
        {
            if (node is not JsonObject state) continue;
            string id = state["behavior"] is JsonValue v && v.TryGetValue(out string? text) ? (text ?? "").Trim() : "";
            if (id.Length > 0 && seen.Add(id)) yield return id;
        }
    }

    // Throws JsonException (with a message for the user) when the text is not an AI file.
    public static Contents Read(string text, string fallbackId)
    {
        JsonNode? node = JsonNode.Parse(text, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        if (node is not JsonObject root) throw new JsonException("the file is not a JSON object");

        var contents = new Contents { Name = Text(root, "name") };
        if (Text(root, "kind") == AiCatalog.PackageKind)
        {
            AddAll(root["brains"] as JsonObject, contents.Brains);
            AddAll(root["behaviors"] as JsonObject, contents.Behaviors);
            if (contents.IsEmpty) throw new JsonException("the package has no AI inside");
            return contents;
        }

        string id = AiCatalog.CleanId(Text(root, "id").Length > 0 ? Text(root, "id") : fallbackId);
        if (root["states"] is JsonArray) contents.Brains.Add((id, root));
        else if (root["move"] != null || root["fire"] != null || root["contact"] != null) contents.Behaviors.Add((id, root));
        else throw new JsonException("this does not look like an AI file (no \"states\", no \"move\")");
        return contents;
    }

    private static void AddAll(JsonObject? from, List<(string Id, JsonObject Root)> to)
    {
        if (from == null) return;
        foreach (KeyValuePair<string, JsonNode?> pair in from)
        {
            if (pair.Value is JsonObject item && AiCatalog.CleanId(pair.Key).Length > 0)
                to.Add((AiCatalog.CleanId(pair.Key), item));
        }
    }

    private static string Text(JsonObject obj, string key)
    {
        return obj[key] is JsonValue v && v.TryGetValue(out string? text) ? (text ?? "").Trim() : "";
    }
}
