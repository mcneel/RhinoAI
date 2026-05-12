---
description: Launch a new Rhino instance with RhinoMCP on a free port for parallel sessions.
argument-hint: [rhino-version] [port]
---

Bring up a `RhinoMCP` listener and report the port back to the user.

## Arguments

- `$1` — Rhino version (`8`, `WIP`, `9`). Defaults to `8`. See table below.
- `$2` — optional explicit port. If given, use exactly that port and skip the probe in Step 1. Required when called by the `/launch-rhinos` orchestrator (it pre-assigns ports from declared slots).

### Version table

| `$1`          | macOS app name | Windows install dir |
|---------------|----------------|---------------------|
| `8` (default) | `Rhino 8`      | `Rhino 8`           |
| `WIP`         | `RhinoWIP`     | `Rhino 9 WIP`       |
| `9`           | `Rhino 9`      | `Rhino 9`           |

If `$1` doesn't match the table, ask the user to clarify rather than guess.

## Step 1 — pick a port (skip if `$2` was provided)

Start at `10500` and walk upward until you find a TCP port nothing is listening on. `ping` is not an option — it tests host reachability, not specific ports.

### macOS (`Darwin`)

```bash
port=10500
while nc -z localhost "$port" 2>/dev/null; do port=$((port+1)); done
```

Fall back to `lsof -i :"$port" >/dev/null 2>&1` if `nc` is missing.

### Windows

PowerShell:
```powershell
$port = 10500
while (Test-NetConnection -ComputerName localhost -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue) {
  $port++
}
```

Or from bash (`netstat` ships with Windows; `findstr` is the cmd equivalent of `grep`):
```bash
port=10500
while netstat -ano -p tcp | grep -q "LISTENING.*:$port "; do port=$((port+1)); done
```

## Step 2 — start the listener

Run `uname` to branch.

### macOS (`Darwin`)

Rhino runs as a single process and the MCP server is tied to the active document.

- If port `10500` is **free**, no Rhino is serving MCP yet. Launch a fresh one:
  ```bash
  open -n -a "${app_name}" --args -nosplash "-runscript=_-RhinoMCP {port} _Enter"
  ```
- If port `10500` is **in use**, a Rhino is already up. Drive it via MCP to open a fresh document and start a new MCP server on the requested port in one call:
  ```
  mcp__plugin_rhino-mcp_rhino__run_command(
    command_name="_New",
    script="_-RhinoMCP {port} _Enter"
  )
  ```

  (The `plugin_rhino-mcp_` prefix is what Claude Code namespaces plugin-distributed MCP tools under. If you installed RhinoMCP outside a plugin — e.g. via `mcp add rhino http://localhost:10500` — the tool is just `mcp__rhino__run_command` instead.)

### Windows

Always launch a new process — multiple Rhinos can coexist.

Use PowerShell.

```powershell
Start-Process "C:/Program Files/{install-dir}/System/Rhino.exe" -Args '/nosplash','/runscript="_RhinoMCP {port} _Enter"'
```

## Step 3 — wait for the listener

```bash
for i in {1..10}; do nc -z localhost "$port" && break; sleep 1; done
```

If the port still isn't open after 10s, report failure plainly — don't keep retrying silently.

## Step 4 — report

State the Rhino version and the assigned port, e.g.
> Rhino 8 MCP listening on port 10501. Point an MCP client at `http://localhost:10501` to drive it.

## Notes

- The leading `_` on each script token suppresses Rhino's command-name localization; the leading `-` on `-RhinoMCP` suppresses dialogs.
- Base port `10500` matches the first slot in [`.mcp.json`](../.mcp.json). Slots `rhino` through `rhino-8` cover `10500`-`10507`. Change here if the project default ever moves.
- The Rhino plugin's own `DefaultPort` (in `rhino/RhMcpHost.cs`) is intentionally a different number — that's the default Rhino suggests for interactive `_RhinoMCP` runs without a port. This skill always passes a port explicitly, so the two are independent.
