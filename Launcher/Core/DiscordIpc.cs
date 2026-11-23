using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SteambirdsLauncher.Core;

// Talks to the Discord desktop app through its local pipe (\\.\pipe\discord-ipc-0 ... 9), the transport of Discord's RPC.
// No SDK and no DLL: 8-byte header (opcode, length; both little-endian uint32) + UTF-8 JSON.
//   handshake  opcode 0  {"v":1,"client_id":"<application id>"}   -> Discord answers a frame with READY
//   command    opcode 1  {"cmd":"SET_ACTIVITY","args":{"pid":N,"activity":{...}},"nonce":"..."}
// Everything is local: nothing goes to the network from here, Discord itself publishes the activity.
// Not thread safe: DiscordPresence calls it from one loop.
internal sealed class DiscordIpc : IDisposable
{
    private const int OpHandshake = 0;
    private const int OpFrame = 1;
    private const int OpClose = 2;
    private const int OpPing = 3;
    private const int OpPong = 4;
    private const int MaxPayload = 64 * 1024;

    private NamedPipeClientStream? pipe;
    private string connectedApplication = "";

    public bool Connected => pipe is { IsConnected: true };

    // Why the last Connect/SetActivity failed (shown in Settings); "" = fine.
    public string Error { get; private set; } = "";

    // Opens the pipe and does the handshake. false: Discord is not running (or refused the application id; see Error).
    public async Task<bool> ConnectAsync(string applicationId, CancellationToken cancel)
    {
        if (Connected && connectedApplication == applicationId) return true;
        Close();
        Error = "";

        for (int i = 0; i < 10; i++)
        {
            var candidate = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await candidate.ConnectAsync(250, cancel);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                candidate.Dispose();
                throw;
            }
            catch
            {
                candidate.Dispose();
                continue;
            }

            pipe = candidate;
            try
            {
                var handshake = new JsonObject { ["v"] = 1, ["client_id"] = applicationId };
                await WriteFrameAsync(OpHandshake, handshake.ToJsonString(), cancel);
                (int op, string json) = await ReadFrameAsync(cancel);
                if (op == OpFrame && IsEvent(json, "READY"))
                {
                    connectedApplication = applicationId;
                    return true;
                }
                Error = DescribeError(json, op);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                Close();
                throw;
            }
            catch (Exception e)
            {
                Error = e.Message;
            }
            Close();
            if (Error.Length > 0) return false; // Discord answered and said no: another pipe will not change it
        }

        Error = L.T("Discord is not running on this PC.");
        return false;
    }

    // activity null = removes the activity of the application from the profile.
    public async Task<bool> SetActivityAsync(JsonObject? activity, CancellationToken cancel)
    {
        if (!Connected) return false;
        try
        {
            var args = new JsonObject { ["pid"] = Environment.ProcessId, ["activity"] = activity };
            var command = new JsonObject
            {
                ["cmd"] = "SET_ACTIVITY",
                ["args"] = args,
                ["nonce"] = Guid.NewGuid().ToString("N")
            };
            await WriteFrameAsync(OpFrame, command.ToJsonString(), cancel);
            (int op, string json) = await ReadFrameAsync(cancel);
            if (op == OpClose)
            {
                Error = DescribeError(json, op);
                Close();
                return false;
            }
            if (IsEvent(json, "ERROR"))
            {
                Error = DescribeError(json, op);
                return false;
            }
            Error = "";
            return true;
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            Close();
            throw;
        }
        catch (Exception e)
        {
            // Discord closed: the loop connects again at the next turn.
            Error = e.Message;
            Close();
            return false;
        }
    }

    // ------------------------------------------------------------------ frames

    private async Task WriteFrameAsync(int opcode, string json, CancellationToken cancel)
    {
        byte[] payload = Encoding.UTF8.GetBytes(json);
        byte[] frame = new byte[8 + payload.Length];
        BitConverter.GetBytes((uint)opcode).CopyTo(frame, 0);
        BitConverter.GetBytes((uint)payload.Length).CopyTo(frame, 4);
        payload.CopyTo(frame, 8);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(3000);
        await pipe!.WriteAsync(frame, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
    }

    // Reads the next frame, answering pings.
    private async Task<(int Opcode, string Json)> ReadFrameAsync(CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(3000);
        for (int turn = 0; turn < 8; turn++)
        {
            byte[] header = new byte[8];
            await ReadExactlyAsync(header, timeout.Token);
            int opcode = (int)BitConverter.ToUInt32(header, 0);
            int length = (int)BitConverter.ToUInt32(header, 4);
            if (length < 0 || length > MaxPayload) throw new InvalidDataException("Discord sent a frame that is too long.");
            byte[] body = new byte[length];
            await ReadExactlyAsync(body, timeout.Token);
            string json = Encoding.UTF8.GetString(body);
            if (opcode == OpPing)
            {
                await WriteFrameAsync(OpPong, json, timeout.Token);
                continue;
            }
            return (opcode, json);
        }
        throw new InvalidDataException("Discord did not answer.");
    }

    private async Task ReadExactlyAsync(byte[] buffer, CancellationToken cancel)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await pipe!.ReadAsync(buffer.AsMemory(read), cancel);
            if (n == 0) throw new EndOfStreamException("Discord closed the connection.");
            read += n;
        }
    }

    private static bool IsEvent(string json, string name)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            return document.RootElement.TryGetProperty("evt", out JsonElement evt) && evt.ValueKind == JsonValueKind.String &&
                   evt.GetString() == name;
        }
        catch
        {
            return false;
        }
    }

    // {"evt":"ERROR","data":{"code":4000,"message":"..."}} or a CLOSE frame {"code":4000,"message":"..."}.
    private static string DescribeError(string json, int opcode)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Object) root = data;
            string message = root.TryGetProperty("message", out JsonElement m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
            string code = root.TryGetProperty("code", out JsonElement c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32().ToString() : "";
            if (message.Length > 0 || code.Length > 0) return L.F("Discord refused it ({0}): {1}", code.Length > 0 ? code : opcode.ToString(), message);
        }
        catch
        {
        }
        return L.F("Discord refused it (opcode {0}).", opcode);
    }

    private void Close()
    {
        try
        {
            pipe?.Dispose();
        }
        catch
        {
        }
        pipe = null;
        connectedApplication = "";
    }

    public void Dispose() => Close();
}
