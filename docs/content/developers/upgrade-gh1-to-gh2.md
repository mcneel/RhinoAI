---
title: Upgrade Grasshopper components to GH2
linkTitle: Upgrade GH1 to GH2
weight: 3
---

Rhino 9 ships Grasshopper 2 alongside Grasshopper 1. If your plugin has
GH1 components and you want GH2 equivalents, this is a great job to hand
to Claude Code with Rhino MCP &mdash; the MCP exposes parallel `g1_*` and
`g2_*` tool families, so the assistant can place a GH1 component and its
new GH2 counterpart on their respective canvases in the same session and
compare what they solve to.

## What you need

- **Claude Code** with the [Rhino MCP plugin](../docs/cc-plugin) installed.
- **Rhino 9** open with Rhino MCP running, and Grasshopper 2 available.
- Your plugin's source checked out locally, with Claude Code started in
  that repo.

## The loop

With both canvases reachable, the assistant can:

- Read each GH1 component's inputs, outputs, and solve logic.
- Scaffold a GH2 equivalent and rebuild.
- Place the GH1 component on a GH1 canvas with sample inputs, place the
  new GH2 component on a GH2 canvas with the same inputs, and compare
  outputs.
- Iterate until both solve the same way, then move to the next component.

The side-by-side check matters: GH2's parameter and data-tree conventions
aren't a one-for-one match with GH1, so "it compiles and places" isn't
enough on its own.

## A prompt to start with

{{< prompt >}}
For each GH1 component in this plugin, add a GH2 equivalent. After each
one, place the GH1 version on a GH1 canvas and the GH2 version on a GH2
canvas with the same sample inputs, and confirm they solve to the same
result. Work one component at a time and show me the diff before each
file change.
{{< /prompt >}}

## What to review

- **Parameter types.** GH2's type system isn't identical to GH1's; check
  that the assistant picked the right GH2 type rather than the
  closest-named one.
- **Data-tree handling.** If your GH1 component does anything non-trivial
  with branches or paths, look at how that translates &mdash; this is the
  most common place for "solves but wrong" bugs.
- **Component metadata.** Names, categories, icons, GUIDs &mdash; easy to
  get wrong, hard to fix later once users have files referencing them.

## When the assistant gets stuck

If a component doesn't have a clean GH2 equivalent (or relies on a GH1
API that's gone), have the assistant skip it and surface the list of
skipped components at the end, rather than trying to fake it. A short
"these need human eyes" list is more useful than a stubbed component that
silently solves to nothing.
