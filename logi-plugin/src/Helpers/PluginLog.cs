namespace Loupedeck.ClaudeCodePlugin;

// Envoltorio del log del Logi Plugin Service.
internal static class PluginLog
{
    private static PluginLogFile _log;

    public static void Init(PluginLogFile log) => _log = log;

    public static void Info(string text) => _log?.Info(text);

    public static void Warning(string text) => _log?.Warning(text);

    public static void Warning(Exception ex, string text) => _log?.Warning(ex, text);

    public static void Error(string text) => _log?.Error(text);

    public static void Error(Exception ex, string text) => _log?.Error(ex, text);
}
