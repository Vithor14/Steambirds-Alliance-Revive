using System.Text.Json.Nodes;
using SteambirdsLauncher.Core;
using SteambirdsLauncher.UI;

namespace SteambirdsLauncher.Editor;

// "Map" page: name, terrain, territory, dungeon settings (key, exit rule, tier), spawn and floor settings.
internal sealed class MapSettingsPage : EditorPage
{
    private JsonForm? form;
    private bool builtForDungeon;
    private readonly Label summary;
    private readonly Label pieceNames;
    private readonly Button defaultPiecesButton;
    private readonly Button centerButton;
    private readonly Panel formHost;

    public MapSettingsPage(EditorContext context) : base(context)
    {
        summary = Ui.Wrapping("", 420, true);
        pieceNames = Ui.Wrapping("", 420, true);
        defaultPiecesButton = Ui.Button(L.T("Use the terrain's floor pieces"), (_, _) => UseDefaultPieces());
        centerButton = Ui.Button(L.T("Center map on the grid"), (_, _) => CenterMap());
        Context.Tip.SetToolTip(defaultPiecesButton, L.T("Sets \"floor pieces\" to the ones the game uses for the terrain above."));
        Context.Tip.SetToolTip(centerButton, L.T("Moves the whole map (floor and everything on it) so it is centered on the client's grid."));

        formHost = new Panel { Dock = DockStyle.Fill };
        FlowLayoutPanel buttons = Ui.WrapFlow(defaultPiecesButton, centerButton);
        buttons.MaximumSize = new Size(430, 0);
        Controls.Add(Stack((formHost, true), (pieceNames, false), (summary, false), (buttons, false)));
        Reload();
    }

    // The entrance (spawn) is edited in this page.
    public override bool ShowObject(MapObject item) => item.Kind == ObjectKind.Entrance;

    public override void Reload()
    {
        bool dungeon = Document.IsDungeon;
        if (form == null || dungeon != builtForDungeon)
        {
            if (form != null)
            {
                formHost.Controls.Remove(form);
                form.Dispose();
            }
            builtForDungeon = dungeon;
            form = new JsonForm(Fields(dungeon), Session.Checkpoint, FieldChanged);
            formHost.Controls.Add(form);
        }
        form.Bind(Document.Root);
        UpdateSummary();
    }

    private IEnumerable<FieldSpec> Fields(bool dungeon)
    {
        yield return FieldSpec.Header("Map");
        yield return new FieldSpec("id", "Id (file name)", FieldKind.Text)
        {
            ReadOnly = true,
            Help = "The id is the name of the file. It is set when the map is saved."
        };
        yield return new FieldSpec("name", "Name", FieldKind.Text) { Help = "Shown in the server log and in messages." };
        yield return new FieldSpec("terrain", "Terrain", FieldKind.EditableChoice)
        {
            Choices = EditorContext.Terrains,
            Help = "Terrain of the client (ClientMapInfo): the look of the walls, the music, the sky."
        };
        yield return new FieldSpec("territory", "Territory", FieldKind.Int)
        {
            Min = 0,
            Max = 255,
            Help = "TerritoryTileConfig of every tile: light, fog, minimap color and walls."
        };
        yield return new FieldSpec("source", "Source", FieldKind.MultiText) { Help = "Where the map comes from (free text)." };
        yield return new FieldSpec("note", "Note", FieldKind.MultiText) { Help = "Free text. The server ignores it." };

        if (dungeon)
        {
            yield return FieldSpec.Header("Dungeon");
            yield return new FieldSpec("key", "Key item", FieldKind.Text) { Help = "Item that opens the dungeon." };
            yield return new FieldSpec("portalItem", "Portal item", FieldKind.Text) { Help = "Id in Items/Consumables/dungeon." };
            yield return new FieldSpec("quest", "Quest", FieldKind.Text) { Help = "Quest of the dungeon." };
            yield return new FieldSpec("values.tier", "Tier", FieldKind.Int) { Min = 1, Max = 10, Default = 5, DefaultBool = false, Help = "Difficulty tier of the dungeon (1 to 10). It sets the level of the loot and of the enemies the dungeon uses." };
            yield return new FieldSpec("values.bossTerritory", "Boss room territory", FieldKind.OptionalNumber)
            {
                Help = "Only when the boss room used another territory in the original dungeon."
            };

            yield return FieldSpec.Header("Exit and ending");
            yield return new FieldSpec("exit.point", "Exit point", FieldKind.EditableChoice)
            {
                Choices = Context.PointNames,
                Help = "Point where the exit carrier appears. Default: the point named \"portal\"."
            };
            yield return new FieldSpec("exit.when", "Exit opens when", FieldKind.Text)
            {
                Help = "Conditions joined by +: death:<unit type>, cleared:<room>, quest:<id>.\nEmpty = all boss rooms cleared."
            };

            yield return FieldSpec.Header("Next dungeon (the server does not use it yet)");
            yield return new FieldSpec("nextDungeon.map", "Map", FieldKind.EditableChoice) { Choices = Context.MapIds, Help = "Map the players go to after this dungeon. The server does not use it yet." };
            yield return new FieldSpec("nextDungeon.point", "Point", FieldKind.Text) { Help = "Point of the next map where the players appear. The server does not use it yet." };
            yield return new FieldSpec("nextDungeon.when", "When", FieldKind.Text) { Help = "Condition that sends the players to the next map. The server does not use it yet." };
        }

        yield return FieldSpec.Header("Spawn (nascimento)");
        yield return new FieldSpec("spawn.u", "u", FieldKind.OptionalNumber)
        {
            Help = "Where the plane appears. In dungeons the last checkpoint is used when the map has any."
        };
        yield return new FieldSpec("spawn.v", "v", FieldKind.OptionalNumber) { Help = "Where the plane appears (v axis)." };
        yield return new FieldSpec("spawn.rot", "Rotation", FieldKind.OptionalNumber) { Help = "Direction the plane faces when it appears, in degrees clockwise from +y. 315 faces +v (the back of the dungeon)." };

        yield return FieldSpec.Header("Floor");
        yield return new FieldSpec("floor.wallsOnEdges", "Automatic walls on the edges", FieldKind.Bool)
        {
            DefaultBool = true,
            Help = "A wall is added on the edge of a tile with floor when the tile next to it has no floor."
        };
        yield return new FieldSpec("floor.voidMargin", "Void ring (tiles)", FieldKind.Int)
        {
            Min = 0,
            Max = 20,
            Default = 4,
            Help = "Ring of tiles without floor (dark abyss) around the floor. 0 = none."
        };
        yield return new FieldSpec("floor.voidTerritory", "Void territory", FieldKind.Int)
        {
            Min = 0,
            Max = 255,
            Default = 74,
            Help = "Only picks the \"floor\" of the abyss (74 = dungeonsky, no floor)."
        };
        yield return new FieldSpec("floor.drawingPieces", "Floor pieces (ids)", FieldKind.IntList)
        {
            Help = "GroundAsset ids drawn on the slots of the floor drawing, separated by commas.\nThe preview draws the first ones of the list."
        };
    }

