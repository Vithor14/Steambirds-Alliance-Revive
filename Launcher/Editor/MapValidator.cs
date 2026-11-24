using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;

namespace SteambirdsLauncher.Editor;

internal enum IssueLevel { Error, Warning }

// One thing the check found. "Where" says what it is about (the entrance, a room, a gate...).
internal sealed class MapIssue
{
    public MapIssue(IssueLevel level, string where, string message)
    {
        Level = level;
        Where = where;
        Message = message;
    }

    public IssueLevel Level { get; }
    public string Where { get; }
    public string Message { get; }
}

// The check done before saving. Errors are things the dungeon cannot work without (the entrance, the exit, a way to
// finish, rooms with an area, unit types that exist); warnings are things that probably are not wanted.
// A dungeon is saved with errors only as a draft ("draft": true), which the server ignores.
internal static class MapValidator
{
    public static List<MapIssue> Validate(MapDocument document)
    {
        var issues = new List<MapIssue>();
        var check = new Checker(document, issues);
        check.Basics();
        check.Floor();
        check.Signs();
        if (document.IsDungeon) check.Dungeon();
        // Errors first (OrderBy is stable: the order of the check is kept inside each level).
        return issues.OrderBy(issue => issue.Level).ToList();
    }

    private sealed class Checker
    {
        private readonly MapDocument document;
        private readonly JsonObject root;
        private readonly List<MapIssue> issues;
        private readonly HashSet<string> types;
        private readonly HashSet<string> rooms;
        private readonly HashSet<string> areas;
        private readonly HashSet<string> lines;

        public Checker(MapDocument document, List<MapIssue> issues)
        {
            this.document = document;
            this.issues = issues;
            root = document.Root;
            types = Names(Json.ObjOrNull(root, "types"));
            areas = Names(Json.ObjOrNull(root, "areas"));
            lines = Names(Json.ObjOrNull(root, "lines"));
            rooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonObject room in Json.Objects(Json.ArrOrNull(root, "encounters")))
            {
                string name = Json.Str(room, "room");
                if (name.Length > 0) rooms.Add(name);
            }
        }

        private void Error(string where, string message) => issues.Add(new MapIssue(IssueLevel.Error, where, message));
        private void Warn(string where, string message) => issues.Add(new MapIssue(IssueLevel.Warning, where, message));

