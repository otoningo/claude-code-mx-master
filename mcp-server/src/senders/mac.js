// macOS: envía los pasos con AppleScript (System Events).
// Requiere permiso de Accesibilidad para la app que lanza el proceso
// (Terminal, Claude, o el cliente MCP que ejecute este servidor).
import { execFile } from "node:child_process";

// Códigos de tecla físicos de macOS para teclas sin carácter.
const MAC_KEY_CODES = {
  esc: 53,
  enter: 36,
  tab: 48,
  space: 49,
  backspace: 51,
  delete: 117,
  up: 126,
  down: 125,
  left: 123,
  right: 124,
};

const MAC_MODIFIERS = {
  mod: "command down",
  cmd: "command down",
  ctrl: "control down",
  shift: "shift down",
  alt: "option down",
};

export function appleScriptString(text) {
  return `"${String(text).replace(/\\/g, "\\\\").replace(/"/g, '\\"')}"`;
}

function usingClause(mods) {
  const parts = [...new Set(mods.map((m) => MAC_MODIFIERS[m]))];
  return parts.length ? ` using {${parts.join(", ")}}` : "";
}

/** Genera el AppleScript completo para una lista de pasos. */
export function buildAppleScript(steps, config) {
  const lines = [];
  const stepDelay = (config.timing?.stepMs ?? 60) / 1000;
  if (config.activateApp !== false) {
    const bundleId = config.mac?.bundleId || "com.anthropic.claudefordesktop";
    lines.push(`tell application id ${appleScriptString(bundleId)} to activate`);
    lines.push("delay 0.15");
  }
  lines.push('tell application "System Events"');
  for (const step of steps) {
    if (step.type === "wait") {
      lines.push(`  delay ${(step.ms / 1000).toFixed(3)}`);
      continue;
    }
    if (step.type === "text") {
      lines.push(`  keystroke ${appleScriptString(step.text)}`);
    } else if (step.key in MAC_KEY_CODES) {
      lines.push(`  key code ${MAC_KEY_CODES[step.key]}${usingClause(step.mods)}`);
    } else {
      lines.push(`  keystroke ${appleScriptString(step.key)}${usingClause(step.mods)}`);
    }
    if (stepDelay > 0) lines.push(`  delay ${stepDelay.toFixed(3)}`);
  }
  lines.push("end tell");
  return lines.join("\n");
}

export function sendMac(steps, config) {
  const script = buildAppleScript(steps, config);
  return new Promise((resolve, reject) => {
    execFile("osascript", ["-e", script], { timeout: 15000 }, (err, _stdout, stderr) => {
      if (err) {
        const hint = /not allowed|1002|assistive/i.test(stderr || "")
          ? " — Da permiso de Accesibilidad en Ajustes del Sistema > Privacidad y seguridad > Accesibilidad."
          : "";
        reject(new Error(`osascript falló: ${(stderr || err.message).trim()}${hint}`));
      } else {
        resolve();
      }
    });
  });
}
