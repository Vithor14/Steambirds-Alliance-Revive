using Lidgren.Network;

// Multiplayer relay: lets the players of the same server (Nexus) see, hear and shoot each other.
//
// The game client trusts its own plane (authority = its own Peer.Id) and tells the server where it is. The server
// forwards that to the other clients as a "remote plane":
//  - SpawnUnitsBatch with playerAuthority = the OWNER's peer id (so the observer does not treat it as its own plane);
//  - UpdateUnitsBatch with position, velocity, rotation, aim and health of the owner;
//  - GunFire of the owner (the observer creates the bullets locally);
//  - KillUnitsBatch when the plane is destroyed, swapped in the hangar or the owner leaves;
//  - ClientProximityChat for chat bubbles and the chat panel;
//  - ClientReceiveRemotePlayerData, when the observer asks for the data of the owner's account (name plate, equipment).
//
// Every plane gets ONE relay id (7,000,000+) that is the same for every observer. It is different from the owner's own
// shortId (1000..1999), which the observers never see. A new relay id is used on every new plane (the client discards a
// spawn whose shortId is still alive), and it also keeps bullet ids unique (a BulletId is owner shortId + counter).
//
// Players are grouped in "rooms": everybody in the Nexus is in the room "nexus", and everybody in the same dungeon instance
// (DungeonServer.RoomKey) is in the room of that instance. The tutorial has no room: one private world per player.
// The shot time of GunFire is the shooter's own clock (Time.fixedTime). The observers compare it with the server clock, so
// the relay converts it with the offset the shooter reports in every input message (ClockSample).
// Skill clouds of a player (poison, heal, damage blast...) are created for every member of the room by the Skills module
// (PlayerRelay.Observers); TriggerOtherPlayerSkill is forwarded to the cloud's owner (ForwardSkillTrigger).
public static class PlayerRelay
{
    public const string NexusRoom = "nexus";

    // First id of the remote planes. Below the loot bags (5,000,000) and above the buildings and enemies.
    private const int FirstRelayId = 7000000;

    private const ushort MsgSpawnUnitsBatch = 45862;
    private const ushort MsgUpdateUnitsBatch = 45864;
    private const ushort MsgGunFire = 62326;
    private const ushort MsgProximityChat = 1612;          // ClientProximityChat

    // FieldUpdate mask bits (FieldUpdateSerializer).
    private const uint FieldPosition = 1u;
    private const uint FieldVelocity = 2u;
    private const uint FieldRotation = 4u;
    private const uint FieldHealth = 8u;
    private const uint FieldAim = 64u;
    private const uint FieldState = 128u;
    private const uint FieldName = 1024u;                   // the name plate text of the plane
    private const uint FieldFlags = 2048u;
    private const uint FieldGuns = 4096u;                   // gun signal (muzzle flash and recoil of the remote plane)
    private const uint FieldEffect1 = 8192u;
    private const uint FieldEffect2 = 16384u;

    public const byte ReasonDestroyed = 2;                 // KillReason.Destroyed
    public const byte ReasonDisconnected = 12;             // KillReason.Disconnected
    public const byte ReasonChangePlane = 16;              // KillReason.ChangePlane

    private const int MaxChatLength = 200;

    // A player connected to the shared server, with or without a plane.
    private sealed class Member
    {
        public long Peer;
        public NetServer Server = null!;
        public NetConnection Connection = null!;
        public string Name = "";
        public byte[] Account = new byte[16];
        public string ServerName = "";
        public int Rank = 1;
        public string Room = "";

        // The live plane (RelayId 0 = none).
        public int RelayId;
        public string? CharacterId;
        public byte[]? CharacterBytes;
        public string? ClassId;
        public short NetClass;
        public short NetVariant;
        public SavedDecoration? Decoration;
        public short Template;
        public float X;
        public float Y;
        public float Rotation;

        // Server clock minus the shooter's own game clock (clientChosenServerTime - clientFixedTime), from the input messages.
        public double ClockOffset;
        public bool HasClock;

        // Owners whose current plane this member already received (so updates are not sent for units it does not know).
        public readonly HashSet<long> Seen = new HashSet<long>();
    }

    private static readonly Dictionary<long, Member> members = new Dictionary<long, Member>();
    private static int nextRelayId = FirstRelayId;
    private static double lastNow;