        private static HashSet<string> Names(JsonObject? obj)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (obj != null)
            {
                foreach (KeyValuePair<string, JsonNode?> pair in obj) set.Add(pair.Key);
            }
            return set;
        }

        // ------------------------------------------------------------------ every map

        public void Basics()
        {
            string where = L.T("Map");
            if (!MapFactory.IsValidId(document.Id))
                Error(where, L.T("The map id must have only lower case letters, digits and _ (it is the file name)."));
            if (document.Name.Trim().Length == 0) Warn(where, L.T("The map has no name."));
            if (document.Terrain.Trim().Length == 0)
                Error(where, L.T("The terrain is empty: the client needs it to know which dungeon this map is."));
            if (document.Territory < 0 || document.Territory > 255)
                Error(where, L.F("The territory {0} is outside 0..255.", document.Territory));
        }

        public void Floor()
        {
            string where = L.T("Floor");
            FloorGrid floor = document.Floor;
            bool hasPieces = Json.ArrOrNull(Json.ObjOrNull(root, "floor"), "pieces")?.Count > 0;
            if (floor.TileCount == 0)
            {
                if (!hasPieces) Error(where, L.T("The map has no floor. Paint some floor tiles."));
                return;
            }

            if (Json.Ints(Json.ObjOrNull(root, "floor")?["drawingPieces"]).Count == 0)
                Error(where, L.T("The floor has no pieces: add at least one floor piece in the Map tab."));

            int outside = floor.Tiles.Count(t => !MapCoords.InsideTileRange(t.A, t.B));
            if (outside > 0)
                Error(where, L.F("{0} floor tile(s) are outside the game grid (tiles {1} to {2}). Use Tools > Center map on the grid or move the floor.",
                    outside, MapCoords.MinTile, MapCoords.MaxTile));

            int ring = Json.Int(Json.ObjOrNull(root, "floor"), "voidMargin");
            if (ring > 0 && outside == 0 && floor.GetBounds(out int minA, out int maxA, out int minB, out int maxB) &&
                (minA - ring < MapCoords.MinTile || minB - ring < MapCoords.MinTile ||
                 maxA + ring > MapCoords.MaxTile || maxB + ring > MapCoords.MaxTile))
            {
                Warn(where, L.T("The floor is so close to the edge of the game grid that the dark ring around it will be cut."));
            }
        }

        // A sign without its text key shows nothing in the game.
        public void Signs()
        {
            foreach (JsonObject sign in Json.Objects(Json.ArrOrNull(root, "signs")))
            {
                if (Json.Str(sign, "key").Trim().Length > 0) continue;
                Warn(L.T("Signs"), L.F("A sign at u {0}, v {1} has no text key, so it shows nothing.", Json.Num(sign, "u"), Json.Num(sign, "v")));
            }
        }

        // ------------------------------------------------------------------ dungeons

        public void Dungeon()
        {
            Entrance();
            Exit();
            Rooms();
            Gates();
            Checkpoints();
            Types();
            Areas();
        }

        private void Entrance()
        {
            string where = L.T("Entrance");
            if (root["spawn"] is not JsonObject spawn)
            {
                Error(where, L.T("The dungeon has no entrance. Place the entrance (where the planes appear) with the Entrance tool."));
                return;
            }
            if (!document.Floor.HasFloorAt(Json.Num(spawn, "u"), Json.Num(spawn, "v")))
                Warn(where, L.T("The entrance is not on the floor."));
        }

        private void Exit()
        {
            string where = L.T("Exit");
            string pointName = document.ExitPointName;
            JsonObject? point = Json.ObjOrNull(Json.ObjOrNull(root, "points"), pointName);
            if (point == null)
                Error(where, L.F("The dungeon has no exit point \"{0}\". Place the exit portal with the Exit tool.", pointName));
            else if (!document.Floor.HasFloorAt(Json.Num(point, "u"), Json.Num(point, "v")))
                Warn(where, L.T("The exit point is not on the floor."));

            string when = Json.Str(Json.ObjOrNull(root, "exit"), "when");
            bool hasBossRoom = Json.Objects(Json.ArrOrNull(root, "encounters")).Any(r => Json.Bool(r, "boss"));
            if (when.Length == 0)
            {
                if (!hasBossRoom)
                    Error(where, L.T("The dungeon has no way to end: set when the exit appears (Map tab) or mark a room as the boss room."));
            }
            else
            {
                CheckCondition(where, when);
            }
        }

        private void Rooms()
        {
            JsonArray? list = Json.ArrOrNull(root, "encounters");
            if (list == null || list.Count == 0)
            {
                Warn(L.T("Rooms"), L.T("The dungeon has no rooms with enemies."));
                return;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonObject room in Json.Objects(list))
            {
                string name = Json.Str(room, "room");
                string where = L.F("Room {0}", name.Length > 0 ? name : "?");
                if (name.Length == 0) Error(where, L.T("A room has no name."));
                else if (!seen.Add(name)) Error(where, L.T("Two rooms have the same name."));

                string area = Json.Str(room, "area");
                if (area.Length == 0) Error(where, L.T("The room has no area: the server needs it to keep the enemies inside the room."));
                else if (!areas.Contains(area)) Error(where, L.F("The area \"{0}\" of the room does not exist.", area));

                string line = Json.Str(room, "line");
                if (line.Length > 0 && !lines.Contains(line)) Warn(where, L.F("The line \"{0}\" of the room does not exist.", line));

                string wakes = Json.Str(room, "wakes");
                if (wakes.StartsWith("death:", StringComparison.OrdinalIgnoreCase)) CheckCondition(where, wakes);

                int units = 0;
                foreach (JsonObject group in Json.Objects(Json.ArrOrNull(room, "enemies")))
                {
                    CheckGroup(where, group, area);
                    units += Json.ArrOrNull(group, "pos")?.Count ?? 0;
                }
                foreach (JsonObject wave in Json.Objects(Json.ArrOrNull(room, "waves")))
                {
                    foreach (JsonObject group in Json.Objects(Json.ArrOrNull(wave, "groups")))
                    {
                        CheckGroup(where, group, area);
                        units += Json.ArrOrNull(group, "pos")?.Count ?? 0;
                    }
                }
                if (units == 0) Warn(where, L.T("The room has no units."));
            }
        }

        private void CheckGroup(string where, JsonObject group, string areaName)
        {
            string type = Json.Str(group, "type");
            if (type.Length == 0) Error(where, L.T("A group of units has no unit type."));
            else if (!types.Contains(type)) Error(where, L.F("The unit type \"{0}\" does not exist in this map.", type));

            string wakes = Json.Str(group, "wakes");
            if (wakes.StartsWith("death:", StringComparison.OrdinalIgnoreCase)) CheckCondition(where, wakes);

            JsonObject? area = Json.ObjOrNull(Json.ObjOrNull(root, "areas"), areaName);
            foreach (JsonObject pos in Json.Objects(Json.ArrOrNull(group, "pos")))
            {
                double u = Json.Num(pos, "u");
                double v = Json.Num(pos, "v");
                if (!document.Floor.HasFloorAt(u, v))
                {
                    Warn(where, L.F("A \"{0}\" is not on the floor (u {1}, v {2}).", type, u, v));
                    continue;
                }
                if (area != null && (u < Json.Num(area, "minU") || u > Json.Num(area, "maxU") ||
                                     v < Json.Num(area, "minV") || v > Json.Num(area, "maxV")))
                    Warn(where, L.F("A \"{0}\" is outside the area of its room (u {1}, v {2}).", type, u, v));
            }
        }

        private void Gates()
        {
            CheckGateList("gates", false);
            CheckGateList("sideGates", true);
        }

        private void CheckGateList(string key, bool column)
        {
            foreach (JsonObject gate in Json.Objects(Json.ArrOrNull(root, key)))
            {
                string name = Json.Str(gate, "name");
                string where = L.F("Gate {0}", name.Length > 0 ? name : "?");
                string type = Json.Str(gate, "type");
                if (type.Length == 0) Error(where, L.T("The gate has no unit type (the boxes it is made of)."));
                else if (!types.Contains(type)) Error(where, L.F("The unit type \"{0}\" of the gate does not exist in this map.", type));

                double from = Json.Num(gate, column ? "sjFrom" : "siFrom");
                double to = Json.Num(gate, column ? "sjTo" : "siTo");
                if (to < from) Error(where, L.T("The end of the gate is before its start."));

                string room = Json.Str(gate, "room");
                if (room.Length > 0 && !rooms.Contains(room)) Warn(where, L.F("The room \"{0}\" of the gate does not exist.", room));

                string opens = Json.Str(gate, "opensWhen");
                if (opens.Length > 0) CheckCondition(where, opens);
                else if (!Json.Bool(gate, "damageable"))
                    Warn(where, L.T("The gate is locked and has no \"opens when\" condition: it will never open."));
            }
        }

        private void Checkpoints()
        {
            JsonArray? list = Json.ArrOrNull(root, "checkpoints");
            if (list == null || list.Count == 0)
            {
                Warn(L.T("Checkpoints"), L.T("The dungeon has no checkpoints: a plane that falls starts again at the entrance."));
                return;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (JsonObject point in Json.Objects(list))
            {
                string name = Json.Str(point, "name");
                if (!seen.Add(name)) Warn(L.F("Checkpoint {0}", name), L.T("Two checkpoints have the same name."));
            }
        }

        private void Types()
        {
            foreach (KeyValuePair<string, JsonObject> pair in Json.ObjectProperties(Json.ObjOrNull(root, "types")))
            {
                string where = L.F("Unit type {0}", pair.Key);
                JsonObject type = pair.Value;
                if (Json.Str(type, "template").Length == 0)
                    Warn(where, L.T("The type has no template (the unit of the game it is based on)."));

                CheckTypeRef(where, Json.Str(type, "onDeath"), L.T("On death"));
                foreach (string name in Json.Strings(type["sequence"])) CheckTypeRef(where, name, L.T("Sequence"));
                JsonObject? summon = Json.ObjOrNull(type, "periodicSummon");
                if (summon != null) CheckTypeRef(where, Json.Str(summon, "type"), L.T("Periodic summon"));
                JsonObject? chest = Json.ObjOrNull(type, "dpsChest");
                if (chest != null)
                {
                    CheckTypeRef(where, Json.Str(chest, "reward"), L.T("DPS chest reward"));
                    CheckTypeRef(where, Json.Str(chest, "fail"), L.T("DPS chest failure"));
                }

                // The enemy AI editor (when there is one) checks the "ai" of the type.
                IEnemyAiEditor? ai = EnemyAiEditors.Current;
                if (ai != null && EnemyAiEditors.HasAi(type))
                {
                    foreach (string problem in ai.Validate(pair.Key, type)) Warn(where, problem);
                }
            }
        }

        private void CheckTypeRef(string where, string name, string field)
        {
            if (name.Length == 0 || types.Contains(name)) return;
            Error(where, L.F("{0}: the unit type \"{1}\" does not exist in this map.", field, name));
        }

        private void Areas()
        {
            foreach (KeyValuePair<string, JsonObject> pair in Json.ObjectProperties(Json.ObjOrNull(root, "areas")))
            {
                JsonObject area = pair.Value;
                if (Json.Num(area, "maxU") <= Json.Num(area, "minU") || Json.Num(area, "maxV") <= Json.Num(area, "minV"))
                    Error(L.F("Area {0}", pair.Key), L.T("The area is empty or upside down (the maximum must be bigger than the minimum)."));
            }
        }

        // "cleared:<room>", "death:<type>" or "quest:<id>", joined with "+".
        private void CheckCondition(string where, string when)
        {
            foreach (string part in when.Split('+'))
            {
                string text = part.Trim();
                if (text.Length == 0) continue;
                int colon = text.IndexOf(':');
                string kind = colon < 0 ? text : text.Substring(0, colon);
                string arg = colon < 0 ? "" : text.Substring(colon + 1).Trim();
                switch (kind.ToLowerInvariant())
                {
                    case "cleared":
                        if (!rooms.Contains(arg)) Error(where, L.F("The condition \"{0}\" uses a room that does not exist.", text));
                        break;
                    case "death":
                        if (!types.Contains(arg)) Error(where, L.F("The condition \"{0}\" uses a unit type that does not exist.", text));
                        break;
                    case "quest":
                        if (arg.Length == 0) Error(where, L.F("The condition \"{0}\" has no quest id.", text));
                        break;
                    default:
                        Error(where, L.F("Unknown condition \"{0}\" (use cleared:<room>, death:<type> or quest:<id>).", text));
                        break;
                }
            }
        }
    }
}
