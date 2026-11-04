namespace Loupedeck.ClaudeCodePlugin;

// Vincula el plugin con la app de escritorio de Claude (pestaña Code = Claude Code Desktop).
// Logi Options+ activa el perfil del plugin cuando Claude está en primer plano.
public sealed class ClaudeCodeApplication : ClientApplication
{
    // Windows: Claude.exe
    protected override string[] GetProcessNames() => new[] { "claude", "Claude" };

    // macOS: /Applications/Claude.app
    protected override string GetBundleName() => "com.anthropic.claudefordesktop";

    public override ClientApplicationStatus GetApplicationStatus() => ClientApplicationStatus.Unknown;
}
