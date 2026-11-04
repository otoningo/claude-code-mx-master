namespace Loupedeck.ClaudeCodePlugin.Platform;

using System.Text.Json.Nodes;
using Loupedeck.ClaudeCodePlugin.Core;

public interface IKeySender
{
    Task SendAsync(IReadOnlyList<Step> steps, JsonObject config, CancellationToken cancellationToken = default);
}

public static class KeySender
{
    public static IKeySender Create() =>
        OperatingSystem.IsMacOS() ? new MacKeySender()
        : OperatingSystem.IsWindows() ? new WindowsKeySender()
        : throw new PlatformNotSupportedException("Claude Desktop solo existe para macOS y Windows.");

    internal static int Int(JsonObject config, string path, int fallback) =>
        ConfigStore.GetPath(config, path) is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;

    internal static bool Bool(JsonObject config, string path, bool fallback) =>
        ConfigStore.GetPath(config, path) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    internal static string Str(JsonObject config, string path, string fallback) =>
        ConfigStore.GetPath(config, path)?.ToString() is { Length: > 0 } s ? s : fallback;
}