    // Same lock as Game and Enemies (reentrant), so the handlers that already hold it can call here.
    private static object Gate => Game.Lock;

    // ------------------------------------------------------------------ joining and leaving

    // The connection is identified (name and account known). Called by Program.cs after the welcome message.
    public static void Joined(NetServer server, NetConnection connection, string room, string name, byte[] account,
        string serverName, int rank)
    {
        lock (Gate)
        {
            members[connection.RemoteUniqueIdentifier] = new Member
            {
                Peer = connection.RemoteUniqueIdentifier,
                Server = server,
                Connection = connection,
                Room = room,
                Name = name,
                Account = (byte[])account.Clone(),
                ServerName = serverName,
                Rank = Math.Max(1, rank)
            };
        }
    }

    // The connection dropped (or the player left to another server).
    public static void Left(NetConnection? connection)
    {
        if (connection == null) return;
        lock (Gate)
        {
            long peer = connection.RemoteUniqueIdentifier;
            if (!members.TryGetValue(peer, out Member? member)) return;
            if (member.Connection != connection) return;   // an older connection of the same client (it switched servers)
            RemoveFromObservers(member, ReasonDisconnected, lastNow);
            members.Remove(peer);
            foreach (Member other in members.Values) other.Seen.Remove(peer);
        }
    }

    // ------------------------------------------------------------------ plane life cycle

    // The player's plane entered the world (SpawnPlane in Program.cs). It is shown to everybody and everybody is shown to it.
    public static void PlaneSpawned(NetConnection connection, string characterId, byte[] characterBytes,
        string classId, short netClass, short netVariant, short template, int rank, float x, float y, float rotation, double now,
        SavedDecoration? decoration = null)
    {
        lock (Gate)
        {
            lastNow = now;
            if (!members.TryGetValue(connection.RemoteUniqueIdentifier, out Member? member)) return;

            // A plane that is still shown (not removed by a swap or a death) leaves first.
            if (member.RelayId != 0) RemoveFromObservers(member, ReasonChangePlane, now);

            member.RelayId = nextRelayId++;
            member.CharacterId = characterId;
            member.CharacterBytes = characterBytes;
            member.ClassId = classId;
            member.NetClass = netClass;
            member.NetVariant = netVariant;
            member.Decoration = decoration?.Copy();
            member.Template = template;
            member.Rank = Math.Max(1, rank);
            member.X = x;
            member.Y = y;
            member.Rotation = rotation;

            foreach (Member other in members.Values)
            {
                if (other == member || other.Room != member.Room || other.Connection.Status != NetConnectionStatus.Connected) continue;
                SendSpawn(other, member, now);
                if (other.RelayId != 0) SendSpawn(member, other, now);
            }
        }
    }

    // A bullet of this player hit something (ServerReportBulletHit): the observers who see the bullet end it at the same place
    // (ClientKillBullet). Their copy carries the relay id of the owner instead of the owner's own unit id.
    public static void BulletHit(long peer, ulong bullet, float x, float y, float rotation, int result)
    {
        lock (Gate)
        {
            if (!members.TryGetValue(peer, out Member? owner) || owner.RelayId == 0) return;
            ulong id = ((ulong)(uint)owner.RelayId << 32) | (bullet & 0xFFFFFFFFUL);
            foreach (Member observer in members.Values)
            {
                if (observer == owner || !observer.Seen.Contains(peer)) continue;
                ScreenEffects.KillBullet(observer.Server, observer.Connection, id, x, y, rotation, result);
            }
        }
    }

    // TriggerOtherPlayerSkill: the owner of the plane with this relay id (same room as the sender) gets TriggerPlayerSkill.
    public static bool ForwardSkillTrigger(NetConnection from, byte trigger, float amount, int relayId, out string ownerName)
    {
        ownerName = "";
        lock (Gate)
        {
            if (!members.TryGetValue(from.RemoteUniqueIdentifier, out Member? sender)) return false;
            Member? owner = members.Values.FirstOrDefault(m => m != sender && m.RelayId == relayId && m.Room == sender.Room &&
                                                               m.Connection.Status == NetConnectionStatus.Connected);
            if (owner == null) return false;
            ownerName = owner.Name;
            ProgressionMessages.Send(owner.Server, owner.Connection, m => ProgressionMessages.WriteTriggerPlayerSkill(m, trigger, amount));
            return true;
        }
    }

