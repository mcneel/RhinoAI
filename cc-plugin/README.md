# Claude Code plugin

A [Claude Code](https://docs.claude.com/en/docs/claude-code) plugin, `rhino-mcp`, that connects Claude Code to Rhino and adds Rhino-specific agents, commands, and skills. Distributed through the `McNeel` marketplace in [`.claude-plugin/marketplace.json`](../.claude-plugin/marketplace.json).

## Prerequisites

- The **Rhino-MCP-Platform** package installed in Rhino (Package Manager, or `/install-mcp` from this plugin).
- Node on `PATH`.

Rhino doesn't need to be running. `.mcp.json` starts `router-launcher.mjs` over stdio as the `rhino` MCP server; it finds the router inside the installed package, and the router launches Rhino when needed.

## Install

From a Claude Code session:

```
/plugin marketplace add mcneel/RhinoAI
/plugin install rhino-mcp@McNeel
```

For local development, point the marketplace at your clone (the repo root, where `.claude-plugin/` lives):

```
/plugin marketplace add /path/to/RhinoAI
/plugin install rhino-mcp@McNeel
```

## Contents

| Path | What |
|---|---|
| `.mcp.json` | Registers the `rhino` MCP server (stdio, via `router-launcher.mjs`) |
| `agents/` | Subagents: `rhino-modeler`, `rhino-drafter`, `rhino-inspector`, `rhino-organizer`, `rhino-teacher`, `grasshopper-scripter`, `grasshopper-reviewer`, `grasshopper-teacher`. `rhino-modeler` is picked automatically for create/edit/inspect geometry work. |
| `commands/` | `/rhino-mcp:snapshot` (capture and describe the active viewport), `/rhino-mcp:scene` (summarize the document), `/rhino-mcp:install-mcp`, `/rhino-mcp:launch-rhino`, `/rhino-mcp:launch-rhinos` |
| `skills/` | `launch-rhino`, `launch-rhinos` |
| `router-launcher.mjs` | Committed copy of `../shared/router-launcher.mjs` |

## Sanity check

After installing, ask something like *"what's in my Rhino doc?"* Claude should use the `rhino-modeler` agent or `/rhino-mcp:scene` and reply with a summary.

## Editing

- Agents, commands, and skills are Markdown with YAML frontmatter.
- An agent's `tools:` list must use tool names the plug-in actually exposes (`mcp__rhino__<tool>`). Check against `rhino/plugin/Tools/` when you add or rename a tool.
- Don't edit `router-launcher.mjs` here. Edit `shared/router-launcher.mjs` and copy it; `tests/launcher` checks the two are byte-equal.
- Bump `version` in `.claude-plugin/plugin.json` when you change the plugin.
