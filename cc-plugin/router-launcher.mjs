#!/usr/bin/env node
// Locate the rhino-mcp-router binary inside an installed Rhino-MCP-Platform yak
// and spawn it with our stdio passed through. Yak layout:
//   <packages>/<rhino-ver>/Rhino-MCP-Platform/<pkg-ver>/router/<rid>/rhino-mcp-router[.exe]
// Replaces router-launcher.sh / .cmd so both cc-plugin and the Claude Desktop
// connector can share one launcher and dodge the bash stdin-inheritance footguns.

import { statSync, readdirSync } from "node:fs";
import { spawn } from "node:child_process";
import { join } from "node:path";
import { homedir, constants as osConstants } from "node:os";

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

if (r.considered.length === 0) {
  process.stderr.write(`rhino-mcp-launcher: no Rhino-MCP-Platform yak installed under ${r.root}\n`);
  process.stderr.write(`rhino-mcp-launcher: install Rhino-MCP-Platform via Rhino's PackageManager.\n`);
  process.exit(1);
}

const summary = r.considered.map(c => `${c.ver}/${c.pkgver}${c === r.picked ? "*" : ""}`).join(", ");
process.stderr.write(`rhino-mcp-launcher: candidates [${summary}] (* = picked)\n`);

if (!r.picked) {
  process.stderr.write(`rhino-mcp-launcher: no ${r.exe} found for ${r.rid} in any installed yak — router/<rid>/ likely wasn't published for this platform\n`);
  process.exit(1);
}

process.stderr.write(`rhino-mcp-launcher: exec ${r.picked.path}\n`);

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
try {
  child = spawn(r.picked.path, process.argv.slice(2), { stdio: "inherit" });
} catch (err) {
  spawnFailed(err);
}

// `error` and `close` can both fire for the same failure (ENOENT,
// non-executable on POSIX). Gate so we don't mask a spawn failure as exit 0.
let terminating = false;

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

// Only SIGTERM is forwarded. SIGINT / SIGHUP from a controlling terminal
// already reach the router via process-group delivery — forwarding would
// send a *second* SIGINT, which .NET's host treats as "force quit" and
// skips the ApplicationStopping hook that closes spawned Rhinos. MCP-protocol
// shutdown flows through stdin EOF (inherited), not signals.
process.on("SIGTERM", () => { try { child.kill("SIGTERM"); } catch { /* child already exited */ } });
