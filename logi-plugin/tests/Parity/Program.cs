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
        description = Localizer.Description(action, config),
        group = Localizer.Group(action, config),
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

// The hook installer keeps the user's settings and is idempotent.
{
    var dir = Path.Combine(Path.GetTempPath(), "claude-mx-installer-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    var settings = Path.Combine(dir, "settings.json");
    var original = """
    {
      // user comment
      "model": "opus",
      "permissions": { "allow": ["Bash(npm test)"] },
      "hooks": {
        "Stop": [ { "hooks": [ { "type": "command", "command": "say done" } ] } ],
      }
    }
    """;
    File.WriteAllText(settings, original);
    var ours = Loupedeck.ClaudeCodePlugin.Bridge.HooksInstaller.BuildHooks(ActionCatalog.Instance.HookEvents.Keys, 47823, "t0ken", 20);
    if (!Loupedeck.ClaudeCodePlugin.Bridge.HooksInstaller.Install(settings, ours)) throw new Exception("first install should change the file");
    if (Loupedeck.ClaudeCodePlugin.Bridge.HooksInstaller.Install(settings, ours)) throw new Exception("second install should be a no-op");
    if (!Loupedeck.ClaudeCodePlugin.Bridge.HooksInstaller.IsInstalled(settings, ours)) throw new Exception("IsInstalled should be true");
    var after = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settings))!;
    if ((string)after["model"] != "opus" || after["permissions"]?["allow"]?[0]?.ToString() != "Bash(npm test)") throw new Exception("user settings lost");
    var stop = after["hooks"]!["Stop"]!.AsArray();
    if (stop.Count != 2 || (string)stop[0]!["hooks"]![0]!["command"] != "say done") throw new Exception("user Stop hook lost");
    if ((string)after["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["url"] != "http://127.0.0.1:47823/claude-mx/hooks/PermissionRequest") throw new Exception("PermissionRequest hook missing");
    if ((int)after["hooks"]!["PermissionRequest"]![0]!["hooks"]![0]!["timeout"] != 30) throw new Exception("PermissionRequest timeout must exceed the wait");
    if (!File.Exists(settings + ".claude-mx.bak")) throw new Exception("backup missing");
    Loupedeck.ClaudeCodePlugin.Bridge.HooksInstaller.Uninstall(settings);
    var removed = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(settings))!;
    if (removed["hooks"]!.AsObject().Count != 1 || removed["hooks"]!["Stop"]!.AsArray().Count != 1) throw new Exception("uninstall should leave only the user's hooks");
    Directory.Delete(dir, recursive: true);
    Console.Error.WriteLine("OK: hooks installer");
}

Console.Error.WriteLine($"OK: {commandTypes.Count} comandos, eventos: {string.Join(",", plugin.PluginEvents.Raised)}");
Console.WriteLine(JsonSerializer.Serialize(result));
