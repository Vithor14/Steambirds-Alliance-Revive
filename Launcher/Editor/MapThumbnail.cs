namespace SteambirdsLauncher.Editor;

// A picture of a whole map file, drawn by the map editor's own canvas (floor, walls, decoration, enemies, gates), for the
// "Maps and dungeons" page of the Server tab. The canvas is never shown: it is sized, fitted to the map and printed to a bitmap.
internal static class MapThumbnail
{
    // Result of Render: the picture and a line about the map ("Rebel City, terrain Nexus").
    internal sealed class Result
    {
        public Bitmap Picture { get; init; } = null!;
        public string Description { get; init; } = "";
    }

    // Draws the map file at the size asked. Throws when the file cannot be read (the caller shows the message).
    public static Result Render(string path, int width, int height)
    {
        MapDocument document = MapDocument.Load(path);
        var session = new MapSession(document);
        using var canvas = new MapCanvas(session)
        {
            Size = new Size(width, height),
            ShowGrid = false,
            ShowLabels = false,
            ShowOverlay = false,
            ShowAreas = false,
            ShowPoints = false,
            UsePictures = EditorAssets.Current.HasPictures
        };
        canvas.FitToMap();

        var bitmap = new Bitmap(width, height);
        try
        {
            canvas.DrawToBitmap(bitmap, new Rectangle(0, 0, width, height));
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }

        string name = document.Name.Length > 0 ? document.Name : document.Id;
        string description = document.Terrain.Length > 0 ? name + " (" + document.Terrain + ")" : name;
        return new Result { Picture = bitmap, Description = description };
    }
}
