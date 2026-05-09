---
name: launch-rhino
description: Launch a new Rhino instance with the RhinoMCP server on an unused port, so multiple Rhino sessions can run in parallel. Use when the user asks to start another Rhino, spin up a parallel Rhino agent, or wants a fresh Rhino MCP session without disturbing an existing one.
---

# Launch a parallel Rhino instance

Starts a fresh Rhino on macOS and runs `RhinoMCP -Port NNNN` inside it on the first free port at or above the base port (`4862`). Each invocation picks a new port so several Rhinos can run side by side.

## Steps

1. **Pick a free port.** Start at `4862` and increment until one is unbound:

   ```bash
   port=4862
   while nc -z localhost "$port" 2>/dev/null; do port=$((port+1)); done
   echo "$port"
   ```

2. **Launch a new Rhino instance and run the command.** `open -n` forces a new process even if Rhino is already running. `--args -runscript=...` tells Rhino to execute the command after startup:

   ```bash
   open -na "Rhino 8" --args -runscript="_-RhinoMCP _-Port _${port} _Enter"
   ```

   If the user has Rhino 7 or RhinoWIP, swap the app name (`"Rhino 7"`, `"RhinoWIP"`).

3. **Confirm the port.** After launching, wait briefly and verify the server is listening:

   ```bash
   for i in {1..30}; do nc -z localhost "$port" && break; sleep 1; done
   ```

4. **Report the result** to the user with the assigned port, e.g. `Rhino launched on port 4863. Point an MCP client at http://localhost:4863 to drive it.`

## Connecting Claude to the new instance

The plugin's [.mcp.json](../../.mcp.json) hardcodes port `4862`. To drive a non-default port from another Claude Code session, that session needs an MCP config pointing at the new port — either edit `.mcp.json` for that workspace, or add an entry under a different server name (e.g. `rhino-b`) so both can coexist.

## Notes

- The leading `_` on each script token suppresses Rhino's command-name localization; the leading `-` on `-RhinoMCP` suppresses any dialog the command might raise.
- If `nc` is not available, substitute `lsof -i :"$port" >/dev/null 2>&1` for the port-probe.
- Base port `4862` matches the default in `.mcp.json`; change it here if the project default ever moves.
