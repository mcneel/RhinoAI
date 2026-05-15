#!/usr/bin/env node
// Locate the rhino-mcp-router binary inside an installed Rhino-MCP-Platform yak
// and spawn it with our stdio passed through. Yak layout:
//   <packages>/<rhino-ver>/Rhino-MCP-Platform/<pkg-ver>/router/<rid>/rhino-mcp-router[.exe]
// Replaces router-launcher.sh / .cmd so both cc-plugin and the Claude Desktop
// connector can share one launcher and dodge the bash stdin-inheritance footguns.

import { statSync, readdirSync } from "node:fs";
import { spawn, spawnSync } from "node:child_process";
import { join } from "node:path";
import { homedir, constants as osConstants } from "node:os";
import { createInterface } from "node:readline";

function resolveRid() {
  if (process.platform === "darwin") return "osx-arm64";
  if (process.platform === "win32") return process.arch === "arm64" ? "win-arm64" : "win-x64";
  return null;
}

function packagesRoot() {
  if (process.platform === "darwin") {
    return join(homedir(), "Library", "Application Support", "McNeel", "Rhinoceros", "packages");
  }
  if (process.platform === "win32") {
    return process.env.APPDATA ? join(process.env.APPDATA, "McNeel", "Rhinoceros", "packages") : null;
  }
  return null;
}

function isDir(p) { try { return statSync(p).isDirectory(); } catch { return false; } }
function isFile(p) { try { return statSync(p).isFile(); } catch { return false; } }
function listDirs(p) { try { return readdirSync(p).filter(n => isDir(join(p, n))); } catch { return []; } }

// Inspect every installed yak and return the full candidate list (search order)
// plus the first one whose router binary exists. Callers log the whole list so
// "why did it pick X" / "why didn't it find anything" is answerable from the
// MCP server log alone.
function findRouter() {
  const rid = resolveRid();
  const root = packagesRoot();
  if (!rid || !root) return { rid, root, considered: [], picked: null };

  const exe = process.platform === "win32" ? "rhino-mcp-router.exe" : "rhino-mcp-router";

  // Numeric-aware reverse sort: 0.10.0 ranks above 0.2.0 (lexical would invert).
  const byVersionDesc = (a, b) => b.localeCompare(a, undefined, { numeric: true });

  const considered = [];
  for (const ver of ["9.0", "8.0"]) {
    const base = join(root, ver, "Rhino-MCP-Platform");
    if (!isDir(base)) continue;
    for (const pkgver of listDirs(base).sort(byVersionDesc)) {
      considered.push({ ver, pkgver, path: join(base, pkgver, "router", rid, exe) });
    }
  }

  const picked = considered.find(c => isFile(c.path)) ?? null;
  return { rid, root, exe, considered, picked };
}

const r = findRouter();

process.stderr.write(`rhino-mcp-launcher: platform=${process.platform}/${process.arch} rid=${r.rid ?? "?"} root=${r.root ?? "?"}\n`);

if (!r.rid || !r.root) {
  process.stderr.write(`rhino-mcp-launcher: unsupported platform\n`);
  process.exit(1);
}

// spawn() can fail two different ways depending on platform:
//   POSIX  — returns a ChildProcess; ENOENT/EACCES surface as an async `error` event.
//   Win32  — throws *synchronously* (e.g. `spawn UNKNOWN` on a file with bad PE format).
// Handle both so the launcher always exits cleanly with code 1 + "spawn failed"
// rather than dumping an unhandled stack trace.
function spawnFailed(err) {
  process.stderr.write(`rhino-mcp-launcher: spawn failed: ${err.message}\n`);
  process.exit(1);
}

let child;
if (r.considered.length === 0) {
  process.stderr.write(`rhino-mcp-launcher: no Rhino-MCP-Platform yak installed under ${r.root}\n`);
  runInstallFallback("no Rhino-MCP-Platform yak is installed");
} else if (!r.picked) {
  const summary = r.considered.map(c => `${c.ver}/${c.pkgver}`).join(", ");
  process.stderr.write(`rhino-mcp-launcher: candidates [${summary}]\n`);
  process.stderr.write(`rhino-mcp-launcher: no ${r.exe} found for ${r.rid} in any installed yak\n`);
  runInstallFallback(`installed Rhino-MCP-Platform yak has no router/${r.rid}/${r.exe}`);
} else {
  const summary = r.considered.map(c => `${c.ver}/${c.pkgver}${c === r.picked ? "*" : ""}`).join(", ");
  process.stderr.write(`rhino-mcp-launcher: candidates [${summary}] (* = picked)\n`);
  process.stderr.write(`rhino-mcp-launcher: exec ${r.picked.path}\n`);
  try {
    child = spawn(r.picked.path, process.argv.slice(2), { stdio: "inherit" });
  } catch (err) {
    spawnFailed(err);
  }
}

// `error` and `close` can both fire for the same failure (ENOENT,
// non-executable on POSIX). Gate so we don't mask a spawn failure as exit 0.
let terminating = false;

