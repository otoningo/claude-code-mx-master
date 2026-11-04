namespace Loupedeck.ClaudeCodePlugin.Core;

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

public sealed record HapticEvent(string Name, string DisplayName, string Description, string Waveform);

public sealed record ActionDefinition(
    string Id,
    string ClassName,
    string DisplayName,
    string DisplayNameFrom,
    string Description,
    string Group,
    string GroupDisplayName,
    string Haptic,
    JsonObject Direct,
    JsonArray Steps)
{
    // Kind of direct interface: "permission", "deeplink" or null (shortcuts only).
    public string DirectType => (string)this.Direct?["type"];
}

// Lee shared/actions.json (incrustado en la DLL) — el mismo catálogo que usa el servidor MCP.
public sealed class ActionCatalog
{
    private static readonly Lazy<ActionCatalog> _instance = new(() => FromJson(ReadResource("ClaudeCode.actions.json")));

    public static ActionCatalog Instance => _instance.Value;

    public IReadOnlyList<ActionDefinition> Actions { get; }

    public IReadOnlyList<HapticEvent> Haptics { get; }

    // "PermissionRequest" / "Notification:idle_prompt" / ... -> haptic event.
    public IReadOnlyDictionary<string, string> HookEvents { get; }

    private readonly Dictionary<string, ActionDefinition> _byId;

    private ActionCatalog(List<ActionDefinition> actions, List<HapticEvent> haptics, Dictionary<string, string> hookEvents)
    {
        this.Actions = actions;
        this.Haptics = haptics;
        this.HookEvents = hookEvents;
        this._byId = actions.ToDictionary(a => a.Id, StringComparer.Ordinal);
    }

    public ActionDefinition Get(string id) =>
        this._byId.TryGetValue(id, out var action)
            ? action
            : throw new KeyNotFoundException($"Acción desconocida en actions.json: {id}");

    public static ActionCatalog FromJson(string json)
    {
        var root = JsonNode.Parse(json)!.AsObject();
        var groups = root["groups"]!.AsObject();

        var actions = new List<ActionDefinition>();
        foreach (var node in root["actions"]!.AsArray())
        {
            var a = node!.AsObject();
            var group = (string)a["group"];
            actions.Add(new ActionDefinition(
                (string)a["id"],
                (string)a["className"],
                (string)a["displayName"],
                (string)a["displayNameFrom"],
                (string)a["description"],
                group,
                (string)groups[group] ?? group,
                (string)a["haptic"],
                a["direct"] as JsonObject,
                a["steps"]!.AsArray()));
        }

        var haptics = new List<HapticEvent>();
        foreach (var (name, value) in root["haptics"]!.AsObject())
        {
            var h = value!.AsObject();
            haptics.Add(new HapticEvent(name, (string)h["displayName"], (string)h["description"], (string)h["waveform"]));
        }

        var hookEvents = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root["hookEvents"] is JsonObject events)
        {
            foreach (var (key, value) in events)
            {
                if (!key.StartsWith('$'))
                {
                    hookEvents[key] = (string)value;
                }
            }
        }

        return new ActionCatalog(actions, haptics, hookEvents);
    }

    internal static string ReadResource(string logicalName)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"Recurso incrustado no encontrado: {logicalName}");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

// Configuración: valores por defecto incrustados + ~/.claude-mx/config.json del usuario.
public static class ConfigStore
{
    public static string UserConfigPath =>
        Environment.GetEnvironmentVariable("CLAUDE_MX_CONFIG")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude-mx", "config.json");

    private static JsonObject _cached;
    private static DateTime _cachedStamp;
    private static readonly object _lock = new();

    // Se relee cuando el archivo del usuario cambia, sin reiniciar el servicio.
    public static JsonObject Load()
    {
        lock (_lock)
        {
            var path = UserConfigPath;
            var stamp = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
            if (_cached != null && stamp == _cachedStamp)
            {
                return _cached;
            }

            var config = JsonNode.Parse(ActionCatalog.ReadResource("ClaudeCode.config.default.json"))!.AsObject();
            if (File.Exists(path))
            {
                try
                {
                    var user = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                    if (user is JsonObject userObj)
                    {
                        config = (JsonObject)DeepMerge(config, userObj);
                    }
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, $"No se pudo leer {path}; se usan los valores por defecto");
                }
            }

            _cached = config;
            _cachedStamp = stamp;
            return config;
        }
    }

    // Igual que deepMerge() del servidor MCP: objetos se combinan, arrays y escalares se reemplazan.
    public static JsonNode DeepMerge(JsonNode baseNode, JsonNode overrideNode)
    {
        if (baseNode is JsonObject baseObj && overrideNode is JsonObject overrideObj)
        {
            var result = (JsonObject)baseObj.DeepClone();
            foreach (var (key, value) in overrideObj)
            {
                result[key] = result.ContainsKey(key) && result[key] != null && value != null
                    ? DeepMerge(result[key], value)
                    : value?.DeepClone();
            }

            return result;
        }

        return overrideNode?.DeepClone();
    }

    public static JsonNode GetPath(JsonNode node, string dotted)
    {
        if (node == null)
        {
            return null;
        }

        foreach (var part in dotted.Split('.'))
        {
            node = node switch
            {
                JsonObject obj => obj[part],
                JsonArray arr when int.TryParse(part, out var i) && i >= 0 && i < arr.Count => arr[i],
                _ => null,
            };
            if (node == null)
            {
                return null;
            }
        }

        return node;
    }
}
