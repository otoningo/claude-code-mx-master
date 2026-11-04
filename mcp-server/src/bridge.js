// Client for the Logi plugin's bridge (127.0.0.1, see logi-plugin/src/Bridge/BridgeServer.cs).
// The plugin receives Claude Code's hooks; through it you can see and answer pending permission requests.
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { execFile } from "node:child_process";
import { userConfigPath, getPath } from "./catalog.js";

export const TOKEN_HEADER = "X-Claude-MX-Token";

export function bridgeToken() {
  const file = path.join(path.dirname(userConfigPath()), "token");
  try {
    return fs.readFileSync(file, "utf8").trim();
  } catch {
    return null;
  }
}

export function bridgePort(config) {
  return Number(getPath(config, "bridge.port") ?? 47823);
}

export function bridgeRequest(config, method, route, body) {
  const token = bridgeToken();
  if (!token) {
    return Promise.reject(new Error("The Logi plugin bridge isn't set up (~/.claude-mx/token is missing). Install the plugin and open Logi Options+."));
  }
  const payload = body ? JSON.stringify(body) : "";
  return new Promise((resolve, reject) => {
    const req = http.request(
      {
        host: "127.0.0.1",
        port: bridgePort(config),
        method,
        path: route,
        headers: { [TOKEN_HEADER]: token, "Content-Type": "application/json", "Content-Length": Buffer.byteLength(payload) },
        timeout: 5000,
      },
      (res) => {
        let data = "";
        res.on("data", (c) => (data += c));
        res.on("end", () => {
          try {
            const json = JSON.parse(data || "{}");
            if (res.statusCode >= 400) reject(new Error(json.error || `HTTP ${res.statusCode}`));
            else resolve(json);
          } catch (err) {
            reject(err);
          }
        });
      },
    );
    req.on("timeout", () => req.destroy(new Error("The bridge didn't respond")));
    req.on("error", (err) =>
      reject(new Error(`The Logi plugin bridge isn't responding on 127.0.0.1:${bridgePort(config)} (${err.code || err.message}). Is Logi Options+ running?`)),
    );
    req.end(payload);
  });
}

export const pendingPermissions = (config) => bridgeRequest(config, "GET", "/claude-mx/api/status");

export const decidePermission = (config, decision, id) =>
  bridgeRequest(config, "POST", "/claude-mx/api/decide", id ? { decision, id } : { decision });

// Opens a claude:// link with the operating system (Claude Desktop handles it).
export function openClaudeUrl(url, platform = process.platform) {
  if (!url.startsWith("claude://")) throw new Error("Only claude:// links are opened");
  const [cmd, args] =
    platform === "darwin" ? ["open", [url]] : platform === "win32" ? ["rundll32", ["url.dll,FileProtocolHandler", url]] : ["xdg-open", [url]];
  return new Promise((resolve, reject) => {
    execFile(cmd, args, { timeout: 10000 }, (err) => (err ? reject(new Error(`Couldn't open ${url}: ${err.message}`)) : resolve()));
  });
}
