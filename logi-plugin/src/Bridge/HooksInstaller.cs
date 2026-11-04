namespace Loupedeck.ClaudeCodePlugin.Bridge;

using System.Text.Json;
using System.Text.Json.Nodes;

// Adds (or removes) this plugin's HTTP hooks in Claude Code's settings.json.
// Claude Desktop runs the hooks in ~/.claude/settings.json in its Code tab sessions.
// Only touches entries whose URL contains /claude-mx/hooks/; the rest of the file is left as is.
public static class HooksInstaller
{
    public static string DefaultSettingsPath
    {
        get
        {
            var dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
            }

            return Path.Combine(dir, "settings.json");
        }
    }

    // hookEvents: keys from actions.json "hookEvents" ("Stop", "Notification:idle_prompt", ...).
    public static JsonObject BuildHooks(IEnumerable<string> hookEvents, int port, string token, int holdSeconds)
    {
        var byEvent = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var key in hookEvents)
        {
            var parts = key.Split(':', 2);
            if (!byEvent.TryGetValue(parts[0], out var matchers))
            {
                byEvent[parts[0]] = matchers = new List<string>();
            }

            if (parts.Length == 2)
            {
                matchers.Add(parts[1]);
            }
        }

        var hooks = new JsonObject();
        foreach (var (hookEvent, matchers) in byEvent)
        {
            var group = new JsonObject();
            if (matchers.Count > 0)
            {
                group["matcher"] = string.Join("|", matchers);
            }

            group["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "http",
                ["url"] = $"http://127.0.0.1:{port}{BridgeServer.HooksPath}{hookEvent}",
                // PermissionRequest waits for the mouse; its timeout must be longer than the wait.
                ["timeout"] = hookEvent == "PermissionRequest" ? holdSeconds + 10 : 5,
                ["headers"] = new JsonObject { [BridgeServer.TokenHeader] = token },
            });
            hooks[hookEvent] = new JsonArray(group);
        }

        return hooks;
    }

    // Returns true if the file changed. Throws if settings.json isn't valid JSON (and doesn't touch it).
    public static bool Install(string settingsPath, JsonObject ourHooks)
    {
        var root = Load(settingsPath, out var original);
        RemoveOurs(root);
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        root["hooks"] = hooks;
        foreach (var (hookEvent, groups) in ourHooks)
        {
            var list = hooks[hookEvent] as JsonArray ?? new JsonArray();
            hooks[hookEvent] = list;
            foreach (var group in groups!.AsArray())
            {
                list.Add(group!.DeepClone());
            }
        }

        return Save(settingsPath, root, original);
    }

    public static bool Uninstall(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return false;
        }

        var root = Load(settingsPath, out var original);
        RemoveOurs(root);
        return Save(settingsPath, root, original);
    }

    public static bool IsInstalled(string settingsPath, JsonObject ourHooks)
    {
        if (!File.Exists(settingsPath))
        {
            return false;
        }

        var root = Load(settingsPath, out _);
        var probe = (JsonObject)root.DeepClone();
        RemoveOurs(probe);
        var expected = (JsonObject)probe.DeepClone();
        var hooks = expected["hooks"] as JsonObject ?? new JsonObject();
        expected["hooks"] = hooks;
        foreach (var (hookEvent, groups) in ourHooks)
        {
            var list = hooks[hookEvent] as JsonArray ?? new JsonArray();
            hooks[hookEvent] = list;
            foreach (var group in groups!.AsArray())
            {
                list.Add(group!.DeepClone());
            }
        }

        return JsonNode.DeepEquals(root, expected);
    }

    private static JsonObject Load(string path, out string original)
    {
        original = File.Exists(path) ? File.ReadAllText(path) : null;
        if (string.IsNullOrWhiteSpace(original))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(original, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new InvalidDataException($"{path} is not a JSON object");
    }

    private static void RemoveOurs(JsonObject root)
    {
        if (root["hooks"] is not JsonObject hooks)
        {
            return;
        }

        foreach (var hookEvent in hooks.Select(kv => kv.Key).ToList())
        {
            if (hooks[hookEvent] is not JsonArray groups)
            {
                continue;
            }

            for (var i = groups.Count - 1; i >= 0; i--)
            {
                if (groups[i] is JsonObject group && group["hooks"] is JsonArray handlers
                    && handlers.Any(h => ((string)h?["url"])?.Contains(BridgeServer.HooksPath, StringComparison.Ordinal) == true))
                {
                    groups.RemoveAt(i);
                }
            }

            if (groups.Count == 0)
            {
                hooks.Remove(hookEvent);
            }
        }

        if (hooks.Count == 0)
        {
            root.Remove("hooks");
        }
    }

    private static bool Save(string path, JsonObject root, string original)
    {
        var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
        if (!string.IsNullOrWhiteSpace(original) && JsonNode.DeepEquals(JsonNode.Parse(original, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }), root))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var backup = path + ".claude-mx.bak";
        if (original != null && !File.Exists(backup))
        {
            File.WriteAllText(backup, original);
        }

        var temp = path + ".claude-mx.tmp";
        File.WriteAllText(temp, text);
        File.Move(temp, path, overwrite: true);
        return true;
    }
}