    // The player picked another variant (skin): players who see the plane from now on get the new one. Those who already
    // see it keep the old look until the plane spawns again.
    public static void VariantChanged(NetConnection? connection, short netVariant, string variantId)
    {
        if (connection == null) return;
        lock (Gate)
        {
            if (!members.TryGetValue(connection.RemoteUniqueIdentifier, out Member? member)) return;
            member.NetVariant = netVariant;
            member.Decoration = member.ClassId != null ? PlaneDecoration.Default(member.ClassId, variantId) : null;   // a new variant brings its own look
        }
    }

    // The plane left the world: destroyed, swapped in the hangar. The observers remove it.
    // now < 0: use the clock of the last message handled.
    public static void PlaneGone(NetConnection? connection, byte reason, double now)
    {
        if (connection == null) return;
        lock (Gate)
        {
            if (now >= 0) lastNow = now;
            if (!members.TryGetValue(connection.RemoteUniqueIdentifier, out Member? member) || member.RelayId == 0) return;
            RemoveFromObservers(member, reason, lastNow);
        }
    }

    private static void RemoveFromObservers(Member owner, byte reason, double now)
    {
        if (owner.RelayId == 0) return;
        foreach (Member other in members.Values)
        {
            if (other == owner || other.Room != owner.Room || !other.Seen.Remove(owner.Peer)) continue;
            WorldObjects.SendRemoval(other.Server, other.Connection, owner.RelayId, owner.X, owner.Y, reason, now);
        }
        owner.RelayId = 0;
    }

    // ------------------------------------------------------------------ movement and shots

    // UpdateUnitsBatch of the owner's plane (position, velocity, rotation, aim and health).
    public static void PlaneMoved(long peer, CopperReader.Fields fields, double now)
    {
        lock (Gate)
        {
            lastNow = now;
            if (!members.TryGetValue(peer, out Member? owner) || owner.RelayId == 0) return;

            if (fields.Has(FieldPosition))
            {
                owner.X = fields.X;
                owner.Y = fields.Y;
            }
            if (fields.Has(FieldRotation)) owner.Rotation = fields.Rotation;

            uint mask = fields.Mask & (FieldPosition | FieldVelocity | FieldRotation | FieldHealth | FieldAim | FieldState |
                FieldName | FieldFlags | FieldGuns | FieldEffect1 | FieldEffect2);
            if ((mask & FieldGuns) != 0 && fields.Guns == null) mask &= ~FieldGuns;
            if (mask == 0) return;

            foreach (Member observer in members.Values)
            {
                if (observer == owner || !observer.Seen.Contains(peer) || observer.Connection.Status != NetConnectionStatus.Connected) continue;

                NetOutgoingMessage m = observer.Server.CreateMessage();
                m.Write(MsgUpdateUnitsBatch);
                m.WriteVariableUInt32(2u);                     // 1 item + 1
                m.WriteVariableInt32(owner.RelayId);
                m.Write((byte)0);                              // no own time
                m.WriteVariableUInt32(mask);
                // Order of FieldUpdateSerializer: position, velocity, rotation, aim, state, health, ..., name, flags, guns, effects.
                if ((mask & FieldPosition) != 0)
                {
                    WriteScaled(m, fields.X, 1024f);
                    WriteScaled(m, fields.Y, 1024f);
                }
                if ((mask & FieldVelocity) != 0)
                {
                    WriteScaled(m, fields.VelX, 1024f);
                    WriteScaled(m, fields.VelY, 1024f);
                }
                if ((mask & FieldRotation) != 0) WriteScaled(m, fields.Rotation, 32f);
                if ((mask & FieldAim) != 0) WriteScaled(m, fields.Aim, 32f);
                if ((mask & FieldState) != 0) m.Write((byte)fields.State);
                if ((mask & FieldHealth) != 0) WriteScaled(m, fields.Health, 512f);
                if ((mask & FieldName) != 0) m.Write(owner.Name);              // always the account name, never an empty label
                if ((mask & FieldFlags) != 0) m.WriteVariableUInt32(fields.Flags);
                if ((mask & FieldGuns) != 0)
                {
                    m.WriteVariableInt32(fields.Guns!.Length);
                    m.Write(fields.Guns);
                }
                if ((mask & FieldEffect1) != 0) m.Write(fields.Effect1);
                if ((mask & FieldEffect2) != 0) m.Write(fields.Effect2);
                m.Write((float)now);                           // batch time
                observer.Server.SendMessage(m, observer.Connection, NetDeliveryMethod.ReliableOrdered, 0);
            }
        }
    }

