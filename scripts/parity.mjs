#!/usr/bin/env node
// Compara la salida del plugin C# (tests/Parity) con la del servidor MCP para cada acción.
// Uso: node scripts/parity.mjs <salida-json-del-programa-csharp> <config.json usado en C#>
import fs from "node:fs";
import { loadCatalog, loadConfig, resolveSteps, localize } from "../mcp-server/src/catalog.js";
import { buildAppleScript } from "../mcp-server/src/senders/mac.js";

const csharp = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
const config = loadConfig({ userFile: process.argv[3] ?? null });
let failures = 0;
const catalog = loadCatalog();
for (const action of catalog.actions) {
  const t = localize(action, config, catalog);
  const expected = { name: t.displayName, description: t.description, group: t.group, appleScript: buildAppleScript(resolveSteps(action, config), config) };
  const actual = csharp[action.id];
  if (!actual || JSON.stringify(actual) !== JSON.stringify(expected)) {
    failures++;
    console.error(`✗ ${action.id}\n--- node ---\n${JSON.stringify(expected, null, 1)}\n--- c# ---\n${JSON.stringify(actual, null, 1)}`);
  }
}
if (failures) process.exit(1);
console.log(`Paridad C# ↔ Node OK en ${Object.keys(csharp).length} acciones.`);
