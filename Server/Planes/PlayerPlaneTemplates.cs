using System.Globalization;

// Unit template ("planeName") that each player class flies with.
//
// The client takes the flight model of the player's plane from the UnitConfig whose templateHash comes in the
// SpawnUnitsBatch (UnitScript.ImportConfig, PlayerUnitController.GetActiveSteerType, UnitScript.getLimitedGunRotation):
//  - steeringType Relative: the plane always flies forward and A/D turn it (playerPlane, assaultClassPlane...);
//  - steeringType Absolute: WASD moves in 8 directions and the plane hovers when no key is pressed
//    (walkerClassPlane, the "helicopter" movement);
//  - maxGunRotationAngleOffset: how far the guns can turn away from the nose toward the mouse
//    (0 = always forward, 180 = any direction).
//
// PlayerClassConfig.planeName (configBinary "playerclasses") says which template each class uses. In the builds
// 190426 to 190911 (OldVersions): walker, paladin, support, pirate, siegetank and daggerknight use
// walkerClassPlane (Absolute, aim 180); olfaithful, duster and wizard use playerPlane (Relative, aim 0);
// assault, engineer and angler use assaultClassPlane; medic, rapier and tank use their own ClassPlane.
// The server used to send playerPlane for every class, so every class flew like olfaithful.
public static class PlayerPlaneTemplates
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string DefaultTemplate = "playerPlane";

    // One player plane template as the client will see it.
    public sealed class Template
    {
        public string Name = DefaultTemplate;
        public short Hash = PlaneSpawn.PlayerPlaneTemplateHash;
        public string Steering = "?";          // "Relative" or "Absolute" (configBinary), "?" when unknown
        public float MaxGunRotation = -1f;     // maxGunRotationAngleOffset, -1 when unknown
        public bool FromGameConfig;            // true = planeName read from the game's configBinary

        public string Describe()
        {
            string movement = Steering.Equals("Absolute", StringComparison.OrdinalIgnoreCase)
                ? "8 directions (hovers in place)"
                : Steering.Equals("Relative", StringComparison.OrdinalIgnoreCase) ? "always forward" : "movement ?";
            string aim = MaxGunRotation < 0f
                ? "aim ?"
                : MaxGunRotation >= 179f ? "free aim with the mouse" : MaxGunRotation <= 0f ? "shoots forward only"
                : "aims up to " + MaxGunRotation.ToString("0.#", Inv) + " degrees";
            return Name + " (hash " + Hash.ToString(Inv) + ", " + movement + ", " + aim + ")";
        }
    }

    // planeName of each class in the 190911 build. Used only when the configBinary is missing or has no planeName.
    private static readonly Dictionary<string, string> FallbackPlaneNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["assault"] = "assaultClassPlane",
            ["medic"] = "medicClassPlane",
            ["rapier"] = "rapierClassPlane",
            ["tank"] = "tankClassPlane",
            ["walker"] = "walkerClassPlane",
            ["olfaithful"] = "playerPlane",
            ["duster"] = "playerPlane",
            ["support"] = "walkerClassPlane",
            ["paladin"] = "walkerClassPlane",
            ["wizard"] = "playerPlane",
            ["engineer"] = "assaultClassPlane",
            ["pirate"] = "walkerClassPlane",
            ["siegetank"] = "walkerClassPlane",
            ["daggerknight"] = "walkerClassPlane",
            ["bigbird"] = "walkerClassPlane",
            ["angler"] = "assaultClassPlane",
        };

    private static readonly object gate = new object();
    private static Dictionary<string, Template> byClass = new Dictionary<string, Template>(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, Template> byTemplateName = new Dictionary<string, Template>(StringComparer.Ordinal);
    private static bool loaded;

    // Reads planeName of every class and the flight data of each template from the configBinary
    // (ConfigBinary must already be loaded; PilotProgression.Start does that).
    public static void Load(Action<string, string> log)
    {
        lock (gate)
        {
            var templates = new Dictionary<string, Template>(StringComparer.Ordinal);
            var classes = new Dictionary<string, Template>(StringComparer.OrdinalIgnoreCase);

            if (ConfigBinary.Loaded)
            {
                foreach (KeyValuePair<string, ConfigNode> file in ConfigBinary.WithPrefix("Units/"))
                {
                    IEnumerable<ConfigNode> nodes = file.Value.IsList ? file.Value.Items : new[] { file.Value };
                    foreach (ConfigNode unit in nodes)
                    {
                        string name = unit["templateName"].AsString("");
                        if (name.Length == 0) continue;
                        if (!IsPlayerTemplate(name, unit)) continue;
                        templates[name] = FromUnitNode(name, unit);
                    }
                }

                foreach (KeyValuePair<string, ConfigNode> pair in ConfigBinary.Get("playerclasses").Pairs)
                {
                    string classId = pair.Value["id"].AsString(pair.Key);
                    string planeName = pair.Value["planeName"].AsString("");
                    bool fromGame = planeName.Length > 0;
                    if (!fromGame)
                        planeName = FallbackPlaneNames.TryGetValue(classId, out string? oldName) ? oldName : DefaultTemplate;
                    classes[classId] = Resolve(planeName, templates, fromGame, classId, log);
                }
            }

            // Classes the configBinary did not list (or no configBinary at all): table of the 190911 build.
            foreach (KeyValuePair<string, string> pair in FallbackPlaneNames)
            {
                if (!classes.ContainsKey(pair.Key))
                    classes[pair.Key] = Resolve(pair.Value, templates, false, pair.Key, log);
            }

            byTemplateName = templates;
            byClass = classes;
            loaded = true;

            string origin = ConfigBinary.Loaded ? "planeName of the configBinary" : "table of build 190911 (configBinary not read)";
            log("PLANE", "flight model per class (" + origin + "): " +
                string.Join("; ", classes.OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => p.Key + " = " + p.Value.Describe())));
        }
    }

    // Template of a class. Unknown class: playerPlane.
    public static Template ForClass(string? classId)
    {
        lock (gate)
        {
            if (!loaded) Load((_, _) => { });
            if (!string.IsNullOrEmpty(classId) && byClass.TryGetValue(classId, out Template? t)) return t;
            return byTemplateName.TryGetValue(DefaultTemplate, out Template? d) ? d : new Template();
        }
    }

    // Player plane templates have a "ClassPlane"/"playerPlane" name or a steeringType / movement tuning.
    private static bool IsPlayerTemplate(string name, ConfigNode unit)
    {
        return name == DefaultTemplate ||
               name.EndsWith("ClassPlane", StringComparison.Ordinal) ||
               unit.Has("steeringType") ||
               unit.Has("movementTuningAbsolute");
    }

    private static Template FromUnitNode(string name, ConfigNode unit)
    {
        // A few UnitConfigs carry a "hashOverride"; the rest use the Mono string hash (see MonoHash).
        int? hashOverride = unit["hashOverride"].AsNullableInt();
        return new Template
        {
            Name = name,
            Hash = hashOverride.HasValue ? unchecked((short)hashOverride.Value) : MonoHash.TemplateHash(name),
            Steering = unit["steeringType"].AsString("Relative"),
            MaxGunRotation = unit["maxGunRotationAngleOffset"].AsFloat(0f),
        };
    }

    private static Template Resolve(string planeName, Dictionary<string, Template> templates, bool fromGame,
        string classId, Action<string, string> log)
    {
        if (templates.TryGetValue(planeName, out Template? found))
        {
            return new Template
            {
                Name = found.Name,
                Hash = found.Hash,
                Steering = found.Steering,
                MaxGunRotation = found.MaxGunRotation,
                FromGameConfig = fromGame,
            };
        }

        if (ConfigBinary.Loaded && templates.Count > 0)
        {
            log("PLANE", "class '" + classId + "': model '" + planeName + "' does not exist in this game version; using " +
                DefaultTemplate);
            if (templates.TryGetValue(DefaultTemplate, out Template? fallback)) return fallback;
            return new Template();
        }

        // No configBinary: the hash is still right (Mono hash of the name), the flight data is unknown.
        return new Template { Name = planeName, Hash = MonoHash.TemplateHash(planeName), FromGameConfig = false };
    }
}
