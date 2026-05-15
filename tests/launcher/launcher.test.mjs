// Tests for cc-plugin/router-launcher.mjs (= connector/router-launcher.mjs).
//
// Strategy: build synthetic yak trees under tmpdir, point the launcher at them
// via HOME (mac) / APPDATA (windows) overrides, and assert behaviour via
// spawnSync. Where a test needs a real runnable "router" binary (to exercise
// exit-code propagation), we copy this process's node binary to the expected
// path and pass `-e <code>` so the child does whatever we want.

import { test } from "node:test";
import { strict as assert } from "node:assert";
import { spawnSync, spawn } from "node:child_process";
import { setTimeout as delay } from "node:timers/promises";
import { mkdtempSync, mkdirSync, writeFileSync, copyFileSync, cpSync, chmodSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const CC_LAUNCHER = join(HERE, "..", "..", "cc-plugin", "router-launcher.mjs");
const CONNECTOR_LAUNCHER = join(HERE, "..", "..", "connector", "router-launcher.mjs");

const isWin = process.platform === "win32";
const isMac = process.platform === "darwin";
const isSupported = isWin || isMac;

const RID = isMac ? "osx-arm64" : isWin ? (process.arch === "arm64" ? "win-arm64" : "win-x64") : null;
const EXE = isWin ? "rhino-mcp-router.exe" : "rhino-mcp-router";

// layout = { "<rhino-ver>": { "<pkg-ver>": { binary: "exec" | "nonexec" | null } } }
// "exec"    → real runnable binary (copy of node) at the router path
// "nonexec" → file exists but isn't a runnable binary (spawn-error path)
// null      → no file (yak-but-no-binary-for-rid path)
function makeFakeRoot(layout) {
  const tmp = mkdtempSync(join(tmpdir(), "rh-launcher-"));
  let pkgRoot, env;
  if (isMac) {
    pkgRoot = join(tmp, "Library", "Application Support", "McNeel", "Rhinoceros", "packages");
    env = { HOME: tmp };
  } else if (isWin) {
    pkgRoot = join(tmp, "McNeel", "Rhinoceros", "packages");
    env = { APPDATA: tmp };
  } else {
    pkgRoot = join(tmp, "packages");
    env = { HOME: tmp };
  }

  for (const [rhinoVer, pkgVers] of Object.entries(layout)) {
    for (const [pkgVer, opts] of Object.entries(pkgVers)) {
      const routerDir = join(pkgRoot, rhinoVer, "Rhino-MCP-Platform", pkgVer, "router", RID ?? "x");
      mkdirSync(routerDir, { recursive: true });
      const dst = join(routerDir, EXE);
      if (opts.binary === "exec") {
        copyFileSync(process.execPath, dst);
        if (!isWin) chmodSync(dst, 0o755);
      } else if (opts.binary === "nonexec") {
        writeFileSync(dst, "not a real binary");
        if (!isWin) chmodSync(dst, 0o644);
      }
    }
  }
  return { env };
}

function runLauncher(env, args = []) {
  return spawnSync(process.execPath, [CC_LAUNCHER, ...args], {
    env: { ...process.env, ...env },
    encoding: "utf8",
    input: "",
  });
}

// --- meta: the two copies of the launcher must stay byte-identical ----------

test("cc-plugin and connector launchers are byte-identical", () => {
  const a = readFileSync(CC_LAUNCHER);
  const b = readFileSync(CONNECTOR_LAUNCHER);
  assert.deepEqual(a, b, "Launchers have drifted — re-copy cc-plugin/router-launcher.mjs to connector/.");
});

// --- platform gating --------------------------------------------------------

test("linux reports unsupported platform and exits 1", { skip: isSupported }, () => {
  const r = runLauncher({ HOME: mkdtempSync(join(tmpdir(), "rh-")) });
  assert.equal(r.status, 1);
  assert.match(r.stderr, /unsupported platform/);
});

// --- resolution -------------------------------------------------------------

test("picks newest pkg-ver under one Rhino major", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.1.0": { binary: "exec" }, "0.2.0": { binary: "exec" } },
  });
  const r = runLauncher(fake.env, ["-e", "process.exit(0)"]);
  assert.equal(r.status, 0);
  assert.match(r.stderr, /9\.0\/0\.2\.0\*/);
});

test("numeric semver: 0.10.0 ranks above 0.2.0", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.2.0": { binary: "exec" }, "0.10.0": { binary: "exec" } },
  });
  const r = runLauncher(fake.env, ["-e", "process.exit(0)"]);
  assert.equal(r.status, 0);
  assert.match(r.stderr, /9\.0\/0\.10\.0\*/);
});

