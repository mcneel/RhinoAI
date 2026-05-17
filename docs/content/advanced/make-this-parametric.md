---
title: Make this parametric
linkTitle: Make this parametric
weight: 3
---

You've modelled something by hand &mdash; a stair, a panelised facade, a
piece of furniture &mdash; and now you wish you'd built it in
Grasshopper from the start, because the client just asked for "the same
thing but 200mm taller and with seven of them instead of five."

This workflow puts the assistant in the role of a Grasshopper author:
it reads the geometry you already made, infers the recipe, and rebuilds
it as a GH2 graph that drives the same result from sliders.

## What you need

- An MCP-connected assistant ([Claude Code](../docs/cc-plugin),
  Claude Desktop, or similar).
- **Rhino 9** with Rhino MCP running, your hand-built model open, and a
  GH2 canvas available.
- A rough mental model of what the parameters should be &mdash; the
  assistant can guess, but you'll get a better graph if you say so.

## A prompt to start with

{{< prompt >}}
I've modelled this stair by hand. Look at the geometry on layer
`Stair`, work out the construction recipe, and rebuild it as a GH2
definition driven by sliders for tread depth, riser height, total
rise, and number of treads. Reference my existing geometry in the
Rhino doc only as a target to match &mdash; the GH definition should
generate it from scratch. Solve and compare.
{{< /prompt >}}

## What you should see

The assistant calls `list_objects` and reads attributes off your
geometry (layer, dimensions, counts, bounding boxes) to reverse-engineer
the rules. Then it builds the GH2 graph with `g2_place_component`,
`g2_connect`, and `g2_place_slider`, solves it, and compares the output
to your reference geometry &mdash; usually by checking key dimensions
and counts rather than a literal Brep diff.

Expect a back-and-forth: the assistant will often ask "is the spacing
uniform, or does it follow a curve?" before committing to a topology.

## What to review

- **The parameter set.** You wanted four sliders &mdash; did you get
  four? The assistant sometimes adds extras for things you'd rather
  hard-code, or hard-codes something you wanted to expose. A quick
  prompt fixes it.
- **Did it match, or did it cheat?** The cheapest way to "match" your
  geometry is to bake your existing Rhino objects into the GH output.
  That's not parametric. Have the assistant show you the graph and
  confirm it generates from primitives, not from referenced objects.
- **Edge cases.** Push the sliders to extremes (1 tread, 100 treads,
  zero rise) and see whether the graph degrades gracefully or
  explodes. Real-world stairs don't have 100 treads, but the failure
  mode tells you whether the topology is right.

## When the assistant gets stuck

- **Ambiguous intent.** If your hand model has irregular spacing or
  one-off tweaks, the assistant can't tell whether those are
  intentional features or sloppy modelling. Tell it explicitly which
  variations are parametric and which were drift.
- **Missing GH2 components.** If the recipe needs something GH2 doesn't
  have yet, ask for the gap to be called out rather than papered over
  with a `run_python` node that hides the logic.
- **Over-fitting to one example.** A graph that exactly reproduces your
  reference but breaks for any other input isn't useful. If you only
  see it solve once, push it.

## Related

- [Upgrade a GH1 script to GH2](../upgrade-gh1-to-gh2) &mdash; for when
  you already have a parametric definition, just in the old canvas.
