---
name: launch-rhino
description: Start a Rhino MCP session on an unused port. Accepts a version argument (e.g. `8`, `WIP`) to pick which Rhino to use. On macOS this opens a new document in the running Rhino; on Windows it launches a new Rhino process. Use when the user asks to start another Rhino, spin up a parallel Rhino agent, or wants a fresh Rhino MCP session without disturbing an existing one.
---

# Launching a Rhino MCP session

Follow the procedure in the [`/launch-rhino`](../../commands/launch-rhino.md) slash command. It owns the full launch flow — port selection, OS branch, listener wait, and reporting. Pass through the version argument (`8`, `WIP`, `9`); default to `8`. Relay the assigned port back to the user.
