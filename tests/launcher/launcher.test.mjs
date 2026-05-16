// Tests for shared/router-launcher.mjs (the canonical launcher; cc-plugin/ and
// connector/ each contain a symlink to it so both packaging paths share one
// source).
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
import { mkdtempSync, mkdirSync, writeFileSync, copyFileSync, cpSync, chmodSync, readFileSync, lstatSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const HERE = dirname(fileURLToPath(import.meta.url));
const SHARED_LAUNCHER = join(HERE, "..", "..", "shared", "router-launcher.mjs");
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
  return spawnSync(process.execPath, [SHARED_LAUNCHER, ...args], {
    env: { ...process.env, ...env },
    encoding: "utf8",
    input: "",
  });
}

// --- meta: cc-plugin/ + connector/ are symlinks into shared/ ----------------
//
// On Windows, `git clone` with `core.symlinks=false` (the default) materializes
// symlinks as plain text files whose contents are the link target string. We
// detect that explicitly and fail with a clear message — otherwise `mcpb pack`
// would silently ship a one-line "../shared/router-launcher.mjs" stub instead
// of the real launcher.

function checkLauncherShim(path, label) {
  // Real symlink → lstat reports a link, regardless of platform support.
  if (lstatSync(path).isSymbolicLink()) {
    // statSync follows the link; if the target is missing or wrong size, fail.
    const real = statSync(path);
    const canonical = statSync(SHARED_LAUNCHER);
    assert.equal(real.size, canonical.size, `${label} symlink resolves to a file of unexpected size — target may be broken`);
    return;
  }
  // Not a symlink. Could be (a) the canonical file copied in by mistake, or
  // (b) a Windows clone that flattened the symlink into a tiny text file.
  const bytes = readFileSync(path);
  const canonical = readFileSync(SHARED_LAUNCHER);
  if (bytes.equals(canonical)) {
    assert.fail(`${label} is a regular file but contains the canonical bytes — it must be a symlink to ../shared/router-launcher.mjs (run: git rm ${label}; ln -s ../shared/router-launcher.mjs ${label}).`);
  }
  const head = bytes.subarray(0, 200).toString("utf8");
  assert.fail(`${label} appears to be a flattened symlink (size=${bytes.length}, head=${JSON.stringify(head)}). This Windows clone has core.symlinks=false. Run: git config --global core.symlinks true && git checkout -- ${label}`);
}

test("cc-plugin/router-launcher.mjs is a symlink into shared/", () => {
  checkLauncherShim(CC_LAUNCHER, "cc-plugin/router-launcher.mjs");
});

test("connector/router-launcher.mjs is a symlink into shared/", () => {
  checkLauncherShim(CONNECTOR_LAUNCHER, "connector/router-launcher.mjs");
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
    "9.0": { "0.1.0": { binary: "exec" }, "0.1.0": { binary: "exec" } },
  });
  const r = runLauncher(fake.env, ["-e", "process.exit(0)"]);
  assert.equal(r.status, 0);
  assert.match(r.stderr, /9\.0\/0\.2\.0\*/);
});

test("numeric semver: 0.10.0 ranks above 0.1.0", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.1.0": { binary: "exec" }, "0.10.0": { binary: "exec" } },
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

// These tests simulate Rhino-being-installed by pointing RHINO_MCP_FAKE_YAK_PATH
// at any existing file. The launcher only checks existence, not contents, so
// process.execPath (a guaranteed-real file) is convenient.
const FAKE_YAK_INSTALLED = { RHINO_MCP_FAKE_YAK_PATH: process.execPath };
const FAKE_YAK_MISSING = { RHINO_MCP_FAKE_YAK_PATH: join(tmpdir(), "definitely-does-not-exist-rhino-yak") };

test("no yak installed → enters install-fallback (exit 0 on stdin EOF)", { skip: !isSupported }, () => {
  // `runLauncher` passes input: "" — readline sees EOF immediately and the
  // fallback exits cleanly with 0. The detailed JSON-RPC behaviour lives
  // further down in the install-fallback test block.
  const fake = makeFakeRoot({});
  const r = runLauncher({ ...fake.env, ...FAKE_YAK_INSTALLED });
  assert.equal(r.status, 0);
  assert.match(r.stderr, /no Rhino-MCP-Platform yak installed/);
  assert.match(r.stderr, /entering install-fallback mode/);
});

test("yak exists but no router binary for this rid → enters install-fallback", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.1.0": { binary: null } },
  });
  const r = runLauncher({ ...fake.env, ...FAKE_YAK_INSTALLED });
  assert.equal(r.status, 0);
  assert.match(r.stderr, new RegExp(`no ${EXE.replace(/\./g, "\\.")} found for ${RID}`));
  assert.match(r.stderr, /entering install-fallback mode/);
});

