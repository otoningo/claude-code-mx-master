namespace Loupedeck.ClaudeCodePlugin.Core;

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

public abstract record Step;

// Atajo: tecla + modificadores ("mod" = Cmd en macOS / Ctrl en Windows).
public sealed record KeyStep(string Key, IReadOnlyList<string> Mods) : Step;

public sealed record TextStep(string Text) : Step;

public sealed record WaitStep(int Milliseconds) : Step;

// Convierte los pasos declarativos de actions.json en pasos concretos.
// Misma lógica que resolveSteps()/parseChord() en mcp-server/src/catalog.js.
public static partial class StepResolver
{
    private static readonly HashSet<string> Modifiers = new(StringComparer.Ordinal) { "mod", "cmd", "ctrl", "shift", "alt" };

    public static readonly HashSet<string> NamedKeys = new(StringComparer.Ordinal)
    {
        "esc", "enter", "tab", "space", "backspace", "delete", "up", "down", "left", "right",
    };

    [GeneratedRegex(@"^\$\{([A-Za-z0-9_.]+)\}$")]
    private static partial Regex Placeholder();

    public static List<Step> Resolve(ActionDefinition action, JsonObject config)
    {
        var steps = new List<Step>();
        foreach (var node in action.Steps)
        {
            if (node is JsonObject obj && obj.ContainsKey("wait"))
            {
                steps.Add(new WaitStep(int.Parse(ResolveValue(obj["wait"], config, action.Id))));
            }
            else if (node is JsonObject textObj && textObj.ContainsKey("text"))
            {
                steps.Add(new TextStep(ResolveValue(textObj["text"], config, action.Id)));
            }
            else
            {
                steps.Add(ParseChord(ResolveValue(node, config, action.Id)));
            }
        }

        return steps;
    }

    public static string DisplayName(ActionDefinition action, JsonObject config) => Localizer.DisplayName(action, config);

    private static string ResolveValue(JsonNode node, JsonObject config, string actionId)
    {
        var raw = node?.ToString() ?? string.Empty;
        var match = Placeholder().Match(raw);
        if (!match.Success)
        {
            return raw;
        }

        var resolved = ConfigStore.GetPath(config, match.Groups[1].Value)
            ?? throw new InvalidOperationException($"La acción \"{actionId}\" usa ${{{match.Groups[1].Value}}}, que no existe en la configuración");
        return resolved.ToString();
    }

    public static KeyStep ParseChord(string chord)
    {
        string key;
        string modPart;
        if (chord == "+")
        {
            key = "+";
            modPart = string.Empty;
        }
        else if (chord.EndsWith("++", StringComparison.Ordinal))
        {
            key = "+";
            modPart = chord[..^2];
        }
        else
        {
            var idx = chord.LastIndexOf('+');
            key = idx == -1 ? chord : chord[(idx + 1)..];
            modPart = idx == -1 ? string.Empty : chord[..idx];
        }

        var mods = modPart.Length == 0
            ? new List<string>()
            : modPart.Split('+').Select(m => m.Trim().ToLowerInvariant()).ToList();
        foreach (var m in mods)
        {
            if (!Modifiers.Contains(m))
            {
                throw new FormatException($"Modificador desconocido \"{m}\" en \"{chord}\"");
            }
        }

        key = key.ToLowerInvariant();
        if (key.Length != 1 && !NamedKeys.Contains(key))
        {
            throw new FormatException($"Tecla desconocida \"{key}\" en \"{chord}\"");
        }

        return new KeyStep(key, mods);
    }
}
