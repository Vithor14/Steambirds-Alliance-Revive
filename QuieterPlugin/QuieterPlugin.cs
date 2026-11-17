using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[BepInPlugin("local.steambirds.quieter", "Steambirds Quieter", "1.20.0")]
public class QuieterPlugin : BaseUnityPlugin
{
    private const string Version = "1.20.0";

    private ConfigEntry<bool> hideDebug;
    private ConfigEntry<bool> swallowPlaylistError;
    private ConfigEntry<bool> disablePhantasm;
    private ConfigEntry<bool> exportPrefabTable;
    private ConfigEntry<bool> exportRuntimeValues;
    private ConfigEntry<bool> exportTerrainConfigs;
    private ConfigEntry<bool> exportAsciiMaps;
    private ConfigEntry<bool> exportFloorAssets;
    private ConfigEntry<bool> diagnoseSpawn;
    private ConfigEntry<bool> diagnoseDispatch;
    private ConfigEntry<bool> reduceHarmonyLog;
    private ConfigEntry<bool> exportActions;
    private ConfigEntry<bool> exportUnits;
    private ConfigEntry<bool> exportMapInfo;
    private ConfigEntry<bool> showFlockHallArt;
    private ConfigEntry<bool> exportPortraits;
    private ConfigEntry<bool> fixDeathScreen;

    // 1.20.0: DeathWrapupStateMachine never sets hasDeathStats back to false after the first death. On the second
    // death the window opens at once with the numbers of the previous death, and the new ones (ClientDeathSummaryInformation)
    // arrive too late. The patch forgets the old numbers when it starts waiting for the new ones.
    private static readonly FieldInfo deathStatsField = AccessTools.Field(typeof(DeathWrapupStateMachine), "hasDeathStats");

    // Flock Hall: the RebelCity_Building_FlockHall art arrives with its 7 renderers turned off (buildings_log.txt of 11/14),
    // so the building is invisible. The plugin turns the mesh renderers on and checks again every second.
    private static bool enableFlockHallArt = true;
    private static readonly List<Transform> flockHallArts = new List<Transform>();
    private static int flockHallReenables;
    private float nextFlockHallCheck;

    private bool tableExported;
    private bool valuesWritten;
    private float nextAttempt;
    private int runtimeValuesFailures;

    private float nextTerrainAttempt;
    private string lastTerrainWritten = "";
    private bool terrainWrittenNotice;

    private bool floorWritten;
    private float nextFloorAttempt;
    private bool floorWrittenNotice;

    private float nextPeerCheck;
    private string previousPeer = "";

    private float nextActionsAttempt;
    private int totalActionsWritten = -1;
    private bool actionsWrittenNotice;

    private float nextUnitsAttempt;
    private int totalUnitsWritten = -1;
    private bool unitsWrittenNotice;

    private float nextMapInfoAttempt;
    private int totalTerrainsWritten = -1;
    private bool mapInfoWrittenNotice;

    private float nextPortraitsAttempt;
    private int totalPortraitsWritten = -1;
    private bool portraitsWrittenNotice;


    private static int spawnUnitCalls;
    private static int dispatchCounter;
    private static string spawnLogFile = "";
    private static string dispatchLogFile = "";
    private static string asciiMapsFile = "";
    private static string floorFile = "";
    private static string buildingsFile = "";

