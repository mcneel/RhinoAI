---
name: launch-rhino
description: Start a Rhino MCP session on an unused port. Accepts a version argument (e.g. `8`, `WIP`) to pick which Rhino to use. On macOS this opens a new document in the running Rhino; on Windows it launches a new Rhino process. Use when the user asks to start another Rhino, spin up a parallel Rhino agent, or wants a fresh Rhino MCP session without disturbing an existing one.
---

# Start a Rhino MCP session on a free port

Picks the first free port at or above `4862`, then:

- **macOS** — Rhino runs as a single process, so prod the running Rhino into a new empty document and start the MCP server there.
- **Windows** — launch a new `Rhino.exe` process and start the MCP server in it.

Each invocation picks a new port, so several MCP sessions can run side by side.

## Argument

The skill takes a single optional argument: the Rhino **version**.

| Argument      | macOS app name | Windows install dir |
|---------------|----------------|---------------------|
| `8` (default) | `Rhino 8`      | `Rhino 8`           |
| `WIP`         | `RhinoWIP`     | `Rhino WIP`         |
| `9`           | `Rhino 9`      | `Rhino 9`           |

If the argument is missing or empty, default to `8`. If it doesn't match a known mapping, ask the user to clarify before proceeding rather than guessing.

## Step 1 — pick a free port

```bash
port=4862
while nc -z localhost "$port" 2>/dev/null; do port=$((port+1)); done
echo "$port"
```

If `nc` is unavailable, swap in `lsof -i :"$port" >/dev/null 2>&1` for the probe.

## Step 2 — start the session

Run the branch matching the user's OS (`uname` returns `Darwin` on macOS).

### macOS

Rhino for Mac has no AppleScript dictionary, so commands are sent via UI scripting (System Events typing into Rhino's command line). Activate Rhino, then keystroke `_-New _None` (return) and `_-RhinoMCP _-Port _<port>` (return, return):

```bash
osascript <<EOF
tell application "${app_name}" to activate
delay 0.8
tell application "System Events"
  keystroke "_-New _None"
  key code 36
  delay 0.3
  keystroke "_-RhinoMCP _-Port _${port}"
  key code 36
  key code 36
end tell
EOF
```

If Rhino isn't already running, launch it first with `open -a "${app_name}"` and give it a few seconds before the AppleScript runs.

The first time the user runs this, macOS will prompt for **Accessibility** permission for whichever app is running the script (Terminal, iTerm, VSCode, Claude Code). They need to approve it in **System Settings → Privacy & Security → Accessibility** before keystroke injection works.

### Windows

Launch a new Rhino instance with a startup script that runs the MCP command. From PowerShell:

```powershell
Start-Process "C:\Program Files\${install_dir}\System\Rhino.exe" `
  -ArgumentList '/nosplash', "/runscript=_-RhinoMCP _-Port _${port} _Enter"
```

Or from a bash-like shell (Git Bash, MSYS):

```bash
"/c/Program Files/${install_dir}/System/Rhino.exe" /nosplash /runscript="_-RhinoMCP _-Port _${port} _Enter" &
```

## Step 3 — confirm the port is listening

```bash
for i in {1..30}; do nc -z localhost "$port" && break; sleep 1; done
```

## Step 4 — report back

Tell the user the chosen version and assigned port, e.g. `Rhino 8 MCP listening on port 4863. Point an MCP client at http://localhost:4863 to drive it.`

## Connecting Claude to the new instance

The plugin's [.mcp.json](../../.mcp.json) hardcodes port `4862`. To drive a non-default port from another Claude Code session, that session needs an MCP config pointing at the new port — either edit `.mcp.json` for that workspace, or add a second entry under a different name (e.g. `rhino-b`) so both can coexist.

## Notes

- The leading `_` on each script token suppresses Rhino's command-name localization; the leading `-` on `-RhinoMCP` and `-New` suppresses dialogs.
- Base port `4862` matches the default in `.mcp.json`; change it here if the project default ever moves.
