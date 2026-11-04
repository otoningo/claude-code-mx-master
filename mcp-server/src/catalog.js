// Carga el catálogo de acciones y la configuración, y convierte cada acción en una
// lista de pasos concretos (atajo, texto o espera) listos para enviarse al sistema.
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));

/** Busca la carpeta `shared/` (en el repo o copiada junto al paquete). */
export function sharedDir() {
  const candidates = [
    process.env.CLAUDE_MX_SHARED_DIR,
    path.resolve(here, "../../shared"),
    path.resolve(here, "../shared"),
  ].filter(Boolean);
  for (const dir of candidates) {
    if (fs.existsSync(path.join(dir, "actions.json"))) return dir;
  }
  throw new Error(`No se encontró shared/actions.json (buscado en: ${candidates.join(", ")})`);
}

export function userConfigPath() {
  return process.env.CLAUDE_MX_CONFIG || path.join(os.homedir(), ".claude-mx", "config.json");
}

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8"));
}

function isPlainObject(v) {
  return v !== null && typeof v === "object" && !Array.isArray(v);
}

/** Mezcla profunda: los objetos se combinan, los arrays y escalares del usuario reemplazan. */
export function deepMerge(base, override) {
  if (!isPlainObject(base) || !isPlainObject(override)) return override === undefined ? base : override;
  const out = { ...base };
  for (const [k, v] of Object.entries(override)) {
    out[k] = k in base ? deepMerge(base[k], v) : v;
  }
  return out;
}

export function loadCatalog() {
  return readJson(path.join(sharedDir(), "actions.json"));
}

export function loadConfig({ userFile = userConfigPath() } = {}) {
  const defaults = readJson(path.join(sharedDir(), "config.default.json"));
  if (userFile && fs.existsSync(userFile)) {
    return deepMerge(defaults, readJson(userFile));
  }
  return defaults;
}

/** Lee una ruta tipo "models.0.label" dentro de la configuración. */
export function getPath(obj, dotted) {
  return dotted.split(".").reduce((acc, part) => (acc == null ? undefined : acc[part]), obj);
}

const PLACEHOLDER = /^\$\{([A-Za-z0-9_.]+)\}$/;

function resolveValue(value, config, actionId) {
  if (typeof value !== "string") return value;
  const m = PLACEHOLDER.exec(value);
  if (!m) return value;
  const resolved = getPath(config, m[1]);
  if (resolved === undefined || resolved === null) {
    throw new Error(`La acción "${actionId}" usa \${${m[1]}}, que no existe en la configuración`);
  }
  return resolved;
}

const MODIFIERS = new Set(["mod", "cmd", "ctrl", "shift", "alt"]);
const NAMED_KEYS = new Set(["esc", "enter", "tab", "space", "backspace", "delete", "up", "down", "left", "right"]);

/** "mod+shift+]" -> { type: "key", key: "]", mods: ["mod", "shift"] } */
export function parseChord(chord) {
  const raw = String(chord);
  let key;
  let modPart;
  if (raw === "+") {
    key = "+";
    modPart = "";
  } else if (raw.endsWith("++")) {
    key = "+";
    modPart = raw.slice(0, -2);
  } else {
    const idx = raw.lastIndexOf("+");
    key = idx === -1 ? raw : raw.slice(idx + 1);
    modPart = idx === -1 ? "" : raw.slice(0, idx);
  }
  const mods = modPart ? modPart.split("+").map((m) => m.trim().toLowerCase()) : [];
  for (const m of mods) {
    if (!MODIFIERS.has(m)) throw new Error(`Modificador desconocido "${m}" en "${raw}"`);
  }
  const normalizedKey = key.toLowerCase();
  if (normalizedKey.length !== 1 && !NAMED_KEYS.has(normalizedKey)) {
    throw new Error(`Tecla desconocida "${key}" en "${raw}"`);
  }
  return { type: "key", key: normalizedKey, mods };
}

/** Convierte los pasos declarativos de una acción en pasos concretos. */
export function resolveSteps(action, config) {
  const out = [];
  for (const step of action.steps) {
    if (isPlainObject(step) && "wait" in step) {
      out.push({ type: "wait", ms: Number(resolveValue(step.wait, config, action.id)) });
    } else if (isPlainObject(step) && "text" in step) {
      out.push({ type: "text", text: String(resolveValue(step.text, config, action.id)) });
    } else {
      const value = resolveValue(step, config, action.id);
      out.push(parseChord(String(value)));
    }
  }
  return out;
}

export function displayNameFor(action, config) {
  if (action.displayNameFrom) {
    const label = getPath(config, action.displayNameFrom);
    if (label) return `Modelo: ${label}`;
  }
  return action.displayName;
}

export function findAction(catalog, id) {
  const action = catalog.actions.find((a) => a.id === id);
  if (!action) {
    const ids = catalog.actions.map((a) => a.id).join(", ");
    throw new Error(`Acción desconocida "${id}". Disponibles: ${ids}`);
  }
  return action;
}
