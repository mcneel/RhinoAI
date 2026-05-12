---
description: Launch N parallel Rhino MCP sessions and fan out one agent per Rhino.
argument-hint: <count> [rhino-version]
---

Pick `$1` free MCP slots from `.mcp.json`, launch a Rhino MCP listener on each, then spawn `$1` parallel agents — one per Rhino. The whole point is to make *agent-per-Rhino* parallelism a single-command workflow.

## Arguments

- `$1` — **required** integer N (number of Rhinos). If missing, ask the user how many they want before proceeding. No upper cap from the user's side, but the project ships **8 pre-declared slots** (`rhino`, `rhino-2` … `rhino-8`, ports `10500`-`10507`) — so the realistic max is 8.
- `$2` — Rhino version (`8`, `WIP`, `9`). Defaults to `8`. See [`/launch-rhino`](launch-rhino.md) for the version table.

## Slot table

| Slot name | Port  |
|-----------|-------|
| `rhino`   | 10500 |
| `rhino-2` | 10501 |
| `rhino-3` | 10502 |
| `rhino-4` | 10503 |
| `rhino-5` | 10504 |
| `rhino-6` | 10505 |
| `rhino-7` | 10506 |
| `rhino-8` | 10507 |

Mapping: port `10500` ↔ slot `rhino`; port `1050X` (X ≥ 1) ↔ slot `rhino-{X+1}`.

## Step 1 — find N free slots

Walk ports `10500`-`10507` and collect the first N that nothing is listening on. If fewer than N are free, proceed with what you got and tell the user clearly: "Asked for N, only M slots were free."

### macOS (`Darwin`)

```bash
N=$1
free_ports=()
for p in $(seq 10500 10507); do
  if ! nc -z localhost "$p" 2>/dev/null; then
    free_ports+=("$p")
    [ "${#free_ports[@]}" -eq "$N" ] && break
  fi
done
echo "free: ${free_ports[@]}"
```

### Windows (bash via git-bash / WSL)

```bash
N=$1
free_ports=()
for p in $(seq 10500 10507); do
  if ! netstat -ano -p tcp | grep -q "LISTENING.*:$p "; then
    free_ports+=("$p")
    [ "${#free_ports[@]}" -eq "$N" ] && break
  fi
done
echo "free: ${free_ports[@]}"
```

## Step 2 — launch the Rhinos

Run `uname` to branch.

### macOS (`Darwin`) — sequential

Only one Rhino process can run on macOS. The first launch creates that process; subsequent slots open new documents inside it (each new doc starts its own MCP server on its own port via the `_-RhinoMCP <port>` runscript).

1. **First port** — launch Rhino fresh:
   ```bash
   open -n -a "${app_name}" --args -nosplash "-runscript=_-RhinoMCP ${free_ports[0]} _Enter"
   ```
2. **Wait** until `free_ports[0]` is bound (poll with `nc -z localhost <port>` for up to 15 s).
3. **Remaining ports** — for each remaining port `p`, identify the **first port's slot name** (call it `first_slot`) and drive that Rhino via MCP to open a new doc + start its MCP server:
   ```
   mcp__plugin_rhino-mcp_${first_slot}__run_command(
     command_name="_New",
     script="_-RhinoMCP <p> _Enter"
   )
   ```
   Wait for `<p>` to bind before moving to the next.

### Windows — parallel

Multiple Rhino processes can coexist. Fire all N launches at once via PowerShell `Start-Process` (non-blocking), then wait for every port to come up.

```bash
for p in "${free_ports[@]}"; do
  powershell.exe -NoProfile -Command \
    "Start-Process 'C:/Program Files/{install-dir}/System/Rhino.exe' -ArgumentList '/nosplash','/runscript=\"_RhinoMCP $p _Enter\"'" &
done
wait
```

Replace `{install-dir}` per the version table in [`/launch-rhino`](launch-rhino.md).

## Step 3 — wait for every listener

```bash
for p in "${free_ports[@]}"; do
  for i in {1..15}; do nc -z localhost "$p" 2>/dev/null && break; sleep 1; done
done
```

If any port fails to come up after 15 s, report which one(s) and proceed with the rest. Don't silently retry.

## Step 3.5 — ask the user to reconnect MCP

