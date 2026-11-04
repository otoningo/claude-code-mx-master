namespace Loupedeck.ClaudeCodePlugin;

using Loupedeck.ClaudeCodePlugin.Bridge;
using Loupedeck.ClaudeCodePlugin.Core;
using Loupedeck.ClaudeCodePlugin.Platform;

// Base of every action. Order of preference:
//   1. Direct interface of the app (actions.json "direct"):
//      - "permission": answers the pending PermissionRequest through the Claude Code hooks.
//      - "deeplink":   opens a claude:// link (e.g. claude://code/new).
//   2. Keyboard shortcuts ("steps") only when there is no direct interface or it doesn't apply
//      (e.g. "Stop" with no pending request presses Esc).
// The concrete classes (one per action) are generated in Commands.g.cs by scripts/generate.mjs.
public abstract class ClaudeCommandBase : PluginDynamicCommand
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ActionDefinition _action;

    protected ClaudeCommandBase(string actionId)
        : this(ActionCatalog.Instance.Get(actionId))
    {
    }

    private ClaudeCommandBase(ActionDefinition action)
        : base(
            Localizer.DisplayName(action, ConfigStore.Load()),
            Localizer.Description(action, ConfigStore.Load()),
            Localizer.Group(action, ConfigStore.Load()))
    {
        this._action = action;
    }

    protected override bool OnLoad()
    {
        if (this._action.DirectType == "permission")
        {
            PermissionBroker.Instance.Changed += this.OnPendingChanged;
        }

        return true;
    }

    protected override bool OnUnload()
    {
        PermissionBroker.Instance.Changed -= this.OnPendingChanged;
        return true;
    }

    private void OnPendingChanged() => this.ActionImageChanged();

    protected override void RunCommand(string actionParameter) => _ = this.ExecuteAsync();

    // Name in the configured language. Permission actions show the pending tool
    // ("Aprobar · Bash"), and "Model 1/2/3" take the label from the config.
    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        try
        {
            var name = StepResolver.DisplayName(this._action, ConfigStore.Load());
            var pending = this._action.DirectType == "permission" ? PermissionBroker.Instance.Oldest : null;
            return pending == null ? name : $"{name} · {pending.ToolName}";
        }
        catch
        {
            return this._action.DisplayName;
        }
    }

    private async Task ExecuteAsync()
    {
        // One action at a time: avoids mixing keystrokes when two actions are pressed quickly.
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            PluginLog.Warning($"Action '{this._action.Id}' dropped: another action is still running");
            return;
        }

        try
        {
            var config = ConfigStore.Load();
            if (this.TryDirect(out var haptic))
            {
                this.Haptic(haptic);
                return;
            }

            var steps = StepResolver.Resolve(this._action, config);
            if (steps.Count == 0)
            {
                // Direct-only action (Approve / Deny) with nothing pending.
                this.Haptic("claudeNothingPending");
                return;
            }

            await KeySender.Create().SendAsync(steps, config);
            this.Haptic(this._action.Haptic);
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, $"Action '{this._action.Id}' failed");
            this.Haptic("claudeError");
        }
        finally
        {
            Gate.Release();
        }
    }

    // True if the direct interface handled the action.
    private bool TryDirect(out string haptic)
    {
        haptic = this._action.Haptic;
        switch (this._action.DirectType)
        {
            case "permission":
                return PermissionBroker.Instance.TryDecide((string)this._action.Direct["decision"]);

            case "deeplink":
                try
                {
                    BridgeHost.OpenUrl((string)this._action.Direct["url"]);
                    return true;
                }
                catch (Exception ex)
                {
                    PluginLog.Warning(ex, $"Couldn't open {(string)this._action.Direct["url"]}; using shortcuts");
                    return false;
                }

            default:
                return false;
        }
    }

    private void Haptic(string eventName) => (this.Plugin as ClaudeCodePlugin)?.RaiseHaptic(eventName);
}
