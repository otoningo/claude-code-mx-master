namespace Loupedeck.ClaudeCodePlugin;

using Loupedeck.ClaudeCodePlugin.Core;
using Loupedeck.ClaudeCodePlugin.Platform;

// Base de todas las acciones: busca su definición en actions.json, resuelve los pasos con la
// configuración actual, envía las teclas a Claude Desktop y dispara la vibración háptica.
// Las clases concretas (una por acción) se generan en Commands.g.cs con scripts/generate.mjs.
public abstract class ClaudeCommandBase : PluginDynamicCommand
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ActionDefinition _action;

    protected ClaudeCommandBase(string actionId)
        : this(ActionCatalog.Instance.Get(actionId))
    {
    }

    private ClaudeCommandBase(ActionDefinition action)
        : base(action.DisplayName, action.Description, action.GroupDisplayName)
    {
        this._action = action;
    }

    protected override void RunCommand(string actionParameter) => _ = this.ExecuteAsync();

    // El nombre de los atajos "Modelo 1/2/3" sale de la configuración (p. ej. "Modelo: Opus").
    protected override string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize)
    {
        try
        {
            return StepResolver.DisplayName(this._action, ConfigStore.Load());
        }
        catch
        {
            return this._action.DisplayName;
        }
    }

    private async Task ExecuteAsync()
    {
        // Una acción a la vez: evita mezclar teclas si se pulsan dos acciones muy rápido.
        if (!await Gate.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            PluginLog.Warning($"Acción '{this._action.Id}' descartada: otra acción sigue en curso");
            return;
        }

        try
        {
            var config = ConfigStore.Load();
            var steps = StepResolver.Resolve(this._action, config);
            await KeySender.Create().SendAsync(steps, config);
            this.Haptic(this._action.Haptic);
        }
        catch (Exception ex)
        {
            PluginLog.Error(ex, $"La acción '{this._action.Id}' falló");
            this.Haptic("claudeError");
        }
        finally
        {
            Gate.Release();
        }
    }

    private void Haptic(string eventName) => (this.Plugin as ClaudeCodePlugin)?.RaiseHaptic(eventName);
}