test("no yak AND Rhino not installed → enters rhino-missing-fallback", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({});
  const r = runLauncher({ ...fake.env, ...FAKE_YAK_MISSING });
  assert.equal(r.status, 0);
  assert.match(r.stderr, /entering rhino-missing-fallback mode/);
});

// --- spawn lifecycle --------------------------------------------------------

test("non-executable binary at picked path → exit 1, not exit 0", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.1.0": { binary: "nonexec" } },
  });
  const r = runLauncher(fake.env);
  // Was the original bug: error+close both fired and process.exit(code ?? 0)
  // masked the failure as a clean exit 0.
  assert.equal(r.status, 1);
  assert.match(r.stderr, /spawn failed/);
});

test("successful child exit (code 0) propagates", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.1.0": { binary: "exec" } },
  });
  const r = runLauncher(fake.env, ["-e", "process.exit(0)"]);
  assert.equal(r.status, 0);
});

test("non-zero child exit code propagates verbatim", { skip: !isSupported }, () => {
  const fake = makeFakeRoot({
    "9.0": { "0.1.0": { binary: "exec" } },
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
  const child = spawn(process.execPath, [SHARED_LAUNCHER], { env: { ...process.env, ...env } });
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

// --- install-fallback (pseudo-MCP) ------------------------------------------
//
// When no router binary is reachable, the launcher *doesn't* exit — it boots
// a minimal MCP server that advertises one tool (install_rhino_mcp_platform)
// so the user can recover from chat instead of staring at a red dot. These
// tests exercise that server's stdio JSON-RPC handshake against a fake yak
// root that's missing the router; we never invoke the real yak install (CI
// runners don't have Rhino installed), only verify the protocol surface and
// the version/arg threading.

// Drive the launcher's stdio with line-delimited JSON-RPC. Resolves a request
// by its id rather than positionally so the test order matches the protocol.
function startFallbackLauncher(env, args = []) {
  const child = spawn(process.execPath, [SHARED_LAUNCHER, ...args], {
    env: { ...process.env, ...env },
    stdio: ["pipe", "pipe", "pipe"],
  });
  let stderr = "";
  child.stderr.on("data", d => { stderr += d.toString(); });

  let buf = "";
  const waiters = new Map(); // id -> resolve
  child.stdout.on("data", d => {
    buf += d.toString();
    let nl;
    while ((nl = buf.indexOf("\n")) !== -1) {
      const line = buf.slice(0, nl);
      buf = buf.slice(nl + 1);
      if (!line.trim()) continue;
      let msg;
      try { msg = JSON.parse(line); } catch { continue; }
      const w = waiters.get(msg.id);
      if (w) { waiters.delete(msg.id); w(msg); }
    }
  });

  async function request(method, params, id) {
    const frame = JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n";
    const p = new Promise(resolve => waiters.set(id, resolve));
    child.stdin.write(frame);
    // 5s is generous; the fallback responds synchronously without external I/O.
    return Promise.race([
      p,
      delay(5000).then(() => { throw new Error(`timed out waiting for id=${id} (${method})\n--- stderr ---\n${stderr}`); }),
    ]);
  }

  async function close() {
    child.stdin.end();
    const code = await new Promise(resolve => child.once("close", resolve));
    return { code, stderr };
  }

  return { child, request, close, getStderr: () => stderr };
}

test("fallback: no yak → initialize advertises rhino-mcp-installer", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_INSTALLED });
  try {
    const r = await l.request("initialize", { protocolVersion: "2024-11-05", capabilities: {}, clientInfo: { name: "t", version: "0" } }, 0);
    assert.equal(r.result?.serverInfo?.name, "rhino-mcp-installer");
    assert.match(l.getStderr(), /entering install-fallback mode/);
    assert.match(l.getStderr(), /no Rhino-MCP-Platform yak is installed/);
  } finally {
    const { code } = await l.close();
    assert.equal(code, 0, "fallback should exit 0 on stdin EOF");
  }
});

test("fallback: yak-but-no-binary path also enters fallback (different reason)", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({ "9.0": { "0.1.0": { binary: null } } });
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_INSTALLED });
  try {
    const r = await l.request("initialize", { protocolVersion: "2024-11-05", capabilities: {}, clientInfo: { name: "t", version: "0" } }, 0);
    assert.equal(r.result?.serverInfo?.name, "rhino-mcp-installer");
    assert.match(l.getStderr(), new RegExp(`installed Rhino-MCP-Platform yak has no router/${RID}/${EXE.replace(/\./g, "\\.")}`));
  } finally {
    await l.close();
  }
});

