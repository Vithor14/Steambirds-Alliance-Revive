using Lidgren.Network;

// ClientSetClientMapInfo (ID 23208): the game server sent the client the ClientMapInfo of the terrain
// (TerrainConfig.clientMapInfo): client events (hints, texts, tutorial conversations), "once" keys,
// the class to respawn as by itself and the teleport locks. Without this message the client has no
// original hints (GameClient.ClientSetClientMapInfo -> ClientEventManager.Set).
//
// The bytes of each terrain are in mapinfo_config.txt (the server's "data" folder; see GameDataFiles), one
// line per terrain:  mapinfo <TAB> terrain name <TAB> bytes in hex (the game's ClientMapInfo.Serializer).
//
// A terrain can get extra events (RegisterMap): the original bytes are read, the events go
// at the end of the list and the ClientMapInfo is written again, in the same format as ClientMapInfo.Serializer.
// The tutorial uses this for the floor signs: the client itself creates the text when the plane gets close
// (trigger "repeatEvery_1", condition PlayerNearPos, reaction lore_text_relative_pos).
public static class MapInfo
{
    public const ushort MsgClientSetClientMapInfo = 23208; // GeneratedDispatcher.Pack_ClientSetClientMapInfo

    // ClientEventConditionEvaluator.ConditionCode
    public const int ConditionAnd = 1;
    public const int ConditionNot = 3;
    public const int ConditionInventoryOpen = 9;
    public const int ConditionLowHealth = 10;
    public const int ConditionKeySet = 14;
    public const int ConditionKeyNotSet = 15;
    public const int ConditionWindowFocused = 18;
    public const int ConditionPlayerNear = 31;          // PlayerNearPos: value0 = x, value1 = y, value2 = radius

    // ------------------------------------------------------------------ model (same as the client's)

    public sealed class Condition
    {
        public int Code;
        public string Name = "";
        public float Value0;
        public float Value1;
        public float Value2;
        public List<Condition> Sub = new List<Condition>();
    }

    public sealed class Reaction
    {
        public string Type = "";
        public string Parameter = "";
        public string Asset = "";
        public float F0;
        public float F1;
        public float F2;
        public float Rotation;
    }

    public sealed class MapEvent
    {
        public string Trigger = "";
        public string Once = "";
        public string WaitKey = "";
        public int Wait;
        public Condition? Condition;
        public List<Reaction> Reactions = new List<Reaction>();
    }

    public sealed class MapInfoData
    {
        public string RespawnWithClass = "";
        public List<string> PersistentKeys = new List<string>();
        public List<MapEvent> Events = new List<MapEvent>();
        public bool StartsAtKeyScreen;
        public bool HomeTeleportOff;
        public bool LocalTeleportOff;
    }

    // ------------------------------------------------------------------ state

    private static readonly object gate = new object();
    private static Dictionary<string, byte[]> byTerrain = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    private static string readFrom = "";
    private static DateTime readAt = DateTime.MinValue;
    private static bool warnedMissing;

    // Terrains with extra events: takes the original ClientMapInfo (or null, without the file) and returns the final one.
    private static readonly Dictionary<string, Func<MapInfoData?, MapInfoData>> builders =
        new Dictionary<string, Func<MapInfoData?, MapInfoData>>(StringComparer.OrdinalIgnoreCase);

    public static string FilePath()
    {
        return GameDataFiles.FilePath("mapinfo_config.txt");
    }

    public static void RegisterMap(string terrain, Func<MapInfoData?, MapInfoData> build)
    {
        lock (gate) builders[terrain] = build;
    }

    // Writes the whole message. Returns the size of the ClientMapInfo in bytes, or -1 if there is nothing
    // for that terrain (then nothing is written).
    public static int WriteMessage(NetBuffer buf, string terrain, Action<string, string> log)
    {
        byte[]? data = Get(terrain, log);
        Func<MapInfoData?, MapInfoData>? build;
        lock (gate) builders.TryGetValue(terrain, out build);

        if (build == null)
        {
            if (data == null) return -1;
            buf.Write(MsgClientSetClientMapInfo);
            buf.Write(data);
            return data.Length;
        }

        MapInfoData? original = null;
        if (data != null)
        {
            try
            {
                original = Read(data);
            }
            catch (Exception e)
            {
                log("MAPINFO", "ClientMapInfo of '" + terrain + "' unreadable (" + e.GetType().Name + ": " + e.Message +
                    "): the server builds a new one");
            }
        }

        MapInfoData final = build(original);
        buf.Write(MsgClientSetClientMapInfo);
        long before = buf.LengthBits;
        Write(buf, final);
        return (int)((buf.LengthBits - before + 7) / 8);
    }

