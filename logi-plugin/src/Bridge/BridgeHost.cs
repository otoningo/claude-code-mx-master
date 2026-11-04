namespace Loupedeck.ClaudeCodePlugin.Bridge;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Loupedeck.ClaudeCodePlugin.Core;
using Loupedeck.ClaudeCodePlugin.Platform;

// Wires together the HTTP server, the hooks in settings.json and the haptic feedback.
public static class BridgeHost
{
    public static string TokenPath =>
        Path.Combine(Path.GetDirectoryName(ConfigStore.UserConfigPath)!, "token");

    // Shared secret between the hooks (header) and the server. Created once, readable only by the user.
    public static string LoadOrCreateToken()
    {
        var path = TokenPath;
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path).Trim();
            if (existing.Length >= 32)
            {
                return existing;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        File.WriteAllText(path, token);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return token;
    }

    public static int Port(JsonObject config) => KeySender.Int(config, "bridge.port", 47823);

    public static int HoldSeconds(JsonObject config) => Math.Clamp(KeySender.Int(config, "bridge.holdSeconds", 20), 1, 550);

    // Which haptic event a Claude Code hook produces, per actions.json "hookEvents".
    public static string HapticFor(JsonObject hookInput)
    {
        var hookEvent = (string)hookInput["hook_event_name"];
        var map = ActionCatalog.Instance.HookEvents;
        if (hookEvent == "Notification")
        {
            return map.TryGetValue($"Notification:{(string)hookInput["notification_type"]}", out var n) ? n : null;
        }

        return hookEvent != null && map.TryGetValue(hookEvent, out var h) ? h : null;
    }

    public static BridgeServer Start(JsonObject config, Action<string> raiseHaptic)
    {
        var token = LoadOrCreateToken();
        var server = new BridgeServer(Port(config), token, () => TimeSpan.FromSeconds(HoldSeconds(ConfigStore.Load())), HapticFor, raiseHaptic);
        server.Start();

        if (KeySender.Bool(config, "bridge.autoInstallHooks", true))
        {
            InstallHooks(config, token);
        }

        return server;
    }

    public static bool InstallHooks(JsonObject config, string token = null, string settingsPath = null)
    {
        var hooks = HooksInstaller.BuildHooks(ActionCatalog.Instance.HookEvents.Keys, Port(config), token ?? LoadOrCreateToken(), HoldSeconds(config));
        var changed = HooksInstaller.Install(settingsPath ?? HooksInstaller.DefaultSettingsPath, hooks);
        PluginLog.Info(changed ? $"Claude Code hooks installed in {settingsPath ?? HooksInstaller.DefaultSettingsPath}" : "Claude Code hooks already up to date");
        return changed;
    }

    // Opens a claude:// link with the operating system (it opens Claude Desktop directly).
    public static void OpenUrl(string url)
    {
        if (!url.StartsWith("claude://", StringComparison.Ordinal))
        {
            throw new ArgumentException("Only claude:// links are opened");
        }

        if (OperatingSystem.IsMacOS())
        {
            using var p = Process.Start("/usr/bin/open", url);
        }
        else if (OperatingSystem.IsWindows())
        {
            using var p = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        else
        {
            throw new PlatformNotSupportedException("Claude Desktop only exists for macOS and Windows.");
        }
    }
}
