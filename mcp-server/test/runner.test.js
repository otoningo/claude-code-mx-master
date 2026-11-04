import { test } from "node:test";
import assert from "node:assert/strict";
import { loadCatalog, loadConfig } from "../src/catalog.js";
import { runAction } from "../src/runner.js";

const catalog = loadCatalog();
const config = { ...loadConfig({ userFile: null }), language: "en" };

function fakeBridge({ pending = true, reachable = true } = {}) {
  const calls = [];
  return {
    calls,
    decide: async (_config, decision) => {
      calls.push(["decide", decision]);
      if (!reachable) throw new Error("bridge down");
      return { ok: pending };
    },
    open: async (url) => calls.push(["open", url]),
  };
}

test("approve-all answers the pending request directly (no shortcuts)", async () => {
  const bridge = fakeBridge();
  const res = await runAction("approve-all", { catalog, config, bridge });
  assert.deepEqual(bridge.calls, [["decide", "approve-all"]]);
  assert.equal(res.direct.decided, true);
  assert.equal(res.steps, undefined);
});

test("approve with nothing pending doesn't press keys", async () => {
  const bridge = fakeBridge({ pending: false });
  const res = await runAction("approve", { catalog, config, bridge });
  assert.equal(res.direct.decided, false);
  assert.match(res.message, /No permission request/);
});

test("stop with nothing pending falls back to Esc", async () => {
  process.env.CLAUDE_MX_DRY_RUN = "1";
  try {
    const bridge = fakeBridge({ pending: false });
    const res = await runAction("stop", { catalog, config, bridge });
    assert.equal(res.dryRun, true);
    assert.deepEqual(res.fallbackSteps, [{ type: "key", key: "esc", mods: [] }]);
  } finally {
    delete process.env.CLAUDE_MX_DRY_RUN;
  }
});

test("new-session opens claude://code/new", async () => {
  const bridge = fakeBridge();
  const res = await runAction("new-session", { catalog, config, bridge });
  assert.deepEqual(bridge.calls, [["open", "claude://code/new"]]);
  assert.equal(res.direct.url, "claude://code/new");
});
