using Lidgren.Network;

// Chat commands of the open world: /world (go there), /worldinfo and, inside it, /carrier [dungeon] (a carrier next to you, to test).
public static partial class WorldServer
{
    public static bool Command(NetServer server, NetConnection? connection, string role, List<string> pieces, double now,
        Action<string, string> log)
    {
        if (connection == null || pieces.Count == 0) return false;
        string command = pieces[0].Trim().ToLowerInvariant();
        string? argument = pieces.Count > 1 ? string.Join(" ", pieces.Skip(1)).Trim() : null;
        bool inWorld = string.Equals(role, Role, StringComparison.OrdinalIgnoreCase);

        switch (command)
        {
            case "/world":
            {
                if (inWorld)
                {
                    WorldObjects.SendChat(server, connection, "You are already in the open world. /worldinfo tells where.");
                }
                else if (!Available(out string reason))
                {
                    WorldObjects.SendChat(server, connection, "The open world is not available: " + reason + ".");
                }
                else if (SwitchToWorld == null)
                {
                    WorldObjects.SendChat(server, connection, "No link to the open world server.");
                }
                else
                {
                    WorldObjects.SendChat(server, connection, "Going to the open world...");
                    SwitchToWorld(server, connection, "/world in the chat (" + role + ")");
                }
                log("COMMAND", "[" + role + "] " + string.Join(" ", pieces));
                return true;
            }

            case "/worldinfo":
            {
                lock (Game.Lock)
                {
                    Session? s = session;
                    if (!inWorld || s?.Map == null)
                    {
                        WorldObjects.SendChat(server, connection, Enabled
                            ? "The open world is built when the first player arrives. Go there with /world or the portal of the Nexus."
                            : "The open world is turned off in " + WorldSettingsFile.FileName + ".");
                        return true;
                    }
                    WorldObjects.SendChat(server, connection, "Open world: seed " + s.Seed + ", " + s.Members.Count + " player(s), " +
                        s.Sites.Count + " spawn site(s) (" + s.Sites.Count(x => x.Active) + " with enemies), " + s.Carriers.Count + " carrier(s).");
                    if (Game.GetPlane(connection.RemoteUniqueIdentifier, out float x, out float y, out _, out _))
                    {
                        WorldData.Region? region = s.Map.RegionOfPoint(x, y);
                        float distance = MathF.Sqrt(x * x + y * y);
                        WorldObjects.SendChat(server, connection, "You are at (" + x.ToString("0") + ", " + y.ToString("0") + "), " +
                            distance.ToString("0") + " units from the center, over " + (region != null ? region.ZoneName + " [" + region.Name + ", tier " + region.Tier + "]" : "water or a canyon") +
                            ", elevation " + WorldData.ElevationAt(distance).ToString("0") + ".");
                    }
                    foreach (Carrier carrier in s.Carriers.Values)
                    {
                        WorldObjects.SendChat(server, connection, "Carrier: " + carrier.Entry.Label + " at (" + carrier.X.ToString("0") + ", " +
                            carrier.Y.ToString("0") + "), " + Math.Max(0, (int)(carrier.EndsAt - now)) + " s left.");
                    }
                }
                return true;
            }

            case "/carrier":
            {
                if (!inWorld)
                {
                    WorldObjects.SendChat(server, connection, "/carrier only works inside the open world.");
                    return true;
                }
                lock (Game.Lock)
                {
                    Session? s = session;
                    if (s?.Map == null) return true;
                    DungeonCatalog.Entry? entry = string.IsNullOrEmpty(argument) ? null : DungeonCatalog.Find(argument);
                    if (!string.IsNullOrEmpty(argument) && (entry == null || !entry.IsOpen(DateTime.Now, out _)))
                    {
                        WorldObjects.SendChat(server, connection, "There is no open dungeon called '" + argument + "'. List: /dungeons");
                        return true;
                    }
                    if (!Game.GetPlane(connection.RemoteUniqueIdentifier, out float px, out float py, out _, out _))
                    {
                        WorldObjects.SendChat(server, connection, "Wait for your plane to spawn.");
                        return true;
                    }
                    // Next to the player (free land), so it can be tested at once.
                    WorldSettings.CarrierSettings settings = WorldSettingsFile.Current.Carriers;
                    entry ??= DungeonCatalog.All().FirstOrDefault(d => d.IsOpen(DateTime.Now, out _));
                    if (entry == null)
                    {
                        WorldObjects.SendChat(server, connection, "No dungeon is open (dungeons.json).");
                        return true;
                    }
                    float cx = px, cy = py;
                    for (int attempt = 0; attempt < 60; attempt++)
                    {
                        double angle = random.NextDouble() * Math.PI * 2.0;
                        float distance = 30f + (float)random.NextDouble() * 30f;
                        float tx = px + (float)Math.Sin(angle) * distance;
                        float ty = py + (float)Math.Cos(angle) * distance;
                        if (!s.Map.IsOpenLand(tx, ty, 14f)) continue;
                        cx = tx;
                        cy = ty;
                        break;
                    }
                    var carrier = new Carrier
                    {
                        Id = WorldObjects.NewShortId(),
                        Entry = entry,
                        X = cx,
                        Y = cy,
                        EndsAt = now + settings.LifetimeSeconds,
                        RemoveAt = now + settings.LifetimeSeconds + LeaveDelay
                    };
                    s.Carriers[carrier.Id] = carrier;
                    foreach (NetConnection member in Connected(s)) SendCarrier(s, member, carrier, now);
                    Chat(s, "A carrier of " + entry.Label + " appeared near " + PlayerSessions.NameOf(connection) + ".");
                    log("WORLD", "[world] /carrier: " + entry.Label + " (" + carrier.Id + ")");
                }
                return true;
            }
        }
        return false;
    }
}
