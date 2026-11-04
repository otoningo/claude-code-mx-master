// Windows: ejecuta windows-sendinput.ps1 con el plan en una variable de entorno
// (así el texto nunca se interpola dentro del código de PowerShell).
import { execFile } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
export const PS_SCRIPT = path.join(here, "windows-sendinput.ps1");

export function buildWindowsPlan(steps, config) {
  return {
    activate: config.activateApp !== false,
    processNames: config.windows?.processNames?.length ? config.windows.processNames : ["claude"],
    keyMapping: config.windows?.keyMapping === "us" ? "us" : "layout",
    stepMs: config.timing?.stepMs ?? 60,
    steps,
  };
}

export function sendWindows(steps, config) {
  const plan = buildWindowsPlan(steps, config);
  return new Promise((resolve, reject) => {
    execFile(
      "powershell.exe",
      ["-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", PS_SCRIPT],
      { timeout: 20000, env: { ...process.env, CLAUDE_MX_PLAN: JSON.stringify(plan) }, windowsHide: true },
      (err, _stdout, stderr) => {
        if (err) reject(new Error(`PowerShell falló: ${(stderr || err.message).trim()}`));
        else resolve();
      },
    );
  });
}