    // ------------------------------------------------------------------ reading the file

    private static byte[]? Get(string terrain, Action<string, string> log)
    {
        lock (gate)
        {
            string path = FilePath();
            try
            {
                if (!File.Exists(path))
                {
                    if (!warnedMissing)
                    {
                        warnedMissing = true;
                        log("MAPINFO", path + " does not exist: the client has no original hints of the map " +
                            "(the file comes in the server's data folder).");
                    }
                    return null;
                }

                DateTime modified = File.GetLastWriteTimeUtc(path);
                if (modified != readAt || path != readFrom)
                {
                    var fresh = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                    foreach (string line in File.ReadAllLines(path))
                    {
                        if (line.Length == 0 || line[0] == '#') continue;
                        string[] p = line.Split('\t');
                        if (p.Length < 3 || p[0] != "mapinfo") continue;
                        try
                        {
                            fresh[p[1]] = Convert.FromHexString(p[2].Trim());
                        }
                        catch (FormatException)
                        {
                            log("MAPINFO", "line of '" + p[1] + "' with invalid hex in " + path);
                        }
                    }
                    byTerrain = fresh;
                    readAt = modified;
                    readFrom = path;
                    warnedMissing = false;
                    log("MAPINFO", path + ": " + fresh.Count + " terrain(s) with ClientMapInfo");
                }
            }
            catch (IOException)
            {
                // The file may be being written right now: it keeps what was already read.
            }
            catch (Exception e)
            {
                log("MAPINFO", "could not read " + path + ": " + e.GetType().Name + ": " + e.Message);
            }

            return byTerrain.TryGetValue(terrain, out byte[]? data) ? data : null;
        }
    }

    // ------------------------------------------------------------------ ClientMapInfo.Serializer

    // Reading in the Lidgren format: bits from the least to the most significant, strings with
    // a variable-integer length and 1-bit bools.
    private sealed class Reader
    {
        private readonly byte[] data;
        private long bit;

        public Reader(byte[] data)
        {
            this.data = data;
        }

        private uint Bits(int count)
        {
            uint val = 0;
            for (int k = 0; k < count; k++)
            {
                long index = bit >> 3;
                if (index >= data.Length) throw new EndOfStreamException("ClientMapInfo ended too early");
                uint b = (uint)(data[index] >> (int)(bit & 7)) & 1u;
                val |= b << k;
                bit++;
            }
            return val;
        }

        public byte Byte() => (byte)Bits(8);
        public bool Bool() => Bits(1) == 1;
        public short Int16() => unchecked((short)Bits(16));
        public int Int32() => unchecked((int)Bits(32));
        public float Float() => BitConverter.Int32BitsToSingle(unchecked((int)Bits(32)));

        public uint VarUInt()
        {
            uint val = 0;
            int offset = 0;
            while (true)
            {
                byte b = Byte();
                val |= (uint)(b & 0x7f) << offset;
                offset += 7;
                if ((b & 0x80) == 0) return val;
                if (offset > 35) throw new InvalidDataException("variable integer too large");
            }
        }

        public int VarInt()
        {
            uint v = VarUInt();
            return (int)(v >> 1) ^ -(int)(v & 1);
        }

        public string Text()
        {
            int size = (int)VarUInt();
            if (size < 0 || size > 65535) throw new InvalidDataException("text too large");
            var bytes = new byte[size];
            for (int i = 0; i < size; i++) bytes[i] = Byte();
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
    }

