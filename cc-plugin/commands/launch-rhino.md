---
description: Launch a new Rhino instance with RhinoMCP on a free port for parallel sessions.
---

Bring up a `RhinoMCP` listener on a free port and report the port back to the user.

## Argument — Rhino version

| Argument      | macOS app name | Windows install dir |
|---------------|----------------|---------------------|
| `8` (default) | `Rhino 8`      | `Rhino 8`           |
| `WIP`         | `RhinoWIP`     | `Rhino 9 WIP`       |
| `9`           | `Rhino 9`      | `Rhino 9`           |

If the argument is missing or empty, default to `8`. If it doesn't match the table, ask the user to clarify rather than guess.

## Step 1 — pick a free port

Start at `4862` and walk upward until you find a TCP port nothing is listening on. `ping` is not an option — it tests host reachability, not specific ports.

### macOS (`Darwin`)

```bash
port=4862
while nc -z localhost "$port" 2>/dev/null; do port=$((port+1)); done
```

Fall back to `lsof -i :"$port" >/dev/null 2>&1` if `nc` is missing.

### Windows

PowerShell:
```powershell
$port = 4862
while (Test-NetConnection -ComputerName localhost -Port $port -InformationLevel Quiet -WarningAction SilentlyContinue) {
  $port++
}
```

Or from bash (`netstat` ships with Windows; `findstr` is the cmd equivalent of `grep`):
```bash
port=4862
while netstat -ano -p tcp | grep -q "LISTENING.*:$port "; do port=$((port+1)); done
```

## Step 2 — start the listener

Run `uname` to branch.

### macOS (`Darwin`)

Rhino runs as a single process and the MCP server is tied to the active document.

- If port `4862` is **free**, no Rhino is serving MCP yet. Launch a fresh one:
  ```bash
  open -n -a "${app_name}" --args -nosplash "-runscript=_-RhinoMCP _Enter"
  ```
- If port `4862` is **in use**, a Rhino is already up. Drive it via MCP to open a fresh document and start a new MCP server in one call:
  ```
  mcp__rhino__run_command(
    command_name="_New",
    script="_-RhinoMCP _Enter"
  )
  ```
  The new RhinoMCP picks the next free port at or above `4862`, which should match the port found in Step 1.

### Windows

Always launch a new process — multiple Rhinos can coexist.

Use PowerShell.

```powershell
Start-Process "C:/Program Files/Rhino 9 WIP/System/Rhino.exe" -Args '/nosplash','/runscript="_RhinoMCP _Enter"'
```


## Step 3 — wait for the listener

```bash
for i in {1..30}; do nc -z localhost "$port" && break; sleep 1; done
```

If the port still isn't open after 30s, report failure plainly — don't keep retrying silently.

## Step 4 — report

State the Rhino version and the assigned port, e.g.
> Rhino 8 MCP listening on port 4863. Point an MCP client at `http://localhost:4863` to drive it.

## Notes

- The leading `_` on each script token suppresses Rhino's command-name localization; the leading `-` on `-RhinoMCP` suppresses dialogs.
- RhinoMCP auto-picks the next free port at or above `4862`. There is no `-Port N` flag on the command — passing one produces `Unknown command: -Port` warnings and the value is ignored.
- Base port `4862` matches the default in [`.mcp.json`](../.mcp.json). Change it here if the project default ever moves.
