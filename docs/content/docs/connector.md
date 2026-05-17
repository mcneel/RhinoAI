---
title: Claude Desktop
weight: 4
---

[Claude Desktop](https://claude.ai/download) is Anthropic's friendly chat
app for Mac and Windows. With our connector installed, anything you ask
Claude in the chat window can now happen in Rhino.

This is the easiest way to get going &mdash; no config files, no terminal.

## Before you start

1. The **Rhino-MCP-Platform** plugin is installed in Rhino. See
   [Getting Started](../getting-started) if you haven't done that yet.
2. **Claude Desktop** is installed. Grab it from
   [claude.ai/download](https://claude.ai/download) if you don't have it.

## Install the connector

1. Download the latest `RhinoMCP.mcpb` connector from the
   [releases page](https://github.com/mcneel/RhinoMCP/releases).
2. Double-click the file.
3. Claude Desktop will open and ask if you want to install it. Confirm.
4. Restart Claude Desktop if it asks.

That's it. The connector is now wired up.

> **Updating?** If you've installed an older version before, open Claude
> Desktop's settings, uninstall the old connector, then double-click the
> new file.

## Try it out

Open Claude Desktop, start a new chat, and ask:

> "Open a fresh Rhino doc and make me a torus, then take a screenshot
> and show me how it looks."

If everything's wired up:

- Rhino will launch (if it wasn't already running).
- A torus will appear.
- Claude will reply with a viewport screenshot in the chat.

If anything goes sideways, [Troubleshooting](../troubleshooting) is your
friend.

## Tips

- **Chat fresh per project.** Each chat is a fresh conversation. Start a
  new one when you switch tasks &mdash; Claude won't get confused about
  which document you mean.
- **Undo is your safety net.** Anything Claude does in Rhino is just a
  regular Rhino edit. Hit Ctrl/Cmd+Z to walk it back.
- **Be conversational.** You don't need to write perfect prompts. Ask,
  see the result, then say "make the legs taller" or "try it in red".

## Power-user tip: build your own

If you'd rather build the connector from a clone of the repo, see the
[Developers](../../developers) section.
