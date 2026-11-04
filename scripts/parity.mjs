#!/usr/bin/env node
// Compara la salida del plugin C# (tests/Parity) con la del servidor MCP para cada acción.
// Uso: node scripts/parity.mjs <salida-json-del-programa-csharp> [config.json]
import fs from "node:fs";
import { loadCatalog, loadConfig, resolveSteps, displayNameFor } from "../mcp-server/src/catalog.js";
import { buildAppleScript } from "../mcp-server/src/senders/mac.js";

const csharp = JSON.parse(fs.readFileSync(process.argv[2], "utf8"));
const config = loadConfig({ userFile: process.argv[3] ?? null });
let failures = 0;
for (const action of loadCatalog().actions) {
  const expected = { name: displayNameFor(action, config), appleScript: buildAppleScript(resolveSteps(action, config), config) };
  const actual = csharp[action.id];
  if (!actual || actual.name !== expected.name || actual.appleScript !== expected.appleScript) {
    failures++;
    console.error(`✗ ${action.id}\n--- node ---\n${JSON.stringify(expected, null, 1)}\n--- c# ---\n${JSON.stringify(actual, null, 1)}`);
  }
}
if (failures) process.exit(1);
console.log(`Paridad C# ↔ Node OK en ${Object.keys(csharp).length} acciones.`);
