---
title: Claude Code
icon: claude
weight: 3
prev: docs/getting-started
next: docs/try-it-out
toc: false
author: Callum
editor: SteveF
keywords:
  - Claude Code
  - Anthropic
  - agents
  - terminal
---

[Claude Code](https://claude.com/claude-code) is Anthropic's terminal-based AI assistant. Claude Code is a great fit if you're comfortable in a terminal or already use it for code. If you're not sure, start with [Claude Desktop](../connector), it's much more friendly.

## 1. Install Claude Code

[Claude Code](https://claude.com/claude-code)

## 2. Install the Rhino plugin

{{< yak package="Rhino-MCP-Platform" version="8" >}}
{{< yak package="Rhino-MCP-Platform" version="9" >}}

If that doesn't work you can try the below:

1. Open Rhino 8 (and/or Rhino 9 WIP/BETA)
2. Run the `PackageManager` command
3. Search for, and install Rhino-MCP-Platform

## 3. Connect the two

1. Run the `MCPConnect` command
2. Copy and Paste the prompt into a new Claude Code session
3. Restart Claude Code if prompted to

<!--

## Install the plugin

Inside a Claude Code session, run:

```
/plugin marketplace add mcneel/RhinoMCP
/plugin install mcneel@rhino-mcp
```

Restart the session when prompted. You'll see a `rhino` MCP server appear in your `/mcp` list.

## What you get

### Slash commands

Quick actions you can fire off without writing a prompt.

| Command | What it does |
| --- | --- |
| `/rhino-mcp:launch-rhino` | Start a Rhino instance for the session. |
| `/rhino-mcp:launch-rhinos` | Start a few Rhino instances side by side. |
| `/rhino-mcp:scene` | Summarize what's in the active document. |
| `/rhino-mcp:snapshot` | Capture the viewport and describe it. |
| `/rhino-mcp:install-mcp` | Wire the Rhino server into the current project. |

### Specialist agents

Each agent is tuned for a different kind of work. You can call them explicitly (*"have the modeller build me a chair"*) or let Claude pick.

| Agent | What it's good at |
| --- | --- |
| `rhino-modeller` | Creating and editing geometry. |
| `rhino-drafter` | Drawings, dimensions, layouts. |
| `rhino-inspector` | Reporting on a document without changing it. |
| `rhino-organizer` | Layers, blocks, naming, tidying up. |
| `rhino-teacher` | Explaining Rhino concepts in plain language. |
| `grasshopper-scripter` | Building Grasshopper definitions. |
| `grasshopper-reviewer` | Auditing a definition for issues or simplifications. |
| `grasshopper-teacher` | Explaining Grasshopper concepts. |

-->

## Try it out

<blockquote class="page-note">
Open Claude Code and follow the prompts on the <a href="../../try-it-out">Try It Out</a> page.
</blockquote>

<!--

## Try the agents

Once that's working, see the specialist agents in action. Ask:

{{< prompt >}}
What's in my Rhino document?
{{< /prompt >}}

Claude should delegate to `rhino-inspector` and reply with a summary of layers, objects, and the viewport.

Then try:

{{< prompt >}}
Modeller: design a 1.6m park bench with wooden slats and cast iron
legs.
{{< /prompt >}}

You should see the bench appear in your Rhino window.

## Tips

- **Agents are colleagues, not commands.** "Hand this off to the Grasshopper scripter" works better than trying to direct every step.
- **Multiple Rhinos.** Use `/rhino-mcp:launch-rhinos` to spin up several windows and have different agents work in each.

-->
