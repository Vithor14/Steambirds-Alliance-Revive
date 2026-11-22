using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BepInEx;
using UnityEngine;

// Separate plugin, compiled in the same DLL as QuieterPlugin: BepInEx loads both classes.
// It only reads values from the game; it changes nothing.
// When the terrain changes (Nexus, tutorial...), it writes to BepInEx\terrain_dump.txt the numeric fields
// of WorldLayout, WorldMaker, GroundPlacement and GameClient. It helps to find the real cellSize, worldRadius and tileSize.
[BepInPlugin("local.steambirds.terraindump", "Steambirds Terrain Dump", "1.0.0")]
public class DumpTerrain : BaseUnityPlugin
{
    // Game types whose numeric fields matter. GameClient is filtered by name so it does not flood the file.
    private static readonly string[] TargetTypes = { "WorldLayout", "WorldMaker", "GroundPlacement", "GameClient" };

    private static readonly Regex RelevantNames = new Regex(
        "cell|radius|tile|size|world|terrain|scale|grid|dist|side|length|width|height|unit|extent|bound|elevation|chunk",
        RegexOptions.IgnoreCase);

    private const int MaxInstances = 3;

    private string file;
    private float nextCheck;
    private string lastTerrain = "";

    private void Awake()
    {
        file = Path.Combine(Paths.BepInExRootPath, "terrain_dump.txt");
        try
        {
            File.WriteAllText(file,
                "=== terrain dump started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine);
        }
        catch
        {
        }

        Logger.LogInfo("Terrain dump on: " + file);
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup < nextCheck) return;
        nextCheck = Time.realtimeSinceStartup + 2f;

        string terrainName;
        try
        {
            var terrain = ReadMember(GameClient.Instance, "CurrentTerrainConfig") as TerrainConfig;
            if (terrain == null) return;
            terrainName = terrain.name ?? "sem_nome";
        }
        catch
        {
            return;
        }

        if (terrainName == lastTerrain) return;
        lastTerrain = terrainName;
        Dump(terrainName);
    }

    private void Dump(string terrainName)
    {
        var lines = new List<string>
        {
            "",
            "=== terrain: " + terrainName + " (" + DateTime.Now.ToString("HH:mm:ss") + ") ==="
        };

        var gameClient = GameClient.Instance;
        foreach (var typeName in TargetTypes)
        {
            try
            {
                DumpType(typeName, gameClient, lines);
            }
            catch (Exception e)
            {
                lines.Add("--- " + typeName + ": error " + e.GetType().Name + " " + e.Message);
            }
        }

        try
        {
            File.AppendAllLines(file, lines);
            Logger.LogInfo("Terrain dump written to " + terrainName);
        }
        catch
        {
        }
    }

    private static void DumpType(string typeName, GameClient gameClient, List<string> lines)
    {
        var type = typeof(GameClient).Assembly.GetType(typeName);
        if (type == null)
        {
            lines.Add("--- " + typeName + ": type not found");
            return;
        }

        bool applyFilter = typeName == "GameClient";
        var flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var fields = type.GetFields(flags).Where(c => IsOfInterest(c, applyFilter)).ToArray();

        lines.Add("--- " + typeName + " ---");

        foreach (var field in fields.Where(c => c.IsStatic))
        {
            lines.Add("   static " + field.Name + " (" + field.FieldType.Name + ") = " + ReadValue(field, null));
        }

        var instances = FindInstances(type, gameClient);
        if (instances.Count == 0)
        {
            lines.Add("   (no instance found)");
            return;
        }

        foreach (var obj in instances.Take(MaxInstances))
        {
            lines.Add("   instancia: " + NameOf(obj));
            foreach (var field in fields.Where(c => !c.IsStatic))
            {
                lines.Add("      " + field.Name + " (" + field.FieldType.Name + ") = " + ReadValue(field, obj));
            }
        }
    }

    // Looks for instances of the type: Unity objects in the scene/memory and GameClient fields that have that type.
    private static List<object> FindInstances(Type type, GameClient gameClient)
    {
        var foundItems = new List<object>();

        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
        {
            foreach (var o in Resources.FindObjectsOfTypeAll(type))
            {
                if (!foundItems.Contains(o)) foundItems.Add(o);
            }
        }

        if (gameClient != null)
        {
            if (type.IsInstanceOfType(gameClient) && !foundItems.Contains(gameClient))
                foundItems.Add(gameClient);

            // Objects stored in GameClient fields (e.g. a reference to WorldLayout).
            foreach (var field in gameClient.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                object value;
                try
                {
                    value = field.GetValue(gameClient);
                }
                catch
                {
                    continue;
                }

                if (value != null && type.IsInstanceOfType(value) && !foundItems.Contains(value))
                    foundItems.Add(value);
            }
        }

        return foundItems;
    }

    private static bool IsOfInterest(FieldInfo field, bool applyFilter)
    {
        var t = field.FieldType;
        bool numeric = t == typeof(float) || t == typeof(int) || t == typeof(double) ||
                        t == typeof(short) || t == typeof(byte) || t == typeof(Vector2) || t == typeof(Vector3);
        if (!numeric) return false;
        return !applyFilter || RelevantNames.IsMatch(field.Name);
    }

    private static string ReadValue(FieldInfo field, object obj)
    {
        try
        {
            return Text(field.GetValue(obj));
        }
        catch (Exception e)
        {
            return "error " + e.GetType().Name;
        }
    }

    private static string Text(object value)
    {
        if (value == null) return "null";
        if (value is float f) return f.ToString(CultureInfo.InvariantCulture);
        if (value is double d) return d.ToString(CultureInfo.InvariantCulture);
        return value.ToString();
    }

    private static string NameOf(object obj)
    {
        if (obj is UnityEngine.Object uo) return uo.name;
        return obj.GetType().Name;
    }

    // Reads an instance property or field, including inherited ones (e.g. CurrentTerrainConfig in GameBase).
    private static object ReadMember(object target, string name)
    {
        if (target == null)
            return null;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
                                   BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var type = target.GetType(); type != null; type = type.BaseType)
        {
            var prop = type.GetProperty(name, flags);
            if (prop != null)
                return prop.GetValue(target, null);

            var field = type.GetField(name, flags);
            if (field != null)
                return field.GetValue(target);
        }
        return null;
    }
}
