#!/usr/bin/env node
// Integration test between the MCP client (Node) and the plugin's real bridge (C#).
// Simulates the HTTP hooks Claude Code sends, without needing Claude Code.
// Usage: CLAUDE_MX_CONFIG=<workdir>/config.json node scripts/bridge-selftest.mjs
//        (with logi-plugin/tests/BridgeSmoke running on that workdir, action "none")
import assert from "node:assert/strict";
import http from "node:http";
import { loadConfig } from "../mcp-server/src/catalog.js";
import { bridgePort, bridgeToken, decidePermission, pendingPermissions, TOKEN_HEADER } from "../mcp-server/src/bridge.js";

const config = loadConfig();
const port = bridgePort(config);

function postHook(event, body, token = bridgeToken()) {
  const payload = JSON.stringify(body);
  return new Promise((resolve, reject) => {
    const req = http.request(
      { host: "127.0.0.1", port, method: "POST", path: `/claude-mx/hooks/${event}`, headers: { [TOKEN_HEADER]: token, "Content-Type": "application/json", "Content-Length": Buffer.byteLength(payload) } },
      (res) => {
        let data = "";
        res.on("data", (c) => (data += c));
        res.on("end", () => resolve({ status: res.statusCode, body: data ? JSON.parse(data) : null }));
      },
    );
    req.on("error", reject);
    req.end(payload);
  });
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const request = { session_id: "s1", hook_event_name: "PermissionRequest", tool_name: "Bash", tool_input: { command: "npm test" } };

// 1. Without the token -> 401.
assert.equal((await postHook("Stop", { session_id: "s1" }, "wrong-token")).status, 401);

// 2. Events without a decision answer {} right away.
assert.deepEqual((await postHook("Stop", { session_id: "s1", hook_event_name: "Stop" })).body, {});

// 3. PermissionRequest waits until someone answers (here: the MCP client).
for (const [decision, expected] of [
  ["approve", { behavior: "allow" }],
  ["approve-all", { behavior: "allow", updatedPermissions: [{ type: "setMode", mode: "acceptEdits", destination: "session" }] }],
  ["deny", { behavior: "deny", message: "The user denied this request from their mouse." }],
  ["stop", { behavior: "deny", message: "The user stopped this from their mouse.", interrupt: true }],
]) {
  const hook = postHook("PermissionRequest", request);
  let pending;
  for (let i = 0; i < 50 && !(pending = (await pendingPermissions(config)).pending)?.length; i++) await sleep(100);
  assert.equal(pending.length, 1);
  assert.equal(pending[0].tool, "Bash");
  assert.equal(pending[0].summary, "npm test");
  assert.equal((await decidePermission(config, decision)).ok, true);
  const res = await hook;
  assert.deepEqual(res.body, { hookSpecificOutput: { hookEventName: "PermissionRequest", decision: expected } }, decision);
  console.log(`ok - ${decision}`);
}

// 4. Answering with nothing pending -> ok:false.
assert.equal((await decidePermission(config, "approve")).ok, false);
console.log("Bridge C# <-> Node OK");
