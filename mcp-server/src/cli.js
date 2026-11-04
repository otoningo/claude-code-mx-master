#!/usr/bin/env node
// CLI para probar acciones sin cliente MCP, o para asignarlas en Logi Options+
// como "Abrir aplicación/ejecutar" si no quieres instalar el plugin.
//   node src/cli.js list
//   node src/cli.js run approve-all
//   node src/cli.js type "hola" --submit
//   CLAUDE_MX_DRY_RUN=1 node src/cli.js run send-now   (muestra el plan sin enviar teclas)
import { listActions, runAction, typePrompt } from "./runner.js";

const [cmd, ...args] = process.argv.slice(2);

async function main() {
  switch (cmd) {
    case "list": {
      for (const a of listActions()) console.log(`${a.id.padEnd(20)} ${a.group.padEnd(10)} ${a.name}`);
      break;
    }
    case "run": {
      if (!args[0]) throw new Error("Uso: claude-mx run <id-de-acción>");
      const result = await runAction(args[0]);
      if (result.dryRun) console.log(typeof result.plan === "string" ? result.plan : JSON.stringify(result.plan, null, 2));
      else console.log(`OK: ${result.name}`);
      break;
    }
    case "type": {
      const submit = args.includes("--submit");
      const text = args.filter((a) => a !== "--submit").join(" ");
      if (!text) throw new Error('Uso: claude-mx type "texto" [--submit]');
      const result = await typePrompt(text, { submit });
      if (result.dryRun) console.log(JSON.stringify(result.plan, null, 2));
      break;
    }
    default:
      console.log("Uso: claude-mx <list | run <id> | type \"texto\" [--submit]>");
      process.exitCode = cmd ? 1 : 0;
  }
}

main().catch((err) => {
  console.error(err.message);
  process.exit(1);
});
