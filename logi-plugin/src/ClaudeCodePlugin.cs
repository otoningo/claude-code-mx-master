namespace Loupedeck.ClaudeCodePlugin;

using Loupedeck.ClaudeCodePlugin.Core;

// Plugin de aplicación: sus acciones aparecen en el perfil de Claude (Claude Desktop)
// en Logi Options+, incluido el Actions Ring del botón háptico del MX Master 4.
public class ClaudeCodePlugin : Plugin
{
    public override bool UsesApplicationApiOnly => true;

    public override bool HasNoApplication => false;

    public ClaudeCodePlugin()
    {
        PluginLog.Init(this.Log);
    }

    public override void Load()
    {
        // Registra los eventos hápticos definidos en shared/actions.json
        // (los mismos que declara package/events/DefaultEventSource.yaml).
        var config = ConfigStore.Load();
        foreach (var haptic in ActionCatalog.Instance.Haptics)
        {
            var (displayName, description) = Localizer.Haptic(haptic, config);
            this.PluginEvents.AddEvent(haptic.Name, displayName, description);
        }

        PluginLog.Info($"Claude Code plugin cargado: {ActionCatalog.Instance.Actions.Count} acciones, idioma {Localizer.Language(config)}, config en {ConfigStore.UserConfigPath}");
    }

    public override void Unload()
    {
    }

    internal void RaiseHaptic(string eventName)
    {
        if (string.IsNullOrEmpty(eventName))
        {
            return;
        }

        try
        {
            this.PluginEvents.RaiseEvent(eventName);
        }
        catch (Exception ex)
        {
            PluginLog.Warning(ex, $"No se pudo lanzar el evento háptico '{eventName}'");
        }
    }
}
