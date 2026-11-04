namespace Loupedeck.ClaudeCodePlugin.Platform;

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Loupedeck.ClaudeCodePlugin.Core;

// Windows: SendInput de user32.dll. Misma lógica que mcp-server/src/senders/windows-sendinput.ps1.
public sealed class WindowsKeySender : IKeySender
{
    private const ushort VK_SHIFT = 0x10;
    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_MENU = 0x12;

    private static readonly Dictionary<string, ushort> Named = new(StringComparer.Ordinal)
    {
        ["esc"] = 0x1B, ["enter"] = 0x0D, ["tab"] = 0x09, ["space"] = 0x20, ["backspace"] = 0x08,
        ["delete"] = 0x2E, ["up"] = 0x26, ["down"] = 0x28, ["left"] = 0x25, ["right"] = 0x27,
    };

    // Posiciones físicas de un teclado US (windows.keyMapping = "us").
    private static readonly Dictionary<char, ushort> UsOem = new()
    {
        ['`'] = 0xC0, ['-'] = 0xBD, ['='] = 0xBB, ['['] = 0xDB, [']'] = 0xDD, ['\\'] = 0xDC,
        [';'] = 0xBA, ['\''] = 0xDE, [','] = 0xBC, ['.'] = 0xBE, ['/'] = 0xBF,
    };

    public async Task SendAsync(IReadOnlyList<Step> steps, JsonObject config, CancellationToken cancellationToken = default)
    {
        var stepMs = KeySender.Int(config, "timing.stepMs", 60);
        var usLayout = KeySender.Str(config, "windows.keyMapping", "layout") == "us";

        if (KeySender.Bool(config, "activateApp", true))
        {
            ActivateClaude(config);
            await Task.Delay(150, cancellationToken);
        }

        foreach (var step in steps)
        {
            switch (step)
            {
                case WaitStep w:
                    await Task.Delay(w.Milliseconds, cancellationToken);
                    continue;
                case TextStep t:
                    SendText(t.Text);
                    break;
                case KeyStep k:
                    SendChord(k, usLayout);
                    break;
            }

            if (stepMs > 0)
            {
                await Task.Delay(stepMs, cancellationToken);
            }
        }
    }

    private static void ActivateClaude(JsonObject config)
    {
        var names = (ConfigStore.GetPath(config, "windows.processNames") as JsonArray)?
            .Select(n => n?.ToString()).Where(n => !string.IsNullOrEmpty(n)).ToArray();
        if (names == null || names.Length == 0)
        {
            names = new[] { "claude" };
        }

        var process = names.SelectMany(Process.GetProcessesByName).FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (process == null)
        {
            throw new InvalidOperationException($"Claude Desktop no está abierto (procesos: {string.Join(", ", names)})");
        }

        if (IsIconic(process.MainWindowHandle))
        {
            ShowWindow(process.MainWindowHandle, SW_RESTORE);
        }

        SetForegroundWindow(process.MainWindowHandle);
    }

    private static void SendChord(KeyStep step, bool usLayout)
    {
        var (vk, extra) = ResolveKey(step.Key, usLayout);
        var mods = new List<ushort>();
        foreach (var m in step.Mods)
        {
            mods.Add(m switch
            {
                "shift" => VK_SHIFT,
                "alt" => VK_MENU,
                _ => VK_CONTROL, // mod, cmd y ctrl son Ctrl en Windows
            });
        }

        mods.AddRange(extra);
        mods = mods.Distinct().ToList();

        var inputs = new List<INPUT>();
        inputs.AddRange(mods.Select(m => Key(m, 0, 0)));
        inputs.Add(Key(vk, 0, 0));
        inputs.Add(Key(vk, 0, KEYEVENTF_KEYUP));
        inputs.AddRange(Enumerable.Reverse(mods).Select(m => Key(m, 0, KEYEVENTF_KEYUP)));
        Send(inputs.ToArray());
    }

    private static (ushort Vk, List<ushort> Extra) ResolveKey(string key, bool usLayout)
    {
        if (Named.TryGetValue(key, out var named))
        {
            return (named, new List<ushort>());
        }

        var c = key[0];
        if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
        {
            return (char.ToUpperInvariant(c), new List<ushort>());
        }

        if (usLayout && UsOem.TryGetValue(c, out var oem))
        {
            return (oem, new List<ushort>());
        }

        // Distribución activa (p. ej. español latinoamericano): qué tecla + modificadores producen el carácter.
        var scan = VkKeyScan(c);
        if (scan == -1)
        {
            throw new InvalidOperationException($"La distribución de teclado actual no puede producir '{c}'. Prueba \"windows\": {{ \"keyMapping\": \"us\" }}.");
        }

        var extra = new List<ushort>();
        var state = (scan >> 8) & 0xFF;
        if ((state & 1) != 0) extra.Add(VK_SHIFT);
        if ((state & 2) != 0) extra.Add(VK_CONTROL);
        if ((state & 4) != 0) extra.Add(VK_MENU);
        return ((ushort)(scan & 0xFF), extra);
    }

    private static void SendText(string text)
    {
        foreach (var c in text)
        {
            Send(new[] { Key(0, c, KEYEVENTF_UNICODE), Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP) });
        }
    }

    private static void Send(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException($"SendInput envió {sent}/{inputs.Length} eventos (error {Marshal.GetLastWin32Error()})");
        }
    }

    private static INPUT Key(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;
    private const int SW_RESTORE = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);
}
