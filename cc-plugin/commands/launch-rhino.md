---
description: Launch a single Rhino MCP session via the router.
argument-hint: [rhino-version]
---

Spawn one Rhino managed by the router. The router (`rhino-mcp-router.exe`, shipped inside the Rhino-MCP-Platform yak) is the single stdio MCP server Claude Code talks to. It owns process lifecycle and port allocation — no port probing, no `/mcp` reconnect, no bash needed.

## Argument

- `$1` — optional Rhino version (`8`, `WIP`, `9`). Defaults to whatever the router was configured with at startup (typically `8`).

If `$1` doesn't match one of the supported versions, ask the user to clarify rather than guess.

## Step 1 — spawn

Call:

```
mcp__plugin_rhino-mcp_rhino__spawn_slot(version="$1")
```

(Omit `version` to use the router's default.)

The call returns a `ChildRhino` object: `{ slot_id, port, pid, version }`. The `slot_id` is an animal name (`armadillo`, `axolotl`, …).

## Step 2 — report

Tell the user the slot ID and let them know subsequent tool calls should pass `slot="<slot_id>"`.

Example:

> Spawned Rhino 8 as slot **`armadillo`** (port 47100). Pass `slot="armadillo"` on any subsequent rhino-mcp tool call to target it.

## Closing the Rhino

When the user is done:

```
mcp__plugin_rhino-mcp_rhino__close_slot(slot="armadillo")
```

For N parallel Rhinos, use `/launch-rhinos` instead — it handles spawn + fan-out + cleanup as one workflow.