**This is unavoidable manual friction.** Claude Code reads `.mcp.json` at session start and lazy-connects HTTP servers. Any slot whose Rhino wasn't listening at session start is in a "not connected" state — its `mcp__plugin_rhino-mcp_<slot>__*` tools aren't usable yet. There is no programmatic API for Claude to trigger reconnect; only the `/mcp` slash command can do it, and it's interactive.

Before fanning out, tell the user *exactly* this (substituting actual numbers):

> Four Rhinos are listening on slots `rhino`, `rhino-2`, `rhino-3`, `rhino-4`. Please run `/mcp` **4 times** so each newly-bound slot gets reconnected. Each `/mcp` invocation auto-selects one unconnected server. Once you've done that, say "go" (or anything) and I'll fan out the agents.

Then **stop and wait for the user's reply**. Don't fan out until they confirm. If a slot's tools turn out to still be unreachable when the agents try to use them, the agent will hit `InputValidationError` — at which point fall back to having the user `/mcp` that specific slot.

(Slots that were already connected at session start because their port was listening before — e.g. a leftover Rhino from a previous orchestrator run — don't need re-reconnection. The user only needs to `/mcp` once per *newly-launched* Rhino.)

## Step 4 — fan out agents

For each successfully-bound port, spawn **one** `general-purpose` agent in parallel (single message, multiple `Agent` tool calls). Each agent's prompt **must**:

1. State the assigned slot name (e.g. `rhino-3`) and port.
2. Instruct the agent to use **only** the `mcp__plugin_rhino-mcp_<slot>__*` tools for that slot. Plugin-distributed MCP servers are namespaced under `plugin_rhino-mcp_` in Claude Code's tool list. Getting the slot wrong means two agents collide on the same Rhino.
3. Carry whatever task the user described in their original request. If the user gave N distinct tasks, distribute them. If one common task, repeat it for each agent. If no task was given, instruct the agent to verify its Rhino is reachable (e.g. `mcp__plugin_rhino-mcp_<slot>__list_objects`) and stand by.

Prompt template:

```
You are driving a dedicated Rhino instance via MCP slot `<slot>` (http://localhost:<port>).

Use ONLY tools with this exact prefix: mcp__plugin_rhino-mcp_<slot>__*
(e.g. mcp__plugin_rhino-mcp_<slot>__run_command, mcp__plugin_rhino-mcp_<slot>__list_objects).
Do NOT use tools for any other slot — those belong to sibling agents working in different Rhinos.

Task: <per-agent task from the user>

When done, report back tersely what you did, what's on screen, and the slot you used.
```

Wait for **all** spawned agents to return before proceeding to Step 5.

## Step 5 — close the Rhinos

Once every agent has finished, gracefully close each Rhino the orchestrator launched. For each slot whose port we brought up in Step 2, call:

```
mcp__plugin_rhino-mcp_<slot>__run_command(
  command_name="_-Exit",
  script="_No _Enter"
)
```

`_-Exit` exits Rhino; `_No` declines the "save changes?" prompt (the agents already saved anything they were asked to). The MCP call itself will likely error or return as the server shuts down — that's expected. After issuing all N close calls, briefly poll netstat to confirm the ports went away:

```bash
for p in "${free_ports[@]}"; do
  for i in {1..10}; do
    if ! netstat -ano -p tcp | grep -q "LISTENING.*:$p "; then break; fi
    sleep 1
  done
done
```

If a port doesn't release within 10 s, report which slot didn't close — the user can clean it up manually.

## Step 6 — report

Tell the user:
- Which slots/ports came up, who did what, and what files (if any) the agents produced.
- Which Rhinos were closed cleanly vs. left running.
- Any failures or notable warnings.

Keep it tight — the agents already reported their own results in Step 4.

## Notes

- Slot pre-declaration in `.mcp.json` is required because Claude Code reads MCP config at session start and plugin-distributed agents cannot override it. See `.mcp.json` to extend beyond 8 slots — just add more entries (and the corresponding ports here).
- If a port in the `10500`-`10507` range is held by an unrelated process, we skip it and use the next free slot. The user just gets fewer Rhinos than requested.
- N=1 is valid — degenerates to a single launch + single agent. Same code path.