    private void Awake()
    {
        hideDebug = Config.Bind("Logs", "EsconderDebugEWarnings", true,
            "Esconde Debug.Log e Debug.LogWarning do Player.log. Erros e excecoes continuam aparecendo.");
        swallowPlaylistError = Config.Bind("Logs", "EngolirErroPlaylist", true,
            "Engole a NullReferenceException do SteambirdsAudioMixer (PlaylistController ausente).");
        disablePhantasm = Config.Bind("Logs", "DesligarTelemetriaPhantasm", true,
            "Stops the Phantasm telemetry from being sent. Avoids the curl errors in the log.");
        exportPrefabTable = Config.Bind("Diagnostico", "ExportarTabelaDePrefabs", true,
            "Writes BepInEx\\prefab_table.txt with the index of each prefab, when the scene loads.");
        exportRuntimeValues = Config.Bind("Diagnostico", "ExportarValoresDeRuntime", true,
            "Writes BepInEx\\runtime_values.txt with LocalPeer.Id, teamIndex, the plane's templateHash, netIdShort, starting items and class variants.");
        exportTerrainConfigs = Config.Bind("Diagnostico", "ExportarConfigsDeTerreno", true,
            "Writes BepInEx\\terrain_config_<name>.txt with the TerrainConfig of each terrain the client enters (Nexus, tutorial...).");
        exportAsciiMaps = Config.Bind("Diagnostico", "ExportarMapasAscii", true,
            "Writes BepInEx\\ascii_maps.txt with each ASCIIMap loaded from the configs: origin, text, grid and legend.");
        exportFloorAssets = Config.Bind("Diagnostico", "ExportarAssetsDeChao", true,
            "Writes BepInEx\\ground_assets.txt with the ID, name, piece and size of each floor asset of the GroundAssetContainer, when the world loads.");
        diagnoseSpawn = Config.Bind("Diagnostico", "DiagnosticarSpawn", true,
            "Registra em BepInEx\\spawn_log.txt as chamadas de EntityHandler.SpawnUnit do plano e o LocalPeer.Id.");
        diagnoseDispatch = Config.Bind("Diagnostico", "DiagnosticarDispatch", true,
            "Logs in BepInEx\\dispatch_log.txt the message dispatch, refusals and receive errors.");
        reduceHarmonyLog = Config.Bind("Logs", "ReduzirLogDoHarmony", true,
            "Lets HarmonyX log only warnings and errors. Without this, each patch dumps the whole IL of the method into LogOutput.log and Player.log.");
        exportActions = Config.Bind("Diagnostico", "ExportarAcoes", true,
            "Writes BepInEx\\actions_config.txt with the skill clouds (name, hash, prefab, duration, radius) and the maneuvers (ActionID and hash). The server reads this file to create the clouds and name the maneuvers.");
        exportUnits = Config.Bind("Diagnostico", "ExportarUnidades", true,
            "Writes BepInEx\\units_config.txt with each UnitConfig (name, hash, health, speed, turn and guns). The server reads this file to create enemies and bosses.");
        showFlockHallArt = Config.Bind("Correcoes", "MostrarArteDoFlockHall", true,
            "Turns on the renderers of the Flock Hall art (RebelCity_Building_FlockHall), which come off in this client and leave the building invisible in the Rebel City. The curved signs (group Signs, with the text HANGAR) stay off.");
        enableFlockHallArt = showFlockHallArt.Value;
        exportMapInfo = Config.Bind("Diagnostico", "ExportarInfoDeMapa", true,
            "Writes BepInEx\\mapinfo_config.txt with the ClientMapInfo of each terrain (client events: tutorial and Nexus hints), serialized with the game's ClientMapInfo.Serializer. The server sends these bytes to the client in ClientSetClientMapInfo.");
        exportPortraits = Config.Bind("Diagnostico", "ExportarRetratos", true,
            "Writes BepInEx\\portraits_config.txt with the pilot portrait pieces (PortraitParts pool: id, slot, species and excluded slot). The server picks the pilot's portrait with them.");
        fixDeathScreen = Config.Bind("Correcoes", "CorrigirTelaDeMorte", true,
            "From the second death on, the client shows the numbers of the previous death in the death window (DeathWrapupStateMachine does not forget the data). Turns on a patch that forgets the old numbers while waiting for the new ones.");

        spawnLogFile = Path.Combine(Paths.BepInExRootPath, "spawn_log.txt");
        dispatchLogFile = Path.Combine(Paths.BepInExRootPath, "dispatch_log.txt");
        asciiMapsFile = Path.Combine(Paths.BepInExRootPath, "mapas_ascii.txt");
        floorFile = Path.Combine(Paths.BepInExRootPath, "ground_assets.txt");
        buildingsFile = Path.Combine(Paths.BepInExRootPath, "predios_log.txt");
        try { File.WriteAllText(buildingsFile, ""); } catch { }
        // placas_log.txt was from the floor signs fix (removed in 1.19.0): deletes the old file.
        try { File.Delete(Path.Combine(Paths.BepInExRootPath, "placas_log.txt")); } catch { }
        try { File.WriteAllText(spawnLogFile, ""); } catch { }
        try { File.WriteAllText(dispatchLogFile, ""); } catch { }
        try { File.WriteAllText(asciiMapsFile, ""); } catch { }

        Logger.LogInfo("Steambirds Quieter " + Version + " iniciado.");

        if (hideDebug.Value)
            Debug.unityLogger.filterLogType = LogType.Error;

        // Before creating Harmony: its creation already writes to the Info channel.
        if (reduceHarmonyLog.Value)
            ReduceHarmonyLog();

        var harmony = new Harmony("local.steambirds.quieter");

        if (swallowPlaylistError.Value)
        {
            var type = AccessTools.TypeByName("SteambirdsAudioMixer");
            var method = type != null ? AccessTools.Method(type, "HandlePlaylistsBasedOnLocation") : null;
            if (method != null)
            {
                harmony.Patch(method, finalizer: new HarmonyMethod(typeof(QuieterPlugin), nameof(SwallowException)));
                Logger.LogInfo("Erro do PlaylistController silenciado.");
            }
            else
            {
                Logger.LogWarning("HandlePlaylistsBasedOnLocation not found.");
            }
        }

        if (disablePhantasm.Value)
        {
            var type = AccessTools.TypeByName("Phantasm");
            var method = type != null ? AccessTools.Method(type, "SendSerialized") : null;
            if (method != null)
            {
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(QuieterPlugin), nameof(IgnorePhantasmSend)));
                Logger.LogInfo("Telemetria Phantasm desligada.");
            }
            else
            {
                Logger.LogWarning("Phantasm.SendSerialized not found.");
            }
        }

        if (diagnoseSpawn.Value)
        {
            var spawnMethod = AccessTools.Method(typeof(EntityHandler), "SpawnUnit");
            if (spawnMethod != null)
            {
                harmony.Patch(spawnMethod, prefix: new HarmonyMethod(typeof(QuieterPlugin), nameof(LogSpawnUnit)));
                Logger.LogInfo("Diagnostico de SpawnUnit ativo.");
            }
            else
            {
                Logger.LogWarning("EntityHandler.SpawnUnit not found.");
            }
        }

        if (diagnoseDispatch.Value)
        {
            var dispatcherType = AccessTools.TypeByName("GeneratedDispatcher");
            var dispatchMethod = dispatcherType != null ? AccessTools.Method(dispatcherType, "Dispatch") : null;
            if (dispatchMethod != null)
            {
                harmony.Patch(dispatchMethod, postfix: new HarmonyMethod(typeof(QuieterPlugin), nameof(LogDispatch)));
                Logger.LogInfo("Diagnostico de Dispatch ativo.");
            }
            else
            {
                Logger.LogWarning("GeneratedDispatcher.Dispatch not found.");
            }

            var errorMethod = AccessTools.Method(typeof(EntityHandler), "MessageRecvError");
            if (errorMethod != null)
            {
                harmony.Patch(errorMethod, prefix: new HarmonyMethod(typeof(QuieterPlugin), nameof(LogReceiveError)));
                Logger.LogInfo("Receive error diagnostics on.");
            }
            else
            {
                Logger.LogWarning("EntityHandler.MessageRecvError not found.");
            }

            var batchMethod = AccessTools.Method(typeof(EntityHandler), "SpawnUnitsBatch");
            if (batchMethod != null)
            {
                harmony.Patch(batchMethod,
                    prefix: new HarmonyMethod(typeof(QuieterPlugin), nameof(LogSpawnBatch)),
                    finalizer: new HarmonyMethod(typeof(QuieterPlugin), nameof(LogBatchFailure)));
                Logger.LogInfo("Diagnostico de SpawnUnitsBatch ativo.");
            }
            else
            {
                Logger.LogWarning("EntityHandler.SpawnUnitsBatch not found.");
            }

            var baseType = AccessTools.TypeByName("Copper.CopperDispatcher") ?? AccessTools.TypeByName("CopperDispatcher");
            var rejectionMethod = baseType != null ? AccessTools.Method(baseType, "RejectMessage") : null;
            if (rejectionMethod != null)
            {
                harmony.Patch(rejectionMethod, prefix: new HarmonyMethod(typeof(QuieterPlugin), nameof(LogRejection)));
                Logger.LogInfo("Diagnostico de recusas ativo.");
            }
            else
            {
                Logger.LogWarning("CopperDispatcher.RejectMessage not found.");
            }
        }

        // 1.17.0: diagnostics of the Nexus buildings (Store, Flock Hall...): which art each one loaded and where it
        // ended up. Writes BepInEx\buildings_log.txt. It helps to know whether an "invisible" building has no art or hidden art.
        try
        {
            var buildingInitMethod = AccessTools.Method(typeof(Building), "Init");
            if (buildingInitMethod != null)
            {
                harmony.Patch(buildingInitMethod, postfix: new HarmonyMethod(typeof(QuieterPlugin), nameof(LogBuilding)));
                Logger.LogInfo("Diagnostico de predios ativo.");
            }
            else
            {
                Logger.LogWarning("Building.Init not found.");
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning("Buildings diagnostics off: " + e.GetType().Name + " " + e.Message);
        }

        if (fixDeathScreen.Value)
        {
            var waitMethod = AccessTools.Method(typeof(DeathWrapupStateMachine), "EnterWaitingForStats");
            if (waitMethod != null && deathStatsField != null)
            {
                harmony.Patch(waitMethod, prefix: new HarmonyMethod(typeof(QuieterPlugin), nameof(ForgetPreviousDeath)));
                Logger.LogInfo("Correcao da janela de morte ativa.");
            }
            else
            {
                Logger.LogWarning("DeathWrapupStateMachine.EnterWaitingForStats or hasDeathStats not found.");
            }
        }

        if (exportAsciiMaps.Value)
        {
            var asciiType = AccessTools.TypeByName("ASCIIMap");
            var fromDocMethod = asciiType != null ? AccessTools.Method(asciiType, "FromDoc") : null;
            if (fromDocMethod != null)
            {
                harmony.Patch(fromDocMethod, postfix: new HarmonyMethod(typeof(QuieterPlugin), nameof(LogAsciiMap)));
                Logger.LogInfo("ASCII maps export on.");
            }
            else
            {
                Logger.LogWarning("ASCIIMap.FromDoc not found.");
            }
        }
    }

    private void Update()
    {
        if (enableFlockHallArt && flockHallArts.Count > 0 && Time.realtimeSinceStartup >= nextFlockHallCheck)
        {
            nextFlockHallCheck = Time.realtimeSinceStartup + 1f;
            CheckFlockHallArt();
        }

        if (!tableExported && exportPrefabTable.Value)
        {
            var container = GlobalPrefabContainer.Instance;
            if (container != null && container.prefabTable != null && container.prefabTable.Length > 0)
            {
                tableExported = true;
                ExportPrefabTable(container);
            }
        }

        if (!valuesWritten && exportRuntimeValues.Value && Time.realtimeSinceStartup >= nextAttempt)
        {
            nextAttempt = Time.realtimeSinceStartup + 2f;
            TryWriteRuntimeValues();
        }

        if (exportTerrainConfigs.Value && Time.realtimeSinceStartup >= nextTerrainAttempt)
        {
            nextTerrainAttempt = Time.realtimeSinceStartup + 2f;
            TryWriteTerrainConfigs();
        }

        if (!floorWritten && exportFloorAssets.Value && Time.realtimeSinceStartup >= nextFloorAttempt)
        {
            nextFloorAttempt = Time.realtimeSinceStartup + 2f;
            TryWriteFloorAssets();
        }

        if (exportActions.Value && Time.realtimeSinceStartup >= nextActionsAttempt)
        {
            nextActionsAttempt = Time.realtimeSinceStartup + 5f;
            TryWriteActions();
        }

        if (exportUnits.Value && Time.realtimeSinceStartup >= nextUnitsAttempt)
        {
            nextUnitsAttempt = Time.realtimeSinceStartup + 5f;
            TryWriteUnits();
        }

        if (exportMapInfo.Value && Time.realtimeSinceStartup >= nextMapInfoAttempt)
        {
            nextMapInfoAttempt = Time.realtimeSinceStartup + 5f;
            TryWriteMapInfo();
        }

        if (exportPortraits.Value && Time.realtimeSinceStartup >= nextPortraitsAttempt)
        {
            nextPortraitsAttempt = Time.realtimeSinceStartup + 5f;
            TryWritePortraits();
        }

        // 1.17.0: the floor signs fix was turned off; in 1.19.0 its code was removed for good. The server puts
        // the floor texts through the ClientMapInfo (lore_text_relative_pos event), with no LoreText entity.

        if (diagnoseSpawn.Value && Time.realtimeSinceStartup >= nextPeerCheck)
        {
            nextPeerCheck = Time.realtimeSinceStartup + 2f;
            var current = GetLocalPeer();
            if (current != previousPeer && current != "(indisponivel)")
            {
                previousPeer = current;
                Log(spawnLogFile, DateTime.Now.ToString("HH:mm:ss.fff") + " LocalPeer.Id = " + current);
            }
        }
    }

    private void ExportPrefabTable(GlobalPrefabContainer container)
    {
        try
        {
            var lines = new List<string>
            {
                "playerPlanePrefabIndex=" + container.playerPlanePrefabIndex,
                "genericPrefabIndex=" + container.genericPrefabIndex,
                "total=" + container.prefabTable.Length,
                "",
                "--- prefabTable: indice <TAB> nome do GameObject ---"
            };

            for (int i = 0; i < container.prefabTable.Length; i++)
            {
                var go = container.prefabTable[i];
                lines.Add(i + "\t" + (go != null ? go.name : "(null)"));
            }

            lines.Add("");
            lines.Add("--- prefabIndicesByName: indice <TAB> templateName ---");
            foreach (var pair in container.prefabIndicesByName.OrderBy(p => p.Value))
                lines.Add(pair.Value + "\t" + pair.Key);

            var path = Path.Combine(Paths.BepInExRootPath, "prefab_table.txt");
            File.WriteAllLines(path, lines);
            Logger.LogInfo("Prefab table written to " + path);
        }
        catch (Exception e)
        {
            Logger.LogError("Falha ao exportar tabela de prefabs: " + e);
        }
    }

    private void TryWriteRuntimeValues()
    {
        try
        {
            var pre = GlobalPrefabContainer.Instance;
            var sb = SBConfig.Instance;
            if (pre == null || sb == null || pre.prefabTable == null || pre.prefabTable.Length == 0)
                return;

            var lines = new List<string>();

            // If LocalPeer does not exist yet (menu), the exception falls into the catch and it tries again.
            lines.Add("LocalPeer.Id=" + GameClient.Instance.LocalPeer.Id.ToString());
            lines.Add("playerTeam.teamIndex=" + Team.playerTeam.teamIndex);

            var planePrefab = pre.prefabTable[pre.playerPlanePrefabIndex];
            var entity = planePrefab.GetComponent<Entity>();
            var entityName = entity != null ? entity.templateName : "(no Entity)";
            lines.Add("prefab playerPlane: GameObject.name=" + planePrefab.name +
                " | Entity.templateName=" + entityName);

            var candidates = new List<string>
            {
                "playerPlane",
                entityName,
                planePrefab.name,
                "PlayerWalker",
                "walker",
                "walkerClassPlane"
            };
            foreach (var name in candidates.Distinct())
                lines.Add("GetTemplateHash(" + name + ")=" + sb.GetTemplateHash(name));

            LogPlayerClasses(sb, lines);
            LogTutorialConfig(sb, lines);

            var path = Path.Combine(Paths.BepInExRootPath, "runtime_values.txt");
            File.WriteAllLines(path, lines);
            valuesWritten = true;
            Logger.LogInfo("Valores de runtime gravados em " + path);
        }
        catch (Exception e)
        {
            runtimeValuesFailures++;
            if (runtimeValuesFailures == 1)
                Logger.LogWarning("Waiting for the game to be ready to read runtime values: " + e.GetType().Name);
        }
    }

    // Writes the client's current terrain whenever the name changes (Nexus -> tutorial, etc.).
    // Each terrain becomes a file: terrain_config_<name>.txt.
    private void TryWriteTerrainConfigs()
    {
        try
        {
            var terrain = ReadMember(GameClient.Instance, "CurrentTerrainConfig") as TerrainConfig;
            if (terrain == null)
                return;

            var name = terrain.name ?? "sem_nome";
            if (name == lastTerrainWritten)
                return;

            var lines = new List<string>();

            lines.Add("=== TerrainConfig atual (GameClient.CurrentTerrainConfig) ===");
            lines.Add("name=" + terrain.name);
            lines.Add("seed=" + terrain.seed + " role=" + terrain.role);
            lines.Add("playerStartElevation=" + terrain.playerStartElevation +
                " originElevation=" + terrain.originElevation +
                " distancePerElevation=" + terrain.distancePerElevation);
            lines.Add("minDiskRadius=" + terrain.minDiskRadius +
                " maxDiskRadius=" + terrain.maxDiskRadius +
                " maxAllowedOverlapOnSubterritory=" + terrain.maxAllowedOverlapOnSubterritory);
            lines.Add("cameraSideLengthOverride=" + terrain.cameraSideLengthOverride +
                " localChatDistance=" + terrain.localChatDistance +
                " squadClientZoomInDistance=" + terrain.squadClientZoomInDistance);
            lines.Add("ambientTrack=" + terrain.ambientTrack +
                " musicTrack=" + terrain.musicTrack +
                " bossMusicTrack=" + terrain.bossMusicTrack);
            lines.Add("isPossibleToSpawnPortals=" + terrain.isPossibleToSpawnPortals +
                " portalSpawnException=" + terrain.portalSpawnException);
            lines.Add("clientMapInfo=" + (terrain.clientMapInfo != null ? "presente" : "null"));
            lines.Add("");
            lines.Add("--- territories (TerritoryConfig) ---");

            if (terrain.territories != null)
            {
                foreach (var tc in terrain.territories)
                {
                    if (tc == null)
                        continue;

                    string scatter = tc.scatter != null
                        ? "scatter.distance=" + tc.scatter.distance + " scatter.size=" + tc.scatter.size
                        : "scatter=null";
                    string strip = tc.strip != null
                        ? "strip.distance=" + tc.strip.distance +
                          " strip.minLength=" + tc.strip.minLength +
                          " strip.maxLength=" + tc.strip.maxLength +
                          " strip.straightness=" + tc.strip.straightness +
                          " strip.chanceOfMissing=" + tc.strip.chanceOfMissing
                        : "strip=null";

                    lines.Add("territory name=" + tc.name +
                        " sortOrder=" + tc.sortOrder +
                        " minElevation=" + tc.minElevation +
                        " maxElevation=" + tc.maxElevation +
                        " tier=" + tc.tier +
                        " wallPlacement=" + tc.wallPlacement +
                        " maxBossEnemies=" + tc.maxBossEnemies +
                        " totalEnemiesPer10x10=" + tc.totalEnemiesPer10x10 +
                        " secondsPerStandardEnemyIn10x10=" + tc.secondsPerStandardEnemyIn10x10);
                    lines.Add("   " + scatter + " | " + strip);
                }
            }

            lines.Add("");
            lines.Add("=== Tile types (TerritoryTileConfigContainer, by ID) ===");
            var tiles = SBConfig.Instance != null ? SBConfig.Instance.GetTerritoryConfigs() : null;
            if (tiles != null && tiles.configs != null)
            {
                foreach (var pair in tiles.configs.OrderBy(p => p.Value.ID))
                {
                    var t = pair.Value;
                    lines.Add("tile name=" + pair.Key +
                        " ID=" + t.ID +
                        " basedOn=" + t.basedOn +
                        " groundTag=" + t.groundTag +
                        " cliffTag=" + t.cliffTag +
                        " wallTag=" + t.wallTag +
                        " scatterTag=" + t.scatterTag +
                        " fenceTag=" + t.fenceTag +
                        " continuityGroup=" + t.continuityGroup +
                        " closeCanyonGroup=" + t.closeCanyonGroup);
                }
            }
            else
            {
                lines.Add("(GetTerritoryConfigs indisponivel)");
            }

            lines.Add("");
            lines.Add("=== Tiles received by the client ===");
            try
            {
                var grid = GameClient.Instance.GetTerritoryTiles();
                lines.Add(grid == null ? "GetTerritoryTiles: null" : "GetTerritoryTiles: exists");
            }
            catch (Exception e)
            {
                lines.Add("GetTerritoryTiles: error " + e.GetType().Name);
            }

            var fileName = "terrain_config_" + SanitizeFileName(name) + ".txt";
            var path = Path.Combine(Paths.BepInExRootPath, fileName);
            File.WriteAllLines(path, lines);
            lastTerrainWritten = name;
            Logger.LogInfo("Configs de terreno gravadas em " + path);
        }
        catch (Exception e)
        {
            if (!terrainWrittenNotice)
            {
                terrainWrittenNotice = true;
                Logger.LogWarning("Falha ao ler configs de terreno: " + e.GetType().Name + " " + e.Message);
            }
        }
    }

    // Writes the floor assets of the GroundAssetContainer (the same one the client's GroundPlacement uses).
    // The ID of each asset is the key that the TerritoryTile sends the client (groundTileAssets / groundScatterAssets).
    private void TryWriteFloorAssets()
    {
        try
        {
            GroundAssetContainer container = null;

            var placement = FindObjectOfType<GroundPlacement>();
            if (placement != null)
                container = placement.GroundAssets;

            if (container == null)
            {
                var all = Resources.FindObjectsOfTypeAll<GroundAssetContainer>();
                if (all != null && all.Length > 0)
                    container = all[0];
            }

            if (container == null || container.AllAssets == null || container.AllAssets.Count == 0)
                return;

            var lines = new List<string>();
            lines.Add("=== GroundAssetContainer.AllAssets ===");
            lines.Add("total=" + container.AllAssets.Count);
            lines.Add("formato: ID | name | fourPiece | size");
            lines.Add("");

            foreach (var a in container.AllAssets.Where(x => x != null).OrderBy(x => x.ID))
            {
                string piece = a.fourPiece.HasValue ? a.fourPiece.Value.ToString() : "-";
                lines.Add("ID=" + a.ID +
                    " name=" + a.name +
                    " fourPiece=" + piece +
                    " size=" + a.size.x + "x" + a.size.y);
            }

            var path = floorFile;
            File.WriteAllLines(path, lines);
            floorWritten = true;
            Logger.LogInfo("Assets de chao gravados em " + path);
        }
        catch (Exception e)
        {
            if (!floorWrittenNotice)
            {
                floorWrittenNotice = true;
                Logger.LogWarning("Falha ao ler assets de chao: " + e.GetType().Name + " " + e.Message);
            }
        }
    }

    // HarmonyX has log channels (Info, IL, Warn, Error...). With Info and IL on in BepInEx.cfg,
    // each patch writes the whole IL of the method: thousands of lines that hide the real errors.
    // Here the filter stays on warnings and errors only. It uses reflection so it does not depend on the HarmonyX version.
    private void ReduceHarmonyLog()
    {
        try
        {
            var type = typeof(Harmony).Assembly.GetType("HarmonyLib.Tools.Logger");
            var filter = type != null
                ? type.GetProperty("ChannelFilter", BindingFlags.Public | BindingFlags.Static)
                : null;
            if (filter == null)
            {
                Logger.LogWarning("HarmonyLib.Tools.Logger.ChannelFilter not found; the HarmonyX log stays as it is.");
                return;
            }

            filter.SetValue(null, Enum.Parse(filter.PropertyType, "Warn, Error"), null);
            Logger.LogInfo("HarmonyX log reduced to warnings and errors.");
        }
        catch (Exception e)
        {
            Logger.LogWarning("Nao consegui reduzir o log do HarmonyX: " + e.GetType().Name + " " + e.Message);
        }
    }

    // Writes the skill clouds and the maneuvers to BepInEx\actions_config.txt, for the server.
    // In the original game the server creates the cloud when the client sends ServerUnitAction(2, name);
    // for that it needs the cloud's hash, the prefab index, the duration and the radius.
    // The file is rewritten if the number of clouds or maneuvers changes (items loaded later).
    private void TryWriteActions()
    {
        try
        {
            var pre = GlobalPrefabContainer.Instance;
            var sb = SBConfig.Instance;
            if (pre == null || sb == null || pre.prefabTable == null || pre.prefabTable.Length == 0)
                return;

            var clouds = sb.GetCloudConfigs();
            var maneuvers = sb.GetManeuverConfigs();
            int totalClouds = clouds != null ? clouds.Count : 0;
            int totalManeuvers = maneuvers != null ? maneuvers.Count : 0;
            if (totalClouds == 0)
                return;
            if (totalClouds + totalManeuvers == totalActionsWritten)
                return;

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new List<string>();
            lines.Add("# Generated by the Steambirds Quieter plugin. Fields separated by TAB; numbers with a decimal point.");
            lines.Add("# nuvem <nome> <hash> <cloudPrefab> <indiceDoPrefab> <lifetime> <growtime> <initialRadius> <finalRadius> <presaAoDono 0/1> <resolvivel 0/1>");
            lines.Add("# manobra <actionId> <hash> <name>");

            // Copy of the keys: the dictionary may gain entries while the items load.
            foreach (var name in new List<string>(clouds.Keys))
            {
                CloudConfig cloud;
                if (!clouds.TryGetValue(name, out cloud) || cloud == null)
                    continue;

                // A cloud with a problem does not stop the others from being written.
                try
                {
                    string prefab = cloud.cloudPrefab ?? "";
                    int index = -1;
                    int found;
                    if (prefab.Length > 0 && pre.prefabIndicesByName != null && pre.prefabIndicesByName.TryGetValue(prefab, out found))
                        index = found;

                    // Hash that goes in the unit options. The client only accepts the cloud if it finds the config by it.
                    short hash = cloud.hash;
                    bool resolvable = sb.GetCloudConfig(hash) == cloud;

                    lines.Add(string.Join("\t", new string[]
                    {
                        "nuvem",
                        name,
                        hash.ToString(inv),
                        prefab,
                        index.ToString(inv),
                        cloud.lifetime.ToString("R", inv),
                        cloud.growtime.ToString("R", inv),
                        cloud.initialRadius.ToString("R", inv),
                        cloud.finalRadius.ToString("R", inv),
                        cloud.isAttachedToSpawner ? "1" : "0",
                        resolvable ? "1" : "0"
                    }));
                }
                catch (Exception e)
                {
                    lines.Add("# nuvem " + name + " ignorada: " + e.GetType().Name);
                }
            }

            if (maneuvers != null)
            {
                foreach (var maneuver in new List<ManeuverConfig>(maneuvers))
                {
                    if (maneuver == null)
                        continue;

                    string action = maneuver.ActionID;
                    if (string.IsNullOrEmpty(action))
                        continue;

                    // Same number the client sends in ServerUnitManeuver.
                    lines.Add("manobra\t" + action + "\t" + action.GetHashCode().ToString(inv) + "\t" + (maneuver.name ?? ""));
                }
            }

            var path = Path.Combine(Paths.BepInExRootPath, "actions_config.txt");
            File.WriteAllLines(path, lines.ToArray());
            totalActionsWritten = totalClouds + totalManeuvers;
            Logger.LogInfo("Acoes gravadas em " + path + " (" + totalClouds + " nuvens, " + totalManeuvers + " manobras)");
        }
        catch (Exception e)
        {
            if (!actionsWrittenNotice)
            {
                actionsWrittenNotice = true;
                Logger.LogWarning("Falha ao gravar actions_config.txt: " + e.GetType().Name + " " + e.Message);
            }
        }
    }

    // Writes all the UnitConfigs to BepInEx\units_config.txt, for the server to create enemies and bosses.
    // The templateHash comes from Mono's string.GetHashCode, which the server (.NET 8) cannot compute:
    // that is why the server depends on this file to send the SpawnUnitsBatch of an enemy.
    // Health already comes out multiplied by the TierConfig (the same calculation as UnitScript.ImportConfig).
    // The guns come out in the order of UnitConfig.gunMounts: the index is the mountIndex that goes in the GunFire.
    // The file is rewritten if the number of units changes.
    private void TryWriteUnits()
    {
        try
        {
            var sb = SBConfig.Instance;
            if (sb == null)
                return;

            var units = sb.GetAllUnitConfigs();
            if (units == null || units.Count == 0)
                return;
            if (units.Count == totalUnitsWritten)
                return;

            var inv = System.Globalization.CultureInfo.InvariantCulture;

            // Player planes: the server does not use these as enemies.
            var playerPlanes = new HashSet<string>();
            var classes = sb.GetPlayerClassConfigs();
            if (classes != null)
            {
                foreach (var cls in classes.Values)
                {
                    if (cls != null && !string.IsNullOrEmpty(cls.planeName))
                        playerPlanes.Add(cls.planeName);
                }
            }

            // Enemies of the island missions (islandQuestEnemies): they are certainly real enemies.
            var islandEnemies = sb.GetIslandQuestEnemyConfigs();

            TierConfig tiers = null;
            try { tiers = sb.GetTierConfig(); } catch { }

            var lines = new List<string>();
            lines.Add("# Generated by the Steambirds Quieter plugin " + Version + ". Fields separated by TAB; numbers with a decimal point.");
            lines.Add("# unit <name> <hash> <tier> <maxHealth> <baseHealth> <minSpeed> <maxSpeed> <turn> <radius> <capsule 0/1> <spawnDuration> <homeDistance> <wall 0/1> <player 0/1> <boss 0/1> <islandMin> <islandMax> <guns> <art> <marks>");
            lines.Add("# arma <unidade> <indice> <nome> <fireMode> <mountAngle> <firingArc> <slewRate> <rof> <burst> <cooldown> <dano> <velocidade> <duracao> <alcance> <multiBullets> <offsetX> <offsetY> <grupo> <curva>");

            int written = 0;
            foreach (var uc in units.OrderBy(u => u != null ? (u.templateName ?? "") : ""))
            {
                if (uc == null || string.IsNullOrEmpty(uc.templateName))
                    continue;

                // A unit with a problem does not stop the others from being written.
                try
                {
                    float multiplier = 1f;
                    if (tiers != null)
                    {
                        try { multiplier = tiers.GetHealthMultiplier(uc.cappedTier); } catch { multiplier = 1f; }
                    }

                    float radius = uc.colliderRadius > 0f
                        ? uc.colliderRadius
                        : Mathf.Max(uc.colliderWidth, uc.colliderHeight) * 0.5f;

                    // Marks that usually indicate a boss: after-fight details and own music.
                    var marks = new List<string>();
                    if (uc.showPostFightDetails) marks.Add("posLuta");
                    if (!string.IsNullOrEmpty(uc.musicTrackOverride)) marks.Add("musica");
                    if (uc.trackLootWorthyKills) marks.Add("loot");
                    if (!uc.isVisibleToAI) marks.Add("invisivelIA");
                    if (uc.noDangerIcon) marks.Add("semPerigo");
                    bool boss = uc.showPostFightDetails || !string.IsNullOrEmpty(uc.musicTrackOverride);

                    int islandMin = -1;
                    int islandMax = -1;
                    IslandQuestEnemyConfig island;
                    if (islandEnemies != null && islandEnemies.TryGetValue(uc.templateName, out island) && island != null)
                    {
                        islandMin = island.minlevel;
                        islandMax = island.maxlevel;
                    }

                    int gunCount = uc.gunMounts != null ? uc.gunMounts.Length : 0;

                    lines.Add(string.Join("\t", new string[]
                    {
                        "unidade",
                        uc.templateName,
                        uc.hash.ToString(inv),
                        uc.tier.ToString(inv),
                        (uc.maxHitPoints * multiplier).ToString("R", inv),
                        uc.maxHitPoints.ToString("R", inv),
                        uc.minSpeed.ToString("R", inv),
                        uc.maxSpeed.ToString("R", inv),
                        uc.maxTurnRate.ToString("R", inv),
                        radius.ToString("R", inv),
                        uc.useSpawnDuration ? "1" : "0",
                        uc.spawnDuration.ToString("R", inv),
                        uc.maxHomeDistance.ToString("R", inv),
                        uc.isWall ? "1" : "0",
                        playerPlanes.Contains(uc.templateName) ? "1" : "0",
                        boss ? "1" : "0",
                        islandMin.ToString(inv),
                        islandMax.ToString(inv),
                        gunCount.ToString(inv),
                        uc.art ?? "",
                        marks.Count > 0 ? string.Join(",", marks.ToArray()) : "-"
                    }));
                    written++;

                    for (int i = 0; i < gunCount; i++)
                    {
                        var gmc = uc.gunMounts[i];
                        if (gmc == null)
                        {
                            lines.Add("# arma " + uc.templateName + " " + i + " nula");
                            continue;
                        }

                        GunMountInfo gmi = null;
                        try
                        {
                            // Same calculation the unit makes when it spawns (SetupGunMounts), without an owner.
                            gmi = GunMountInfo.Get(null, gmc, sb);
                            lines.Add(string.Join("\t", new string[]
                            {
                                "arma",
                                uc.templateName,
                                i.ToString(inv),
                                gmc.mountName ?? "",
                                ((int)gmc.fireMode).ToString(inv),
                                gmc.mountAngle.ToString("R", inv),
                                gmc.firingArc.ToString("R", inv),
                                gmc.slewRate.ToString("R", inv),
                                gmi.rof.ToString("R", inv),
                                gmi.burst.ToString(inv),
                                gmi.cooldown.ToString("R", inv),
                                gmi.damage.ToString("R", inv),
                                gmi.speed.ToString("R", inv),
                                gmi.lifetime.ToString("R", inv),
                                gmi.GetEffectiveRange().ToString("R", inv),
                                gmi.multiBullets.ToString(inv),
                                gmc.offset.x.ToString("R", inv),
                                gmc.offset.y.ToString("R", inv),
                                ((int)gmi.group).ToString(inv),
                                gmi.turn.ToString("R", inv)
                            }));
                        }
                        catch (Exception e)
                        {
                            lines.Add("# arma " + uc.templateName + " " + i + " ignorada: " + e.GetType().Name);
                        }
                        finally
                        {
                            if (gmi != null)
                                GunMountInfo.Put(gmi);
                        }
                    }
                }
                catch (Exception e)
                {
                    lines.Add("# unidade " + uc.templateName + " ignorada: " + e.GetType().Name);
                }
            }

            var path = Path.Combine(Paths.BepInExRootPath, "units_config.txt");
            File.WriteAllLines(path, lines.ToArray());
            totalUnitsWritten = units.Count;
            Logger.LogInfo("Unidades gravadas em " + path + " (" + written + " de " + units.Count + ")");
        }
        catch (Exception e)
        {
            if (!unitsWrittenNotice)
            {
                unitsWrittenNotice = true;
                Logger.LogWarning("Falha ao gravar units_config.txt: " + e.GetType().Name + " " + e.Message);
            }
        }
    }

    // Writes BepInEx\mapinfo_config.txt with the ClientMapInfo of each terrain (TerrainConfig.clientMapInfo).
    // In the original game the server sent this info to the client (ClientSetClientMapInfo, ID 23208): they are the
    // client events with the hints, texts and conversations of the tutorial and the Nexus. The client has the config,
    // but only uses it when the message arrives. Here it comes out serialized by ClientMapInfo.Serializer itself
    // (line "mapinfo <TAB> terrain <TAB> hex"), ready for the server to send as it is,
    // and also described in # lines for reading (triggers, conditions and reactions of each event).
    // The file is rewritten if the number of terrains changes.
    private void TryWriteMapInfo()
    {
        try
        {
            var sb = SBConfig.Instance;
            if (sb == null)
                return;

            var terrains = sb.GetTerrains();
            if (terrains == null || terrains.Count == 0)
                return;
            if (terrains.Count == totalTerrainsWritten)
                return;

            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var lines = new List<string>();
            lines.Add("# Generated by the Steambirds Quieter plugin " + Version + ". Fields separated by TAB.");
            lines.Add("# mapinfo <terrain> <ClientMapInfo serialized by the game's ClientMapInfo.Serializer, in hex>");
            lines.Add("# The server sends these bytes in ClientSetClientMapInfo (23208). The lines with # are for reading only.");

            var serializer = new ClientMapInfo.Serializer();
            int withInfo = 0;
            foreach (var pair in terrains.OrderBy(p => p.Key))
            {
                var terrain = pair.Value;
                if (terrain == null || terrain.clientMapInfo == null)
                    continue;

                string name = string.IsNullOrEmpty(terrain.name) ? pair.Key : terrain.name;
                try
                {
                    var info = terrain.clientMapInfo;
                    // Detailed events only for the tutorials and the Nexus (the rest comes out with just the summary, so the
                    // server report does not grow too much); the bytes come out for all.
                    bool detail = name.IndexOf("Tutorial", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        string.Equals(name, "Nexus", StringComparison.OrdinalIgnoreCase);
                    DescribeMapInfo(name, terrain, info, lines, inv, detail);

                    var buf = new Lidgren.Network.NetBuffer();
                    serializer.Write(buf, ref info);
                    lines.Add("mapinfo\t" + name + "\t" + BitConverter.ToString(buf.Data, 0, buf.LengthBytes).Replace("-", ""));
                    withInfo++;
                }
                catch (Exception e)
                {
                    lines.Add("# terreno " + name + " ignorado: " + e.GetType().Name + " " + e.Message);
                }
            }

            // Conversations (ConversationsFileConfig): the ids with "tutorial" help to read the events above.
            try
            {
                var file = sb.conversationsFile;
                if (file != null && file.conversations != null)
                {
                    var doTutorial = file.conversations
                        .Where(c => c != null && !string.IsNullOrEmpty(c.id) && c.id.IndexOf("tutorial", StringComparison.OrdinalIgnoreCase) >= 0)
                        .Select(c => c.id)
                        .OrderBy(id => id)
                        .ToArray();
                    lines.Add("# ---- conversas: " + file.conversations.Length + " in total; with 'tutorial' in the id: " +
                        (doTutorial.Length == 0 ? "none" : string.Join(", ", doTutorial)));
                }
            }
            catch (Exception e)
            {
                lines.Add("# conversas ignoradas: " + e.GetType().Name);
            }

            var path = Path.Combine(Paths.BepInExRootPath, "mapinfo_config.txt");
            File.WriteAllLines(path, lines.ToArray());
            totalTerrainsWritten = terrains.Count;
            Logger.LogInfo("Map info written to " + path + " (" + withInfo + " de " + terrains.Count + " terrains with ClientMapInfo)");
        }
        catch (Exception e)
        {
            if (!mapInfoWrittenNotice)
            {
                mapInfoWrittenNotice = true;
                Logger.LogWarning("Falha ao gravar mapinfo_config.txt: " + e.GetType().Name + " " + e.Message);
            }
        }
    }

    // Prefix of DeathWrapupStateMachine.EnterWaitingForStats: the window only opens with the numbers of this death.
    private static void ForgetPreviousDeath(DeathWrapupStateMachine __instance)
    {
        try
        {
            if (deathStatsField != null)
                deathStatsField.SetValue(__instance, false);
        }
        catch
        {
        }
    }

    // 1.20.0: writes BepInEx\portraits_config.txt with the pilot portrait pieces ("PortraitParts" pool).
    // In this build the client has no screen to pick a portrait: the original server picked one
    // (Portrait.GenerateRandomPortrait). The pieces are Unity prefabs, so only the client knows the id of each one.
    // Lines: part <id> <slot> <slotName> <species> <excludedSlot: 0 = none, otherwise slot + 1> <prefab>.
    // The file is rewritten if the number of pieces changes.
    private void TryWritePortraits()
    {
        try
        {
            PathologicalGames.SpawnPool pool;
            if (!PathologicalGames.PoolManager.Pools.TryGetValue("PortraitParts", out pool) || pool == null || pool.prefabs == null)
                return;
            int total = pool.prefabs.Count;
            if (total == 0 || total == totalPortraitsWritten)
                return;

            var lines = new List<string>();
            lines.Add("# Generated by the Steambirds Quieter plugin " + Version + ". Fields separated by TAB.");
            lines.Add("# part <id> <slot> <slotName> <species> <excludedSlot: 0 = none, otherwise slot + 1> <prefab>");
            lines.Add("# Slots (PortraitSlotEnum): 0 Base, 1 Beak, 2 Deco, 3 Clothes, 4 Eye, 5 Hat, 6 Mask, 7 Pattern");

            int written = 0;
            foreach (var pair in pool.prefabs)
            {
                if (pair.Value == null)
                    continue;
                var part = pair.Value.GetComponent<PortraitPart>();
                if (part == null || part.PartId <= 0)
                    continue;

                lines.Add(string.Join("\t", new string[]
                {
                    "parte",
                    part.PartId.ToString(),
                    ((int)part.Slot).ToString(),
                    part.Slot.ToString(),
                    part.SpeciesName ?? "",
                    ((int)part.ExcludeSlot).ToString(),
                    pair.Key ?? ""
                }));
                written++;
            }

            var path = Path.Combine(Paths.BepInExRootPath, "portraits_config.txt");
            File.WriteAllLines(path, lines.ToArray());
            totalPortraitsWritten = total;
            Logger.LogInfo("Retratos gravados em " + path + " (" + written + " pecas de " + total + " prefabs)");
        }
        catch (Exception e)
        {
            if (!portraitsWrittenNotice)
            {
                portraitsWrittenNotice = true;
                Logger.LogWarning("Falha ao gravar portraits_config.txt: " + e.GetType().Name + " " + e.Message);
            }
        }
    }

    private static void DescribeMapInfo(string name, TerrainConfig terrain, ClientMapInfo info, List<string> lines,
        System.Globalization.CultureInfo inv, bool detail)
    {
        int events = info.clientEvents != null ? info.clientEvents.Count : 0;
        lines.Add("# ---- terreno " + name + " (role " + terrain.role + "): " + events + " evento(s)" +
            ", autoRespawnWithPlaneClassId=" + (info.autoRespawnWithPlaneClassId ?? "") +
            ", isStartingInAnyKeyScreen=" + info.isStartingInAnyKeyScreen +
            ", isTeleportHomeDisabled=" + info.isTeleportHomeDisabled +
            ", isTeleportLocalDisabled=" + info.isTeleportLocalDisabled);

        if (!detail)
            return;

        if (info.persistentOnceKeys != null && info.persistentOnceKeys.Count > 0)
            lines.Add("#    persistentOnceKeys: " + string.Join(", ", info.persistentOnceKeys.ToArray()));

        if (info.clientEvents == null)
            return;

        foreach (var evt in info.clientEvents)
        {
            if (evt == null)
                continue;

            lines.Add("#    event trigger=" + evt.trigger +
                (string.IsNullOrEmpty(evt.once_key) ? "" : " once=" + evt.once_key) +
                (string.IsNullOrEmpty(evt.timeout_key) ? "" : " timeout=" + evt.timeout_key + "/" + evt.timeout_duration + "s"));

            if (evt.condition != null)
                DescribeCondition(evt.condition, lines, inv, 0);

            if (evt.reactions == null)
                continue;

            foreach (var reaction in evt.reactions)
            {
                if (reaction == null)
                    continue;
                lines.Add("#       -> " + reaction.reaction +
                    (string.IsNullOrEmpty(reaction.parameter) ? "" : " '" + reaction.parameter + "'") +
                    (string.IsNullOrEmpty(reaction.asset) ? "" : " asset=" + reaction.asset) +
                    " f=" + reaction.parameterF0.ToString("0.##", inv) + "/" + reaction.parameterF1.ToString("0.##", inv) +
                    "/" + reaction.parameterF2.ToString("0.##", inv) + " rot=" + reaction.rotation.ToString("0.##", inv));
            }
        }
    }

    private static void DescribeCondition(ClientEventConditionEvaluator.Condition condition, List<string> lines,
        System.Globalization.CultureInfo inv, int level)
    {
        if (condition == null || level > 8)
            return;

        lines.Add("#       if " + new string(' ', level * 2) + condition.code +
            (string.IsNullOrEmpty(condition.name) ? "" : " '" + condition.name + "'") +
            " " + condition.value0.ToString("0.##", inv) + " " + condition.value1.ToString("0.##", inv) +
            " " + condition.value2.ToString("0.##", inv));

        if (condition.subconditions == null)
            return;
        foreach (var sub in condition.subconditions)
            DescribeCondition(sub, lines, inv, level + 1);
    }

    // Global tutorial config: when the client sends the player to the tutorial and whether it uses client events.
    private static void LogTutorialConfig(SBConfig sb, List<string> lines)
    {
        lines.Add("");
        lines.Add("--- config global do tutorial ---");
        try
        {
            var global = sb.GetGlobalConfig();
            if (global == null)
            {
                lines.Add("(GetGlobalConfig indisponivel)");
                return;
            }
            lines.Add("doesUseClientEvents=" + global.doesUseClientEvents);
            lines.Add("doesSpawnTutorialOnFirstTime=" + global.doesSpawnTutorialOnFirstTime +
                " doesSpawnTutorialOnFirstTimeOnlyIfNoPlanes=" + global.doesSpawnTutorialOnFirstTimeOnlyIfNoPlanes);
            lines.Add("doesSpawnAdditionalTutorials=" + global.doesSpawnAdditionalTutorials);
            lines.Add("planeClassesUsedInTutorial=" + (global.planeClassesUsedInTutorial != null
                ? string.Join(", ", global.planeClassesUsedInTutorial) : "(none)"));
            lines.Add("kickForTutorialIdleAfterSeconds=" + global.kickForTutorialIdleAfterSeconds);
        }
        catch (Exception e)
        {
            lines.Add("(error reading the global config: " + e.GetType().Name + ")");
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
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

    // Lists the player classes by reading the internal dictionary of SBConfig (by textual id).
    // netIdShort can be negative, so the variant search goes through the whole short range.
    private static void LogPlayerClasses(SBConfig sb, List<string> lines)
    {
        lines.Add("");
        lines.Add("--- player classes: netIdShort, starting items and variants ---");

        var containerField = AccessTools.Field(typeof(SBConfig), "playerClassConfigs");
        var container = containerField != null ? containerField.GetValue(sb) : null;
        var fieldById = container != null ? AccessTools.Field(container.GetType(), "configsByStringId") : null;
        var byId = fieldById != null ? fieldById.GetValue(container) as System.Collections.IDictionary : null;

        if (byId == null)
        {
            lines.Add("(could not read configsByStringId)");
            return;
        }

        foreach (System.Collections.DictionaryEntry pair in byId)
        {
            var cls = pair.Value as PlayerClassConfig;
            if (cls == null)
                continue;

            lines.Add("class id=" + pair.Key + " netIdShort=" + cls.netIdShort);
            lines.Add("   basicTutorial=" + (cls.basicTutorial ?? "") + " additionalTutorial=" + (cls.additionalTutorial ?? ""));

            // Starting items of the cls: the server needs to put these items in the plane's inventory.
            if (cls.startingItems != null)
            {
                foreach (var initial in cls.startingItems)
                {
                    if (initial != null)
                        lines.Add("   startingItem slot=" + initial.slot + " item=" + initial.item);
                }
            }

            for (int j = short.MinValue; j <= short.MaxValue; j++)
            {
                var variant = cls.GetVariantByNetIdShort((short)j);
                if (variant != null)
                    lines.Add("   variante id=" + variant.id + " netIdShort=" + variant.netIdShort);
            }
        }
    }

    // Harmony postfix of ASCIIMap.FromDoc: writes each ASCII map loaded from the config, with its origin.
    // Uses object and reflection so it does not depend on the DocNode/EntityRecipe types at compile time.
    private static void LogAsciiMap(object __result, object doc)
    {
        try
        {
            if (__result == null || doc == null)
                return;

            var lines = new List<string>();
            lines.Add("=== ASCIIMap " + DateTime.Now.ToString("HH:mm:ss") + " ===");
            lines.Add("origem: " + (ReadMember(doc, "SourceInformation") ?? "(no origin)"));

            var originalText = ReadDocString(doc, "ascii");
            lines.Add("ascii original:");
            lines.Add(originalText ?? "(no 'ascii' field)");

            // Grid already parsed (after transform, if any). First line = largest y (top).
            var grid = ReadMember(__result, "map") as Array;
            var size = ReadMember(__result, "size");
            if (grid != null && size != null)
            {
                int width = Convert.ToInt32(ReadMember(size, "x"));
                int height = Convert.ToInt32(ReadMember(size, "y"));
                lines.Add("tamanho parseado: " + width + " x " + height);

                // Fixed symbols for the names PlaceMap understands; the rest get letters.
                const string alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                var symbols = new Dictionary<string, char>();
                var prefabs = new Dictionary<string, string>();
                var next = 0;
                var gridLines = new List<string>();

                for (int y = height - 1; y >= 0; y--)
                {
                    var sb = new System.Text.StringBuilder();
                    for (int x = 0; x < width; x++)
                    {
                        var cell = grid.GetValue(x, y);
                        var name = (ReadMember(cell, "name") as string) ?? "?";
                        if (!symbols.TryGetValue(name, out var symbol))
                        {
                            if (name == "_wall_") symbol = '#';
                            else if (name == "_origin_") symbol = 'O';
                            else if (name == "_interior_") symbol = '.';
                            else symbol = next < alphabet.Length ? alphabet[next++] : '?';

                            symbols[name] = symbol;
                            prefabs[name] = (ReadMember(cell, "prefabName") as string) ?? "";
                        }
                        sb.Append(symbol);
                    }
                    gridLines.Add(sb.ToString());
                }

                lines.Add("grade (primeira linha = y maior):");
                lines.AddRange(gridLines);
                lines.Add("legenda:");
                foreach (var pair in symbols)
                    lines.Add("   " + pair.Value + " = " + pair.Key + " (prefab=" + prefabs[pair.Key] + ")");
            }

            var configs = ReadMember(__result, "translationConfigs") as System.Collections.IList;
            if (configs != null && configs.Count > 0)
            {
                var map = ReadMember(configs[0], "characterMapping") as System.Collections.IDictionary;
                if (map != null)
                {
                    lines.Add("characterMapping (translationConfigs[0]):");
                    foreach (System.Collections.DictionaryEntry pair in map)
                    {
                        lines.Add("   '" + pair.Key + "' -> prefab=" + ReadMember(pair.Value, "prefabName") +
                            " name=" + ReadMember(pair.Value, "name"));
                    }
                }
            }

            lines.Add("");
            File.AppendAllLines(asciiMapsFile, lines);
        }
        catch (Exception e)
        {
            Log(asciiMapsFile, "Failed to register ASCII map: " + e.GetType().Name + " " + e.Message);
        }
    }

    // Reads a string field of a DocNode by key (string indexer). Returns null if the key does not exist.
    private static string ReadDocString(object doc, string key)
    {
        try
        {
            var indexer = doc.GetType().GetMethod("get_Item", new[] { typeof(string) });
            if (indexer == null)
                return null;

            var no = indexer.Invoke(doc, new object[] { key });
            return ReadMember(no, "StringValue") as string;
        }
        catch
        {
            return null;
        }
    }

    // Harmony prefix of EntityHandler.SpawnUnit: does not change the call, it only logs the arguments.
    // Order: prefabId, templateHash, shortId, playerAuthority, teamIndex, parentShortId,
    // unitOptions, fields, spawnTime, familyRootTemplateHash, artReplacement.
    private static void LogSpawnUnit(object[] __args, EntityHandler __instance)
    {
        try
        {
            spawnUnitCalls++;
            var prefabId = (int)__args[0];
            var shortId = (int)__args[2];
            if (spawnUnitCalls > 50 && prefabId != 0 && shortId != 1000)
                return;

            var line = DateTime.Now.ToString("HH:mm:ss.fff") +
                " SpawnUnit #" + spawnUnitCalls +
                " prefabId=" + prefabId +
                " templateHash=" + __args[1] +
                " shortId=" + shortId +
                " playerAuthority=" + (__args[3] == null ? "null" : __args[3].ToString()) +
                " teamIndex=" + __args[4] +
                " parentShortId=" + __args[5] +
                " unitOptions=" + (__args[6] == null ? "null" : "buffer") +
                " fields=" + (__args[7] == null ? "null" : "ok") +
                " spawnTime=" + __args[8] +
                " IsClient=" + __instance.IsClient +
                " LocalPeer.Id=" + GetLocalPeer();
            Log(spawnLogFile, line);
        }
        catch (Exception e)
        {
            Log(spawnLogFile, "Falha ao registrar SpawnUnit: " + e.GetType().Name + " " + e.Message);
        }
    }

    // Harmony postfix of Building.Init: name of the building's art, whether it exists and the box its renderers occupy.
    private static void LogBuilding(Building __instance)
    {
        try
        {
            var art = __instance.artObject;
            string description = "SEM ARTE (artObject nulo)";
            if (art != null)
            {
                var renderers = art.GetComponentsInChildren<Renderer>(true);
                int enabled = 0;
                bool hasBox = false;
                var box = new Bounds();
                foreach (var r in renderers)
                {
                    if (r.enabled && r.gameObject.activeInHierarchy) enabled++;
                    if (!hasBox) { box = r.bounds; hasBox = true; }
                    else box.Encapsulate(r.bounds);
                }
                description = "arte='" + art.name + "' ativa=" + art.gameObject.activeInHierarchy +
                    " renderers=" + renderers.Length + " (ligados " + enabled + ")" +
                    (hasBox ? " caixa centro=" + box.center.ToString("F2") + " tamanho=" + box.size.ToString("F2") : "");
            }
            var line = DateTime.Now.ToString("HH:mm:ss.fff") + " " + __instance.GetType().Name +
                " #" + __instance.shortId +
                " pos=" + __instance.transform.position.ToString("F2") +
                " artPrefab='" + (__instance.artPrefab != null ? __instance.artPrefab.name : "(nulo)") + "' " + description;
            Log(buildingsFile, line);

            if (art != null && __instance.GetType().Name == "FlockHallBuilding")
            {
                Log(buildingsFile, "  Flock Hall art renderers before the fix:" + DescribeRenderers(art));
                if (enableFlockHallArt)
                {
                    int changes = EnableArt(art);
                    Log(buildingsFile, "  Flock Hall: " + changes + " renderer(s)/object(s) turned on by the plugin (config ShowFlockHallArt)");
                    if (!flockHallArts.Contains(art)) flockHallArts.Add(art);
                }
            }
        }
        catch (Exception e)
        {
            Log(buildingsFile, "Falha ao registrar predio: " + e.GetType().Name + " " + e.Message);
        }
    }

    // Mesh/sprite renderers (particle effects stay as they are): turns on the component and the parents that were off up to
    // the root of the art. Returns how many things changed.
    // 1.19.0: the "Signs" group (sign and sign (1)) stays off: they are curved signs with "HANGAR", a leftover of the
    // Hangar art, which do not appear in the Flock Hall of the Open Beta video (14:29-14:40).
    private static int EnableArt(Transform art)
    {
        int changes = 0;
        foreach (var r in art.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            var type = r.GetType().Name;
            if (type != "MeshRenderer" && type != "SkinnedMeshRenderer" && type != "SpriteRenderer") continue;
            if (IsInGroup(r.transform, art, "Signs")) continue;
            for (var t = r.transform; t != null && t != art; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                    changes++;
                }
            }
            if (!r.enabled)
            {
                r.enabled = true;
                changes++;
            }
        }
        return changes;
    }

    // true if the object, or some parent of it below the art root, has that name.
    private static bool IsInGroup(Transform obj, Transform root, string name)
    {
        for (var t = obj; t != null && t != root; t = t.parent)
        {
            if (t.name == name) return true;
        }
        return false;
    }

    private static string DescribeRenderers(Transform art)
    {
        var text = new System.Text.StringBuilder();
        foreach (var r in art.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null) continue;
            string parentDisabled = "";
            for (var t = r.transform; t != null && t != art; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    parentDisabled = " object off='" + t.name + "'";
                    break;
                }
            }
            text.Append(" [" + r.name + " " + r.GetType().Name + " enabled=" + r.enabled + parentDisabled +
                (IsInGroup(r.transform, art, "Signs") ? " (sign: stays off)" : "") + "]");
        }
        int animators = 0;
        foreach (var c in art.GetComponentsInChildren<Component>(true))
        {
            if (c != null && c.GetType().Name == "Animator") animators++;
        }
        text.Append(" animators=" + animators);
        return text.ToString();
    }

    // Once per second: if something turned the Flock Hall art off again (pool, animation), turns it on once more.
    private void CheckFlockHallArt()
    {
        try
        {
            for (int i = flockHallArts.Count - 1; i >= 0; i--)
            {
                var art = flockHallArts[i];
                if (art == null)
                {
                    flockHallArts.RemoveAt(i);
                    continue;
                }
                if (!art.gameObject.activeInHierarchy) continue; // back in the pool or building still spawning
                int changes = EnableArt(art);
                if (changes > 0)
                {
                    flockHallReenables++;
                    if (flockHallReenables <= 5)
                        Log(buildingsFile, DateTime.Now.ToString("HH:mm:ss.fff") +
                            " Flock Hall: a arte foi desligada de novo; " + changes + " coisa(s) religadas (vez " + flockHallReenables + ")");
                }
            }
        }
        catch (Exception e)
        {
            Log(buildingsFile, "Falha ao conferir a arte do Flock Hall: " + e.GetType().Name + " " + e.Message);
            flockHallArts.Clear();
        }
    }

    // Harmony prefix of EntityHandler.SpawnUnitsBatch: counts how many items the message brought.
    private static void LogSpawnBatch(object[] __args)
    {
        try
        {
            var list = __args[0] as System.Collections.ICollection;
            var text = DateTime.Now.ToString("HH:mm:ss.fff") + " SpawnUnitsBatch entrou: itens=" +
                (list == null ? "null" : list.Count.ToString());
            Log(dispatchLogFile, text);
            Log(spawnLogFile, text);
        }
        catch
        {
        }
    }

    // Harmony finalizer of EntityHandler.SpawnUnitsBatch: logs an exception thrown inside the method.
    private static void LogBatchFailure(Exception __exception)
    {
        try
        {
            if (__exception == null)
                return;
            var text = DateTime.Now.ToString("HH:mm:ss.fff") + " FALHA em SpawnUnitsBatch: " +
                __exception.GetType().FullName + ": " + __exception.Message + Environment.NewLine + __exception.StackTrace;
            Log(dispatchLogFile, text);
            Log(spawnLogFile, text);
        }
        catch
        {
        }
    }

    // Harmony prefix of CopperDispatcher.RejectMessage: logs each message refused by the dispatcher.
    // Order: obj, key, methodName, info.
    private static void LogRejection(object[] __args)
    {
        try
        {
            var text = DateTime.Now.ToString("HH:mm:ss.fff") + " RECUSADA key=" + __args[1] + " metodo=" + __args[2];
            Log(dispatchLogFile, text);
        }
        catch
        {
        }
    }

    // Harmony postfix of GeneratedDispatcher.Dispatch: logs the name of each dispatched message.
    private static void LogDispatch(string __result)
    {
        try
        {
            dispatchCounter++;
            if (dispatchCounter > 3000)
                return;
            Log(dispatchLogFile,
                DateTime.Now.ToString("HH:mm:ss.fff") + " Dispatch #" + dispatchCounter + " -> " + (__result ?? "(null)"));
        }
        catch
        {
        }
    }

    // Harmony prefix of EntityHandler.MessageRecvError: shows the exception that used to go only to the error tracker.
    // Order: bytes, sender, e.
    private static void LogReceiveError(object[] __args)
    {
        try
        {
            var e = __args[2] as Exception;
            var text = DateTime.Now.ToString("HH:mm:ss.fff") +
                " ERRO DE RECEBIMENTO bytes=" + __args[0] + " sender=" + __args[1];
            if (e != null)
                text += " " + e.GetType().FullName + ": " + e.Message + Environment.NewLine + e.StackTrace;
            Log(dispatchLogFile, text);
            Log(spawnLogFile, text);
        }
        catch
        {
        }
    }

    private static string GetLocalPeer()
    {
        try
        {
            return GameClient.Instance.LocalPeer.Id.ToString();
        }
        catch
        {
            return "(indisponivel)";
        }
    }

    private static void Log(string file, string text)
    {
        try
        {
            File.AppendAllText(file, text + Environment.NewLine);
        }
        catch
        {
        }
    }

    // Returning null swallows the exception: Unity does not log it.
    private static Exception SwallowException(Exception __exception)
    {
        return null;
    }

    // Returning false skips the original method: no UnityWebRequest request is made.
    private static bool IgnorePhantasmSend()
    {
        return false;
    }
}
