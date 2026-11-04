# Claude Code × MX Master 4

**English** · [Español](README.es.md)

Control **Claude Code Desktop** (the *Code* tab of the Claude desktop app) from the **Logitech MX Master 4**.
Approve, deny or stop Claude's permission requests **directly from the mouse** through Claude Code's official
hooks (not keyboard shortcuts), and **feel** when Claude needs you or finishes. The same actions are exposed as an
**MCP server**.

<img src="docs/actions-ring-preview.png" width="320" alt="Actions Ring preview">

## How it talks to Claude

| Mechanism | What it's used for | Works when… |
|---|---|---|
| **Claude Code hooks** (official, [docs](https://code.claude.com/docs/en/hooks)) → the plugin's local server | Approve · Approve all · Deny · Stop a pending permission request, and vibrations on real events | Always: any window, any focus. Claude Desktop runs the hooks from `~/.claude/settings.json` |
| **`claude://code/new` link** (official, [docs](https://code.claude.com/docs/en/desktop)) | New session | Always |
| Keyboard shortcuts (documented ones) | Only what has no official interface: send now, model and permission-mode menus, panes, slash commands | Claude Desktop is in front |

```
 Claude Code (Desktop) ──HTTP hooks──▶ Logi plugin (127.0.0.1:47823) ──▶ MX Master 4 vibrates
        ▲                                    │
        └──── decision (allow/deny) ◀── you press Approve / Deny / Stop on the Actions Ring
```

When Claude asks for permission, the mouse knocks, the bubble shows the tool (`Approve · Bash`) and the request
waits up to 20 s for your answer. If you don't answer from the mouse, Claude Desktop shows its usual dialog.
If Logi Options+ isn't running, Claude Code just works as usual (a hook that can't connect doesn't block anything).

## Actions

| Group | Action | How |
|---|---|---|
| Permissions | **Approve** | Hook: allows the pending request (just that one) |
| | **Approve all** | Hook: allows it **and** switches the session to *Accept edits*. With nothing pending: permission menu → *Accept edits* |
| | **Deny** | Hook: denies it; Claude keeps working another way |
| | Auto / Plan / Manual mode · Permission menu | Permission menu (`Cmd/Ctrl+Shift+M`) |
| Prompt | **Stop** | Hook: denies the pending request and interrupts Claude. With nothing pending: `Esc` |
| | **Send now** | `Esc` + `Enter` (interrupt and send what you typed) |
| | Send · Accept suggestion | `Enter` · `Tab` |
| Sessions | **New session** | `claude://code/new` (fallback `Cmd/Ctrl+N`) |
| | Close · Next · Previous session | `Cmd/Ctrl+W` · `Cmd/Ctrl+Shift+]` · `Cmd/Ctrl+Shift+[` |
| Model | Switch model · Model: Opus / Sonnet / Haiku · Effort | Model menu (`Cmd/Ctrl+Shift+I`) → 1/2/3 · `Cmd/Ctrl+Shift+E` |
| View | Diff · Terminal · Browser · Side chat · Cycle view · Close pane · Shortcuts | `Cmd/Ctrl+Shift+D` · ``Ctrl+` `` · `Cmd/Ctrl+Shift+B` · `Cmd/Ctrl+;` · `Ctrl+O` · `Cmd/Ctrl+\` · `Cmd/Ctrl+/` |
| Commands | `/compact` · `/context` · `/code-review` | Types the command and sends it (empty prompt box) |

### Vibrations

| When | Waveform |
|---|---|
| Claude asks for permission | `knock` |
| Claude is waiting for your answer (idle, question) | `ringing` |
| Claude finished its turn | `completed` |
| You approved · denied · stopped | `happy_alert` · `damp_collision` · `sharp_collision` |
| Model changed | `damp_collision` |
| Nothing was pending · an action failed | `subtle_collision` · `angry_alert` |

## Install

### 1. Logi Options+ plugin

You need **Logi Options+** with the MX Master 4 paired.

- **Option A — prebuilt package:** download `ClaudeCode_0.1.0.lplug4` (repo *Releases*, or *Actions* → latest run →
  artifact `claude-code-mx-master`) and double-click it.
- **Option B — build it** (needs the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Node 20+):

  macOS:
  ```bash
  ./scripts/build-plugin.sh
  ```
  Windows (PowerShell):
  ```powershell
  ./scripts/build-plugin.ps1
  ```

When the plugin loads it:

1. starts a server on `127.0.0.1:47823` (local only), protected by a random token stored in `~/.claude-mx/token`;
2. adds its HTTP hooks to `~/.claude/settings.json` (it keeps a backup in `settings.json.claude-mx.bak`, only touches
   entries whose URL contains `/claude-mx/hooks/`, and leaves your own hooks and settings as they are).

Restart Claude Desktop once afterwards so it picks up the hooks. To manage the hooks yourself, set
`"bridge": { "autoInstallHooks": false }` in your config; to remove them, delete the entries containing
`/claude-mx/hooks/` from `~/.claude/settings.json`.

**macOS:** allow *LogiPluginService* under **System Settings → Privacy & Security → Accessibility** (needed only for
the shortcut-based actions). Logs: `~/Library/Application Support/Logi/LogiPluginService/Logs/`.
**Windows:** no extra permission. Logs: `%LOCALAPPDATA%\Logi\LogiPluginService\Logs\`.

### 2. MX Master 4 profile

Follow **[docs/mx-master-4.md](docs/mx-master-4.md)**. Summary:

**Haptic button → Actions Ring** (*Claude* app profile), clockwise from the top:

| Bubble | Action |
|---|---|
| 1 (top) | New session |
| 2 | Send now |
| 3 | Approve |
| 4 | Approve all |
| 5 | Stop |
| 6 | Deny |
| 7 | 📁 Model & mode → Opus · Sonnet · Haiku · Model menu · Effort · Plan · Auto · Manual · Permission menu |
| 8 | 📁 More → Send · Accept suggestion · Diff · Terminal · Browser · Side chat · /compact · /context · /code-review |

**Gesture button** (keystrokes):

| Gesture | Action | macOS | Windows |
|---|---|---|---|
| Click | Send | `Return` | `Enter` |
| ↑ up | New session | `⌘N` | `Ctrl+N` |
| ↓ down | Stop | `Esc` | `Esc` |
| ← left | Previous session | `⌘⇧[` | `Ctrl+Shift+[` |
| → right | Next session | `⌘⇧]` | `Ctrl+Shift+]` |

If your Options+ version lets you assign plugin actions to gestures, use *Approve* / *Deny* there to answer without
opening the ring.

`profiles/ClaudeCode-ActionsRing-en.lp5` (and `-es.lp5`) contains this ring, ready to import (*experimental*).

### 3. Config (`~/.claude-mx/config.json`)

Only include what you want to change (Windows: `%USERPROFILE%\.claude-mx\config.json`):

```json
{
  "language": "auto",
  "bridge": { "port": 47823, "holdSeconds": 20, "autoInstallHooks": true },
  "permissionMenu": { "manual": 1, "acceptEdits": 2, "plan": 3, "auto": 4 },
  "models": [
    { "label": "Opus",   "menuIndex": 1 },
    { "label": "Sonnet", "menuIndex": 2 },
    { "label": "Haiku",  "menuIndex": 3 }
  ],
  "timing": { "menuOpenMs": 250 },
  "windows": { "keyMapping": "layout" }
}
```

- `bridge.holdSeconds`: how long a permission request waits for the mouse before Claude Desktop shows its dialog.
- `bridge.port`: change it if 47823 is taken (the hooks are rewritten on the next load).
- `permissionMenu` / `models`: positions (1-9) in Claude Desktop's menus, for the shortcut-based actions.
- `language`: `"auto"`, `"en"` or `"es"` (restart Logi Options+ after changing it).
- `windows.keyMapping`: `"layout"` (your keyboard layout) or `"us"`.

## MCP server

`mcp-server/` exposes the same actions as MCP tools. Permission tools talk to the plugin's server.

| Tool | Description |
|---|---|
| `pending_permissions` | Permission requests waiting for an answer |
| `decide_permission` | `approve` · `approve-all` · `deny` · `stop` (oldest request, or by id) |
| `new_session` | Opens `claude://code/new`, optionally with a prompt filled in |
| `run_action` | Any catalog action (direct interface first, shortcuts as fallback) |
| `set_permission_mode` · `switch_model` · `type_prompt` · `list_actions` | Mode/model menus, typing, listing |

```bash
cd mcp-server && npm install
```

```bash
claude mcp add claude-desktop-control -- node /PATH/claude-code-mx-master/mcp-server/src/index.js
```

Or in any MCP client config (`claude_desktop_config.json`, `.mcp.json`):

```json
{
  "mcpServers": {
    "claude-desktop-control": { "command": "node", "args": ["/PATH/claude-code-mx-master/mcp-server/src/index.js"] }
  }
}
```

CLI without an MCP client:

```bash
node mcp-server/src/cli.js run approve
```

```bash
CLAUDE_MX_DRY_RUN=1 node mcp-server/src/cli.js run new-session
```

## Tests

- **End to end with real Claude Code (v2.1.318):** `logi-plugin/tests/BridgeSmoke` starts the plugin's bridge and
  installs the hooks; Claude Code asks for permission; the plugin's actions answer:
  *Approve* → the command runs · *Deny* → Claude is told and continues · *Stop* → Claude is interrupted ·
  no answer → no decision · *Approve all* → three edits with a single prompt (the session switched to Accept edits).
- **CI:** MCP tests, generated files up to date, build against the official `PluginApi.dll` (NuGet `LogiPluginTool`
  6.4.2) with warnings as errors, C# ↔ Node parity (keystrokes and labels, en/es), hook installer (keeps your
  settings, idempotent, uninstalls cleanly), bridge C# ↔ Node over HTTP, `.lplug4` pack + verify.
- Not yet tested on a physical MX Master 4 with Claude Desktop: if something doesn't respond, open an issue with the
  plugin log.

## Limitations

- **Model, permission-mode menus, send now and panes still use shortcuts:** Claude Code has no official interface to
  trigger them from outside (hooks react to events; they can't start a model switch). They need Claude Desktop in front.
- **"Approve all" without a pending request** switches the mode through the menu (shortcut), since there's nothing to answer.
- **The `.lp5` profiles are experimental** (built from the format of an Options+ 6.4 export); if Options+ won't import
  one, build the ring by hand in 2 minutes.

## Adding actions

1. Edit `shared/actions.json` (`direct` for official interfaces, `steps` for shortcuts) and `shared/i18n.en.json`.
2. `node scripts/generate.mjs` regenerates the C# classes, icons, haptic events and `.lp5` profiles.
3. `cd mcp-server && npm test`, then rebuild the plugin.

## Layout

```
shared/                 action catalog, hook events, English strings, default config, ring layout
logi-plugin/src/        C# plugin (Logi Actions SDK) + Bridge/ (HTTP server, permission queue, hooks installer)
logi-plugin/tests/      parity (C# ↔ Node) and BridgeSmoke (end to end with Claude Code)
logi-plugin/stubs/      minimal PluginApi stub for the tests
mcp-server/             MCP server + CLI (Node)
profiles/               Actions Ring .lp5 profiles (en/es)
scripts/                generator, build, parity, bridge self-test
docs/                   MX Master 4 setup guide (en/es)
```

MIT license. Independent project, not affiliated with Anthropic or Logitech.
