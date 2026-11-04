// Punto común para ejecutar acciones: resuelve pasos y elige el emisor según el sistema.
import { findAction, loadCatalog, loadConfig, resolveSteps, displayNameFor, localize } from "./catalog.js";
import { sendMac, buildAppleScript } from "./senders/mac.js";
import { sendWindows, buildWindowsPlan } from "./senders/windows.js";
import { decidePermission, openClaudeUrl, pendingPermissions } from "./bridge.js";

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

// Same order as the plugin: direct interface first (permission via the bridge, claude:// link),
// keyboard shortcuts only as a fallback.
export async function runAction(id, { catalog = loadCatalog(), config = loadConfig(), bridge = defaultBridge } = {}) {
  const action = findAction(catalog, id);
  const name = displayNameFor(action, config);
  const steps = resolveSteps(action, config);
  const direct = action.direct;
  let directNote;

  if (direct?.type === "permission") {
    if (isDryRun()) return { id, name, dryRun: true, direct: { type: "permission", decision: direct.decision }, fallbackSteps: steps };
    try {
      const res = await bridge.decide(config, direct.decision);
      if (res.ok) return { id, name, direct: { type: "permission", decision: direct.decision, decided: true } };
      directNote = "No permission request was pending.";
    } catch (err) {
      directNote = err.message;
    }
    if (steps.length === 0) return { id, name, direct: { type: "permission", decision: direct.decision, decided: false }, message: directNote };
  } else if (direct?.type === "deeplink") {
    if (isDryRun()) return { id, name, dryRun: true, direct: { type: "deeplink", url: direct.url }, fallbackSteps: steps };
    try {
      await bridge.open(direct.url);
      return { id, name, direct: { type: "deeplink", url: direct.url } };
    } catch (err) {
      directNote = err.message;
    }
  }

  const result = await sendSteps(steps, config);
  return { id, name, steps, ...result, ...(directNote ? { fallbackReason: directNote } : {}) };
}

export const defaultBridge = {
  decide: (config, decision, id) => decidePermission(config, decision, id),
  pending: (config) => pendingPermissions(config),
  open: (url) => openClaudeUrl(url),
};

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
