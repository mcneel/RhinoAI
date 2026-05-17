---
title: Architecture
weight: 1
---

Rhino MCP is split across three processes that talk via stdio and a local HTTP channel.

```
  AI Agent  ──stdio──▶  Router  ──HTTP──▶  Rhino plugin
 (Claude,                (rhino-mcp-       (RhMcp.rhp,
  Cursor, …)              router)           in-process)
```

## The router

A standalone process &mdash; `rhino-mcp-router` &mdash; that speaks MCP over stdio with the AI agent and forwards tool calls to the Rhino plugin.

Why a router instead of an HTTP server inside Rhino?

- **Instant connection.** Stdio comes up immediately; no socket reconnect dance when Rhino restarts.
- **Crash recovery.** If Rhino dies, the router reports the crash to the agent (with the reason) instead of going silent, so the agent can recover gracefully.
- **No port conflicts.** No fixed port to clash with other tools.
- **Rhino launching.** The router can start Rhino itself, so users don't have to babysit the process.
- **Multiple instances.** The router can manage several Rhinos at once, including Rhino 8 and 9 side by side.

It's published as **NativeAOT on macOS** and a self-contained **.exe on Windows** to keep the binary tiny and dependency-free.

## State and concurrency

Each AI agent spawns its own router instance. To keep multi-agent setups sane:

- The router itself holds **no in-memory state**.
- All shared state lives in a local SQLite database.
- Two agents can each open their own Rhino and operate simultaneously without stepping on each other.

## The Rhino plugin

`RhMcp.rhp` lives inside Rhino. It hosts the MCP tool implementations and exposes them over a local HTTP endpoint (default `http://localhost:10500`) that the router calls into.

Tools live under [rhino/plugin/Tools/](https://github.com/mcneel/RhinoMCP/tree/main/rhino/plugin/Tools) and are split into:

- Core Rhino tools (geometry, viewport, selection, layers, materials, scripts).
- `GH1/` &mdash; Grasshopper 1 canvas control.
- `GH2/` &mdash; Grasshopper 2 canvas control.

Rhino 8 ships GH1; Rhino 9 ships GH2. The router knows which set is available per instance.

## Codegen

The router doesn't hand-write a wrapper for every tool. Instead it **codegens** generic tool wrappers from the Rhino plugin source, so adding a new tool on the plugin side automatically surfaces in the router after a rebuild.

> **Heads up:** the router has zero Rhino dependencies. Keep it that way to preserve the small footprint.

## Clients

Three first-party clients ship in the repo:

- **Claude Desktop** &mdash; via the [`.mcpb` connector](../../docs/connector).
- **Claude Code** &mdash; via the [`cc-plugin`](../../docs/cc-plugin).
- **Anything else** &mdash; any MCP client that can launch a stdio server works; just point it at `rhino-mcp-router`.