test("documents current cross-major policy (Rhino 9 before 8)", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.1.0": { binary: "exec" } },
    "8.0": { "0.3.0": { binary: "exec" } },
  });
  const r = runLauncher(fake.env, ["-e", "process.exit(0)"]);
  assert.equal(r.status, 0);
  // Current policy: any Rhino 9 hit short-circuits before considering Rhino 8,
  // even when Rhino 8 has a newer package. Change this test if the policy
  // changes to "newest pkg-ver across all majors".
  assert.match(r.stderr, /9\.0\/0\.1\.0\*/);
});

test("no yak installed → exit 1 with explanatory message", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({});
  const r = runLauncher(fake.env);
  assert.equal(r.status, 1);
  assert.match(r.stderr, /no Rhino-MCP-Platform yak installed/);
});

test("yak exists but no router binary for this rid → exit 1", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.2.0": { binary: null } },
  });
  const r = runLauncher(fake.env);
  assert.equal(r.status, 1);
  assert.match(r.stderr, new RegExp(`no ${EXE.replace(/\./g, "\\.")} found for ${RID}`));
});

// --- spawn lifecycle --------------------------------------------------------

test("non-executable binary at picked path → exit 1, not exit 0", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.2.0": { binary: "nonexec" } },
  });
  const r = runLauncher(fake.env);
  // Was the original bug: error+close both fired and process.exit(code ?? 0)
  // masked the failure as a clean exit 0.
  assert.equal(r.status, 1);
  assert.match(r.stderr, /spawn failed/);
});

test("successful child exit (code 0) propagates", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.2.0": { binary: "exec" } },
  });
  const r = runLauncher(fake.env, ["-e", "process.exit(0)"]);
  assert.equal(r.status, 0);
});

test("non-zero child exit code propagates verbatim", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.2.0": { binary: "exec" } },
  });
  const r = runLauncher(fake.env, ["-e", "process.exit(42)"]);
  assert.equal(r.status, 42);
});

// --- integration: real router (CI only, gated on RHINO_MCP_TEST_ROUTER_DIR) ---
//
// CI publishes rhino/router/Router.csproj for the host rid and points this env
// var at the publish output. We stage that into a fake yak tree, spawn the
// launcher, send a real initialize JSON-RPC frame, and assert the router
// replies with its serverInfo on stdout — proves end-to-end stdio pass-through
// against an actual router build, not just a fake exit-code stub.

const HAS_REAL_ROUTER = !!process.env.RHINO_MCP_TEST_ROUTER_DIR;

test("integration: real router answers initialize over stdio", { skip: !isSupported || !HAS_REAL_ROUTER }, async () => {
  const publishDir = process.env.RHINO_MCP_TEST_ROUTER_DIR;

  const tmp = mkdtempSync(join(tmpdir(), "rh-launcher-int-"));
  let pkgRoot, env;
  if (isMac) {
    pkgRoot = join(tmp, "Library", "Application Support", "McNeel", "Rhinoceros", "packages");
    env = { HOME: tmp };
  } else {
    pkgRoot = join(tmp, "McNeel", "Rhinoceros", "packages");
    env = { APPDATA: tmp };
  }
  const routerDir = join(pkgRoot, "9.0", "Rhino-MCP-Platform", "0.0.1-test", "router", RID);
  mkdirSync(routerDir, { recursive: true });
  // Copy the whole publish output: .NET single-file/AOT still ships sidecar
  // files (.pdb, *.deps.json on framework-dependent, etc.) we don't care to
  // enumerate.
  cpSync(publishDir, routerDir, { recursive: true });

  const initMsg = JSON.stringify({
    jsonrpc: "2.0",
    id: 0,
    method: "initialize",
    params: { protocolVersion: "2024-11-05", capabilities: {}, clientInfo: { name: "ci", version: "1.0" } },
  }) + "\n";

  // Use async spawn rather than spawnSync's `input` mode: that closes stdin
  // the instant the message is written, and the .NET MCP host's stdio reader
  // sees EOF before it flushes the initialize response — the test would see
  // empty stdout even though the router processed the request.
  const child = spawn(process.execPath, [CC_LAUNCHER], { env: { ...process.env, ...env } });
  let stdout = "";
  let stderr = "";
  child.stdout.on("data", d => { stdout += d.toString(); });
  child.stderr.on("data", d => { stderr += d.toString(); });

  child.stdin.write(initMsg);
  await delay(1500);  // let the router boot + respond before EOF
  child.stdin.end();

  const exitCode = await new Promise(resolve => child.once("close", resolve));

  assert.equal(exitCode, 0, `launcher exit=${exitCode}\n--- stdout ---\n${stdout}\n--- stderr ---\n${stderr}`);
  assert.match(stdout, /"name"\s*:\s*"rhino-mcp-router"/, `expected initialize response on stdout\n--- stdout ---\n${stdout}\n--- stderr ---\n${stderr}`);
});
