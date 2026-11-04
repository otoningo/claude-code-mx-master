using System.Text.Json.Nodes;
using Loupedeck.ClaudeCodePlugin;
using Loupedeck.ClaudeCodePlugin.Bridge;
using Loupedeck.ClaudeCodePlugin.Core;

// Uso: BridgeSmoke <workdir> <port> <action-id|none> [holdSeconds]
//   Writes <workdir>/settings.json with the hooks (pass it to `claude --settings`).
//   When a PermissionRequest arrives it waits 500 ms and runs the plugin action <action-id>
//   (approve, deny, approve-all, stop) as if it had been pressed on the mouse.
//   Ends when <workdir>/done exists. Prints the received events and the vibrations.
var workdir = args[0];
var port = int.Parse(args[1]);
var actionId = args[2];
var hold = args.Length > 3 ? int.Parse(args[3]) : 20;

Directory.CreateDirectory(workdir);
var configPath = Path.Combine(workdir, "config.json");
File.WriteAllText(configPath, new JsonObject
{
    ["language"] = "es",
    ["bridge"] = new JsonObject { ["port"] = port, ["holdSeconds"] = hold, ["autoInstallHooks"] = false },
}.ToJsonString());
Environment.SetEnvironmentVariable("CLAUDE_MX_CONFIG", configPath);

var plugin = new ClaudeCodePlugin();
plugin.Load();
BridgeHost.InstallHooks(ConfigStore.Load(), settingsPath: Path.Combine(workdir, "settings.json"));
Console.WriteLine("READY");

var commandType = actionId == "none" ? null : typeof(ClaudeCommandBase).Assembly.GetTypes()
    .First(t => t.IsSubclassOf(typeof(ClaudeCommandBase)) && !t.IsAbstract
        && ActionCatalog.Instance.Actions.Any(a => a.Id == actionId && a.ClassName == t.Name));

var seen = new HashSet<string>();
var deadline = DateTime.UtcNow.AddMinutes(4);
while (!File.Exists(Path.Combine(workdir, "done")) && DateTime.UtcNow < deadline)
{
    var pending = PermissionBroker.Instance.Oldest;
    if (pending != null && seen.Add(pending.Id))
    {
        Console.WriteLine($"PENDING tool={pending.ToolName} summary=\"{pending.Summary}\"");
        if (commandType != null)
        {
            var cmd = (ClaudeCommandBase)Activator.CreateInstance(commandType);
            cmd.Plugin = plugin;
            cmd.Load();
            Console.WriteLine($"BUBBLE \"{cmd.GetName()}\"");
            Thread.Sleep(500);
            cmd.Run();
            Console.WriteLine($"PRESSED {actionId}");
        }
    }

    Thread.Sleep(100);
}

Thread.Sleep(300);
Console.WriteLine("HAPTICS " + string.Join(",", plugin.PluginEvents.Raised));
plugin.Unload();
