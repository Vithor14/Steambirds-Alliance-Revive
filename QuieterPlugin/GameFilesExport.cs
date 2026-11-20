using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PathologicalGames;
using UnityEngine;

// Export mode of the Steambirds Game Files tool (SteambirdsGameFiles.exe, folder GameFiles of the project).
//
// The tool opens the game with the environment variable STEAMBIRDS_EXPORT = <output folder>. QuieterPlugin then adds
// this component, which:
//   1. waits until the game has loaded the floor assets (GroundAssetContainer), the "Planes" art pool and SBConfig, and
//      until QuieterPlugin has written its data files to the BepInEx folder (units, actions, map info, portraits and
//      floor assets), or until a time limit;
//   2. draws a top view of every floor piece of the GroundAssetContainer and of every unit art (UnitConfig.art), with the
//      game's own meshes, materials and light, into <output>\ground\<name>.png and <output>\units\<art>.png;
//   3. writes <output>\thumbs.json ("half" of each picture in world units, the format Launcher\Editor\EditorAssets.cs
//      reads) and <output>\export_done.txt (the result), then closes the game.
// Without the variable this component is never created: normal play is not affected.
//
// Pictures: orthographic camera looking along +Z (the game's "down"), +X to the right and +Y up. That is the frame of
// the floor grid in the map editor (u right, v up) and the frame of a unit facing rotation 0 (+Y). The picture is
// centered on the pivot of the object (where the server places it), and "half" is half of its side in world units.
// Each object is drawn twice, over black and over white: the alpha comes from the difference, so shaders that do not
// write alpha still give a clean transparent background. Drawn at 2x and reduced, for smooth edges.
public class GameFilesExport : MonoBehaviour
{
    public const string EnvironmentVariable = "STEAMBIRDS_EXPORT";
    public const string DoneFileName = "export_done.txt";

    private const int ExportLayer = 31;
    private const int RenderSize = 512;
    private const int PictureSize = 256;
    private const float ObjectsTimeoutSeconds = 600f;
    private const float DataFilesTimeoutSeconds = 90f;
    private const int ItemsPerFrame = 4;
    private const int MaxProblems = 300;

    // Far from any map, so nothing of the world is in front of the export camera.
    private static readonly Vector3 RenderPlace = new Vector3(20000f, 20000f, 0f);

    // Files QuieterPlugin writes to the BepInEx folder. The tool copies them to the server (data folder) and to the
    // map editor (ground_assets.txt).
    private static readonly string[] DataFiles =
    {
        "units_config.txt", "actions_config.txt", "mapinfo_config.txt", "portraits_config.txt", "ground_assets.txt"
    };

    internal static ManualLogSource Log;

    private string outputFolder;
    private DateTime startedUtc;
    private float objectsReadyAt = -1f;
    private bool running;
    private bool finished;

    private Camera exportCamera;
    private Light exportLight;
    private RenderTexture renderTexture;
    private Texture2D readTexture;
    private Texture2D pictureTexture;
    private Transform renderRoot;
    private readonly Dictionary<Light, int> sceneLightMasks = new Dictionary<Light, int>();
    private bool savedFog;

    private readonly SortedDictionary<string, float> groundHalves = new SortedDictionary<string, float>(StringComparer.Ordinal);
    private readonly SortedDictionary<string, float> unitHalves = new SortedDictionary<string, float>(StringComparer.Ordinal);
    private readonly List<string> problems = new List<string>();
    private int groundTotal;
    private int unitTotal;

