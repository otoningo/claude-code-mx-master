namespace Loupedeck.ClaudeCodePlugin.Platform;

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Loupedeck.ClaudeCodePlugin.Core;

// macOS: genera un AppleScript (System Events) y lo ejecuta con osascript.
// Logi Plugin Service necesita permiso de Accesibilidad (Ajustes > Privacidad y seguridad > Accesibilidad).
public sealed class MacKeySender : IKeySender
{
    private static readonly Dictionary<string, int> KeyCodes = new(StringComparer.Ordinal)
    {
        ["esc"] = 53, ["enter"] = 36, ["tab"] = 48, ["space"] = 49, ["backspace"] = 51,
        ["delete"] = 117, ["up"] = 126, ["down"] = 125, ["left"] = 123, ["right"] = 124,
    };

    private static readonly Dictionary<string, string> Modifiers = new(StringComparer.Ordinal)
    {
        ["mod"] = "command down", ["cmd"] = "command down", ["ctrl"] = "control down",
        ["shift"] = "shift down", ["alt"] = "option down",
    };

    public async Task SendAsync(IReadOnlyList<Step> steps, JsonObject config, CancellationToken cancellationToken = default)
    {
        var script = BuildAppleScript(steps, config);
        var psi = new ProcessStartInfo("/usr/bin/osascript")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-e");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar osascript");
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"osascript falló ({process.ExitCode}): {stderr.Trim()}");
        }
    }

    public static string Quote(string text) =>
        "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";

    private static string Using(IReadOnlyList<string> mods)
    {
        var parts = mods.Select(m => Modifiers[m]).Distinct().ToList();
        return parts.Count == 0 ? string.Empty : $" using {{{string.Join(", ", parts)}}}";
    }

    private static string Seconds(int ms) => (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);

    public static string BuildAppleScript(IReadOnlyList<Step> steps, JsonObject config)
    {
        var sb = new StringBuilder();
        var stepMs = KeySender.Int(config, "timing.stepMs", 60);
        if (KeySender.Bool(config, "activateApp", true))
        {
            sb.AppendLine($"tell application id {Quote(KeySender.Str(config, "mac.bundleId", "com.anthropic.claudefordesktop"))} to activate");
            sb.AppendLine("delay 0.15");
        }

        sb.AppendLine("tell application \"System Events\"");
        foreach (var step in steps)
        {
            switch (step)
            {
                case WaitStep w:
                    sb.AppendLine($"  delay {Seconds(w.Milliseconds)}");
                    continue;
                case TextStep t:
                    sb.AppendLine($"  keystroke {Quote(t.Text)}");
                    break;
                case KeyStep k when KeyCodes.TryGetValue(k.Key, out var code):
                    sb.AppendLine($"  key code {code}{Using(k.Mods)}");
                    break;
                case KeyStep k:
                    sb.AppendLine($"  keystroke {Quote(k.Key)}{Using(k.Mods)}");
                    break;
            }

            if (stepMs > 0)
            {
                sb.AppendLine($"  delay {Seconds(stepMs)}");
            }
        }

        sb.Append("end tell");
        return sb.ToString();
    }
}
