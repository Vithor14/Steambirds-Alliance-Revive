using System.Net;
using System.Net.Sockets;
using System.Text.Json;

// Program (part): the HTTP (Ozy) side: login, session, client uploads and the server info for the launchers.

public static partial class Program
{
    static string? JsonText(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // Address of this server as the player's game knows it: the host of the OZY_URL it used (typed IP, found in the search
    // or "localhost"). It is the one that goes in the login "procs" and in every server switch.
    static IPAddress ServerAddressForClient(HttpContext ctx)
    {
        string host = ctx.Request.Host.Host.Trim('[', ']');
        if (IPAddress.TryParse(host, out IPAddress? ip))
        {
            ip = PlayerSessions.Normalize(ip);
            if (ip.AddressFamily == AddressFamily.InterNetwork && !ip.Equals(IPAddress.Any)) return ip;
            if (IPAddress.IsLoopback(ip)) return IPAddress.Loopback;
        }
        else if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }
        else if (host.Length > 0)
        {
            try
            {
                IPAddress? resolved = Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
                if (resolved != null) return resolved;
            }
            catch
            {
            }
        }
        IPAddress? local = ctx.Connection.LocalIpAddress;
        if (local != null)
        {
            local = PlayerSessions.Normalize(local);
            if (local.AddressFamily == AddressFamily.InterNetwork && !local.Equals(IPAddress.Any)) return local;
        }
        IPAddress? client = ctx.Connection.RemoteIpAddress;
        return client != null ? PlayerSessions.LocalAddressToward(client) : IPAddress.Loopback;
    }

    // Ozy account answer: the Nexus proc points to the address the game used to reach it.
    static object AccountResponse(PlayerSession session)
    {
        ServerNetworkSettings network = ServerNetworkSettingsFile.Current;
        return new
        {
            username = session.Name,
            userid = session.Account.IdText,
            sessid = session.Ticket,
            procs = new[]
            {
                new
                {
                    ips = new[] { session.ServerAddress.ToString() },
                    port = NexusPort,
                    procid = "revive-1",
                    role = "Nexus",
                    scenario = "",
                    meta = "",
                    region = "UN.UN",
                    procname = network.ServerName,
                    started_by = "",
                    players_connected = PlayerSessions.OnlineCount(),
                    players_family = -1,
                    players_max = Math.Max(1, network.MaxPlayers),
                    is_closing = false,
                    is_ready = true,
                    protocol_version = ProtocolVersion,
                    peerid = "1"
                }
            }
        };
    }

    // POST /ozy/user/login: who the player is (Steam ID from the ticket), their account, their name and a new session.
    static object Join(HttpContext ctx, string body, string? launcherName)
    {
        string? steamTicket = null, guid = null, bodyUser = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            steamTicket = JsonText(doc.RootElement, "steam_ticket");
            guid = JsonText(doc.RootElement, "session_guid");
            bodyUser = JsonText(doc.RootElement, "username");
        }
        catch
        {
            // Empty or invalid body: account by IP.
        }

        IPAddress? client = ctx.Connection.RemoteIpAddress is IPAddress remote ? PlayerSessions.Normalize(remote) : null;
        var (key, steamId, how) = SteamTicket.AccountKey(steamTicket, guid, client?.ToString());
        bool fromThisPc = PlayerSessions.IsThisPc(client);
        string name = PlayerNamePath.Clean(launcherName ?? bodyUser);

        Accounts.Account account = Accounts.GetOrCreate(key, name, steamId, out string note);
        if (name.Length == 0)
        {
            // No name in the launcher (game opened from outside): the name saved in the account, or "Pilot" + the end of the Steam ID.
            name = account.SavedName.Length > 0 ? account.SavedName
                : "Pilot " + (steamId != null && steamId.Length >= 4 ? steamId.Substring(steamId.Length - 4) : key.Substring(Math.Max(0, key.Length - 4)));
            Accounts.GetOrCreate(key, name, steamId, out _);
        }
        PrepareAccount(account);

