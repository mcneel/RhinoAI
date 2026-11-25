# rhino-mcp-router

A stdio MCP server that sits between an MCP client and one or more running Rhinos. Clients talk to one stable endpoint; the router launches Rhino when needed, tracks instances, and forwards tool calls over HTTP to the plug-in in each one.

```
client ──stdio/JSON-RPC──▶ router ──HTTP──▶ Rhino (slot A)
                              │  └──HTTP──▶ Rhino (slot B)
                              └── state.db (SQLite slot registry)
```

## Why a stdio router

Running the MCP server as a stdio process outside Rhino, instead of having clients connect straight to Rhino over HTTP:

- **Connects immediately.** No restarting the client or syncing it with Rhino. The connection is more stable and has no socket conflicts.
- **Launches Rhino for you.** The router can start Rhino when a client needs it.
- **Runs several Rhinos at once**, including Rhino 8 and 9 side by side, so you can upgrade or downgrade files and plug-ins between versions.
- **Recovers from crashes.** If Rhino crashes, the router tells the agent it happened and why (from the crash report), so the agent can work around it and recover.

## Concurrency

Each MCP client starts its own router process, so several agents mean several routers with no shared memory. The router keeps no state in-process; everything lives in a SQLite database (`state.db`). That lets multiple agents each open their own Rhinos and work at the same time without conflicts.

## Concepts

- **Slot:** one Rhino instance the router knows about. Every proxied tool takes a `slot` argument.
- **Router tools:** `spawn_slot`, `list_slots`, `close_slot`. Everything else is proxied to the plug-in.
- **Slot store:** a SQLite registry shared across router processes, so several clients can share Rhinos without stepping on each other. The slot row doubles as the launch lock.
- **Generated proxies:** `codegen/` is a source generator that reads the plug-in's `[McpServerTool]` methods and emits a proxy for each. You don't hand-write router wrappers for plug-in tools.

## Building

```sh
dotnet build Router.csproj -c Release

# Publish a runnable binary
dotnet publish Router.csproj -c Release -r osx-arm64   # NativeAOT, self-contained
dotnet publish Router.csproj -c Release -r win-x64     # single-file, framework-dependent
```

Supported RIDs: `win-x64`, `win-arm64`, `osx-arm64`. It's published as NativeAOT on macOS and a single-file `.exe` on Windows to keep size and dependencies small. The plug-in build publishes the router into its own output, so most of the time you don't build this directly.

Rhino 8 and Rhino 9 get slightly different router builds (`RhinoTarget`), because Rhino 8 can't reference the latest Grasshopper 2 packages from NuGet.

## Configuration

| CLI flag | Env var | Purpose |
|---|---|---|
| `--default-version`, `-v` | `RHINO_MCP_DEFAULT_VERSION` | Rhino to launch when none is specified: `8`, `9`, `WIP` |
| `--rhino-exe <ver>=<path>` | `RHINO_MCP_RHINO_EXE_8`, `_9`, `_WIP` | Override the Rhino executable (`Rhino.exe` or `.app` bundle) per version |
| `--startup-timeout` | `RHINO_MCP_STARTUP_TIMEOUT` | Seconds to wait for Rhino to become ready |
| `--tool-timeout` | `RHINO_MCP_TOOL_TIMEOUT` | Per-call timeout |
| | `RHINO_MCP_HOME` | Override the router's state directory (`state.db`, staged binaries) |

CLI flags win over env vars.

## Rules

- Never write to stdout except JSON-RPC. All logging goes to stderr.
- Keep the router free of Rhino dependencies. It has to stay small and run without Rhino loaded.
- NativeAOT on macOS: avoid reflection-based JSON. Add types to `RouterJsonContext`.

## Debugging

Close and restart your MCP client after each router rebuild. The client keeps the old router process running until it restarts.

## Tests

```sh
dotnet test ../../tests/Router.Tests -c Release
dotnet test ../../tests/Integration.Tests            # spawns real Rhinos; local only
```
