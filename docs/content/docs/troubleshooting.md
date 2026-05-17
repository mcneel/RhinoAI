---
title: Troubleshooting
weight: 100
---

The common gotchas, in plain language. If you don't find your problem
here, [open an issue on GitHub](https://github.com/mcneel/RhinoMCP/issues)
or ask in the [Rhino Discourse AI category](https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162).

## My assistant doesn't see Rhino at all

Most often this means the connection wasn't made on startup.

1. Fully **quit and reopen** your AI assistant. Many assistants only look
   for MCP connections when they start up.
2. Make sure you installed the Rhino plugin via Rhino's `PackageManager`
   (search for **Rhino-MCP-Platform**).
3. In Claude Desktop, check Settings &rarr; Extensions to see if the Rhino3d
   connector is listed as connected.

## My assistant connected, but nothing happens when I ask for geometry

My agent can't reach Rhino

- Open Rhino and run `RhinoMCP`.
- Try again. If Rhino was just opening, the first call sometimes lands
  before the plugin has finished loading.

## Rhino just crashed mid-conversation

The connector notices when Rhino crashes and tells your assistant, so the
assistant can offer to relaunch and retry.

> If the same prompt keeps causing a crash ask Claude to help you file an issue on GitHub.

## The assistant says it did something, but I don't see it

A few things to check:

- **Are you looking at the right Rhino window?** If you have several
  open, the assistant may have edited a different one. Look at the
  window titles.
- **Use View &rarr; Zoom &rarr; Zoom Extents.** the geometry may be far from the origin.
- **Check the layers panel.** Geometry may be on a hidden layer.

## I want to use Rhino 9 (WIP) instead of Rhino 8

The router defaults to Rhino 8. To target Rhino 9:

- **Claude Desktop:** Open the settings for the connector and change 8 to 9
- **Claude Code / custom config:** Add `-v 9` to the `rhino-mcp-router`
  arguments in your MCP config.

## Grasshopper tools aren't working

- Grasshopper 2 tools (`gh2_`) require **Rhino 9 WIP**.
- In Rhino 9, you may need to ask the assistant to **start Grasshopper 2**
  before placing components.

## Anywhere else to ask?

- [GitHub issues](https://github.com/mcneel/RhinoMCP/issues) for bugs or documentation errors.
- [Rhino Discourse AI category](https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162)
  for questions and ideas.