test("fallback: tools/list returns install_rhino_mcp_platform", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_INSTALLED });
  try {
    await l.request("initialize", { protocolVersion: "2024-11-05", capabilities: {}, clientInfo: { name: "t", version: "0" } }, 0);
    const r = await l.request("tools/list", {}, 1);
    const tools = r.result?.tools ?? [];
    assert.equal(tools.length, 1);
    assert.equal(tools[0].name, "install_rhino_mcp_platform");
    // Default version is 8 when no --default-version flag is passed.
    assert.match(tools[0].description, /Rhino 8/);
  } finally {
    await l.close();
  }
});

test("fallback: --default-version 9 propagates into the tool description", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_INSTALLED }, ["--default-version", "9"]);
  try {
    await l.request("initialize", {}, 0);
    const r = await l.request("tools/list", {}, 1);
    assert.match(r.result.tools[0].description, /Rhino 9/);
    assert.doesNotMatch(r.result.tools[0].description, /Rhino 8/);
  } finally {
    await l.close();
  }
});

test("fallback: -v WIP propagates into the tool description", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_INSTALLED }, ["-v", "WIP"]);
  try {
    await l.request("initialize", {}, 0);
    const r = await l.request("tools/list", {}, 1);
    assert.match(r.result.tools[0].description, /Rhino WIP/);
  } finally {
    await l.close();
  }
});

// The old "tools/call install_rhino_mcp_platform with unresolvable yak" test is
// gone: with the upfront probe in place, an unresolvable yak now routes to
// rhino-missing-fallback before install_rhino_mcp_platform is ever exposed. The
// defensive null-yak branch inside doInstall still exists for the race where
// Rhino is uninstalled mid-flight, but isn't reachable through stdio alone.

test("fallback: tools/call for an unknown tool → JSON-RPC -32601", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_INSTALLED });
  try {
    await l.request("initialize", {}, 0);
    const r = await l.request("tools/call", { name: "not_a_tool", arguments: {} }, 1);
    assert.equal(r.error?.code, -32601);
    assert.match(r.error.message, /unknown tool/);
  } finally {
    await l.close();
  }
});

test("fallback: unsupported method on a request id → JSON-RPC -32601", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_INSTALLED });
  try {
    await l.request("initialize", {}, 0);
    const r = await l.request("resources/list", {}, 1);
    assert.equal(r.error?.code, -32601);
    assert.match(r.error.message, /method not supported in install-fallback mode/);
  } finally {
    await l.close();
  }
});

// --- rhino-missing-fallback (pseudo-MCP, no Rhino installed) ----------------

test("rhino-missing: no Rhino → initialize advertises rhino-mcp-no-rhino", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_MISSING });
  try {
    const r = await l.request("initialize", { protocolVersion: "2024-11-05", capabilities: {}, clientInfo: { name: "t", version: "0" } }, 0);
    assert.equal(r.result?.serverInfo?.name, "rhino-mcp-no-rhino");
    assert.match(l.getStderr(), /entering rhino-missing-fallback mode/);
  } finally {
    const { code } = await l.close();
    assert.equal(code, 0);
  }
});

test("rhino-missing: tools/list returns rhino_not_installed pointing at rhino3d.com", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_MISSING });
  try {
    await l.request("initialize", {}, 0);
    const r = await l.request("tools/list", {}, 1);
    const tools = r.result?.tools ?? [];
    assert.equal(tools.length, 1);
    assert.equal(tools[0].name, "rhino_not_installed");
    assert.match(tools[0].description, /rhino3d\.com\/download/);
    assert.match(tools[0].description, /Rhino 8/);
  } finally {
    await l.close();
  }
});

test("rhino-missing: --default-version 9 propagates into the description", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_MISSING }, ["--default-version", "9"]);
  try {
    await l.request("initialize", {}, 0);
    const r = await l.request("tools/list", {}, 1);
    assert.match(r.result.tools[0].description, /Rhino 9/);
    assert.doesNotMatch(r.result.tools[0].description, /Rhino 8/);
  } finally {
    await l.close();
  }
});

test("rhino-missing: tools/call rhino_not_installed returns download instructions", { skip: !isSupported }, async () => {
  const fake = makeFakeRoot({});
  const l = startFallbackLauncher({ ...fake.env, ...FAKE_YAK_MISSING });
  try {
    await l.request("initialize", {}, 0);
    const r = await l.request("tools/call", { name: "rhino_not_installed", arguments: {} }, 1);
    // Not an error result — just informational text. The model uses this to
    // remind the user what to do next.
    assert.notEqual(r.result?.isError, true);
    assert.match(r.result.content[0].text, /rhino3d\.com\/download/);
  } finally {
    await l.close();
  }
});
