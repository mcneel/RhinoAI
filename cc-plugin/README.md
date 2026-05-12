# rhino-mcp Claude Code plugin

A minimal Claude Code plugin that wires the [Rhino MCP server](https://github.com/mcneel/RhinoMCP) into Claude Code, plus a couple of slash commands and a modelling agent.

## Prerequisites

1. Rhino is running.
2. The `RhinoMCP` command has been run inside Rhino, and the server is listening on the default port `4862`. (If you changed the port, edit `.mcp.json`.)

## Install

From a Claude Code session:

```
/plugin marketplace add mcneel/RhinoMCP
/plugin install mcneel@rhino-mcp
```

For local development against a clone, point the marketplace at your working tree instead of the GitHub repo:

```
/plugin marketplace add /path/to/RhinoMCP
/plugin install mcneel@rhino-mcp
```

## What's in it

- **MCP server** — connects to `http://localhost:4862` as `rhino`.
- **`/rhino-mcp:snapshot`** — capture the active viewport and describe what's on screen.
- **`/rhino-mcp:scene`** — summarize the contents of the current Rhino document.
- **`rhino-modeler` agent** — drives Rhino for create/edit/inspect tasks; auto-delegated when the request involves geometry work.

## Sanity check

Once installed, ask Claude something like *"what's in my Rhino doc?"* — it should call the `rhino-modeler` agent (or the `/rhino-mcp:scene` command) and respond with a summary.
