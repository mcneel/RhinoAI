---
name: launch-rhino
description: Start a single Rhino MCP session. Accepts a version argument (e.g. `8`, `WIP`) and an optional explicit port. Walks forward from `10500` to pick a free port if none was given. On macOS this opens a new document in the running Rhino; on Windows it launches a new Rhino process. Use when the user asks to start a Rhino MCP session — for N parallel Rhinos use `/launch-rhinos` instead.
---

# Launching a Rhino MCP session

Follow the procedure in the [`/launch-rhino`](../../commands/launch-rhino.md) slash command. It owns the full launch flow — port selection, OS branch, listener wait, and reporting. Pass through the version argument (`8`, `WIP`, `9`); default to `8`. If a port was provided as the second argument, pass that through too and skip the port probe. Relay the assigned port back to the user.