if (child) {
  child.on("error", err => {
    if (terminating) return;
    terminating = true;
    spawnFailed(err);
  });

  // `close` (not `exit`) so stdio fully drains — `exit` can truncate the
  // router's final MCP frame on a fast shutdown.
  child.on("close", (code, signal) => {
    if (terminating) return;
    terminating = true;
    if (signal) {
      process.exit(128 + (osConstants.signals[signal] ?? 15));
    } else {
      process.exit(code ?? 0);
    }
  });
}

// When the router can't be found, we don't just die — we'd surface a red dot
// in Claude Desktop's MCP list and the user has no way to recover from chat.
// Instead, start a *placeholder* MCP server that advertises one tool:
// install_rhino_mcp_platform. The user asks Claude to connect to Rhino, the
// model finds only this tool, and the user gets a chat-native consent prompt
// to install the yak into their configured Rhino version.
function parseDefaultVersion(args) {
  for (let i = 0; i < args.length - 1; i++) {
    if (args[i] === "--default-version" || args[i] === "-v") return args[i + 1];
  }
  return "8";
}

function resolveYak(version) {
  if (process.platform === "darwin") {
    const app = { "8": "Rhino 8.app", "9": "Rhino 9.app", WIP: "RhinoWIP.app" }[version];
    if (!app) return null;
    const p = `/Applications/${app}/Contents/Resources/bin/yak`;
    return isFile(p) ? p : null;
  }
  if (process.platform === "win32") {
    const dir = { "8": "Rhino 8", "9": "Rhino 9", WIP: "Rhino 9 WIP" }[version];
    if (!dir) return null;
    const p = join("C:\\Program Files", dir, "System", "Yak.exe");
    return isFile(p) ? p : null;
  }
  return null;
}

function runInstallFallback(reason) {
  const version = parseDefaultVersion(process.argv.slice(2));
  process.stderr.write(`rhino-mcp-launcher: entering install-fallback mode (version=${version}, reason=${reason})\n`);

  const send = msg => process.stdout.write(JSON.stringify(msg) + "\n");
  const reply = (id, result) => send({ jsonrpc: "2.0", id, result });
  const error = (id, code, message) => send({ jsonrpc: "2.0", id, error: { code, message } });

  const tool = {
    name: "install_rhino_mcp_platform",
    description:
      `The Rhino-MCP-Platform plugin is not installed in Rhino ${version} (reason: ${reason}). ` +
      `Ask the user whether to install it now via yak. If they agree, call this tool with no arguments. ` +
      `After install the user must reload this connector (or restart Claude Desktop) so the real router takes over.`,
    inputSchema: { type: "object", properties: {}, additionalProperties: false },
  };

  function doInstall() {
    const yak = resolveYak(version);
    if (!yak) {
      return {
        isError: true,
        content: [{ type: "text", text: `Cannot find yak for Rhino ${version}. Expected it under the Rhino install directory; is Rhino ${version} installed?` }],
      };
    }
    const args = ["install", "Rhino-MCP-Platform"];
    process.stderr.write(`rhino-mcp-launcher: running ${yak} ${args.join(" ")}\n`);
    const res = spawnSync(yak, args, { encoding: "utf8" });
    const stdout = (res.stdout ?? "").trim();
    const stderr = (res.stderr ?? "").trim();
    const ok = res.status === 0;
    const text =
      `yak ${args.join(" ")} (Rhino ${version}) exited ${res.status ?? "?"}` +
      (stdout ? `\n--- stdout ---\n${stdout}` : "") +
      (stderr ? `\n--- stderr ---\n${stderr}` : "") +
      (ok ? `\n\nInstall reported success. Reload this connector or restart Claude Desktop for the real Rhino tools to appear.` : "");
    return { isError: !ok, content: [{ type: "text", text }] };
  }

  const rl = createInterface({ input: process.stdin });
  rl.on("line", line => {
    let req;
    try { req = JSON.parse(line); } catch { return; }
    const { id, method, params } = req;
    if (method === "initialize") {
      reply(id, {
        protocolVersion: params?.protocolVersion ?? "2024-11-05",
        capabilities: { tools: {} },
        serverInfo: { name: "rhino-mcp-installer", version: "0.0.1" },
      });
    } else if (method === "tools/list") {
      reply(id, { tools: [tool] });
    } else if (method === "tools/call") {
      if (params?.name !== tool.name) {
        error(id, -32601, `unknown tool: ${params?.name}`);
        return;
      }
      reply(id, doInstall());
    } else if (id !== undefined) {
      error(id, -32601, `method not supported in install-fallback mode: ${method}`);
    }
  });
  rl.on("close", () => process.exit(0));
  // Keep alive — readline holds stdin open.
}

// Only SIGTERM is forwarded. SIGINT / SIGHUP from a controlling terminal
// already reach the router via process-group delivery — forwarding would
// send a *second* SIGINT, which .NET's host treats as "force quit" and
// skips the ApplicationStopping hook that closes spawned Rhinos. MCP-protocol
// shutdown flows through stdin EOF (inherited), not signals.
process.on("SIGTERM", () => { try { child?.kill("SIGTERM"); } catch { /* child already exited */ } });