    public static bool IsRequested()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentVariable));
    }

    // Folder of the pictures: the value of STEAMBIRDS_EXPORT, or BepInEx\export when it is only "1" / "true".
    public static string OutputFolder()
    {
        string value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrEmpty(value) || value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Paths.BepInExRootPath, "export");
        return value;
    }

    private void Awake()
    {
        outputFolder = OutputFolder();
        // The data files must be written in this run: anything older than the start of the game is stale.
        startedUtc = DateTime.UtcNow.AddSeconds(-10);
        try
        {
            Directory.CreateDirectory(outputFolder);
            string done = Path.Combine(outputFolder, DoneFileName);
            if (File.Exists(done)) File.Delete(done);
        }
        catch (Exception e)
        {
            Warn("cannot use the export folder " + outputFolder + ": " + e.Message);
        }

        // The game opens only to give its files: no music.
        try { AudioListener.volume = 0f; } catch { }

        Info("Export mode: waiting for the game to load (pictures go to " + outputFolder + ").");
    }

    private void Update()
    {
        if (running || finished)
            return;

        float now = Time.realtimeSinceStartup;
        if (!ObjectsReady())
        {
            if (now > ObjectsTimeoutSeconds)
                Finish("failed", "the game did not load its floor assets and art pools in " + (int)ObjectsTimeoutSeconds + " s (see BepInEx\\LogOutput.log)");
            return;
        }

        if (objectsReadyAt < 0f)
        {
            objectsReadyAt = now;
            Info("Export mode: game loaded; waiting for the data files of the plugin.");
        }

        if (MissingDataFiles().Count > 0 && now - objectsReadyAt < DataFilesTimeoutSeconds)
            return;

        running = true;
        StartCoroutine(Run());
    }

    // ------------------------------------------------------------------ the export

    private IEnumerator Run()
    {
        Info("Export mode: drawing the pictures for the map editor.");

        string error = null;
        try
        {
            Prepare();
        }
        catch (Exception e)
        {
            error = e.GetType().Name + " " + e.Message;
        }
        if (error != null)
        {
            Restore();
            Finish("failed", "could not prepare the export camera: " + error);
            yield break;
        }

        // Floor pieces, walls and decorations: the same objects GroundPlacement spawns.
        GroundAssetContainer container = FindGroundContainer();
        GroundPlacement placement = GroundPlacement.Instance;
        GameObject genericPrefab = placement != null ? placement.GenericPrefab : null;
        SpawnPool groundPool = FindPool("Ground");
        var ground = new List<GroundAsset>();
        if (container != null && container.AllAssets != null)
            ground.AddRange(container.AllAssets);
        groundTotal = ground.Count;
        for (int i = 0; i < ground.Count; i++)
        {
            ExportGround(ground[i], groundPool, genericPrefab);
            if ((i + 1) % ItemsPerFrame == 0)
                yield return null;
        }
        Info("Export mode: " + groundHalves.Count + " of " + groundTotal + " floor pieces drawn.");

        // Unit art: the same object a unit shows in the game (Entity.GetArtObject, pool "Planes").
        SpawnPool planes = FindPool("Planes");
        List<string> arts = UnitArtList();
        unitTotal = arts.Count;
        if (planes == null)
        {
            AddProblem("the Planes pool does not exist: no unit pictures");
        }
        else
        {
            var propertyBlock = new EntityMaterialPropertyBlock();
            for (int i = 0; i < arts.Count; i++)
            {
                ExportUnit(arts[i], planes, propertyBlock);
                if ((i + 1) % ItemsPerFrame == 0)
                    yield return null;
            }
        }
        Info("Export mode: " + unitHalves.Count + " of " + unitTotal + " unit arts drawn.");

        Restore();

        try
        {
            WriteThumbs();
        }
        catch (Exception e)
        {
            AddProblem("thumbs.json: " + e.GetType().Name + " " + e.Message);
        }

        string result = groundHalves.Count > 0 && unitHalves.Count > 0 ? "ok"
            : groundHalves.Count > 0 || unitHalves.Count > 0 ? "partial"
            : "failed";
        List<string> missing = MissingDataFiles();
        string detail = groundHalves.Count + " floor pictures, " + unitHalves.Count + " unit pictures" +
            (missing.Count > 0 ? "; data files not written in this run: " + string.Join(", ", missing.ToArray()) : "");
        Finish(result, detail);
    }

    private void ExportGround(GroundAsset asset, SpawnPool pool, GameObject genericPrefab)
    {
        if (asset == null || string.IsNullOrEmpty(asset.name))
            return;
        string key = asset.name.ToLowerInvariant();
        if (groundHalves.ContainsKey(key))
            return;

        GameObject source = asset.prefabAsset != null ? asset.prefabAsset : (asset.meshAsset != null ? genericPrefab : null);
        if (source == null)
        {
            AddProblem("floor " + asset.name + ": no prefab, or a mesh without GroundPlacement.GenericPrefab");
            return;
        }

        Transform instance = null;
        try
        {
            instance = Spawn(pool, source);
            instance.SetParent(renderRoot, false);
            instance.localPosition = Vector3.zero;
            instance.localRotation = source.transform.localRotation;
            instance.localScale = source.transform.localScale;
            if (asset.prefabAsset == null)
            {
                MeshFilter filter = instance.GetComponent<MeshFilter>();
                if (filter != null)
                    filter.sharedMesh = asset.meshAsset;
            }

            float half;
            if (Draw(instance.GetComponentsInChildren<Renderer>(), Path.Combine(Path.Combine(outputFolder, "ground"), FileName(key)), out half))
                groundHalves[key] = half;
            else
                AddProblem("floor " + asset.name + ": nothing to draw");
        }
        catch (Exception e)
        {
            AddProblem("floor " + asset.name + ": " + e.GetType().Name + " " + e.Message);
        }
        finally
        {
            Release(pool, instance);
        }
    }

    private void ExportUnit(string art, SpawnPool planes, EntityMaterialPropertyBlock propertyBlock)
    {
        string key = art.ToLowerInvariant();
        if (unitHalves.ContainsKey(key))
            return;

        // GetArtObject falls back to "UnknownArt" for names it does not know: those are skipped here.
        if (!planes.prefabs.ContainsKey(art) && !IsMeshAsset(art))
        {
            AddProblem("unit art " + art + ": not in the Planes pool");
            return;
        }

        Transform instance = null;
        try
        {
            Tuple<Transform, ArtInfo> spawned = Entity.GetArtObject(art, planes, propertyBlock, renderRoot);
            instance = spawned != null ? spawned.Item1 : null;
            ArtInfo info = spawned != null ? spawned.Item2 : null;
            if (instance == null)
            {
                AddProblem("unit art " + art + ": the game did not create it");
                return;
            }

            if (info != null)
                info.SetHighPoly(true);

            Renderer[] renderers = info != null && info.renderers != null && info.renderers.Length > 0
                ? info.renderers
                : instance.GetComponentsInChildren<Renderer>();

            float half;
            if (Draw(renderers, Path.Combine(Path.Combine(outputFolder, "units"), FileName(key)), out half))
                unitHalves[key] = half;
            else
                AddProblem("unit art " + art + ": nothing to draw");
        }
        catch (Exception e)
        {
            AddProblem("unit art " + art + ": " + e.GetType().Name + " " + e.Message);
        }
        finally
        {
            if (instance != null)
            {
                try { planes.Despawn(instance); } catch { }
            }
        }
    }

    // ------------------------------------------------------------------ drawing

    private void Prepare()
    {
        Directory.CreateDirectory(Path.Combine(outputFolder, "ground"));
        Directory.CreateDirectory(Path.Combine(outputFolder, "units"));

        renderRoot = new GameObject("SteambirdsExportRoot").transform;
        renderRoot.position = RenderPlace;
        renderRoot.rotation = Quaternion.identity;

        renderTexture = new RenderTexture(RenderSize, RenderSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        renderTexture.Create();
        readTexture = new Texture2D(RenderSize, RenderSize, TextureFormat.RGBA32, false);
        pictureTexture = new Texture2D(PictureSize, PictureSize, TextureFormat.RGBA32, false);

        exportCamera = new GameObject("SteambirdsExportCamera").AddComponent<Camera>();
        exportCamera.enabled = false;
        exportCamera.orthographic = true;
        exportCamera.aspect = 1f;
        exportCamera.cullingMask = 1 << ExportLayer;
        exportCamera.clearFlags = CameraClearFlags.SolidColor;
        exportCamera.allowHDR = false;
        exportCamera.allowMSAA = false;
        exportCamera.useOcclusionCulling = false;
        exportCamera.targetTexture = renderTexture;

        // The lights of the scene stop lighting the export layer; one light of our own takes the direction, color and
        // strength of the brightest directional light of the scene, so every picture gets the same light.
        Light sun = null;
        foreach (Light light in FindObjectsOfType<Light>())
        {
            if (light == null)
                continue;
            sceneLightMasks[light] = light.cullingMask;
            light.cullingMask &= ~(1 << ExportLayer);
            if (light.type == LightType.Directional && light.enabled && (sun == null || light.intensity > sun.intensity))
                sun = light;
        }

        exportLight = new GameObject("SteambirdsExportLight").AddComponent<Light>();
        exportLight.type = LightType.Directional;
        exportLight.cullingMask = 1 << ExportLayer;
        exportLight.shadows = LightShadows.None;
        if (sun != null)
        {
            exportLight.transform.rotation = sun.transform.rotation;
            exportLight.color = sun.color;
            exportLight.intensity = sun.intensity;
        }
        else
        {
            exportLight.transform.rotation = Quaternion.LookRotation(new Vector3(-0.3f, 0.4f, 1f));
            exportLight.intensity = 1f;
        }

        savedFog = RenderSettings.fog;
        RenderSettings.fog = false;
    }

    private void Restore()
    {
        foreach (KeyValuePair<Light, int> pair in sceneLightMasks)
        {
            if (pair.Key != null)
                pair.Key.cullingMask = pair.Value;
        }
        sceneLightMasks.Clear();
        RenderSettings.fog = savedFog;

        if (exportCamera != null)
        {
            exportCamera.targetTexture = null;
            Destroy(exportCamera.gameObject);
            exportCamera = null;
        }
        if (exportLight != null)
        {
            Destroy(exportLight.gameObject);
            exportLight = null;
        }
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
            renderTexture = null;
        }
        if (readTexture != null)
        {
            Destroy(readTexture);
            readTexture = null;
        }
        if (pictureTexture != null)
        {
            Destroy(pictureTexture);
            pictureTexture = null;
        }
        if (renderRoot != null)
        {
            Destroy(renderRoot.gameObject);
            renderRoot = null;
        }
    }

    // Draws the renderers into a PNG centered on the export root (the pivot of the object). False when there is nothing
    // visible to draw.
    private bool Draw(Renderer[] renderers, string file, out float half)
    {
        half = 0f;
        var used = new List<Renderer>();
        if (renderers != null)
        {
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;
                // Particles, trails and lines are effects, not the shape of the object.
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer) && !(renderer is SpriteRenderer))
                    continue;
                used.Add(renderer);
            }
        }
        if (used.Count == 0)
            return false;

        Bounds bounds = used[0].bounds;
        for (int i = 1; i < used.Count; i++)
            bounds.Encapsulate(used[i].bounds);

        Vector3 pivot = renderRoot.position;
        float extent = Mathf.Max(
            Mathf.Max(Mathf.Abs(bounds.min.x - pivot.x), Mathf.Abs(bounds.max.x - pivot.x)),
            Mathf.Max(Mathf.Abs(bounds.min.y - pivot.y), Mathf.Abs(bounds.max.y - pivot.y)));
        if (extent < 0.01f)
            return false;
        half = Mathf.Ceil(extent * 1.04f * 100f) / 100f;

        var layers = new Dictionary<GameObject, int>();
        try
        {
            foreach (Renderer renderer in used)
            {
                GameObject owner = renderer.gameObject;
                if (layers.ContainsKey(owner))
                    continue;
                layers[owner] = owner.layer;
                owner.layer = ExportLayer;
            }

            exportCamera.transform.position = new Vector3(pivot.x, pivot.y, bounds.min.z - 10f);
            exportCamera.transform.rotation = Quaternion.identity;
            exportCamera.orthographicSize = half;
            exportCamera.nearClipPlane = 1f;
            exportCamera.farClipPlane = bounds.size.z + 20f;

            Color32[] overBlack = Capture(new Color(0f, 0f, 0f, 0f));
            Color32[] overWhite = Capture(new Color(1f, 1f, 1f, 0f));
            pictureTexture.SetPixels32(Combine(overBlack, overWhite));
            File.WriteAllBytes(file, pictureTexture.EncodeToPNG());
        }
        finally
        {
            // Pooled objects go back to the game: they must keep their own layers.
            foreach (KeyValuePair<GameObject, int> pair in layers)
            {
                if (pair.Key != null)
                    pair.Key.layer = pair.Value;
            }
        }
        return true;
    }

    private Color32[] Capture(Color background)
    {
        exportCamera.backgroundColor = background;
        exportCamera.Render();
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTexture;
        readTexture.ReadPixels(new Rect(0f, 0f, RenderSize, RenderSize), 0, 0, false);
        RenderTexture.active = previous;
        return readTexture.GetPixels32();
    }

    // Alpha from the two backgrounds (a = 1 - (white - black)); the color over black is the color times alpha.
    // Each output pixel averages a block of the 2x render.
    private static Color32[] Combine(Color32[] overBlack, Color32[] overWhite)
    {
        const int scale = RenderSize / PictureSize;
        const float samples = scale * scale;
        var result = new Color32[PictureSize * PictureSize];
        for (int y = 0; y < PictureSize; y++)
        {
            for (int x = 0; x < PictureSize; x++)
            {
                float alpha = 0f, red = 0f, green = 0f, blue = 0f;
                for (int dy = 0; dy < scale; dy++)
                {
                    for (int dx = 0; dx < scale; dx++)
                    {
                        int i = (y * scale + dy) * RenderSize + x * scale + dx;
                        Color32 black = overBlack[i];
                        Color32 white = overWhite[i];
                        float a = 1f - ((white.r - black.r) + (white.g - black.g) + (white.b - black.b)) / (3f * 255f);
                        if (a < 0f) a = 0f;
                        else if (a > 1f) a = 1f;
                        alpha += a;
                        red += black.r;
                        green += black.g;
                        blue += black.b;
                    }
                }
                alpha /= samples;
                red /= samples;
                green /= samples;
                blue /= samples;

                int o = y * PictureSize + x;
                if (alpha < 1f / 255f)
                {
                    result[o] = new Color32(0, 0, 0, 0);
                    continue;
                }
                result[o] = new Color32(ToByte(red / alpha), ToByte(green / alpha), ToByte(blue / alpha), ToByte(alpha * 255f));
            }
        }
        return result;
    }

    private static byte ToByte(float value)
    {
        if (value <= 0f) return 0;
        if (value >= 255f) return 255;
        return (byte)(value + 0.5f);
    }

    // ------------------------------------------------------------------ game objects

    private static bool ObjectsReady()
    {
        try
        {
            if (FindGroundContainer() == null)
                return false;
            if (FindPool("Planes") == null)
                return false;
            return SBConfig.Instance != null;
        }
        catch
        {
            return false;
        }
    }

    private static GroundAssetContainer FindGroundContainer()
    {
        GroundPlacement placement = GroundPlacement.Instance;
        if (placement == null)
            placement = FindObjectOfType<GroundPlacement>();
        if (placement != null && placement.GroundAssets != null && placement.GroundAssets.AllAssets != null && placement.GroundAssets.AllAssets.Count > 0)
            return placement.GroundAssets;

        GroundAssetContainer[] all = Resources.FindObjectsOfTypeAll<GroundAssetContainer>();
        if (all != null)
        {
            foreach (GroundAssetContainer container in all)
            {
                if (container != null && container.AllAssets != null && container.AllAssets.Count > 0)
                    return container;
            }
        }
        return null;
    }

    private static SpawnPool FindPool(string name)
    {
        try
        {
            SpawnPool pool;
            return PoolManager.Pools.TryGetValue(name, out pool) ? pool : null;
        }
        catch
        {
            return null;
        }
    }

    private static Transform Spawn(SpawnPool pool, GameObject prefab)
    {
        if (pool != null)
            return pool.Spawn(prefab);
        GameObject copy = UnityEngine.Object.Instantiate(prefab);
        return copy.transform;
    }

    private static void Release(SpawnPool pool, Transform instance)
    {
        if (instance == null)
            return;
        try
        {
            if (pool != null && pool.IsSpawned(instance))
                pool.Despawn(instance);
            else
                UnityEngine.Object.Destroy(instance.gameObject);
        }
        catch
        {
        }
    }

    // Every art name of the units of the game (UnitConfig.art), the names the map editor looks for.
    private static List<string> UnitArtList()
    {
        var names = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            SBConfig config = SBConfig.Instance;
            var units = config != null ? config.GetAllUnitConfigs() : null;
            if (units != null)
            {
                foreach (var unit in units)
                {
                    if (unit != null && !string.IsNullOrEmpty(unit.art) && !names.ContainsKey(unit.art))
                        names[unit.art] = unit.art;
                }
            }
        }
        catch (Exception e)
        {
            Warn("could not read the unit configs: " + e.GetType().Name + " " + e.Message);
        }
        return new List<string>(names.Values);
    }

    // Art drawn from GameClient.MeshAssets (a bare mesh on a generic plane prefab), read by reflection.
    private static bool IsMeshAsset(string art)
    {
        try
        {
            Type clientType = AccessTools.TypeByName("GameClient");
            object client = Member(null, clientType, "Instance");
            object assets = Member(client, null, "MeshAssets");
            IDictionary byName = Member(assets, null, "ByName") as IDictionary;
            return byName != null && byName.Contains(art);
        }
        catch
        {
            return false;
        }
    }

    private static object Member(object target, Type staticType, string name)
    {
        if (target == null && staticType == null)
            return null;
        Traverse traverse = target != null ? Traverse.Create(target) : Traverse.Create(staticType);
        Traverse field = traverse.Field(name);
        if (field.FieldExists())
            return field.GetValue();
        Traverse property = traverse.Property(name);
        if (property.PropertyExists())
            return property.GetValue();
        return null;
    }

    // ------------------------------------------------------------------ files

    // Data files of QuieterPlugin that were not written since the game started.
    private List<string> MissingDataFiles()
    {
        var missing = new List<string>();
        foreach (string name in DataFiles)
        {
            string path = Path.Combine(Paths.BepInExRootPath, name);
            try
            {
                if (!File.Exists(path) || File.GetLastWriteTimeUtc(path) < startedUtc)
                    missing.Add(name);
            }
            catch
            {
                missing.Add(name);
            }
        }
        return missing;
    }

    private static string FileName(string key)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        var name = new StringBuilder(key.Length + 4);
        foreach (char c in key)
            name.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return name.Append(".png").ToString();
    }

    private void WriteThumbs()
    {
        var json = new StringBuilder();
        json.Append("{\n");
        AppendSection(json, "ground", groundHalves);
        json.Append(",\n");
        AppendSection(json, "units", unitHalves);
        json.Append("\n}\n");
        File.WriteAllText(Path.Combine(outputFolder, "thumbs.json"), json.ToString(), new UTF8Encoding(false));
    }

    private static void AppendSection(StringBuilder json, string name, SortedDictionary<string, float> halves)
    {
        json.Append(" \"").Append(name).Append("\": {");
        bool first = true;
        foreach (KeyValuePair<string, float> pair in halves)
        {
            json.Append(first ? "\n" : ",\n");
            first = false;
            json.Append("  \"").Append(JsonEscape(pair.Key)).Append("\": {\"half\": ")
                .Append(pair.Value.ToString("0.0#", CultureInfo.InvariantCulture)).Append('}');
        }
        json.Append(first ? "}" : "\n }");
    }

    private static string JsonEscape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // Writes export_done.txt (read by SteambirdsGameFiles.exe) and closes the game.
    private void Finish(string result, string detail)
    {
        if (finished)
            return;
        finished = true;

        var lines = new List<string>();
        lines.Add("result=" + result);
        lines.Add("detail=" + detail);
        lines.Add("ground=" + groundHalves.Count + "/" + groundTotal);
        lines.Add("units=" + unitHalves.Count + "/" + unitTotal);
        List<string> missing = MissingDataFiles();
        lines.Add("missing_data=" + (missing.Count == 0 ? "-" : string.Join(",", missing.ToArray())));
        lines.Add("seconds=" + ((int)Time.realtimeSinceStartup).ToString(CultureInfo.InvariantCulture));
        foreach (string problem in problems)
            lines.Add("problem=" + problem);

        try
        {
            // Written to a temporary name first, so the tool never reads half a file.
            string done = Path.Combine(outputFolder, DoneFileName);
            string temporary = done + ".tmp";
            File.WriteAllLines(temporary, lines.ToArray());
            if (File.Exists(done))
                File.Delete(done);
            File.Move(temporary, done);
        }
        catch (Exception e)
        {
            Warn("could not write " + DoneFileName + ": " + e.Message);
        }

        Info("Export mode finished (" + result + "): " + detail + ". Closing the game.");
        Application.Quit();
    }

    private void AddProblem(string text)
    {
        if (problems.Count < MaxProblems)
            problems.Add(text);
        else if (problems.Count == MaxProblems)
            problems.Add("(more problems not listed)");
    }

    private static void Info(string text)
    {
        if (Log != null) Log.LogInfo(text);
    }

    private static void Warn(string text)
    {
        if (Log != null) Log.LogWarning("Export mode: " + text);
    }
}
