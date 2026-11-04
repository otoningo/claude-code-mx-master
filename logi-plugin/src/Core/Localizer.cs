namespace Loupedeck.ClaudeCodePlugin.Core;

using System.Globalization;
using System.Text.Json.Nodes;

// Idioma de los textos (español en actions.json, inglés en i18n.en.json).
// Misma lógica que localize()/resolveLanguage() en mcp-server/src/catalog.js.
public static class Localizer
{
    private static readonly Lazy<JsonObject> English = new(() =>
        JsonNode.Parse(ActionCatalog.ReadResource("ClaudeCode.i18n.en.json"))!.AsObject());

    // config.language: "es", "en" o "auto" (idioma del sistema: español -> es, cualquier otro -> en).
    public static string Language(JsonObject config)
    {
        var configured = ConfigStore.GetPath(config, "language")?.ToString()?.ToLowerInvariant() ?? "auto";
        if (configured is "es" or "en")
        {
            return configured;
        }

        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
    }

    private static JsonObject Strings(JsonObject config) => Language(config) == "en" ? English.Value : null;

    public static string DisplayName(ActionDefinition action, JsonObject config)
    {
        var strings = Strings(config);
        var name = ConfigStore.GetPath(strings, $"actions.{action.Id}.displayName")?.ToString() ?? action.DisplayName;
        if (!string.IsNullOrEmpty(action.DisplayNameFrom))
        {
            var label = ConfigStore.GetPath(config, action.DisplayNameFrom)?.ToString();
            if (!string.IsNullOrWhiteSpace(label))
            {
                var prefix = ConfigStore.GetPath(strings, "modelPrefix")?.ToString() ?? "Modelo";
                name = $"{prefix}: {label}";
            }
        }

        return name;
    }

    public static string Description(ActionDefinition action, JsonObject config) =>
        ConfigStore.GetPath(Strings(config), $"actions.{action.Id}.description")?.ToString() ?? action.Description;

    public static string Group(ActionDefinition action, JsonObject config) =>
        ConfigStore.GetPath(Strings(config), $"groups.{action.Group}")?.ToString() ?? action.GroupDisplayName;

    public static (string DisplayName, string Description) Haptic(HapticEvent haptic, JsonObject config)
    {
        var strings = Strings(config);
        return (
            ConfigStore.GetPath(strings, $"haptics.{haptic.Name}.displayName")?.ToString() ?? haptic.DisplayName,
            ConfigStore.GetPath(strings, $"haptics.{haptic.Name}.description")?.ToString() ?? haptic.Description);
    }
}