    private void FieldChanged(FieldSpec spec)
    {
        switch (spec.Key)
        {
            case "floor.wallsOnEdges":
                Document.Floor.AutoBorders = Json.Bool(Document.FloorJson, "wallsOnEdges", true);
                break;
            case "terrain":
                OfferTerrainDefaults();
                break;
        }
        Session.Touch();
        UpdateSummary();
    }

    // A known terrain brings its own territory and floor pieces: offer to use them.
    private void OfferTerrainDefaults()
    {
        TerritoryInfo? info = Territories.ForTerrain(Document.Terrain);
        if (info == null) return;
        bool territoryDiffers = Document.Territory != info.Territory;
        bool piecesDiffer = !Json.Ints(Document.FloorJson["drawingPieces"]).SequenceEqual(info.DefaultPieces);
        if (!territoryDiffers && !piecesDiffer) return;
        if (!Ui.Confirm(this, L.F("Use the territory ({0}) and the floor pieces of {1} too?", info.Territory, info.Label))) return;

        Document.Territory = info.Territory;
        if (info.DefaultPieces.Length > 0) Document.FloorJson["drawingPieces"] = Json.IntArray(info.DefaultPieces);
        form?.RefreshValues();
    }

    private void UseDefaultPieces()
    {
        TerritoryInfo? info = Territories.ForTerrain(Document.Terrain) ?? Territories.ForTerritory(Document.Territory);
        if (info == null || info.DefaultPieces.Length == 0)
        {
            Ui.ShowInfo(this, L.T("The editor does not know floor pieces for this terrain. Type the ids in \"Floor pieces\"."));
            return;
        }
        Session.Checkpoint();
        Document.FloorJson["drawingPieces"] = Json.IntArray(info.DefaultPieces);
        Session.Touch();
        form?.RefreshValues();
        UpdateSummary();
    }

    private void CenterMap()
    {
        Session.Checkpoint();
        if (!MapTransforms.CenterOnGrid(Document))
        {
            Ui.ShowInfo(this, L.T("The map has no floor yet, or it is already centered."));
            return;
        }
        Session.Touch();
        Canvas.FitToMap();
    }

    private void UpdateSummary()
    {
        FloorGrid floor = Document.Floor;
        if (floor.GetBounds(out int minA, out int maxA, out int minB, out int maxB))
        {
            summary.Text = L.F("Floor: {0} tiles ({1} x {2}), {3} extra wall cells. Tiles from {4} to {5} (a) and {6} to {7} (b); the client keeps {8} to {9}.",
                floor.TileCount, maxA - minA + 1, maxB - minB + 1, floor.WallCount, minA, maxA, minB, maxB, MapCoords.MinTile, MapCoords.MaxTile);
        }
        else
        {
            summary.Text = L.T("Floor: empty. Pick the Floor tool and paint on the map.");
        }

        List<int> ids = Json.Ints(Document.FloorJson["drawingPieces"]);
        EditorAssets assets = EditorAssets.Current;
        var names = new List<string>();
        foreach (int id in ids.Take(6))
        {
            string name = assets.GroundName(id);
            names.Add(name.Length > 0 ? name : id.ToString());
        }
        pieceNames.Text = ids.Count == 0
            ? L.T("No floor pieces: the map is drawn with flat colors.")
            : L.F("Pieces: {0}{1}", string.Join(", ", names), ids.Count > 6 ? ", ..." : "");
    }
}
