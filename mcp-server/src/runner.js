// Punto común para ejecutar acciones: resuelve pasos y elige el emisor según el sistema.
import { findAction, loadCatalog, loadConfig, resolveSteps, displayNameFor, localize } from "./catalog.js";
import { sendMac, buildAppleScript } from "./senders/mac.js";
import { sendWindows, buildWindowsPlan } from "./senders/windows.js";

export function isDryRun() {
  return process.env.CLAUDE_MX_DRY_RUN === "1";
}

export async function sendSteps(steps, config, platform = process.platform) {
  if (isDryRun()) {
    return { dryRun: true, platform, plan: platform === "darwin" ? buildAppleScript(steps, config) : buildWindowsPlan(steps, config) };
  }
  if (platform === "darwin") await sendMac(steps, config);
  else if (platform === "win32") await sendWindows(steps, config);
  else throw new Error(`Plataforma no soportada: ${platform}. Claude Desktop existe para macOS y Windows.`);
  return { dryRun: false, platform };
}

export async function runAction(id, { catalog = loadCatalog(), config = loadConfig() } = {}) {
  const action = findAction(catalog, id);
  const steps = resolveSteps(action, config);
  const result = await sendSteps(steps, config);
  return { id, name: displayNameFor(action, config), steps, ...result };
}

export async function typePrompt(text, { submit = false, config = loadConfig() } = {}) {
  const steps = [{ type: "text", text }];
  if (submit) {
    steps.push({ type: "wait", ms: config.timing?.afterTextMs ?? 150 });
    steps.push({ type: "key", key: "enter", mods: [] });
  }
  return sendSteps(steps, config);
}

export function listActions({ catalog = loadCatalog(), config = loadConfig() } = {}) {
  return catalog.actions.map((a) => {
    const t = localize(a, config, catalog);
    return { id: a.id, name: t.displayName, group: t.group, description: t.description };
  });
}
