---
title: Getting Started
weight: 1
---

In about ten minutes you'll have an AI assistant making geometry in your
Rhino window. We'll do it in three steps:

1. Install the Rhino plugin.
2. Pick an AI assistant and connect it.
3. Try a few prompts &mdash; starting simple and building up.

## 1. Install the plugin

Inside Rhino:

1. Run the `PackageManager` command.
2. Search for **Rhino-MCP-Platform** and install it.
3. Restart Rhino if it asks you to.

That's it for the Rhino side. The plugin runs quietly in the background
whenever Rhino is open.

## 2. Pick an AI assistant

Any AI assistant that speaks the [Model Context
Protocol](https://modelcontextprotocol.io) can drive Rhino. The two we
support out of the box:

{{< cards >}}
  {{< card link="../connector" title="Claude Desktop" subtitle="The friendly chat app from Anthropic. Easiest install &mdash; one double-click." >}}
  {{< card link="../cc-plugin" title="Claude Code" subtitle="A terminal-based assistant. Ships with ready-made Rhino and Grasshopper agents." >}}
{{< /cards >}}

Cursor and other MCP-compatible tools work too &mdash; in Rhino, run the
`RhinoMCPConnect` command and it'll print a config snippet you can paste
into your tool of choice.

> **Tip:** You don't need to leave Rhino open. Your assistant can launch
> Rhino for you the first time it needs it.

## 3. Try your first prompts

Open your assistant and start a new chat. Try these in order &mdash; each
one is a little more ambitious than the last.

### A. Say hello

> "Make a 100mm cube at the origin."

You should see Rhino come forward (or launch fresh) and a cube appear.
You've just had an AI use Rhino on your behalf. That's the whole
party trick.

### B. Get a little fancier

> "Make a stack of 12 cylinders, each rotated 15&deg; from the one below,
> radius 50 and height 20."

The assistant will pick a strategy, run it, and explain what it did.
Hit undo (Ctrl/Cmd+Z) if you don't like the result, then ask for a
tweak: *"Same thing but taper the radius from 50 down to 10."*

### C. Look at the document

> "What's in my Rhino document? Summarise it by layer and tell me what's
> on each."

The assistant can **see** your scene &mdash; it can list objects, capture
viewport screenshots, and read layer structure. This is where it stops
feeling like a toy.

### D. Generative modelling

> "Create a procedural park bench, 1.6m long. Five wooden slats on top,
> two cast iron legs. Put it on a layer called `Furniture::Bench`."

Now you're describing **intent** instead of geometry. The assistant
decides the curves and surfaces; you judge the result.

### E. Grasshopper

If you're on Rhino 8 (Grasshopper 1) or Rhino 9 (Grasshopper 2), open
Grasshopper and try:

> "Build me a Grasshopper definition that takes a curve, divides it into
> 20 points, and lofts a circle of varying radius along it."

Components appear, wires connect themselves, and you get a live
definition you can take over and edit.

### F. Tidy up

> "Rename every layer to title case, delete any empty layers, and put
> all curves on a layer called `Sketch`."

Boring chores are where AI shines. Drop a messy file in front of it
and ask for a cleanup pass.

## Where to next?

- [Recipes](../recipes) &mdash; a growing library of prompts grouped by goal.
- [Troubleshooting](../troubleshooting) &mdash; if something didn't work.
- [Developers](../../developers) &mdash; how the platform is built underneath.