        IPAddress serverAddress = ServerAddressForClient(ctx);
        PlayerSession session = PlayerSessions.Create(account, name, serverAddress, client);
        Log("LOGIN", $"{name} ({how}{(fromThisPc ? ", this PC" : ", from " + client)}) -> account {key}" +
            $"{(note.Length > 0 ? " (" + note + ")" : "")}, Nexus at {serverAddress}:{NexusPort}" +
            $"{(launcherName != null ? ", launcher name" : "")}");
        return AccountResponse(session);
    }

    // /ozy/user/self and /ozy/user/queue_check: the account of the X-Session header's session (or the last login of the same IP).
    static object SessionAccount(HttpContext ctx, string body, string? launcherName)
    {
        string ticket = ctx.Request.Headers["X-Session"].ToString();
        IPAddress? client = ctx.Connection.RemoteIpAddress is IPAddress remote ? PlayerSessions.Normalize(remote) : null;
        PlayerSession? session = PlayerSessions.ByTicket(ticket) ?? PlayerSessions.LatestFrom(client);
        return session != null ? AccountResponse(session) : Join(ctx, body, launcherName);
    }

    // Server info for the launchers (GET /revive/info and the answer of the server search).
    static Dictionary<string, object?> ServerInfo()
    {
        ServerNetworkSettings network = ServerNetworkSettingsFile.Current;
        List<string> names = PlayerSessions.OnlineNames();
        return new Dictionary<string, object?>
        {
            ["game"] = "steambirds-revive",
            ["infoVersion"] = 2,
            ["name"] = network.ServerName,
            ["build"] = BuildVersion,
            ["protocol"] = ProtocolVersion,
            ["players"] = names.Count,
            ["maxPlayers"] = Math.Max(1, network.MaxPlayers),
            ["playerNames"] = names,
            ["playerDetails"] = PlayerStatus.Describe(),
            ["httpPort"] = HttpPort,
            ["nexusPort"] = NexusPort,
            ["openToOtherPcs"] = ServerNetworkSettingsFile.OpenToOtherPcs,
            ["acceptsPlayerName"] = true,
            ["accountsPerPlayer"] = true
        };
    }

    // Slot for a client upload: the file name joins the kind (errorlog, bug, death) and the time.
    const string UploadPrefix = "/ozy/user_uploads/put/";
    const string UploadsFolder = "client-uploads";
    static object UploadSlot(HttpContext ctx, string body)
    {
        var type = "upload";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("path", out var p) && p.GetString() is { Length: > 0 } s) type = s;
        }
        catch { }
        type = new string(type.Where(char.IsLetterOrDigit).ToArray());
        if (type.Length == 0) type = "upload";
        var id = $"{type}{DateTime.Now:yyyyMMddHHmmss}{Random.Shared.Next(1000, 9999)}";
        // The same address the game used to reach it (another PC does not reach the server's localhost).
        var baseUrl = $"http://{ctx.Request.Host}";
        return new
        {
            upload_url = $"{baseUrl}{UploadPrefix}{id}",
            finish_url = $"{baseUrl}/ozy/user_uploads/finish/{id}",
            download_url = $"{baseUrl}/{UploadsFolder}/{id}",
            uploadid = id
        };
    }
    static string UploadExtension(string? mimeType) => (mimeType ?? "") switch
    {
        var t when t.StartsWith("image/gif") => ".gif",
        var t when t.StartsWith("image/png") => ".png",
        var t when t.StartsWith("image/jpeg") => ".jpg",
        _ => ".txt"
    };

    // The HTTP (Ozy) API: login, session, uploads and the launcher's server info.
    private static void MapHttp(WebApplication app)
    {
        app.MapFallback(async ctx =>
        {
            var req = ctx.Request;

            // Player name chosen in the launcher: OZY_URL = http://<ip>:<port>/n/<name in hex>/ozy (Network/PlayerNamePath).
            var httpPath = PlayerNamePath.Split(req.Path.Value ?? "", out string? launcherName);

            // Client uploads (the error log the game sends by itself when something breaks, and the bug/death
            // GIFs): the client asks for a slot at /ozy/user_uploads/upload, PUTs the file to the
            // upload_url and then POSTs to the finish_url. Without a valid URL "Curl error 3: malformed" showed up.
            // The file goes to the client-uploads folder, without passing through the server log.
            if (httpPath.StartsWith(UploadPrefix))
            {
                var name = new string(httpPath.Substring(UploadPrefix.Length).Where(char.IsLetterOrDigit).ToArray());
                if (name.Length == 0) name = "upload";
                Directory.CreateDirectory(UploadsFolder);
                var destination = Path.Combine(UploadsFolder, name + UploadExtension(req.ContentType));
                await using (var fileName = File.Create(destination)) await req.Body.CopyToAsync(fileName);
                Log("HTTP", $"client upload saved at {Path.GetFullPath(destination)} ({new FileInfo(destination).Length} bytes)");
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{}");
                return;
            }

            // Server info for the launcher (Connect by IP and the server list).
            if (httpPath == "/revive/info")
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync(JsonSerializer.Serialize(ServerInfo()));
                return;
            }

            using var reader = new StreamReader(req.Body);
            var body = await reader.ReadToEndAsync();
            // Tickets and passwords come out redacted in the log.
            Log("HTTP", $"{req.Method} {httpPath}{req.QueryString} from {ctx.Connection.RemoteIpAddress} body={MessageLog.Redact(body)}");

            object response = httpPath switch
            {
                "/ozy/maintenance_info" => new { is_active = false, is_planned = false, timestamp_planned = 0, message = "" },
                "/ozy/alive" => new { ok = true },
                "/ozy/user/login" => Join(ctx, body, launcherName),
                "/ozy/user/self" or "/ozy/user/queue_check" => SessionAccount(ctx, body, launcherName),
                "/ozy/user_uploads/upload" => UploadSlot(ctx, body),
                _ => new { }
            };

            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(response));
        });
    }
}
