import { test } from "node:test";
import assert from "node:assert/strict";
import { loadCatalog, loadConfig, resolveSteps, parseChord, deepMerge, findAction, displayNameFor } from "../src/catalog.js";
import { buildAppleScript } from "../src/senders/mac.js";
import { buildWindowsPlan } from "../src/senders/windows.js";

const catalog = loadCatalog();
const config = loadConfig({ userFile: null });

test("cada acción resuelve sus pasos sin errores", () => {
  for (const action of catalog.actions) {
    const steps = resolveSteps(action, config);
    assert.ok(steps.length > 0, action.id);
  }
});

test("ids y nombres de clase son únicos", () => {
  const ids = new Set(catalog.actions.map((a) => a.id));
  const classes = new Set(catalog.actions.map((a) => a.className));
  assert.equal(ids.size, catalog.actions.length);
  assert.equal(classes.size, catalog.actions.length);
  for (const a of catalog.actions) {
    assert.match(a.className, /^[A-Z][A-Za-z0-9]+Command$/);
    assert.ok(catalog.groups[a.group], `grupo ${a.group}`);
    assert.ok(catalog.haptics[a.haptic], `evento háptico ${a.haptic}`);
  }
});

test("parseChord entiende modificadores y teclas especiales", () => {
  assert.deepEqual(parseChord("mod+shift+]"), { type: "key", key: "]", mods: ["mod", "shift"] });
  assert.deepEqual(parseChord("ctrl+`"), { type: "key", key: "`", mods: ["ctrl"] });
  assert.deepEqual(parseChord("esc"), { type: "key", key: "esc", mods: [] });
  assert.deepEqual(parseChord("mod++"), { type: "key", key: "+", mods: ["mod"] });
  assert.deepEqual(parseChord("2"), { type: "key", key: "2", mods: [] });
  assert.throws(() => parseChord("hyper+x"));
  assert.throws(() => parseChord("mod+pageup"));
});

test("approve-all abre el menú de permisos y elige Accept edits", () => {
  const steps = resolveSteps(findAction(catalog, "approve-all"), config);
  assert.deepEqual(steps, [
    { type: "key", key: "m", mods: ["mod", "shift"] },
    { type: "wait", ms: 250 },
    { type: "key", key: "2", mods: [] },
  ]);
});

test("send-now detiene y luego envía", () => {
  const steps = resolveSteps(findAction(catalog, "send-now"), config);
  assert.equal(steps[0].key, "esc");
  assert.equal(steps[1].type, "wait");
  assert.equal(steps[2].key, "enter");
});

test("la configuración del usuario cambia los índices del menú y los nombres", () => {
  const custom = deepMerge(config, { permissionMenu: { acceptEdits: 3 }, models: [{ label: "Fable", menuIndex: 4 }] });
  const steps = resolveSteps(findAction(catalog, "approve-all"), custom);
  assert.equal(steps[2].key, "3");
  assert.equal(displayNameFor(findAction(catalog, "model-1"), custom), "Modelo: Fable");
  assert.throws(() => resolveSteps(findAction(catalog, "model-2"), custom), /models\.1\.menuIndex/);
});

test("AppleScript usa key code para teclas especiales y escapa texto", () => {
  const script = buildAppleScript(
    [parseChord("esc"), parseChord("mod+shift+i"), { type: "text", text: 'di "hola" \\ ya' }],
    config,
  );
  assert.match(script, /tell application id "com\.anthropic\.claudefordesktop" to activate/);
  assert.match(script, /key code 53\n/);
  assert.match(script, /keystroke "i" using \{command down, shift down\}/);
  assert.match(script, /keystroke "di \\"hola\\" \\\\ ya"/);
});

test("el plan de Windows lleva procesos y mapeo de teclado", () => {
  const plan = buildWindowsPlan([parseChord("mod+n")], config);
  assert.deepEqual(plan.processNames, ["claude"]);
  assert.equal(plan.keyMapping, "layout");
  assert.equal(plan.steps[0].key, "n");
});
