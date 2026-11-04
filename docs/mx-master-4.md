# Set up the MX Master 4 for Claude Code Desktop

**English** · [Español](mx-master-4.es.md)

Everything lives in an **app-specific profile for Claude**, so the buttons only change while Claude Desktop is in
the foreground; everywhere else the mouse behaves as usual.

> Menu names may differ slightly between Logi Options+ versions.

## 0. Before you start

1. Install **Logi Options+** and pair the MX Master 4 (Bluetooth or Logi Bolt receiver).
2. Install the plugin: double-click `ClaudeCode_0.1.0.lplug4`. The Logi Plugin Service installer opens; accept it.
   If Windows or macOS warns that the file came from the internet, confirm you want to open it.
3. **macOS only:** System Settings → Privacy & Security → **Accessibility** → enable *LogiPluginService*
   (and *Logi Options+* if listed). Not needed on Windows.
4. **Restart Claude Desktop** after installing the plugin: when it loads, the plugin adds its hooks to
   `~/.claude/settings.json` (Windows: `C:\Users\YourName\.claude\settings.json`) and Claude Desktop reads them on
   startup. That's how the mouse approves/denies directly and vibrates on Claude's events.
5. Open Claude Desktop and go to the **Code** tab.

## 1. Create the Claude profile

1. Open **Logi Options+** and select the **MX Master 4**.
2. At the top, in the app selector (*All applications*), click **+ Add application** and pick **Claude**.
3. With *Claude* selected, everything below applies only to Claude.

## 2. Haptic button → Actions Ring

The haptic button (the thumb panel) opens the **Actions Ring**.

![Actions Ring preview](actions-ring-preview.png)

Clockwise from the top: New session · Send now · Approve · Approve all · Stop · Deny · Model & mode · More.

When Claude asks for permission, the mouse knocks and the permission bubbles show the tool, e.g. **Approve · Bash**.
You have 20 s (`bridge.holdSeconds`) to answer from the mouse; otherwise Claude Desktop shows its usual dialog.

### Option A: import the profile (experimental)

1. In Options+, open the **Actions Ring** settings with the *Claude* profile selected.
2. In the profile menu (the **⋯** next to the profile name) choose **Import profile** and select
   `ClaudeCode-ActionsRing-en.lp5` (or `-es.lp5` for Spanish).
3. If it's rejected or shows up empty, use option B.

### Option B: build it by hand (2 minutes)

In the *Claude* profile's Actions Ring, drag actions from the **Claude Code** group onto each bubble:

| Bubble | Action (plugin group) |
|---|---|
| 1 (top) | New session *(Sessions)* |
| 2 | Send now *(Prompt)* |
| 3 | Approve *(Permissions)* |
| 4 | Approve all *(Permissions)* |
| 5 | Stop *(Prompt)* |
| 6 | Deny *(Permissions)* |
| 7 | **Model & mode** folder: Model: Opus · Model: Sonnet · Model: Haiku · Switch model · Effort · Plan mode · Auto mode · Manual mode · Permission menu |
| 8 | **More** folder: Send · Accept suggestion · Diff pane · Terminal · Browser · Side chat · /compact · /context · /code-review |

Folders are created with the Actions Ring's own **Folder** (navigation) action.

Once it looks right, you can **Export profile** and replace `profiles/ClaudeCode-ActionsRing-en.lp5` in the repo
with your export.

### Haptic feedback

In Options+ → MX Master 4 → **Haptic feedback**, keep it enabled. Each plugin action uses its own waveform:

| Event | Waveform | When |
|---|---|---|
| Needs approval | `knock` | Claude is waiting for your approval |
| Waiting for you | `ringing` | Claude is idle or asked you a question |
| Finished | `completed` | Claude finished its turn |
| Approved | `happy_alert` | Approve, Approve all, Auto mode |
| Denied | `damp_collision` | Deny |
| Stopped | `sharp_collision` | Stop |
| Model | `damp_collision` | Model / effort change |
| Message sent | `wave` | Send, Send now, /compact… |
| New session | `damp_state_change` | New session |
| Navigation | `subtle_collision` | Next / previous session |
| Nothing pending | `subtle_collision` | You pressed Approve/Deny with no pending request |
| Error | `angry_alert` | The action failed |

To change them, edit `haptics` in `shared/actions.json` and run `node scripts/generate.mjs`.

## 3. Gesture button

With the *Claude* profile selected, click the **gesture button** (the thumb button in front of the haptic panel) and
choose **Gestures** / **Custom gestures**. For each direction assign a **Keystroke**:

| Gesture | Action | macOS | Windows |
|---|---|---|---|
| Click | Send | `Return` | `Enter` |
| Hold + ↑ | New session | `⌘ N` | `Ctrl N` |
| Hold + ↓ | Stop | `Esc` | `Esc` |
| Hold + ← | Previous session | `⌘ ⇧ [` | `Ctrl Shift [` |
| Hold + → | Next session | `⌘ ⇧ ]` | `Ctrl Shift ]` |

If your Options+ version lets you assign **plugin actions** to gestures, I recommend: click = *Approve*,
↓ = *Deny* (or *Stop*). That way you answer Claude without opening the ring.

## 4. Thumb wheel (optional)

In the *Claude* profile, the horizontal wheel can keep its normal scrolling (handy in the diff pane). If you prefer,
set it to keystrokes to switch sessions: left `Ctrl Shift Tab`, right `Ctrl Tab`.

## 5. Match your menu order

In Claude Desktop, open the permission menu (`Ctrl+Shift+M` / `⌘⇧M`) and the model menu (`Ctrl+Shift+I` / `⌘⇧I`)
and count where each option is. If it doesn't match 1 = Manual, 2 = Accept edits, 3 = Plan, 4 = Auto and
1 = Opus, 2 = Sonnet, 3 = Haiku, create the config file:

- **Windows:** `%USERPROFILE%\.claude-mx\config.json` (e.g. `C:\Users\YourName\.claude-mx\config.json`)
- **macOS:** `~/.claude-mx/config.json`

```json
{
  "permissionMenu": { "manual": 1, "acceptEdits": 2, "plan": 3, "auto": 4 },
  "models": [
    { "label": "Opus",   "menuIndex": 1 },
    { "label": "Sonnet", "menuIndex": 2 },
    { "label": "Haiku",  "menuIndex": 3 }
  ]
}
```

## Troubleshooting

| Symptom | Fix |
|---|---|
| The action vibrates with the error pattern and nothing happens (macOS) | Grant Accessibility permission to LogiPluginService |
| "Approve all" or "Model: X" picks the wrong option | Adjust `permissionMenu` / `models` in `config.json` to your actual menu order |
| The menu opens but nothing gets picked | Raise `timing.menuOpenMs` (e.g. to 400) |
| Shortcuts with `[`, `]`, `` ` `` don't respond (Windows) | Set `windows.keyMapping` to `"us"` |
| Plugin actions don't show up | Restart Logi Options+; make sure the *Claude* profile is selected |
| The mouse doesn't vibrate when Claude asks for permission | Restart Claude Desktop; check that `~/.claude/settings.json` has entries with `/claude-mx/hooks/` and that Logi Options+ is running |
| Approve/Deny vibrate "nothing pending" | No request was waiting (or `holdSeconds` already ran out and Claude showed its dialog) |
| The log says port 47823 couldn't be opened | Another program uses it: set another `bridge.port` and restart Logi Options+ and Claude Desktop |
| Labels show in the wrong language | Set `"language": "en"` or `"es"` in `config.json` and restart Logi Options+ |
