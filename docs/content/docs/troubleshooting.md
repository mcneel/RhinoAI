---
title: Troubleshooting
weight: 6
---

The common gotchas, in plain language. If you don't find your problem
here, [open an issue on GitHub](https://github.com/mcneel/RhinoMCP/issues)
or ask in the [Rhino Discourse AI category](https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162).

## My assistant doesn't see Rhino at all

Most often this means the connection wasn't made on startup.

1. Make sure you installed the Rhino plugin via Rhino's `PackageManager`
   (search for **Rhino-MCP-Platform**).
2. Fully **quit and reopen** your AI assistant. Many assistants only look
   for MCP connections when they start up.
3. In Claude Desktop, check Settings &rarr; Developer to see if the Rhino
   connector is listed as connected.

## My assistant connected, but nothing happens when I ask for geometry

The assistant is talking to the router, but Rhino itself isn't reachable.

- Open Rhino. The router can launch it for you, but if you've blocked
  that or it's a fresh install, opening Rhino yourself is the quickest
  fix.
- Try again. If Rhino was just opening, the first call sometimes lands
  before the plugin has finished loading.

## Rhino just crashed mid-conversation

The router notices when Rhino crashes and tells your assistant, so the
assistant can offer to relaunch and retry.

If the same prompt keeps causing a crash:

1. Grab the crash report from Rhino's crash log folder.
2. File an issue on [GitHub](https://github.com/mcneel/RhinoMCP/issues)
   with the prompt and the crash report attached.

## The assistant says it did something, but I don't see it

A few things to check:

- **Are you looking at the right Rhino window?** If you have several
  open, the assistant may have edited a different one. Look at the
  window titles.
- **Hit `Zoom &rarr; Extents`.** Sometimes the assistant places geometry
  far from origin.
- **Check the layers panel.** Geometry may be on a hidden layer.

## I want to use Rhino 9 (WIP) instead of Rhino 8

The router defaults to Rhino 8. To target Rhino 9:

- **Claude Desktop:** Reinstall the connector with the Rhino 9 variant
  (when available) or edit the connector config to pass `--default-version 9`
  to the router.
- **Claude Code / custom config:** Add `-v 9` to the `rhino-mcp-router`
  arguments in your MCP config.

You can keep Rhino 8 and 9 connected side by side &mdash; the router
treats them as separate slots.

## Grasshopper tools aren't working

- Grasshopper 1 tools (anything starting with `gh1_`) need **Rhino 8**.
- Grasshopper 2 tools (`gh2_`) need **Rhino 9 WIP**.
- In Rhino 9, you may need to ask the assistant to **start Grasshopper 2**
  before placing components.

## I'm writing custom Python or C# snippets

If you're using `run_python` or `run_csharp` (advanced), use the injected
`__rhino_doc__` variable rather than `sc.doc` or `rs.*`. With multiple
Rhino instances open, those globals may point at the wrong document.

## Anywhere else to ask?

- [GitHub issues](https://github.com/mcneel/RhinoMCP/issues) for bugs.
- [Rhino Discourse AI category](https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162)
  for questions and ideas.
