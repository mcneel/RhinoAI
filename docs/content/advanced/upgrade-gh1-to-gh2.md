---
title: Upgrade a GH1 script to GH2
linkTitle: Upgrade GH1 to GH2
weight: 1
---

Rhino 9 ships Grasshopper 2 alongside Grasshopper 1. If you have a GH1
definition you'd like to move forward, the MCP exposes parallel `g1_*`
and `g2_*` tool families &mdash; an assistant can read your GH1 graph,
rebuild it on a GH2 canvas, and solve both side-by-side to confirm they
match.

This is different from
[upgrading a plugin's compiled components](../developers/upgrade-gh1-to-gh2);
here you're porting a `.gh` definition, not source code.

## What you need

- An MCP-connected assistant ([Claude Code](../docs/cc-plugin),
  Claude Desktop, or similar).
- **Rhino 9** with Rhino MCP running, the GH1 definition open, and a
  GH2 canvas available.

## A prompt to start with

{{< prompt >}}
Look at my GH1 canvas and rebuild the same definition on a GH2 canvas.
Use the same sample inputs on both, solve them, and tell me whether
the outputs match. If anything doesn't have a clean GH2 equivalent,
stop and ask before substituting.
{{< /prompt >}}

## What you should see

The assistant calls `g1_get_canvas_graph` to read your definition, then
uses `g2_place_component`, `g2_connect`, and `g2_place_slider` to
reconstruct it on the GH2 side. It solves both canvases and compares
the outputs. You can swap slider values on either side and check that
they still agree.

## What to review

- **Data trees.** GH2's branch and path handling isn't identical to
  GH1's. If your definition does anything non-trivial with grafting,
  flattening, or path mapping, look closely at the rebuilt version
  &mdash; this is the most common source of "solves but wrong" bugs.
- **Component substitutions.** GH2 component names and parameter types
  don't always line up one-to-one. Have the assistant flag any case
  where it picked the closest match rather than an exact equivalent.
- **Third-party components.** If your GH1 definition relies on a plugin
  that doesn't have a GH2 build yet, the assistant should surface that
  and skip those nodes rather than fake them.

## When the assistant gets stuck

If part of the definition can't be cleanly ported, ask for a short
"these need human eyes" list at the end instead of stubbed nodes that
silently solve to nothing. A flagged gap is more useful than a quiet
mismatch.
