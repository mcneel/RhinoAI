---
description: Launch N parallel Rhino MCP sessions via the router and fan out one agent per Rhino.
argument-hint: <count> [rhino-version]
---

Spawn `$1` Rhinos through the router, fan out `$1` parallel agents (one per Rhino), wait for them to finish, then close every Rhino. The router (`rhino-mcp-router.exe`) is the single stdio MCP server Claude Code is connected to; it handles all process and port management.

## Arguments

- `$1` — **required** integer N (number of Rhinos). If missing, ask the user how many they want before proceeding.
- `$2` — optional Rhino version (`8`, `WIP`, `9`). Defaults to the router's configured default (typically `8`).

## Step 1 — spawn N slots

Call `spawn_slot` N times. Each call takes ~15-30 s (cold Rhino startup) and is independent — they can run sequentially via separate tool calls, or you can issue several in parallel by making multiple tool-call requests in one message.

```
slot_1 = mcp__plugin_rhino-mcp_rhino__spawn_slot(version="$2")
slot_2 = mcp__plugin_rhino-mcp_rhino__spawn_slot(version="$2")
…
slot_N = mcp__plugin_rhino-mcp_rhino__spawn_slot(version="$2")
```

Each returns `{ slot_id, port, pid, version }`. Collect the `slot_id` values — those are what the agents will use.

If any spawn fails (e.g. router throws `TimeoutException` because Rhino didn't bind), report which one(s) and proceed with the rest.

## Step 2 — fan out

Spawn N **general-purpose** agents in parallel (single message, N `Agent` tool calls). Each agent's prompt **must**:

1. State its assigned slot ID.
2. Instruct it to pass `slot="<slot_id>"` as the **first argument** on every `mcp__plugin_rhino-mcp_rhino__*` tool call. Skipping or substituting the slot id means the agent will fail or — worse — accidentally drive a sibling's Rhino.
3. Carry whatever task the user described in their original request. If the user gave N distinct tasks, distribute them. One common task → repeat. No task → instruct the agent to verify its Rhino is reachable (e.g. `list_objects(slot="<slot_id>")`) and stand by.

Prompt template:

```
You are driving a dedicated Rhino instance via slot `<slot_id>` (port <port>).

Pass slot="<slot_id>" as the first argument on EVERY mcp__plugin_rhino-mcp_rhino__* tool call.
The router uses this argument to route your call to YOUR Rhino. Other slot IDs belong to
sibling agents working in different Rhinos — don't touch them.

Task: <per-agent task from the user>

When done, report back tersely what you did and the slot you used.
```

Wait for **all** spawned agents to return before proceeding to Step 3.

## Step 3 — close the Rhinos

For each slot ID from Step 1, call:

```
mcp__plugin_rhino-mcp_rhino__close_slot(slot="<slot_id>")
```

The router will terminate each child Rhino. If a slot is already gone (e.g. crashed), `close_slot` returns `false` — that's fine.

## Step 4 — report

Briefly tell the user:
- How many Rhinos came up successfully and which slots got what task.
- Any spawn or close failures.
- That all Rhinos are now closed (or which ones couldn't be killed).

The agents already reported their own results in Step 2 — no need to recap each.

## Why this is simpler than the old model

The router speaks **stdio** MCP to Claude Code, so the connection is alive from session start with no `/mcp` reconnect needed. Children live and die under router control on internal ports Claude never sees. One slot in `.mcp.json` (`rhino`), one MCP namespace (`mcp__plugin_rhino-mcp_rhino__*`), `slot` arg routes to the right child Rhino.
