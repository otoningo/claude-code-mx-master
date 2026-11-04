# Envía un plan de teclas a Claude Desktop en Windows usando SendInput.
# El plan llega como JSON en la variable de entorno CLAUDE_MX_PLAN:
#   { "activate": true, "processNames": ["claude"], "keyMapping": "layout"|"us", "stepMs": 60,
#     "steps": [ {"type":"key","key":"n","mods":["mod"]}, {"type":"text","text":"/compact"}, {"type":"wait","ms":250} ] }
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class ClaudeMxInput {
    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx; public int dy; public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    const uint INPUT_KEYBOARD = 1;
    const uint KEYEVENTF_KEYUP = 0x0002;
    const uint KEYEVENTF_UNICODE = 0x0004;

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern short VkKeyScan(char ch);
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    static INPUT Key(ushort vk, ushort scan, uint flags) {
        INPUT i = new INPUT();
        i.type = INPUT_KEYBOARD;
        i.u.ki.wVk = vk; i.u.ki.wScan = scan; i.u.ki.dwFlags = flags;
        return i;
    }

    public static void Chord(ushort[] modifiers, ushort vk) {
        int n = modifiers.Length * 2 + 2;
        INPUT[] inputs = new INPUT[n];
        int k = 0;
        foreach (ushort m in modifiers) inputs[k++] = Key(m, 0, 0);
        inputs[k++] = Key(vk, 0, 0);
        inputs[k++] = Key(vk, 0, KEYEVENTF_KEYUP);
        for (int j = modifiers.Length - 1; j >= 0; j--) inputs[k++] = Key(modifiers[j], 0, KEYEVENTF_KEYUP);
        SendInput((uint)n, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void Text(string text) {
        foreach (char c in text) {
            INPUT[] inputs = new INPUT[] { Key(0, c, KEYEVENTF_UNICODE), Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP) };
            SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
        }
    }
}
"@

$plan = $env:CLAUDE_MX_PLAN | ConvertFrom-Json

$VK_SHIFT = 0x10; $VK_CONTROL = 0x11; $VK_MENU = 0x12
$named = @{ esc = 0x1B; enter = 0x0D; tab = 0x09; space = 0x20; backspace = 0x08; delete = 0x2E; up = 0x26; down = 0x28; left = 0x25; right = 0x27 }
# Posiciones físicas del teclado US (se usan con keyMapping = "us").
$usOem = @{ '`' = 0xC0; '-' = 0xBD; '=' = 0xBB; '[' = 0xDB; ']' = 0xDD; '\' = 0xDC; ';' = 0xBA; "'" = 0xDE; ',' = 0xBC; '.' = 0xBE; '/' = 0xBF }

function Resolve-Key([string]$key, [string]$mapping) {
    if ($named.ContainsKey($key)) { return @{ vk = $named[$key]; extra = @() } }
    $c = [char]$key
    if (($c -ge 'a' -and $c -le 'z') -or ($c -ge '0' -and $c -le '9')) { return @{ vk = [int][char]::ToUpper($c); extra = @() } }
    if ($mapping -eq 'us' -and $usOem.ContainsKey($key)) { return @{ vk = $usOem[$key]; extra = @() } }
    # Distribución activa: VkKeyScan dice qué tecla y qué modificadores producen el carácter.
    $scan = [ClaudeMxInput]::VkKeyScan($c)
    if ($scan -eq -1) { throw "La distribución de teclado actual no puede producir '$key'. Prueba windows.keyMapping = 'us'." }
    $vk = $scan -band 0xFF
    $state = ($scan -shr 8) -band 0xFF
    $extra = @()
    if ($state -band 1) { $extra += $VK_SHIFT }
    if ($state -band 2) { $extra += $VK_CONTROL }
    if ($state -band 4) { $extra += $VK_MENU }
    return @{ vk = $vk; extra = $extra }
}

if ($plan.activate) {
    $proc = Get-Process -Name $plan.processNames -ErrorAction SilentlyContinue |
        Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
    if (-not $proc) { throw "Claude Desktop no está abierto (procesos buscados: $($plan.processNames -join ', '))." }
    $wsh = New-Object -ComObject WScript.Shell
    [void]$wsh.AppActivate($proc.Id)
    [void][ClaudeMxInput]::SetForegroundWindow($proc.MainWindowHandle)
    Start-Sleep -Milliseconds 150
}

foreach ($step in $plan.steps) {
    switch ($step.type) {
        'wait' { Start-Sleep -Milliseconds ([int]$step.ms) }
        'text' { [ClaudeMxInput]::Text([string]$step.text) }
        'key' {
            $resolved = Resolve-Key ([string]$step.key) ([string]$plan.keyMapping)
            $mods = New-Object System.Collections.Generic.List[uint16]
            foreach ($m in $step.mods) {
                switch ($m) {
                    'mod' { $mods.Add($VK_CONTROL) }
                    'cmd' { $mods.Add($VK_CONTROL) }
                    'ctrl' { $mods.Add($VK_CONTROL) }
                    'shift' { $mods.Add($VK_SHIFT) }
                    'alt' { $mods.Add($VK_MENU) }
                }
            }
            foreach ($e in $resolved.extra) { $mods.Add([uint16]$e) }
            $unique = [uint16[]]($mods | Select-Object -Unique)
            if ($null -eq $unique) { $unique = [uint16[]]@() }
            [ClaudeMxInput]::Chord($unique, [uint16]$resolved.vk)
        }
    }
    if ($plan.stepMs -gt 0) { Start-Sleep -Milliseconds ([int]$plan.stepMs) }
}
