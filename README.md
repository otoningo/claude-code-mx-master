# Claude Code × MX Master 4

Controla **Claude Code Desktop** (la pestaña *Code* de la app de escritorio de Claude) desde el
**Logitech MX Master 4**: el **botón háptico** (Actions Ring) y el **botón de gestos**. Además expone las mismas
funciones como **servidor MCP**, para controlar la app desde cualquier cliente MCP.

```
                 shared/actions.json  ← catálogo único de acciones (atajos de Claude Desktop)
                 shared/config.default.json
                        │
   ┌────────────────────┼─────────────────────────┐
   ▼                    ▼                         ▼
logi-plugin/        mcp-server/              profiles/
Plugin C# para      Servidor MCP (stdio)     Perfil del Actions Ring (.lp5)
Logi Options+       + CLI `claude-mx`        + guía del botón de gestos
(Actions Ring,      (tools: run_action,
 vibración háptica)  switch_model, ...)
```

Las tres piezas leen el mismo catálogo, así que una acción nueva se agrega en un solo lugar.

## Acciones

| Grupo | Acción | Qué hace en Claude Desktop |
|---|---|---|
| Sesiones | **Nueva sesión** | `Cmd/Ctrl+N` |
| | Cerrar sesión | `Cmd/Ctrl+W` |
| | Sesión siguiente / anterior | `Cmd/Ctrl+Shift+]` / `[` |
| Prompt | **Enviar ahora** | `Esc` (detiene la respuesta en curso) y luego `Enter` (envía lo que escribiste) |
| | Enviar | `Enter` (si Claude está trabajando, el mensaje entra al terminar la acción actual) |
| | **Detener** | `Esc` |
| | Aceptar sugerencia | `Tab` (con la caja vacía) |
| Permisos | **Aprobar todo** | Menú de permisos (`Cmd/Ctrl+Shift+M`) → *Accept edits*: aplica ediciones sin pedir aprobación una por una |
| | Modo Auto / Plan / Manual | Menú de permisos → opción correspondiente |
| | Menú de permisos | `Cmd/Ctrl+Shift+M` |
| Modelo | **Cambiar modelo** | `Cmd/Ctrl+Shift+I` (abre el menú) |
| | Modelo: Opus / Sonnet / Haiku | Menú de modelos → opción 1 / 2 / 3 (configurable) |
| | Esfuerzo | `Cmd/Ctrl+Shift+E` |
| Vista | Panel de diff · Terminal · Navegador | `Cmd/Ctrl+Shift+D` · ``Ctrl+` `` · `Cmd/Ctrl+Shift+B` |
| | Chat lateral · Cambiar vista · Cerrar panel · Atajos | `Cmd/Ctrl+;` · `Ctrl+O` · `Cmd/Ctrl+\` · `Cmd/Ctrl+/` |
| Comandos | `/compact` · `/context` · `/code-review` | Escribe el comando y lo envía (usar con la caja vacía) |

Los atajos son los documentados en <https://code.claude.com/docs/en/desktop> (tabla *Keyboard shortcuts*).

Cada acción vibra en el MX Master 4 con una forma de onda distinta (enviar = `completed`, aprobar = `happy_alert`,
detener = `sharp_collision`, error = `angry_alert`, …).

## Instalación

### 1. Plugin de Logi Options+

Necesitas **Logi Options+** con el MX Master 4 emparejado.

- **Opción A — paquete listo:** descarga `ClaudeCode_0.1.0.lplug4` (pestaña *Actions* del repo → último run → artefacto
  `claude-code-mx-master`) y haz doble clic. Logi Plugin Service lo instala.
- **Opción B — compilar** (requiere [.NET 10 SDK](https://dotnet.microsoft.com/download) y Node 20+):

  macOS:
  ```bash
  ./scripts/build-plugin.sh
  ```
  Windows (PowerShell):
  ```powershell
  ./scripts/build-plugin.ps1
  ```
  Si Options+ está instalado, el plugin queda enlazado al servicio y se recarga solo; el `.lplug4` queda en `dist/`.

**macOS:** la primera vez, autoriza a *LogiPluginService* en **Ajustes del Sistema → Privacidad y seguridad →
Accesibilidad** (el plugin envía teclas a Claude con System Events). Si una acción vibra con el patrón de error,
casi siempre es esto. El log está en `~/Library/Application Support/Logi/LogiPluginService/Logs/`.

### 2. Perfil del MX Master 4

Sigue **[docs/mx-master-4.md](docs/mx-master-4.md)**. Resumen:

**Botón háptico → Actions Ring** (perfil de la app *Claude*):

| Burbuja | Acción |
|---|---|
| 1 | Nueva sesión |
| 2 | Enviar ahora |
| 3 | Aprobar todo |
| 4 | 📁 Modelo → Opus · Sonnet · Haiku · Menú de modelos · Esfuerzo |
| 5 | Detener |
| 6 | Modo Plan |
| 7 | 📁 Vista → Diff · Terminal · Navegador · Chat lateral · Cambiar vista · Cerrar panel · Atajos |
| 8 | 📁 Más → Enviar · Aceptar sugerencia · Modo Auto · Modo Manual · Menú de permisos · /compact · /context · /code-review |

**Botón de gestos** (atajos de teclado, funcionan incluso sin el plugin):

| Gesto | Acción | macOS | Windows |
|---|---|---|---|
| Clic | Enviar | `Return` | `Enter` |
| ↑ arriba | Nueva sesión | `⌘N` | `Ctrl+N` |
| ↓ abajo | Detener | `Esc` | `Esc` |
| ← izquierda | Sesión anterior | `⌘⇧[` | `Ctrl+Shift+[` |
| → derecha | Sesión siguiente | `⌘⇧]` | `Ctrl+Shift+]` |

`profiles/ClaudeCode-ActionsRing.lp5` contiene el Actions Ring de arriba ya armado, para importarlo en Options+
(*experimental*, ver [limitaciones](#limitaciones)). Lo genera `node scripts/generate.mjs` a partir de
`shared/ring-layout.json`.

### 3. Ajustar a tu Claude Desktop (`~/.claude-mx/config.json`)

"Aprobar todo", "Modo Plan" y "Modelo: X" abren un menú de Claude Desktop y eligen la opción por su **posición**
(1-9). Si en tu app el orden es otro, copia `shared/config.default.json` a `~/.claude-mx/config.json`
(Windows: `%USERPROFILE%\.claude-mx\config.json`) y ajusta:

```json
{
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

Solo pon las claves que quieras cambiar; el resto se toma de los valores por defecto. El plugin relee el archivo
cuando cambia (no hace falta reiniciar nada) y el nombre de las burbujas "Modelo: …" sale de `label`.

- `timing.menuOpenMs`: súbelo (p. ej. 400) si el menú no alcanza a abrirse antes de elegir la opción.
- `windows.keyMapping`: `"layout"` usa tu distribución de teclado (p. ej. español latinoamericano) para producir
  `[`, `]`, `` ` ``, `;`… Si un atajo con esas teclas no responde, prueba `"us"`.

## Servidor MCP

"Volver Claude Code Desktop un MCP": `mcp-server/` expone la app como herramientas MCP.

| Tool | Descripción |
|---|---|
| `list_actions` | Lista las acciones disponibles |
| `run_action` | Ejecuta cualquier acción del catálogo (`new-session`, `send-now`, `approve-all`, …) |
| `new_session` | Abre una sesión nueva y opcionalmente escribe/envía un prompt |
| `set_permission_mode` | `manual` · `acceptEdits` · `plan` · `auto` |
| `switch_model` | Por nombre (`"Opus"`) o por posición en el menú (`"2"`) |
| `type_prompt` | Escribe texto en la caja de prompt y opcionalmente lo envía |

Instalar:

```bash
cd mcp-server && npm install
```

Registrarlo en Claude Code (CLI):

```bash
claude mcp add claude-desktop-control -- node /RUTA/claude-code-mx-master/mcp-server/src/index.js
```

O en cualquier cliente MCP con JSON (p. ej. `claude_desktop_config.json` o `.mcp.json`):

```json
{
  "mcpServers": {
    "claude-desktop-control": {
      "command": "node",
      "args": ["/RUTA/claude-code-mx-master/mcp-server/src/index.js"]
    }
  }
}
```

También hay una CLI para probar sin cliente MCP:

```bash
node mcp-server/src/cli.js list
```

```bash
node mcp-server/src/cli.js run approve-all
```

```bash
CLAUDE_MX_DRY_RUN=1 node mcp-server/src/cli.js run send-now
```

(`CLAUDE_MX_DRY_RUN=1` muestra el AppleScript / plan de teclas sin enviar nada.)

> No confundir con `claude mcp serve`, que expone las *herramientas* de Claude Code (leer/editar archivos, bash…)
> como servidor MCP. Este proyecto expone la *interfaz* de Claude Code Desktop (sesiones, permisos, modelo…).

## Agregar o cambiar acciones

1. Edita `shared/actions.json` (y `shared/ring-layout.json` si quieres ponerla en el Actions Ring).
2. Ejecuta `node scripts/generate.mjs` — regenera las clases C#, iconos, eventos hápticos y el `.lp5`.
3. `cd mcp-server && npm test` y vuelve a compilar el plugin.

Pasos disponibles: atajo (`"mod+shift+i"`, `mod` = Cmd en macOS / Ctrl en Windows), tecla (`"esc"`, `"enter"`,
`"tab"`…), `{"text": "..."}`, `{"wait": 250}` y placeholders de la config (`"${permissionMenu.plan}"`).

## Limitaciones

- **Funciona por atajos de teclado.** Claude Desktop no tiene API de control externo, así que todo se hace con las
  teclas documentadas. Si Anthropic cambia un atajo o el orden de un menú, se ajusta en `actions.json` o en tu
  `config.json`.
- **"Aprobar todo" = modo *Accept edits*.** Claude Desktop no documenta un atajo para pulsar *Allow* en un aviso de
  permiso concreto; lo que hace esta acción es cambiar el modo para que no vuelva a preguntar por ediciones.
- **"Enviar ahora" detiene la respuesta en curso** antes de enviar. Si solo quieres encolar el mensaje sin
  interrumpir, usa "Enviar".
- **`/compact`, `/context`, `/code-review`** escriben el texto en la caja: úsalos con la caja vacía.
- **El `.lp5` es experimental:** está construido con el formato de un perfil exportado por Options+ 6.4, pero Logitech
  recomienda crear los perfiles desde la interfaz. Si Options+ no lo importa, arma el anillo a mano en 2 minutos con
  la tabla de arriba y, si quieres, expórtalo para reemplazar el del repo.
- **Probado en compilación, no en hardware:** el plugin compila contra el `PluginApi.dll` oficial (paquete NuGet
  `LogiPluginTool` 6.4.2), el `.lplug4` pasa `logiplugintool verify`, y una prueba de paridad confirma que el plugin y
  el servidor MCP generan exactamente las mismas secuencias de teclas para las 28 acciones. Falta probarlo con un
  MX Master 4 real; si algo no responde, abre un issue con el log del plugin.

## Estructura

```
shared/                 catálogo de acciones, config por defecto y layout del Actions Ring
logi-plugin/src/        plugin C# (Logi Actions SDK)
logi-plugin/tests/      prueba de paridad C# ↔ Node
logi-plugin/stubs/      stub mínimo de PluginApi, solo para la prueba de paridad en CI
mcp-server/             servidor MCP + CLI (Node)
profiles/               perfil .lp5 del Actions Ring
scripts/                generador, build y paridad
docs/                   guía de configuración del MX Master 4
```

Licencia MIT. Proyecto independiente, no afiliado a Anthropic ni a Logitech.
