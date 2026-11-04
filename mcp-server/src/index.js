#!/usr/bin/env node
// Servidor MCP (stdio) que expone Claude Code Desktop como herramientas:
// nueva sesión, enviar ahora, aprobar todo, cambiar modelo, modo plan, etc.
import { McpServer } from "@modelcontextprotocol/sdk/server/mcp.js";
import { StdioServerTransport } from "@modelcontextprotocol/sdk/server/stdio.js";
import { z } from "zod";
import { loadCatalog, loadConfig, resolveSteps, findAction } from "./catalog.js";
import { listActions, runAction, sendSteps, typePrompt } from "./runner.js";

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
      "Ejecuta una acción en Claude Code Desktop enviando sus atajos de teclado (trae la app al frente). " +
      "Ejemplos: new-session, send-now, approve-all, plan-mode, model-menu, stop, toggle-diff.",
    inputSchema: { action: z.enum(actionIds).describe("Id de la acción (ver list_actions)") },
  },
  async ({ action }) => guard(() => runAction(action, { catalog, config: loadConfig() })),
);

server.registerTool(
  "new_session",
  {
    title: "Nueva sesión",
    description: "Abre una sesión nueva en Claude Code Desktop y, opcionalmente, escribe (y envía) un prompt inicial.",
    inputSchema: {
      prompt: z.string().optional().describe("Texto a escribir en la sesión nueva"),
      submit: z.boolean().default(false).describe("Enviar el prompt con Enter"),
    },
  },
  async ({ prompt, submit }) =>
    guard(async () => {
      const config = loadConfig();
      const steps = resolveSteps(findAction(catalog, "new-session"), config);
      if (prompt) {
        steps.push({ type: "wait", ms: 600 }, { type: "text", text: prompt });
        if (submit) steps.push({ type: "wait", ms: config.timing?.afterTextMs ?? 150 }, { type: "key", key: "enter", mods: [] });
      }
      return sendSteps(steps, config);
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
    guard(() => {
      const ids = { manual: "manual-mode", acceptEdits: "approve-all", plan: "plan-mode", auto: "auto-mode" };
      return runAction(ids[mode], { catalog, config: loadConfig() });
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
