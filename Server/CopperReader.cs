using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Lidgren.Network;

// Reading of the Copper protocol types that the client sends to the server.
//
// Each Read* method copies, in the same order, the Read method of the matching serializer in the decompiled
// code (GunFireSpecSerializer, FieldUpdateSerializer, ALInputAction.Serializer, KillInfoSerializer,
// InventoryPositionSerializer...). Three Copper rules show up all the time:
//  - a lone bool takes 1 BIT (what comes after is shifted; Lidgren's NetBuffer takes care of it);
//  - "scaled" is a variable integer (zigzag) that represents value * scale;
//  - lists and buffers send "count + 1", and 0 means null.
public static class CopperReader
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // The game's EquipmentSlot. The index is the enum value.
    public static readonly string[] SlotNames =
    {
        "Plane", "Armor", "Engine", "Clockwork", "MainGun", "SecondaryAttack", "Health", "Energy",
        "Consumable", "Portal", "Upgrade", "UserUpgrade", "ConsumableAction", "Quest", "DeathGun", "ManeuverGun"
    };

    // The game's SkillTriggerType.
    public static readonly string[] TriggerNames =
    {
        "OnKill", "OnHit", "OnSuperspeedStart", "OnSuperspeedEnd", "OnManeuverStart", "OnManeuverEnd",
        "OnSecondary", "OnCloudAffectsAlly", "OnCloudAffectsEnemy"
    };

    // The game's KillReason.
    public static readonly string[] DeathReasonNames =
    {
        "Unknown", "OutOfView", "Destroyed", "BulletDestroyed", "LootBagDestroyed", "DestroyedKillFamily",
        "DestroyedKillChildren", "DestroyedKillDescendants", "DestroyedFromAI", "DestroyedSelfDestruct",
        "DestroyedFromDamage", "Fizzled", "Disconnected", "OffMap", "Stale", "NoDamage", "ChangePlane"
    };

    // The game's BulletHitResult.
    public static readonly string[] BulletResultNames =
    {
        "None", "Damage", "Shield", "ElementalWeak", "ElementalArmor"
    };

    // The game's InventoryLocation.
    public static readonly string[] InventoryLocationNames =
    {
        "Cargo", "Equipped", "Permanent", "Equipped_Alternative"
    };

    // The game's UnitScript.States (maneuver state of the plane).
    public static readonly string[] StateNames =
    {
        "Default", "Flat180_1", "Flat180_2", "Flat180_3", "Himmelman_1", "Himmelman_2", "Himmelman_3",
        "Pin360_1", "Pin360_2", "Stop_1", "Stop_2", "Stop_3", "BarrelRoll_Intro", "BarrelRoll_Roll",
        "BarrelRoll_Outro", "BarrelRoll_Cooldown", "InvincibleDash_Intro", "InvincibleDash", "InvincibleDash_Outro",
        "Siege_Intro", "Siege_Intro_Anim", "Siege", "Siege_Outro_Anim", "Siege_Outro", "ShortShield_Start",
        "ShortShield_Protect", "WarpForward_Start", "WarpToAim_Start", "StopNSpin_Intro", "StopNSpin_Stopped",
        "StopNSpin_Outro", "Tank_Intro", "Tank_Up", "Tank_Outro", "Strafe", "Reverse_Intro", "Reverse",
        "Reverse_Outro", "Bash_Intro", "Bash", "Bash_Outro", "Dock_Intro", "Dock", "Docked", "Undocking",
        "Chilling_Intro", "Chilling", "Chilling_Outro", "Slow", "Slow_Outro", "Dash_Intro", "Dash", "Dash_Outro",
        "Dash_Back_Intro", "Dash_Back", "Dash_Back_Outro", "Flak_Start", "Flak_End", "TeleportToTurret_Start",
        "Redline_Intro", "Redline_Outro", "Flak_Start_Siege", "Flak_End_Siege"
    };

    // The game's EffectType (the values are not consecutive).
    private static readonly Dictionary<int, string> EffectNames = new Dictionary<int, string>
    {
        [0] = "None", [1] = "Slow", [2] = "Jam", [3] = "Burn", [4] = "Silence", [5] = "Weaken", [6] = "Sicken",
        [7] = "Confuse", [8] = "Blind", [9] = "Shielded", [12] = "Scaled", [13] = "Lightning", [14] = "Water",
        [15] = "Poison", [16] = "Bash", [20] = "Teleporting", [21] = "MaxEffects"
    };

    // Enums that the client writes as one byte and that only appear here as a number.
    private static readonly HashSet<string> OneByteEnums = new HashSet<string>
    {
        "AISpeechFormat", "FriendType", "GameClient.ClientTextType", "GameServer.DebugSetting",
        "GameServer.ItemDestination", "GuildPermissions.Role", "ServerRole", "SquadChange",
        "TeleportTargetType", "UnitScript.CloudType"
    };

    public static string Name(string[] table, int index)
    {
        if (index >= 0 && index < table.Length) return table[index];
        return index.ToString(Inv);
    }

    public static string EffectName(int effect)
    {
        return EffectNames.TryGetValue(effect, out var name) ? name : effect.ToString(Inv);
    }

    // Number with a decimal point and up to two decimals, so it is not confused with the comma that separates the fields.
    public static string Num(float val)
    {
        return val.ToString("0.##", Inv);
    }

    // ------------------------------------------------------------------ parsed structures

    // GunFireSpec: one shot of a gun.
    public sealed class Shot
    {
        public int Seed;
        public bool IsPlayer;
        public int Owner;
        public float Time;
        public float X;
        public float Y;
        public float Rotation;
        public uint Gun;
        public float GunRotation;
        public int Counter;
        public int Multiple;
        public int Target;
        public bool HasState;
        public int Ammo;
    }

    // FieldUpdate: the fields of a unit that changed.
    public sealed class Fields
    {
        public uint Mask;
        public float X;
        public float Y;
        public float VelX;
        public float VelY;
        public float Rotation;
        public float Aim;
        public int State;
        public float Health;
        public float Energy;
        public int Ammo;
        public float Scale;
        public string Name = "";
        public uint Flags;
        public byte[]? Guns;
        public short Effect1;
        public short Effect2;

        public bool Has(uint bit)
        {
            return (Mask & bit) != 0;
        }
    }

    // ALInputAction: what the player is pressing in this frame.
    public sealed class Entry
    {
        public uint ShotMask;
        public float Rudder;
        public float Throttle;
        public int State;
        public ushort SlotMask;
        public bool Rest;
        public bool Teleport;
        public bool AbsoluteDirection;
        public uint Index;
        public int Target;
        public bool HasCheck;
        public float X;
        public float Y;
        public float Rotation;
        public float Aim;
    }

    // KillInfo: reason and place of a death.
    public sealed class Death
    {
        public int Reason;
        public bool HasPosition;
        public float X;
        public float Y;
        public float Rotation;
        public int BulletResult;
    }

    // EntitySpawnStruct: a created unit.
    public sealed class Spawn
    {
        public float Time;
        public int Prefab;
        public int ShortId;
        public short Template;
        public short RootTemplate;
        public int Parent;
        public int Team;
        public short ArtSwapped;
        public long Authority;
        public int OptionBytes;
        public Fields Fields = new Fields();
    }

    // ------------------------------------------------------------------ readers

    public static long BitsLeft(NetBuffer b)
    {
        return b.LengthBits - b.Position;
    }

    public static float ReadScaled(NetBuffer b, float scale)
    {
        return b.ReadVariableInt32() / scale;
    }

    // GeneralID: two ulongs (highbits, lowbits). Returns the 16 bytes in the order PlayerData uses.
    public static byte[] ReadGeneralId(NetBuffer b)
    {
        var bytes = new byte[16];
        ulong high = b.ReadUInt64();
        ulong low = b.ReadUInt64();
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(0, 8), high);
        BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8, 8), low);
        return bytes;
    }

    public static string GeneralIdText(byte[] id16)
    {
        bool empty = true;
        foreach (byte part in id16)
        {
            if (part != 0) empty = false;
        }
        return empty ? "(none)" : PlayerData.ToBase64Url(id16);
    }

    // GunFireSpecSerializer.Read
    public static Shot ReadShot(NetBuffer b)
    {
        var d = new Shot();
        byte flags = b.ReadByte();
        d.Seed = flags & 0x1F;
        bool hasState = (flags & 0x20) != 0;
        bool hasTarget = (flags & 0x40) != 0;
        d.IsPlayer = (flags & 0x80) != 0;
        d.Owner = b.ReadVariableInt32();
        d.Time = b.ReadSingle();
        d.X = ReadScaled(b, 64f);
        d.Y = ReadScaled(b, 64f);
        d.Rotation = ReadScaled(b, 256f);
        if (d.IsPlayer)
        {
            d.Gun = b.ReadVariableUInt32();
        }
        else
        {
            d.Gun = b.ReadByte();
        }
        d.GunRotation = ReadScaled(b, 256f);
        d.Counter = b.ReadVariableInt32();
        d.Multiple = b.ReadByte();
        if (hasTarget)
        {
            d.Target = b.ReadVariableInt32();
        }
        if (hasState)
        {
            d.HasState = true;
            d.Ammo = b.ReadInt32();
            b.ReadSingle(); // FireClock
            b.ReadSingle(); // ReloadClock
        }
        return d;
    }

    public static string ShotText(Shot d)
    {
        var sb = new StringBuilder();
        sb.Append("owner=").Append(d.Owner);
        sb.Append(" gun=").Append(d.Gun);
        sb.Append(" pos=(").Append(Num(d.X)).Append(", ").Append(Num(d.Y)).Append(')');
        sb.Append(" rot=").Append(Num(d.Rotation));
        sb.Append(" gunRotation=").Append(Num(d.GunRotation));
        sb.Append(" time=").Append(Num(d.Time));
        sb.Append(" bullet#").Append(d.Counter);
        sb.Append(" x").Append(d.Multiple);
        if (d.Target != 0) sb.Append(" target=").Append(d.Target);
        if (d.HasState) sb.Append(" ammo=").Append(d.Ammo);
        return sb.ToString();
    }

    // FieldUpdateSerializer.Read. The fields the server does not use are read and discarded,
    // so the read position ends in the right place.
    public static Fields ReadFields(NetBuffer b)
    {
        var c = new Fields();
        c.Mask = b.ReadVariableUInt32();
        if (c.Has(1u))
        {
            c.X = ReadScaled(b, 1024f);
            c.Y = ReadScaled(b, 1024f);
        }
        if (c.Has(2u))
        {
            c.VelX = ReadScaled(b, 1024f);
            c.VelY = ReadScaled(b, 1024f);
        }
        if (c.Has(4u)) c.Rotation = ReadScaled(b, 32f);
        if (c.Has(64u)) c.Aim = ReadScaled(b, 32f);
        if (c.Has(128u)) c.State = b.ReadByte();
        if (c.Has(8u)) c.Health = ReadScaled(b, 512f);
        if (c.Has(16u)) c.Energy = ReadScaled(b, 512f);
        if (c.Has(32u)) c.Ammo = b.ReadVariableInt32();
        if (c.Has(32768u)) c.Scale = ReadScaled(b, 32f);
        if (c.Has(256u)) ReadScaled(b, 64f); // alpha
        if (c.Has(512u))
        {
            ReadScaled(b, 64f); // r
            ReadScaled(b, 64f); // g
            ReadScaled(b, 64f); // b
            ReadScaled(b, 64f); // a
        }
        if (c.Has(1024u)) c.Name = b.ReadString() ?? "";
        if (c.Has(2048u)) c.Flags = b.ReadVariableUInt32();
        if (c.Has(4096u))
        {
            int gunBytes = b.ReadVariableInt32();
            if (gunBytes > 0 && gunBytes * 8L <= BitsLeft(b)) c.Guns = b.ReadBytes(gunBytes);
        }
        if (c.Has(8192u)) c.Effect1 = b.ReadInt16();
        if (c.Has(16384u)) c.Effect2 = b.ReadInt16();
        if (c.Has(65536u)) ReadDecoration(b);
        if (c.Has(131072u)) b.ReadSingle(); // gunRange
        if (c.Has(262144u)) b.ReadSingle(); // bulletSpeed
        if (c.Has(524288u)) b.ReadString(); // variant
        return c;
    }

    // DecorationSerializer.Read: a presence byte, up to three colors (uint), livery and two trails.
    private static void ReadDecoration(NetBuffer b)
    {
        byte presence = b.ReadByte();
        if ((presence & 1) != 0) b.ReadUInt32();
        if ((presence & 2) != 0) b.ReadUInt32();
        if ((presence & 4) != 0) b.ReadUInt32();
        if ((presence & 8) != 0) b.ReadString();
        if ((presence & 16) != 0) b.ReadInt16();
        if ((presence & 32) != 0) b.ReadInt16();
    }

    public static string FieldsText(Fields c)
    {
        var sb = new StringBuilder();
        sb.Append("mask=0x").Append(c.Mask.ToString("X", Inv));
        if (c.Has(1u)) sb.Append(" pos=(").Append(Num(c.X)).Append(", ").Append(Num(c.Y)).Append(')');
        if (c.Has(2u)) sb.Append(" vel=(").Append(Num(c.VelX)).Append(", ").Append(Num(c.VelY)).Append(')');
        if (c.Has(4u)) sb.Append(" rot=").Append(Num(c.Rotation));
        if (c.Has(64u)) sb.Append(" aim=").Append(Num(c.Aim));
        if (c.Has(128u)) sb.Append(" state=").Append(Name(StateNames, c.State));
        if (c.Has(8u)) sb.Append(" health=").Append(Num(c.Health));
        if (c.Has(16u)) sb.Append(" energy=").Append(Num(c.Energy));
        if (c.Has(32u)) sb.Append(" ammo=").Append(c.Ammo);
        if (c.Has(32768u)) sb.Append(" scale=").Append(Num(c.Scale));
        if (c.Has(1024u)) sb.Append(" name='").Append(c.Name).Append('\'');
        return sb.ToString();
    }

    // ALInputAction.Serializer.Read
    public static Entry ReadInput(NetBuffer b)
    {
        var e = new Entry();
        e.ShotMask = b.ReadUInt32();
        e.Rudder = b.ReadSingle();
        e.Throttle = b.ReadSingle();
        e.State = b.ReadByte();
        e.SlotMask = b.ReadUInt16();
        byte bits = b.ReadByte();
        e.Rest = (bits & 1) != 0;
        e.Teleport = (bits & 2) != 0;
        e.AbsoluteDirection = (bits & 4) != 0;
        e.Index = b.ReadUInt32();
        e.Target = b.ReadInt32();
        e.HasCheck = b.ReadBoolean();
        if (e.HasCheck)
        {
            e.X = b.ReadSingle();
            e.Y = b.ReadSingle();
            e.Rotation = b.ReadSingle();
            e.Aim = b.ReadSingle();
        }
        return e;
    }

    public static string InputText(Entry e)
    {
        var sb = new StringBuilder();
        sb.Append("shots=0x").Append(e.ShotMask.ToString("X", Inv));
        sb.Append(" slots=0x").Append(e.SlotMask.ToString("X", Inv));
        sb.Append(" rudder=").Append(Num(e.Rudder));
        sb.Append(" throttle=").Append(Num(e.Throttle));
        sb.Append(" state=").Append(Name(StateNames, e.State));
        sb.Append(" frame=").Append(e.Index);
        if (e.Target != 0) sb.Append(" target=").Append(e.Target);
        if (e.HasCheck)
        {
            sb.Append(" pos=(").Append(Num(e.X)).Append(", ").Append(Num(e.Y)).Append(')');
            sb.Append(" rot=").Append(Num(e.Rotation));
            sb.Append(" aim=").Append(Num(e.Aim));
        }
        return sb.ToString();
    }

    // KillInfoSerializer.Read. The position goes in bits: 16 + 16 for x and y, 8 for the rotation.
    public static Death ReadDeath(NetBuffer b)
    {
        var m = new Death();
        m.Reason = b.ReadByte();
        if (m.Reason != 1) // 1 = OutOfView, which sends no position
        {
            m.HasPosition = true;
            m.X = b.ReadRangedSingle(-2000f, 2000f, 16);
            m.Y = b.ReadRangedSingle(-2000f, 2000f, 16);
            m.Rotation = b.ReadRangedSingle(-180f, 180f, 8);
            m.BulletResult = b.ReadByte();
        }
        return m;
    }

    public static string DeathText(Death m)
    {
        var sb = new StringBuilder();
        sb.Append(Name(DeathReasonNames, m.Reason));
        if (m.HasPosition)
        {
            sb.Append(" at (").Append(Num(m.X)).Append(", ").Append(Num(m.Y)).Append(')');
            if (m.BulletResult != 0) sb.Append(' ').Append(Name(BulletResultNames, m.BulletResult));
        }
        return sb.ToString();
    }

    // EntitySpawnStructSerializer.Read
    public static Spawn ReadSpawn(NetBuffer b)
    {
        var n = new Spawn();
        n.Time = b.ReadSingle();
        n.Prefab = b.ReadVariableInt32();
        n.ShortId = b.ReadVariableInt32();
        n.Template = b.ReadInt16();
        n.RootTemplate = b.ReadInt16();
        n.Parent = b.ReadVariableInt32();
        n.Team = b.ReadVariableInt32();
        n.ArtSwapped = b.ReadInt16();
        n.Authority = b.ReadInt64();
        n.OptionBytes = SkipBuffer(b);
        n.Fields = ReadFields(b);
        return n;
    }

    // NetBufferCopperSerializer.Read: variable integer with (bytes + 1), then the bytes. 0 = null.
    // Returns how many bytes the buffer had (-1 if it was null) and leaves the read right after it.
    public static int SkipBuffer(NetBuffer b)
    {
        int sizePlusOne = b.ReadVariableInt32();
        if (sizePlusOne <= 0) return -1;
        int size = sizePlusOne - 1;
        if (size > 0 && size * 8L <= BitsLeft(b)) b.ReadBytes(size);
        return size;
    }

    // InventoryPositionSerializer.Read: one byte (0x80 valid, bits 5-6 location, bits 0-4 slot)
    // and, when the location has an index (Cargo and Permanent), a variable integer.
    public static string ReadInventoryPosition(NetBuffer b)
    {
        byte val = b.ReadByte();
        bool valid = (val & 0x80) != 0;
        int local = (val >> 5) & 3;
        int slot = val & 0x1F;
        bool hasIndex = local != 1 && local != 3;
        int index = -1;
        if (valid && hasIndex) index = b.ReadVariableInt32();
        if (!valid) return "(invalid)";
        string locationName = Name(InventoryLocationNames, local);
        if (hasIndex) return locationName + "[" + index.ToString(Inv) + "]";
        return locationName + ":" + Name(SlotNames, slot);
    }

    // InventoryItemSerializer.Read. Null item = empty string.
    private static string ReadInventoryItem(NetBuffer b)
    {
        string defId = b.ReadString() ?? "";
        if (defId.Length == 0) return "(empty)";
        string id = b.ReadString() ?? "";
        int uses = b.ReadVariableInt32();
        b.ReadVariableInt32(); // decayTimestamp
        b.ReadBoolean();       // soulboundInstance
        b.ReadBoolean();       // usedInstance
        b.ReadBoolean();       // salvagedInstance
        b.ReadInt32();         // condition
        b.ReadBoolean();       // favorite
        int modifiers = b.ReadVariableInt32();
        for (int i = 0; i < modifiers && i < 64; i++)
        {
            b.ReadString();
            b.ReadVariableInt32();
            b.ReadVariableInt32();
            b.ReadBoolean();
            b.ReadVariableInt32();
        }
        return defId + "#" + id + " uses=" + uses.ToString(Inv);
    }

    // ------------------------------------------------------------------ generic reading by the catalog

    // Splits "Type name, Type<A, B> name" into pairs. The comma inside <> does not separate arguments.
    public static List<(string Type, string Name)> SplitArgs(string args)
    {
        var list = new List<(string Type, string Name)>();
        int level = 0;
        int start = 0;
        for (int i = 0; i <= args.Length; i++)
        {
            char c = i < args.Length ? args[i] : ',';
            if (c == '<')
            {
                level++;
            }
            else if (c == '>')
            {
                level--;
            }
            else if (c == ',' && level == 0)
            {
                string chunk = args.Substring(start, i - start).Trim();
                start = i + 1;
                if (chunk.Length == 0) continue;
                int space = chunk.LastIndexOf(' ');
                if (space < 0)
                {
                    list.Add((chunk, "?"));
                }
                else
                {
                    list.Add((chunk.Substring(0, space).Trim(), chunk.Substring(space + 1)));
                }
            }
        }
        return list;
    }

    // Reads the arguments of a catalog message and returns "name=value, name=value".
    // Stops at the first type it cannot read and shows the rest in hexadecimal. Never throws.
    public static string Describe(NetBuffer b, string args)
    {
        return Describe(b, args, out _);
    }

    // "complete" comes back true when all the arguments were read; then the read position
    // is right after the last argument (and that is where the rpcId of an RPC is).
    public static string Describe(NetBuffer b, string args, out bool complete)
    {
        complete = true;
        var sb = new StringBuilder();
        foreach (var (type, name) in SplitArgs(args))
        {
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(name).Append('=');

            if (BitsLeft(b) <= 0)
            {
                sb.Append("<the message ended early>");
                complete = false;
                break;
            }

            string? val;
            try
            {
                val = ReadValue(b, type);
            }
            catch (Exception e)
            {
                sb.Append("<error reading ").Append(type).Append(": ").Append(e.GetType().Name).Append('>');
                complete = false;
                break;
            }

            if (val == null)
            {
                sb.Append('<').Append(type).Append(" not decoded; rest ").Append(RestAsHex(b)).Append('>');
                complete = false;
                break;
            }

            sb.Append(val);
            if (b.Position > b.LengthBits)
            {
                sb.Append(" <went past the end of the message>");
                complete = false;
                break;
            }
        }
        return sb.ToString();
    }

    // Bytes not read yet, in hexadecimal (up to "max" bytes).
    public static string RestAsHex(NetBuffer b, int max = 48)
    {
        long bits = BitsLeft(b);
        if (bits <= 0) return "(empty)";
        int total = (int)(bits / 8);
        if (total == 0) return bits.ToString(Inv) + " bit(s)";
        int howMany = Math.Min(total, max);
        string hex = Convert.ToHexString(b.ReadBytes(howMany));
        if (total > howMany) return total.ToString(Inv) + "B " + hex + "...";
        return total.ToString(Inv) + "B " + hex;
    }

    // A value of a known type, already as text. Null = a type the server cannot read yet.
    private static string? ReadValue(NetBuffer b, string type)
    {
        switch (type)
        {
            case "string":
            case "InventoryId":
                return "'" + (b.ReadString() ?? "") + "'";
            case "int":
                return b.ReadInt32().ToString(Inv);
            case "float":
                return Num(b.ReadSingle());
            case "double":
                return b.ReadDouble().ToString("0.###", Inv);
            case "bool":
                return b.ReadBoolean() ? "yes" : "no";
            case "short":
                return b.ReadInt16().ToString(Inv);
            case "byte":
                return b.ReadByte().ToString(Inv);
            case "GeneralID":
                return GeneralIdText(ReadGeneralId(b));
            case "PeerId":
                return b.ReadInt64().ToString("X16", Inv);
            case "Vector2":
            {
                float x = b.ReadSingle();
                float y = b.ReadSingle();
                return "(" + Num(x) + ", " + Num(y) + ")";
            }
            case "Vector3":
            {
                float x = b.ReadSingle();
                float y = b.ReadSingle();
                float z = b.ReadSingle();
                return "(" + Num(x) + ", " + Num(y) + ", " + Num(z) + ")";
            }
            case "EquipmentSlot":
                return Name(SlotNames, b.ReadByte());
            case "SkillTriggerType":
                return Name(TriggerNames, b.ReadByte());
            case "EffectType":
                return EffectName(b.ReadByte());
            case "InventoryPosition":
                return ReadInventoryPosition(b);
            case "InventoryItem":
                return ReadInventoryItem(b);
            case "BulletId":
            {
                ulong id = b.ReadUInt64();
                ulong owner = id >> 32;
                ulong counter = id & 0xFFFFFFFFUL;
                return owner.ToString(Inv) + "#" + counter.ToString(Inv);
            }
            case "KillInfo":
                return DeathText(ReadDeath(b));
            case "FieldUpdate":
                return FieldsText(ReadFields(b));
            case "GunFireSpec":
                return ShotText(ReadShot(b));
            case "ALInputAction":
                return InputText(ReadInput(b));
            case "NetBuffer":
            {
                int size = SkipBuffer(b);
                return size < 0 ? "(null)" : size.ToString(Inv) + "B";
            }
            case "InstantEffectRequest":
            {
                int shortId = b.ReadVariableInt32();
                short fx = b.ReadInt16();
                float ox = ReadScaled(b, 20f);
                float oy = ReadScaled(b, 20f);
                float oz = ReadScaled(b, 20f);
                float scale = ReadScaled(b, 10f);
                return "unit " + shortId.ToString(Inv) + " fx " + fx.ToString(Inv) +
                       " offset (" + Num(ox) + ", " + Num(oy) + ", " + Num(oz) + ") scale " + Num(scale);
            }
            case "EntityHandler.BulletHitInfuseHealInfo":
            {
                bool zeroed = b.ReadBoolean();
                if (zeroed) return "(no heal)";
                float heal = ReadScaled(b, 1024f);
                float infusion = ReadScaled(b, 1024f);
                float radius = ReadScaled(b, 1024f);
                b.ReadInt16();
                b.ReadInt16();
                return "heal " + Num(heal) + " infusion " + Num(infusion) + " radius " + Num(radius);
            }
            case "LegalNoticeAcceptance":
            {
                b.ReadInt64();
                b.ReadInt32();
                string locale = b.ReadString() ?? "";
                string version = b.ReadString() ?? "";
                b.ReadString();
                return "locale '" + locale + "' version '" + version + "'";
            }
            case "List<string>":
            {
                uint countPlusOne = b.ReadVariableUInt32();
                if (countPlusOne == 0) return "(null)";
                var items = new List<string>();
                for (uint i = 0; i < countPlusOne - 1 && i < 64 && BitsLeft(b) > 0; i++)
                {
                    items.Add("'" + (b.ReadString() ?? "") + "'");
                }
                return "[" + string.Join(", ", items) + "]";
            }
            case "List<InventoryPosition>":
            {
                uint countPlusOne = b.ReadVariableUInt32();
                if (countPlusOne == 0) return "(null)";
                var items = new List<string>();
                for (uint i = 0; i < countPlusOne - 1 && i < 64 && BitsLeft(b) > 0; i++)
                {
                    items.Add(ReadInventoryPosition(b));
                }
                return "[" + string.Join(", ", items) + "]";
            }
        }

        if (OneByteEnums.Contains(type)) return b.ReadByte().ToString(Inv);
        return null;
    }
}