    // ClientChosenServerTime - ClientFixedTime of an input message: the offset between the player's clock and the server clock.
    public static void ClockSample(long peer, float clientFixedTime, float clientChosenServerTime, Action<string, string> log)
    {
        if (!float.IsFinite(clientFixedTime) || !float.IsFinite(clientChosenServerTime)) return;
        lock (Gate)
        {
            if (!members.TryGetValue(peer, out Member? member)) return;
            bool first = !member.HasClock;
            member.ClockOffset = (double)clientChosenServerTime - clientFixedTime;
            member.HasClock = true;
            if (first)
                log("RELAY", "clock of '" + member.Name + "': server time = player time + " + member.ClockOffset.ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture) + " s");
        }
    }

    // GunFire of the owner's plane. The observers create the bullets locally with the owner's relay id.
    public static void PlaneShot(long peer, CopperReader.Shot shot, double now)
    {
        lock (Gate)
        {
            lastNow = now;
            if (!members.TryGetValue(peer, out Member? owner) || owner.RelayId == 0 || !shot.IsPlayer) return;

            // The shot time is the shooter's clock; the observers wait for their server clock to reach it.
            float shotTime = owner.HasClock ? (float)(shot.Time + owner.ClockOffset) : (float)now;

            // A target that is a plane of another client does not mean the same unit for the observer: no homing then.
            int target = shot.Target >= PlaneSpawn.PlayerPlaneShortId && shot.Target <= PlaneSpawn.LastPlayerPlaneShortId ? 0 : shot.Target;

            foreach (Member observer in members.Values)
            {
                if (observer == owner || !observer.Seen.Contains(peer) || observer.Connection.Status != NetConnectionStatus.Connected) continue;

                // GunFireSpecSerializer.Write, for a player's gun (isPlayer: mountId as a variable integer).
                NetOutgoingMessage m = observer.Server.CreateMessage();
                m.Write(MsgGunFire);
                m.Write((byte)((shot.Seed & 0x1F) | (target != 0 ? 64 : 0) | 128));
                m.WriteVariableInt32(owner.RelayId);
                m.Write(shotTime);
                WriteScaled(m, shot.X, 64f);
                WriteScaled(m, shot.Y, 64f);
                WriteScaled(m, shot.Rotation, 256f);
                m.WriteVariableUInt32(shot.Gun);
                WriteScaled(m, shot.GunRotation, 256f);
                m.WriteVariableInt32(shot.Counter);
                m.Write((byte)Math.Clamp(shot.Multiple, 0, 255));
                if (target != 0) m.WriteVariableInt32(target);
                observer.Server.SendMessage(m, observer.Connection, NetDeliveryMethod.ReliableOrdered, 0);
            }
        }
    }

    // ------------------------------------------------------------------ chat

    // ServerProximityChat(string text, short quickChatId): forwarded to everybody else on the server as ClientProximityChat.
    // The sender's own client already shows its message. Returns how many players received it.
    public static int Chat(long peer, string text, short quickChatId, Action<string, string> log)
    {
        lock (Gate)
        {
            if (!members.TryGetValue(peer, out Member? sender)) return 0;
            if (text.Length > MaxChatLength) text = text.Substring(0, MaxChatLength);
            if (string.IsNullOrWhiteSpace(text) && quickChatId == 0) return 0;

            // The sender's default flock goes with the line (the chat shows it next to the name).
            byte[]? flockId = null;
            string flockName = "";
            Accounts.Account? senderAccount = Accounts.FindById(PlayerData.ToBase64Url(sender.Account));
            if (senderAccount != null) SocialHandler.DefaultFlock(senderAccount, out flockId, out flockName);

            int delivered = 0;
            foreach (Member observer in members.Values)
            {
                if (observer == sender || observer.Room != sender.Room || observer.Connection.Status != NetConnectionStatus.Connected) continue;

                NetOutgoingMessage m = observer.Server.CreateMessage();
                m.Write(MsgProximityChat);
                m.Write(sender.RelayId != 0 && observer.Seen.Contains(peer) ? sender.RelayId : 0);   // senderShortId (0 = no bubble)
                WriteGeneralId(m, sender.Account);              // accountId
                m.Write(sender.Name);                           // userName
                m.Write(text);                                  // text
                WriteGeneralId(m, flockId);                     // flockId
                m.Write(flockName);                             // flockName
                m.Write(quickChatId);                           // quickChatId
                m.Write(false);                                 // showToSelf
                m.Write(sender.Rank);                           // rank
                m.Write(0f);                                    // offset (Vector3)
                m.Write(0f);
                m.Write(0f);
                observer.Server.SendMessage(m, observer.Connection, NetDeliveryMethod.ReliableOrdered, 0);
                delivered++;
            }

            log("CHAT", "[" + sender.Room + "] " + sender.Name + ": " + text + " -> " + delivered + " player(s)");
            ChatMonitor.Emit(log, "map", sender.Room, ChatMonitor.LabelOfRoom(sender.Room), sender.Name, "",
                string.IsNullOrWhiteSpace(text) ? "(quick chat #" + quickChatId + ")" : text);
            return delivered;
        }
    }

    // The room (map instance) this connection is in. Null for the tutorial and for unknown connections.
    public static string? RoomOf(NetConnection? connection)
    {
        if (connection == null) return null;
        lock (Gate) return members.TryGetValue(connection.RemoteUniqueIdentifier, out Member? member) ? member.Room : null;
    }

    // Where the live plane of this account is now (the last position its client reported). False when it has no plane in a room.
    public static bool PositionOfAccount(byte[] account, out float x, out float y, out float rotation)
    {
        x = 0f;
        y = 0f;
        rotation = 0f;
        lock (Gate)
        {
            Member? owner = members.Values.FirstOrDefault(m => m.RelayId != 0 && m.Account.SequenceEqual(account) &&
                                                               m.Connection.Status == NetConnectionStatus.Connected);
            if (owner == null) return false;
            x = owner.X;
            y = owner.Y;
            rotation = owner.Rotation;
            return true;
        }
    }

    // Name of the player whose plane has this relay id (what the other players see as its unit id), "" if none.
    public static string NameOfRelayId(int relayId)
    {
        if (relayId == 0) return "";
        lock (Gate)
        {
            return members.Values.FirstOrDefault(m => m.RelayId == relayId)?.Name ?? "";
        }
    }

    // The connection of the player whose plane the OTHER players of the room of "asker" know as relayId (the id the client of
    // the asker sees). Null when there is none in that room, or it is not connected.
    public static NetConnection? ConnectionByRelayId(NetConnection asker, int relayId)
    {
        if (relayId == 0) return null;
        lock (Gate)
        {
            if (!members.TryGetValue(asker.RemoteUniqueIdentifier, out Member? from)) return null;
            Member? owner = members.Values.FirstOrDefault(m => m.RelayId == relayId && m.Room == from.Room &&
                                                               m.Connection.Status == NetConnectionStatus.Connected);
            return owner?.Connection;
        }
    }

    // The id under which the OTHER players know the plane of this peer (0 = no plane or unknown).
    public static int RelayIdOf(long peer)
    {
        lock (Gate)
        {
            return members.TryGetValue(peer, out Member? m) ? m.RelayId : 0;
        }
    }

    // The other players of the room that already see the plane of this connection, with the id of that plane for them.
    // Used for things the owner's plane creates in the world (skill clouds): the others need them too.
    public static List<(NetConnection Connection, int OwnerId)> Observers(NetConnection? owner)
    {
        var result = new List<(NetConnection, int)>();
        if (owner == null) return result;
        lock (Gate)
        {
            if (!members.TryGetValue(owner.RemoteUniqueIdentifier, out Member? member) || member.RelayId == 0) return result;
            foreach (Member other in members.Values)
            {
                if (other == member || other.Room != member.Room || !other.Seen.Contains(member.Peer)) continue;
                if (other.Connection.Status != NetConnectionStatus.Connected) continue;
                result.Add((other.Connection, member.RelayId));
            }
        }
        return result;
    }

    // ------------------------------------------------------------------ data of other players

    // ServerRequestRemotePlayerData for ANOTHER account: answers when that account is online on this server.
    // The unit id is the relay id (the observer's id for that plane). False = unknown account (no answer).
    public static bool AnswerPlayerData(NetServer server, NetConnection requester, byte[] account, Action<string, string> log)
    {
        lock (Gate)
        {
            members.TryGetValue(requester.RemoteUniqueIdentifier, out Member? asking);
            Member? owner = members.Values.FirstOrDefault(m => m.Account.SequenceEqual(account) && (asking == null || m.Room == asking.Room));
            if (owner == null || owner.Connection.Status != NetConnectionStatus.Connected) return false;

            // The data (pilot, plane level and inventory) comes from the owner's account.
            using Accounts.Scope scope = Accounts.UseFor(owner.Connection);
            Accounts.SavedPlane? plane = owner.RelayId != 0 ? Accounts.GetPlane(owner.CharacterId) : null;

            // The owner's default flock: the name plate shows it and the radar colors flockmates.
            byte[]? flockId = null;
            string flockName = "";
            SocialHandler.DefaultFlock(Accounts.Current, out flockId, out flockName);

            NetOutgoingMessage m = server.CreateMessage();
            PlayerData.WriteClientReceiveRemotePlayerData(m, owner.RelayId, owner.Account, owner.Name, false,
                plane != null ? owner.CharacterId : null, plane != null ? owner.ClassId : null, owner.ServerName,
                plane?.Level ?? PlayerData.StartingPlaneLevel, Accounts.Pilot(), flockId, flockName);
            server.SendMessage(m, requester, NetDeliveryMethod.ReliableOrdered, 0);

            log("MSG", "data of '" + owner.Name + "' sent to another player (ClientReceiveRemotePlayerData, " +
                m.LengthBytes + " bytes, unit " + owner.RelayId + ")");
            return true;
        }
    }

    // ------------------------------------------------------------------ writers

    // SpawnUnitsBatch of the owner's plane for one observer (same layout as PlaneSpawn.WritePlaneSpawn, but with the owner's
    // peer as authority, the relay id and a velocity (0, 0): spawns without a velocity are extrapolated with the "unset" value).
    private static void SendSpawn(Member observer, Member owner, double now)
    {
        if (owner.RelayId == 0 || owner.CharacterBytes == null) return;

        NetOutgoingMessage options = observer.Server.CreateMessage();
        PlayerData.WritePlaneOptions(options, owner.NetClass, owner.NetVariant, owner.Account, owner.CharacterBytes, owner.Decoration);

        NetOutgoingMessage m = observer.Server.CreateMessage();
        m.Write(MsgSpawnUnitsBatch);
        m.WriteVariableUInt32(2u);                              // 1 item + 1
        m.Write((float)now);                                    // spawnTime
        m.WriteVariableInt32(PlaneSpawn.PlayerPlanePrefabId);
        m.WriteVariableInt32(owner.RelayId);
        m.Write(owner.Template);
        m.Write((short)0);                                      // familyRootTemplateHash
        m.WriteVariableInt32(0);                                // parentShortId
        m.WriteVariableInt32(PlaneSpawn.PlayerTeamIndex);
        m.Write((short)0);                                      // artReplacement
        m.Write(owner.Peer);                                    // authority = the owner, not the observer
        m.WriteVariableInt32(options.LengthBytes + 1);          // unitOptions (bytes + 1)
        m.Write(options.Data, 0, options.LengthBytes);          // never m.Write(options): loose bits would shift the rest
        m.WriteVariableUInt32(FieldPosition | FieldVelocity | FieldRotation | FieldName);
        WriteScaled(m, owner.X, 1024f);
        WriteScaled(m, owner.Y, 1024f);
        WriteScaled(m, 0f, 1024f);
        WriteScaled(m, 0f, 1024f);
        WriteScaled(m, owner.Rotation, 32f);
        m.Write(owner.Name);                                    // name plate: the client builds it from this field
        observer.Server.SendMessage(m, observer.Connection, NetDeliveryMethod.ReliableOrdered, 0);
        observer.Seen.Add(owner.Peer);
    }

    private static void WriteScaled(NetBuffer buf, float value, float scale)
    {
        buf.WriteVariableInt32((int)Math.Floor((double)value * scale));
    }

    // GeneralIDSerializer: two ulongs, big-endian halves of the 16 bytes (null = none).
    private static void WriteGeneralId(NetBuffer buf, byte[]? id16)
    {
        if (id16 == null || id16.Length < 16)
        {
            buf.Write(0UL);
            buf.Write(0UL);
            return;
        }
        buf.Write(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(id16.AsSpan(0, 8)));
        buf.Write(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(id16.AsSpan(8, 8)));
    }
}
