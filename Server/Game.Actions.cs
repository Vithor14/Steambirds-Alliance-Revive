using System.Globalization;
using System.Text;
using Lidgren.Network;

public static partial class Game
{
    // ------------------------------------------------------------------ actions_config.txt

    private static string ActionsPath()
    {
        return GameDataFiles.FilePath("actions_config.txt");
    }

    // Reads (or rereads, if the file changed) the clouds and maneuvers that the plugin wrote.
    // Lines separated by TAB:
    //   cloud    <name> <hash> <cloudPrefab> <prefabIndex> <lifetime> <growtime> <startRadius> <endRadius> <attached 0/1> <resolvable 0/1>
    //   maneuver <actionId> <hash> <name>
    private static void LoadActions(Action<string, string> log)
    {
        string path = ActionsPath();
        try
        {
            if (!File.Exists(path))
            {
                if (!warnedMissingActions)
                {
                    warnedMissingActions = true;
                    log("ACTIONS", "file " + path + " does not exist yet: maneuvers stay without a name and clouds are not created " +
                        "(the file comes in the server's data folder: Server\\data)");
                }
                return;
            }

            DateTime modified = File.GetLastWriteTimeUtc(path);
            if (modified == actionsReadAt) return;

            var newClouds = new Dictionary<string, Cloud>();
            var newManeuvers = new Dictionary<int, string>();

            foreach (string line in File.ReadAllLines(path))
            {
                if (line.Length == 0 || line[0] == '#') continue;
                string[] p = line.Split('\t');

                if (p[0] == "cloud" && p.Length >= 11)
                {
                    if (!short.TryParse(p[2], NumberStyles.Integer, Inv, out short hash)) continue;
                    if (!int.TryParse(p[4], NumberStyles.Integer, Inv, out int index)) continue;
                    if (!float.TryParse(p[5], NumberStyles.Float, Inv, out float duration)) continue;
                    if (!float.TryParse(p[8], NumberStyles.Float, Inv, out float finalRadius)) continue;

                    newClouds[p[1]] = new Cloud
                    {
                        Name = p[1],
                        Hash = hash,
                        Prefab = p[3],
                        PrefabIndex = index,
                        Duration = duration,
                        FinalRadius = finalRadius,
                        AttachedToOwner = p[9] == "1",
                        Resolvable = p[10] == "1"
                    };
                }
                else if (p[0] == "maneuver" && p.Length >= 3)
                {
                    if (int.TryParse(p[2], NumberStyles.Integer, Inv, out int maneuverHash)) newManeuvers[maneuverHash] = p[1];
                }
            }

            clouds = newClouds;
            maneuvers = newManeuvers;
            actionsReadAt = modified;
            log("ACTIONS", path + ": " + clouds.Count + " cloud(s) and " + maneuvers.Count + " maneuver(s) loaded");
        }
        catch (Exception e)
        {
            log("ACTIONS", "could not read " + path + ": " + e.Message);
        }
    }
}
