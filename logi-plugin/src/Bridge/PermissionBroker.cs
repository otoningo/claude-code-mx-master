namespace Loupedeck.ClaudeCodePlugin.Bridge;

using System.Text.Json.Nodes;

// A permission request from Claude Code that is waiting for an answer from the mouse.
public sealed class PendingPermission
{
    public string Id { get; init; }
    public string SessionId { get; init; }
    public string ToolName { get; init; }
    public string Summary { get; init; }
    public DateTime ReceivedUtc { get; init; }
    internal TaskCompletionSource<JsonObject> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}

// Queue of pending PermissionRequests. The HTTP hook holds Claude Code's request until the
// user answers with the mouse (approve / approve all / deny / stop) or the wait time runs out;
// in that case it returns no decision and Claude Desktop shows its usual dialog.
public sealed class PermissionBroker
{
    public static PermissionBroker Instance { get; } = new();

    public const string DenyMessage = "The user denied this request from their mouse.";
    public const string StopMessage = "The user stopped this from their mouse.";

    private readonly List<PendingPermission> _pending = new();
    private readonly object _lock = new();

    public event Action Changed;

    public IReadOnlyList<PendingPermission> Snapshot()
    {
        lock (this._lock)
        {
            return this._pending.ToList();
        }
    }

    public PendingPermission Oldest
    {
        get
        {
            lock (this._lock)
            {
                return this._pending.FirstOrDefault();
            }
        }
    }

    public bool HasPending(string sessionId = null)
    {
        lock (this._lock)
        {
            return sessionId == null ? this._pending.Count > 0 : this._pending.Any(p => p.SessionId == sessionId);
        }
    }

    // Waits for the answer. Returns null when the time runs out (= no decision).
    public async Task<JsonObject> WaitForDecisionAsync(JsonObject hookInput, TimeSpan hold, CancellationToken cancellationToken = default)
    {
        var pending = new PendingPermission
        {
            Id = Guid.NewGuid().ToString("N")[..8],
            SessionId = (string)hookInput["session_id"],
            ToolName = (string)hookInput["tool_name"] ?? "?",
            Summary = Summarize(hookInput),
            ReceivedUtc = DateTime.UtcNow,
        };

        lock (this._lock)
        {
            this._pending.Add(pending);
        }

        this.Changed?.Invoke();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(hold);
            var finished = await Task.WhenAny(pending.Answer.Task, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false);
            return finished == pending.Answer.Task ? await pending.Answer.Task.ConfigureAwait(false) : null;
        }
        finally
        {
            lock (this._lock)
            {
                this._pending.Remove(pending);
            }

            this.Changed?.Invoke();
        }
    }

    // Answers the oldest request (or the one with that id). Returns false if nothing was pending.
    public bool TryDecide(string decision, string id = null)
    {
        PendingPermission target;
        lock (this._lock)
        {
            target = id == null ? this._pending.FirstOrDefault() : this._pending.FirstOrDefault(p => p.Id == id);
        }

        return target != null && target.Answer.TrySetResult(BuildDecision(decision));
    }

    // Response format documented for PermissionRequest hooks (hookSpecificOutput.decision).
    public static JsonObject BuildDecision(string decision)
    {
        var body = decision switch
        {
            "approve" => new JsonObject { ["behavior"] = "allow" },
            "approve-all" => new JsonObject
            {
                ["behavior"] = "allow",
                ["updatedPermissions"] = new JsonArray(new JsonObject
                {
                    ["type"] = "setMode",
                    ["mode"] = "acceptEdits",
                    ["destination"] = "session",
                }),
            },
            "deny" => new JsonObject { ["behavior"] = "deny", ["message"] = DenyMessage },
            "stop" => new JsonObject { ["behavior"] = "deny", ["message"] = StopMessage, ["interrupt"] = true },
            _ => throw new ArgumentException($"Unknown decision: {decision}"),
        };

        return new JsonObject
        {
            ["hookSpecificOutput"] = new JsonObject
            {
                ["hookEventName"] = "PermissionRequest",
                ["decision"] = body,
            },
        };
    }

    private static string Summarize(JsonObject input)
    {
        var toolInput = input["tool_input"] as JsonObject;
        var detail = (string)toolInput?["command"] ?? (string)toolInput?["file_path"] ?? (string)toolInput?["url"] ?? string.Empty;
        detail = detail.ReplaceLineEndings(" ");
        return detail.Length > 60 ? detail[..57] + "..." : detail;
    }
}
