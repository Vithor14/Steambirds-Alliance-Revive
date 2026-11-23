using System.Net;
using Lidgren.Network;

// Program (part): one UDP server per role (Nexus, tutorial, dungeon) and its update loop.

public static partial class Program
{
    static void StartUdp(int port, byte role)
    {
        var cfg = new NetPeerConfiguration(AppId) { Port = port, MaximumConnections = 128 };
        cfg.EnableMessageType(NetIncomingMessageType.ConnectionApproval);
        cfg.EnableMessageType(NetIncomingMessageType.StatusChanged);
        cfg.EnableMessageType(NetIncomingMessageType.Data);
        cfg.EnableMessageType(NetIncomingMessageType.DebugMessage);
        cfg.EnableMessageType(NetIncomingMessageType.WarningMessage);
        cfg.EnableMessageType(NetIncomingMessageType.ErrorMessage);

        var server = new NetServer(cfg);
        server.Start();
        Log("UDP", $"[{RoleName(role)}] listening on port {port}");

        new Thread(() =>
        {
            while (true)
            {
                NetIncomingMessage? msg;
                while ((msg = server.ReadMessage()) != null)
                {
                    switch (msg.MessageType)
                    {
                        case NetIncomingMessageType.ConnectionApproval:
                        {
                            var hail = msg.ReadBytes(msg.LengthBytes);
                            IPAddress remote = PlayerSessions.Normalize(msg.SenderEndPoint.Address);
                            // network.json "allowOtherPlayers": false -> only this PC's game gets in.
                            if (!ServerNetworkSettingsFile.OpenToOtherPcs && !PlayerSessions.IsThisPc(remote))
                            {
                                Log("UDP", $"[{RoleName(role)}] connection from {msg.SenderEndPoint} refused: network.json allowOtherPlayers = false");
                                msg.SenderConnection.Deny("This server only accepts players from its own PC");
                                break;
                            }
                            Log("UDP", $"[{RoleName(role)}] approval from {msg.SenderEndPoint} hail={Convert.ToHexString(hail)}");
                            var response = server.CreateMessage();
                            response.Write(ProtocolVersion);
                            msg.SenderConnection.Approve(response);
                            break;
                        }
                        case NetIncomingMessageType.StatusChanged:
                        {
                            var status = (NetConnectionStatus)msg.ReadByte();
                            Log("UDP", $"[{RoleName(role)}] status {msg.SenderEndPoint}: {status} ({msg.ReadString()})");

                            // Connection closed: the session summary comes out and the player's state is discarded.
                            if (status == NetConnectionStatus.Disconnected)
                            {
                                var connection = msg.SenderConnection;
                                PlayerSession? whoLeft = PlayerSessions.Of(connection);
                                using (Accounts.UseFor(connection))
                                {
                                    PlayerRelay.Left(connection);
                                    SocialHandler.PlayerLeft(connection);
                                    Game.Disconnected(connection, Log);
                                    Enemies.Disconnected(connection, Log);
                                    Tutorial.Disconnected(connection, Log);
                                    DungeonServer.Disconnected(connection, Log);
                                    WorldServer.Disconnected(connection, Log);
                                    PilotProgression.Disconnected(connection);
                                    LootDrops.Disconnected(connection);
                                }
                                PlayerSessions.Unbind(connection);
                                // Only the Nexus thread touches this set.
                                if (role == RoleNexus && connection != null)
                                    WelcomedInNexus.Remove(connection.RemoteUniqueIdentifier);
                                if (whoLeft != null)
                                {
                                    bool stillOnline = PlayerSessions.IsOnline(whoLeft.Account, null);
                                    Log("PLAYER", $"{whoLeft.Name} left the {RoleName(role)}" +
                                        (stillOnline ? " (still online on another server)" : $"; {PlayerSessions.OnlineCount()} player(s) online"));
                                    if (!stillOnline && role == RoleNexus)
                                        TellOthers(server, connection, $"{whoLeft.Name} left the server.");
                                }
                            }
                            break;
                        }
                        case NetIncomingMessageType.Data:
                        {
                            // An error in a handler becomes a log line; the UDP thread keeps going.
                            var received = msg;
                            MessageLog.RunSafe($"data from {received.SenderEndPoint} [{RoleName(role)}]",
                                () => HandleData(server, received, role), t => Log("ERROR", t));
                            break;
                        }
                        case NetIncomingMessageType.DebugMessage:
                        case NetIncomingMessageType.WarningMessage:
                        case NetIncomingMessageType.ErrorMessage:
                            Log("UDP", $"{msg.MessageType}: {msg.ReadString()}");
                            break;
                    }
                    server.Recycle(msg);
                }

                // Enemies of this server: AI, shots, waves and UpdateUnitsBatch (20 times per second).
                Enemies.Update(server, RoleName(role), Clock.Elapsed.TotalSeconds, Log);

                // Tutorial script: rooms, gates, boss, crates and the portal to the Nexus.
                if (role == RoleTutorial)
                    Tutorial.Update(server, Clock.Elapsed.TotalSeconds, Log);

                // Dungeon: rooms, gates, bosses, exit (DungeonServer).
                if (role == RoleDungeon)
                    DungeonServer.Update(server, Clock.Elapsed.TotalSeconds, Log);

                // Open world: regions, enemies near the players and the dungeon carriers (WorldServer).
                if (role == RoleWorld)
                    WorldServer.Update(server, Clock.Elapsed.TotalSeconds, Log);

                // Progression: players near each plane (ClientReceiveReliableStats), once per second.
                PilotProgression.Update(server, Clock.Elapsed.TotalSeconds);

                // Loot bags whose time ran out leave the world.
                LootDrops.Update(server, Clock.Elapsed.TotalSeconds);

                Thread.Sleep(5);
            }
        }) { IsBackground = true }.Start();
    }
}
