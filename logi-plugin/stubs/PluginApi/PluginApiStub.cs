// Superficie mínima de la API de Logi Actions SDK que usa el plugin, para poder compilar
// sin Logi Options+ instalado. Las firmas siguen los ejemplos oficiales (github.com/Logitech/actions-sdk).
namespace Loupedeck;

public enum ClientApplicationStatus { Unknown, Installed, NotInstalled }

public enum PluginImageSize { None, Width60, Width90, Width116 }

public class PluginLogFile
{
    public void Info(string text) => Console.Error.WriteLine($"[info] {text}");
    public void Warning(string text) => Console.Error.WriteLine($"[warn] {text}");
    public void Warning(Exception ex, string text) => Console.Error.WriteLine($"[warn] {text}: {ex.Message}");
    public void Error(string text) => Console.Error.WriteLine($"[error] {text}");
    public void Error(Exception ex, string text) => Console.Error.WriteLine($"[error] {text}: {ex.Message}");
}

public class PluginEvents
{
    public readonly List<string> Raised = new();
    public void AddEvent(string name, string displayName, string description) { }
    public void RaiseEvent(string name) => this.Raised.Add(name);
}

public abstract class ClientApplication
{
    protected virtual string GetProcessName() => "";
    protected virtual string[] GetProcessNames() => new[] { this.GetProcessName() };
    protected virtual string GetBundleName() => "";
    public virtual ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}

public abstract class Plugin
{
    public virtual bool UsesApplicationApiOnly => false;
    public virtual bool HasNoApplication => false;
    public PluginLogFile Log { get; } = new();
    public PluginEvents PluginEvents { get; } = new();
    public abstract void Load();
    public abstract void Unload();
}

public abstract class PluginDynamicCommand
{
    protected PluginDynamicCommand(string displayName, string description, string groupName)
    {
        this.DisplayName = displayName;
        this.Description = description;
        this.GroupName = groupName;
    }

    public string DisplayName { get; }
    public string Description { get; }
    public string GroupName { get; }
    public Plugin Plugin { get; set; }

    protected virtual void RunCommand(string actionParameter) { }
    protected virtual string GetCommandDisplayName(string actionParameter, PluginImageSize imageSize) => this.DisplayName;
    protected virtual bool OnLoad() => true;

    // Solo para pruebas con el stub.
    public void Run(string actionParameter = null) => this.RunCommand(actionParameter);
    public string GetName() => this.GetCommandDisplayName(null, PluginImageSize.None);
}
