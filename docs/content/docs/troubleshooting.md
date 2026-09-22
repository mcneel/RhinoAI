---
title: Troubleshooting
icon: question-mark-circle
weight: 100
prev: docs
author: Callum
editor: SteveF
keywords:
  - troubleshooting
  - FAQ
  - support
  - connection
---

Common issues. If you don't find your problem here, [open an issue on GitHub](https://github.com/mcneel/RhinoMCP/issues) or ask in the [Rhino Discourse AI category](https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162).

## My AI Agent doesn't see Rhino at all

Most often this means the connection wasn't made on startup.

1. Restart **quit and reopen** your AI assistant
2. Make sure you installed the Rhino plugin via Rhino's `PackageManager` (search for **Rhino-MCP-Platform**).
3. In Claude Desktop, check Settings &rarr; Extensions to see if the Rhino3d connector is listed as connected.

## Rhino just crashed mid-conversation

The connector notices when Rhino crashes and tells your assistant, so the assistant can offer to relaunch and retry.

<blockquote class="page-alert">
If the same prompt keeps causing a crash, ask Claude or your LLM of choice to help you file an issue on GitHub.
</blockquote>

## The assistant says it did something, but I don't see it

A few things to check:

- **Are you looking at the right Rhino window?** If you have several open, the assistant may have edited a different one. Look at the window titles.
- **Use View &rarr; Zoom &rarr; Zoom Extents.** the geometry may be far from the origin.
- **Check the layers panel.** Geometry may be on a hidden layer.

## I want to use Rhino 9 (WIP/BETA) instead of Rhino 8

The router defaults to Rhino 8. To target Rhino 9:

- **Claude Desktop:** Open the settings for the connector and change 8 to 9
- **Claude Code / custom config:** Add `"args": ["-v", "9"]` to the `mcpServers` -> `rhino` key in `~/.claude.json`.

## Spawned Rhino windows are burying my desktop

Every slot the assistant spawns is a full Rhino window, so a batch of ten is ten windows fighting for the screen. The router can start them with the window hidden instead:

- **Claude Code / custom config:** add `"args": ["--hidden"]` to the `mcpServers` -> `rhino` key in `~/.claude.json`, alongside any `-v` you already pass.
- **Prefer to keep them reachable?** `--hidden=minimized` sends them to the taskbar rather than out of sight, and they never steal focus.
- The environment variable `RHINO_MCP_HIDDEN=1` does the same thing where passing arguments is awkward. An argument beats the variable.

The Rhino behind a hidden slot is still a full Rhino: it loads plugins, opens documents, and `get_viewport_image` renders from it exactly as it would from a window you can see. Only the window is hidden, and only on Windows.

Two things to know before you turn it on. A Rhino you cannot see is one you cannot close by hand, so if a run leaks a slot you will need `list_slots` and `close_slot` to clear it. And a dialog that would normally block startup in plain sight, a license prompt for instance, now blocks it invisibly; if slots stop appearing, drop `--hidden` and watch a spawn happen.

## Grasshopper tools aren't working

- Grasshopper 2 tools (`gh2_`) require **Rhino 9 WIP/BETA**.
- If you want to use Grasshopper 2, specify Rhino WIP to your AI Agent

## The MCP Plugin doesn't load or show up in the package manager?

- Net framework is not supported
- Intel Macs are not supported
- Update your Rhino to the latest release

## Anywhere else to ask?

<blockquote class="page-note">
<ul>
<li><a href="https://github.com/mcneel/RhinoMCP/issues">GitHub issues</a> for bugs or documentation errors.</li>
<li><a href="https://discourse.mcneel.com/c/rhino/artificial-intelligence-rhino/162">Rhino Discourse AI category</a> for questions and ideas.</li>
</ul>
</blockquote>