    public static MapInfoData Read(byte[] data)
    {
        var l = new Reader(data);
        var info = new MapInfoData { RespawnWithClass = l.Text() };
        int keys = l.Int16();
        for (int i = 0; i < keys; i++) info.PersistentKeys.Add(l.Text());
        int events = l.Int16();
        for (int i = 0; i < events; i++) info.Events.Add(ReadEvent(l));
        info.StartsAtKeyScreen = l.Bool();
        info.HomeTeleportOff = l.Bool();
        info.LocalTeleportOff = l.Bool();
        return info;
    }

    private static MapEvent ReadEvent(Reader l)
    {
        var e = new MapEvent
        {
            Trigger = l.Text(),
            Once = l.Text(),
            WaitKey = l.Text(),
            Wait = l.VarInt()
        };
        if (l.Bool()) e.Condition = ReadCondition(l, 0);
        int reactions = l.Int32();
        for (int i = 0; i < reactions; i++)
        {
            e.Reactions.Add(new Reaction
            {
                Type = l.Text(),
                Parameter = l.Text(),
                Asset = l.Text(),
                F0 = l.Float(),
                F1 = l.Float(),
                F2 = l.Float(),
                Rotation = l.Float()
            });
        }
        return e;
    }

    private static Condition ReadCondition(Reader l, int depth)
    {
        if (depth > 16) throw new InvalidDataException("conditions nested too deep");
        var c = new Condition
        {
            Code = l.Int32(),
            Name = l.Text(),
            Value0 = l.Float(),
            Value1 = l.Float(),
            Value2 = l.Float()
        };
        int sub = l.Int32();
        for (int i = 0; i < sub; i++) c.Sub.Add(ReadCondition(l, depth + 1));
        return c;
    }

    public static void Write(NetBuffer buf, MapInfoData info)
    {
        buf.Write(info.RespawnWithClass ?? "");
        buf.Write((short)info.PersistentKeys.Count);
        foreach (string key in info.PersistentKeys) buf.Write(key ?? "");
        buf.Write((short)info.Events.Count);
        foreach (MapEvent e in info.Events) WriteEvent(buf, e);
        buf.Write(info.StartsAtKeyScreen);
        buf.Write(info.HomeTeleportOff);
        buf.Write(info.LocalTeleportOff);
    }

    private static void WriteEvent(NetBuffer buf, MapEvent e)
    {
        buf.Write(e.Trigger ?? "");
        buf.Write(e.Once ?? "");
        buf.Write(e.WaitKey ?? "");
        buf.WriteVariableInt32(e.Wait);
        buf.Write(e.Condition != null);
        if (e.Condition != null) WriteCondition(buf, e.Condition);
        buf.Write(e.Reactions.Count);
        foreach (Reaction r in e.Reactions)
        {
            buf.Write(r.Type ?? "");
            buf.Write(r.Parameter ?? "");
            buf.Write(r.Asset ?? "");
            buf.Write(r.F0);
            buf.Write(r.F1);
            buf.Write(r.F2);
            buf.Write(r.Rotation);
        }
    }

    private static void WriteCondition(NetBuffer buf, Condition c)
    {
        buf.Write(c.Code);
        buf.Write(c.Name ?? "");
        buf.Write(c.Value0);
        buf.Write(c.Value1);
        buf.Write(c.Value2);
        buf.Write(c.Sub.Count);
        foreach (Condition s in c.Sub) WriteCondition(buf, s);
    }

    // ------------------------------------------------------------------ helpers to build events

    public static Condition Cond(int code, string name = "", float v0 = 0f, float v1 = 0f, float v2 = 0f, params Condition[] sub)
    {
        return new Condition { Code = code, Name = name, Value0 = v0, Value1 = v1, Value2 = v2, Sub = sub.ToList() };
    }

    public static Reaction Reac(string type, string parameter = "", string asset = "", float f0 = 0f, float f1 = 0f, float f2 = 0f,
        float rotation = 0f)
    {
        return new Reaction { Type = type, Parameter = parameter, Asset = asset, F0 = f0, F1 = f1, F2 = f2, Rotation = rotation };
    }

    public static MapEvent Ev(string trigger, string once, Condition? condition, params Reaction[] reactions)
    {
        return new MapEvent { Trigger = trigger, Once = once, Condition = condition, Reactions = reactions.ToList() };
    }
}
