#!/usr/bin/env node
// Servidor MCP (stdio) que expone Claude Code Desktop como herramientas:
// nueva sesión, enviar ahora, aprobar todo, cambiar modelo, modo plan, etc.
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { loadCatalog, loadConfig, resolveSteps, findAction } from "./catalog.js";
import { listActions, runAction, sendSteps, typePrompt, isDryRun, defaultBridge } from "./runner.js";

const catalog = loadCatalog();
const actionIds = catalog.actions.map((a) => a.id);

const server = new McpServer({ name: "claude-desktop-control", version: "0.1.0" });

function ok(data) {
  return { content: [{ type: "text", text: typeof data === "string" ? data : JSON.stringify(data, null, 2) }] };
}

function fail(err) {
  return { isError: true, content: [{ type: "text", text: err instanceof Error ? err.message : String(err) }] };
}

async function guard(fn) {
  try {
    return ok(await fn());
  } catch (err) {
    return fail(err);
  }
}

server.registerTool(
  "list_actions",
  {
    title: "Listar acciones",
    description: "Lista todas las acciones de Claude Code Desktop que este servidor puede ejecutar (las mismas que el plugin del MX Master 4).",
    inputSchema: {},
  },
  async () => guard(() => listActions({ catalog, config: loadConfig() })),
);

server.registerTool(
  "run_action",
  {
    title: "Ejecutar acción",
    description:
      "Ejecuta una acción en Claude Code Desktop. Usa la interfaz directa cuando existe (approve/approve-all/deny/stop " +
      "responden la solicitud de permiso pendiente a través del plugin; new-session abre claude://code/new) y si no, " +
      "atajos de teclado. Ejemplos: new-session, approve, approve-all, deny, stop, send-now, plan-mode, model-menu.",
    inputSchema: { action: z.enum(actionIds).describe("Id de la acción (ver list_actions)") },
  },
  async ({ action }) => guard(() => runAction(action, { catalog, config: loadConfig() })),
);

server.registerTool(
  "new_session",
  {
    title: "Nueva sesión",
    description:
      "Abre una sesión nueva en Claude Code Desktop con el enlace oficial claude://code/new. Si das un prompt, " +
      "Desktop lo deja escrito en la caja (nunca lo envía solo); con submit=true se envía con Enter.",
    inputSchema: {
      prompt: z.string().optional().describe("Texto para la caja de prompt de la sesión nueva"),
      submit: z.boolean().default(false).describe("Enviar el prompt con Enter"),
    },
  },
  async ({ prompt, submit }) =>
    guard(async () => {
      const config = loadConfig();
      const url = prompt ? `claude://code/new?q=${encodeURIComponent(prompt)}` : "claude://code/new";
      if (isDryRun()) return { dryRun: true, url, submit };
      await defaultBridge.open(url);
      if (prompt && submit) {
        await sendSteps([{ type: "wait", ms: 1200 }, { type: "key", key: "enter", mods: [] }], config);
      }
      return { opened: url, submitted: Boolean(prompt && submit) };
    }),
);

server.registerTool(
  "pending_permissions",
  {
    title: "Permisos pendientes",
    description: "Lista las solicitudes de permiso de Claude Code que están esperando respuesta (vía el plugin de Logi Options+).",
    inputSchema: {},
  },
  async () => guard(() => defaultBridge.pending(loadConfig())),
);

server.registerTool(
  "decide_permission",
  {
    title: "Responder permiso",
    description:
      "Responde una solicitud de permiso pendiente de Claude Code: approve (solo esa), approve-all (esa y pasa a Accept edits), " +
      "deny (Claude sigue con otra estrategia) o stop (rechaza y detiene a Claude). Sin id responde la más antigua.",
    inputSchema: {
      decision: z.enum(["approve", "approve-all", "deny", "stop"]),
      id: z.string().optional().describe("Id de pending_permissions; por defecto la más antigua"),
    },
  },
  async ({ decision, id }) =>
    guard(async () => {
      const res = await defaultBridge.decide(loadConfig(), decision, id);
      if (!res.ok) throw new Error("No había ninguna solicitud de permiso pendiente con ese id.");
      return { decided: decision };
    }),
);

server.registerTool(
  "set_permission_mode",
  {
    title: "Cambiar modo de permisos",
    description:
      "Cambia el modo de permisos de la sesión actual. acceptEdits = 'aprobar todo' (aplica ediciones sin preguntar), " +
      "plan = solo planear, auto = modo automático, manual = pedir aprobación para cada cambio.",
    inputSchema: { mode: z.enum(["manual", "acceptEdits", "plan", "auto"]) },
  },
  async ({ mode }) =>
    guard(async () => {
      // Only the mode (menu); to also approve a pending request use decide_permission.
      const ids = { manual: "manual-mode", acceptEdits: "approve-all", plan: "plan-mode", auto: "auto-mode" };
      const config = loadConfig();
      return sendSteps(resolveSteps(findAction(catalog, ids[mode]), config), config);
    }),
);

server.registerTool(
  "switch_model",
  {
    title: "Cambiar modelo",
    description:
      "Cambia el modelo de la sesión actual. Acepta el nombre configurado (p. ej. 'Opus', 'Sonnet', 'Haiku') " +
      "o la posición 1-9 dentro del menú de modelos de Claude Desktop.",
    inputSchema: { model: z.string().describe("Nombre del modelo configurado o posición en el menú (1-9)") },
  },
  async ({ model }) =>
    guard(async () => {
      const config = loadConfig();
      let index;
      if (/^[1-9]$/.test(model.trim())) {
        index = Number(model.trim());
      } else {
        const match = (config.models || []).find((m) => m.label.toLowerCase() === model.trim().toLowerCase());
        if (!match) {
          const known = (config.models || []).map((m) => m.label).join(", ");
          throw new Error(`Modelo "${model}" no está en la configuración (${known}). Usa una posición 1-9 o edita ~/.claude-mx/config.json.`);
        }
        index = match.menuIndex;
      }
      const steps = [
        { type: "key", key: "i", mods: ["mod", "shift"] },
        { type: "wait", ms: config.timing?.menuOpenMs ?? 250 },
        { type: "key", key: String(index), mods: [] },
      ];
      return { model, menuIndex: index, ...(await sendSteps(steps, config)) };
    }),
);

server.registerTool(
  "type_prompt",
  {
    title: "Escribir en el prompt",
    description: "Escribe texto en la caja de prompt de Claude Code Desktop (en la sesión enfocada) y opcionalmente lo envía.",
    inputSchema: {
      text: z.string().min(1),
      submit: z.boolean().default(false).describe("Enviar con Enter al terminar de escribir"),
    },
  },
  async ({ text, submit }) => guard(() => typePrompt(text, { submit, config: loadConfig() })),
);

await server.connect(new StdioServerTransport());
