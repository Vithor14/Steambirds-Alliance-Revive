using Lidgren.Network;

// Writes the floor of the open world to the client: ClientReceiveTerrainTiles (ID 38376) with the TerritoryTile list of
// TerritoryTileSerializer. The client adds the tiles of every message it receives to its grid, so the world goes out in chunks
// (the nearest tiles to the spawn point first) instead of one message of about half a megabyte.
public static class WorldFloor
{
    // Flags of GroundAssetPlacement (TerritoryTileSerializer.WriteGroundAssetPlacement).
    private const byte FlagScale = 0x10;
    private const byte FlagRotation = 0x08;

    private const int TilesPerMessage = 200;

    // Sends every tile of the map. Returns the bytes and messages sent.
    public static (int Bytes, int Messages) Send(NetServer server, NetConnection connection, WorldMap map)
    {
        int bytes = 0, messages = 0;
        for (int start = 0; start < map.Tiles.Count; start += TilesPerMessage)
        {
            int count = Math.Min(TilesPerMessage, map.Tiles.Count - start);
            NetOutgoingMessage m = server.CreateMessage();
            m.Write(Ground.MsgClientReceiveTerrainTiles);
            m.Write(Ground.CellSize);
            m.Write(map.Radius);
            m.Write(Ground.WallsPerCell);
            m.WriteVariableUInt32((uint)(count + 1));   // List<TerritoryTile>: count + 1 (0 = null list)
            for (int i = 0; i < count; i++) WriteTile(m, map.Tiles[start + i], map.Half);
            bytes += m.LengthBytes;
            messages++;
            server.SendMessage(m, connection, NetDeliveryMethod.ReliableOrdered, 0);
        }
        return (bytes, messages);
    }

    private static void WriteTile(NetBuffer m, WorldMap.Tile tile, int half)
    {
        m.Write(half + tile.A);
        m.Write(half + tile.B);
        m.Write(tile.Kind.Id);
        for (int i = 0; i < 4; i++) m.Write(tile.Pieces[i]);
        for (int i = 0; i < 4; i++) WritePlacement(m, tile.Slots[i], true);

        int scatter = Math.Min(tile.Scatter.Count, 255);
        m.Write((byte)scatter);
        for (int i = 0; i < scatter; i++) WritePlacement(m, tile.Scatter[i], false);

        bool hasWalls = tile.Walls != null && HasAny(tile.Walls);
        m.Write(hasWalls);
        if (!hasWalls) return;
        m.Write((byte)Ground.WallsPerCell);
        for (int i = 0; i < Ground.WallsPerCell; i++)
        {
            for (int j = 0; j < Ground.WallsPerCell; j++) m.Write(tile.Walls![i, j]);
        }
    }

    // A piece with Id 0 draws nothing (an empty slot).
    private static void WritePlacement(NetBuffer m, WorldMap.Placement p, bool slot)
    {
        bool scale = p.ScaleX != 0f || p.ScaleY != 0f;
        byte flags = 0;
        if (scale) flags |= FlagScale;
        if (p.Rotation.HasValue) flags |= FlagRotation;
        m.Write(flags);
        m.Write(p.Id);
        if (scale)
        {
            m.Write((sbyte)Math.Clamp(MathF.Round(p.ScaleX * 8f), -127f, 127f));
            m.Write((sbyte)Math.Clamp(MathF.Round(p.ScaleY * 8f), -127f, 127f));
        }
        if (p.Rotation.HasValue) m.Write(Maps.DegreesToByte(p.Rotation.Value));
        float px = p.X, py = p.Y;
        if (slot && p.Id == 0)
        {
            // Empty slot: the same position the client would give it.
            px = 1f;
            py = 1f;
        }
        m.Write((sbyte)Math.Clamp(MathF.Round(px * 2f), -127f, 127f));
        m.Write((sbyte)Math.Clamp(MathF.Round(py * 2f), -127f, 127f));
    }

    private static bool HasAny(bool[,] walls)
    {
        foreach (bool w in walls)
        {
            if (w) return true;
        }
        return false;
    }
}
