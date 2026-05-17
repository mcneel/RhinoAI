---
title: Claude Code
weight: 3
prev: docs/getting-started
next: docs/try-it-out
toc: false
---

[Claude Code](https://claude.com/claude-code) is Anthropic's
terminal-based AI assistant. The Rhino Claude Code plugin ships ready-made
agents for modelling, drafting, Grasshopper, and more.

Claude Code is a great fit if you're comfortable in a terminal or already
use it for code. If you're not sure, start with [Claude
Desktop](../connector) it's much more friendly.

## Before you start

1. The **Rhino-MCP-Platform** plugin is installed in Rhino. See
   [Getting Started](../getting-started) if you haven't done that yet.
2. **Claude Code** is installed and signed in. See the
   [Claude Code install guide](https://docs.claude.com/claude-code/quickstart)
   if you need it.

## Install the plugin

Inside a Claude Code session, run:

```
/plugin marketplace add mcneel/RhinoMCP
/plugin install mcneel@rhino-mcp
```

Restart the session when prompted. You'll see a `rhino` MCP server
appear in your `/mcp` list.

## What you get

### Slash commands

Quick actions you can fire off without writing a prompt.

| Command | What it does |
| --- | --- |
| `/rhino-mcp:launch-rhino` | Start a Rhino instance for the session. |
| `/rhino-mcp:launch-rhinos` | Start a few Rhino instances side by side. |
| `/rhino-mcp:scene` | Summarise what's in the active document. |
| `/rhino-mcp:snapshot` | Capture the viewport and describe it. |
| `/rhino-mcp:install-mcp` | Wire the Rhino server into the current project. |

### Specialist agents

Each agent is tuned for a different kind of work. You can call them
explicitly (*"have the modeller build me a chair"*) or let Claude pick.

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

## Try it out

Start a Claude Code session and follow the prompts on the
[Try it out](../try-it-out) page.

## Try the agents

Once that's working, see the specialist agents in action. Ask:

{{< prompt >}}
What's in my Rhino document?
{{< /prompt >}}

Claude should delegate to `rhino-inspector` and reply with a summary of
layers, objects, and the viewport.

Then try:

{{< prompt >}}
Modeller: design a 1.6m park bench with wooden slats and cast iron
legs.
{{< /prompt >}}

You should see the bench appear in your Rhino window.

## Tips

- **Agents are colleagues, not commands.** "Hand this off to the
  Grasshopper scripter" works better than trying to direct every step.
- **Multiple Rhinos.** Use `/rhino-mcp:launch-rhinos` to spin up several
  windows and have different agents work in each.

