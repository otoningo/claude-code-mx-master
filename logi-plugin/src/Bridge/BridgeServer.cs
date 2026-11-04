namespace Loupedeck.ClaudeCodePlugin.Bridge;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

// Minimal HTTP server on 127.0.0.1 that receives Claude Code's HTTP hooks
// (https://code.claude.com/docs/en/hooks) and exposes a small API for the MCP server.
// Uses TcpListener instead of HttpListener so it needs neither admin rights nor URL ACLs on Windows.
//
//   POST /claude-mx/hooks/<Event>   Claude Code hooks (X-Claude-MX-Token header)
//   GET  /claude-mx/api/status      pending requests
//   POST /claude-mx/api/decide      {"decision": "approve|approve-all|deny|stop", "id"?: "..."}
public sealed class BridgeServer : IDisposable
{
    public const string TokenHeader = "X-Claude-MX-Token";
    public const string HooksPath = "/claude-mx/hooks/";
    private const int MaxBody = 1 << 20;

    private readonly int _port;
    private readonly string _token;
    private readonly Func<TimeSpan> _hold;
    private readonly Func<JsonObject, string> _hapticFor;
    private readonly Action<string> _raiseHaptic;
    private readonly PermissionBroker _broker;
    private readonly CancellationTokenSource _cts = new();
    private TcpListener _listener;

    public BridgeServer(int port, string token, Func<TimeSpan> hold, Func<JsonObject, string> hapticFor, Action<string> raiseHaptic, PermissionBroker broker = null)
    {
        this._port = port;
        this._token = token;
        this._hold = hold;
        this._hapticFor = hapticFor;
        this._raiseHaptic = raiseHaptic;
        this._broker = broker ?? PermissionBroker.Instance;
    }

    public int Port => ((IPEndPoint)this._listener?.LocalEndpoint)?.Port ?? this._port;

    public void Start()
    {
        this._listener = new TcpListener(IPAddress.Loopback, this._port);
        this._listener.Start();
        _ = Task.Run(() => this.AcceptLoopAsync(this._cts.Token));
    }

    public void Dispose()
    {
        this._cts.Cancel();
        try
        {
            this._listener?.Stop();
        }
        catch
        {
            // already stopped
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await this._listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            _ = Task.Run(() => this.HandleAsync(client, ct));
        }
    }

    private async Task HandleAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream, ct).ConfigureAwait(false);
                if (request == null)
                {
                    return;
                }

                var (status, body) = await this.RouteAsync(request, ct).ConfigureAwait(false);
                await WriteResponseAsync(stream, status, body, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PluginLog.Warning(ex, "Bridge: request failed");
            }
        }
    }

    private async Task<(int Status, string Body)> RouteAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.Headers.TryGetValue(TokenHeader, out var token) || !FixedTimeEquals(token, this._token))
        {
            return (401, "{\"error\":\"unauthorized\"}");
        }

        JsonObject json = null;
        if (request.Body.Length > 0)
        {
            try
            {
                json = JsonNode.Parse(request.Body) as JsonObject;
            }
            catch
            {
                return (400, "{\"error\":\"invalid json\"}");
            }
        }

        if (request.Method == "POST" && request.Path.StartsWith(HooksPath, StringComparison.Ordinal))
        {
            return (200, await this.HandleHookAsync(request.Path[HooksPath.Length..], json ?? new JsonObject(), ct).ConfigureAwait(false));
        }

        if (request.Method == "GET" && request.Path == "/claude-mx/api/status")
        {
            var list = new JsonArray();
            foreach (var p in this._broker.Snapshot())
            {
                list.Add(new JsonObject
                {
                    ["id"] = p.Id,
                    ["tool"] = p.ToolName,
                    ["summary"] = p.Summary,
                    ["sessionId"] = p.SessionId,
                    ["waitingSeconds"] = (int)(DateTime.UtcNow - p.ReceivedUtc).TotalSeconds,
                });
            }

            return (200, new JsonObject { ["ok"] = true, ["pending"] = list }.ToJsonString());
        }

        if (request.Method == "POST" && request.Path == "/claude-mx/api/decide")
        {
            var decision = (string)json?["decision"];
            if (decision is not ("approve" or "approve-all" or "deny" or "stop"))
            {
                return (400, "{\"error\":\"decision must be approve, approve-all, deny or stop\"}");
            }

            var decided = this._broker.TryDecide(decision, (string)json?["id"]);
            return (200, new JsonObject { ["ok"] = decided, ["pending"] = decided }.ToJsonString());
        }

        return (404, "{\"error\":\"not found\"}");
    }

    private async Task<string> HandleHookAsync(string hookEvent, JsonObject input, CancellationToken ct)
    {
        input["hook_event_name"] ??= hookEvent;
        var haptic = this._hapticFor(input);

        if (hookEvent == "PermissionRequest")
        {
            if (haptic != null)
            {
                this._raiseHaptic(haptic);
            }

            var decision = await this._broker.WaitForDecisionAsync(input, this._hold(), ct).ConfigureAwait(false);
            return decision?.ToJsonString() ?? "{}";
        }

        // The permission_prompt notification arrives ~6 s after PermissionRequest; don't vibrate twice.
        var isPermissionNotice = hookEvent == "Notification" && (string)input["notification_type"] == "permission_prompt";
        if (haptic != null && !(isPermissionNotice && this._broker.HasPending((string)input["session_id"])))
        {
            this._raiseHaptic(haptic);
        }

        return "{}";
    }

    private static bool FixedTimeEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a ?? string.Empty), Encoding.UTF8.GetBytes(b ?? string.Empty));

    private sealed record HttpRequest(string Method, string Path, Dictionary<string, string> Headers, string Body);

    private static async Task<HttpRequest> ReadRequestAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int headerEnd = -1;
        while (headerEnd < 0)
        {
            var read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
            headerEnd = IndexOf(buffer.GetBuffer(), (int)buffer.Length, "\r\n\r\n"u8);
            if (buffer.Length > 64 * 1024 && headerEnd < 0)
            {
                return null;
            }
        }

        var all = buffer.ToArray();
        var headerText = Encoding.ASCII.GetString(all, 0, headerEnd);
        var lines = headerText.Split("\r\n");
        var requestLine = lines[0].Split(' ');
        if (requestLine.Length < 2)
        {
            return null;
        }

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
            {
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
            }
        }

        var length = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var n) ? Math.Clamp(n, 0, MaxBody) : 0;
        var body = new byte[length];
        var already = Math.Min(all.Length - (headerEnd + 4), length);
        Array.Copy(all, headerEnd + 4, body, 0, already);
        var offset = already;
        while (offset < length)
        {
            var read = await stream.ReadAsync(body.AsMemory(offset, length - offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        var path = requestLine[1];
        var q = path.IndexOf('?');
        return new HttpRequest(requestLine[0].ToUpperInvariant(), q >= 0 ? path[..q] : path, headers, Encoding.UTF8.GetString(body, 0, offset));
    }

    private static int IndexOf(byte[] data, int length, ReadOnlySpan<byte> pattern) =>
        data.AsSpan(0, length).IndexOf(pattern);

    private static async Task WriteResponseAsync(NetworkStream stream, int status, string body, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(body);
        var reason = status switch { 200 => "OK", 400 => "Bad Request", 401 => "Unauthorized", _ => "Not Found" };
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\nContent-Type: application/json\r\nContent-Length: {payload.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct).ConfigureAwait(false);
        await stream.WriteAsync(payload, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
