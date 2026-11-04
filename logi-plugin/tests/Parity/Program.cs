using System.Reflection;
using System.Text.Json;
using Loupedeck.ClaudeCodePlugin;
using Loupedeck.ClaudeCodePlugin.Core;
using Loupedeck.ClaudeCodePlugin.Platform;

// Uso: dotnet run -- <config.json opcional>
if (args.Length > 0) Environment.SetEnvironmentVariable("CLAUDE_MX_CONFIG", args[0]);
else Environment.SetEnvironmentVariable("CLAUDE_MX_CONFIG", "/nonexistent/config.json");

var config = ConfigStore.Load();
var result = new SortedDictionary<string, object>();
foreach (var action in ActionCatalog.Instance.Actions)
{
    var steps = StepResolver.Resolve(action, config);
    result[action.Id] = new
    {
        name = StepResolver.DisplayName(action, config),
        appleScript = MacKeySender.BuildAppleScript(steps, config),
    };
}

// Cada acción del catálogo tiene su clase generada y se puede instanciar.
var commandTypes = typeof(ClaudeCommandBase).Assembly.GetTypes()
    .Where(t => t.IsSubclassOf(typeof(ClaudeCommandBase)) && !t.IsAbstract).ToList();
if (commandTypes.Count != ActionCatalog.Instance.Actions.Count)
    throw new Exception($"Clases generadas: {commandTypes.Count}, acciones: {ActionCatalog.Instance.Actions.Count}");

// Ejecutar una acción fuera de macOS/Windows debe fallar con gracia y vibrar "claudeError".
var plugin = new ClaudeCodePlugin();
plugin.Load();
var cmd = (ClaudeCommandBase)Activator.CreateInstance(commandTypes.First());
cmd.Plugin = plugin;
cmd.Run();
for (var i = 0; i < 50 && plugin.PluginEvents.Raised.Count == 0; i++) Thread.Sleep(20);
if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsWindows() && !plugin.PluginEvents.Raised.Contains("claudeError"))
    throw new Exception("Se esperaba el evento háptico claudeError");

Console.Error.WriteLine($"OK: {commandTypes.Count} comandos, eventos: {string.Join(",", plugin.PluginEvents.Raised)}");
Console.WriteLine(JsonSerializer.Serialize(result));
