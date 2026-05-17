---
title: Recipes
weight: 10
---

A growing library of prompts you can copy straight into your AI assistant.
Treat them as starting points &mdash; tweak the numbers, swap the
materials, ask follow-up questions. The assistant is conversational; you
don't have to get the prompt right on the first try.

> **Where to paste these:** any MCP-connected assistant works &mdash;
> Claude Desktop, Claude Code, Cursor. If you haven't set one up yet,
> start with [Getting Started](../getting-started).

## Modelling

### Parametric furniture

{{< prompt >}}
Design a parametric coffee table. Top is 1200&times;600&times;30mm walnut.
Four tapered legs, 400mm tall, splayed outward 5&deg;. Put it on a layer
called `Furniture` and apply a wood material.
{{< /prompt >}}

### A spiral staircase

{{< prompt >}}
Build a spiral staircase. 20 steps, 250mm rise per step, 1.5m radius.
Each tread is a 30mm-thick wedge in oak. Add a central column in steel.
{{< /prompt >}}

### Random scatter

{{< prompt >}}
Scatter 200 spheres of varying radius (between 10 and 80mm) across a
5&times;5m region. Colour them by radius from blue (small) to red
(large). No overlaps.
{{< /prompt >}}

### Boolean exploration

{{< prompt >}}
Make me five variations of a chair, each blending a primitive
(cube, sphere, cylinder, cone, torus) into the seat in a different way.
Lay them out in a row, 1.5m apart.
{{< /prompt >}}

## Inspection &amp; reporting

### Document audit

{{< prompt >}}
Audit this document. Tell me: how many objects per layer, anything
off-layer, any zero-length curves, any naked edges on the breps, and
the total bounding box.
{{< /prompt >}}

### What's selected?

{{< prompt >}}
Look at what I have selected and describe it to me. Give me the
object IDs, types, areas, and what layers they're on.
{{< /prompt >}}

### Viewport snapshot

{{< prompt >}}
Take a screenshot of the current viewport and describe what you see.
If anything looks broken or off, point it out.
{{< /prompt >}}

## Drawings &amp; drafting

### Plan and elevations

{{< prompt >}}
Set up four standard views of the current geometry: top plan, front
elevation, right elevation, and a 3/4 perspective. Frame each one to
fit the geometry.
{{< /prompt >}}

### Quick dimensions

{{< prompt >}}
Add dimensions to the selected object. Use horizontal/vertical
dimensions for orthogonal edges, and aligned dimensions for any
diagonals.
{{< /prompt >}}

## Grasshopper

### From scratch

{{< prompt >}}
Build a Grasshopper definition that takes a curve, divides it into
N points (with a slider for N), and places a sphere of slider-controlled
radius at each point.
{{< /prompt >}}

### Explain this definition

{{< prompt >}}
Look at the current Grasshopper canvas and walk me through it.
What does each cluster of components do, and what's the final output?
{{< /prompt >}}

### Simplify

{{< prompt >}}
Look at the Grasshopper canvas. Are there components that could be
merged, removed, or replaced with a simpler equivalent? Suggest
changes, but don't apply them until I say so.
{{< /prompt >}}

### De-spaghetify

{{< prompt >}}
This canvas is a mess. Tidy it up: align components into a left-to-right
flow, group related logic with coloured groups and short scribbled
labels, straighten out crossed wires (insert relays where needed), and
hide any wires that are purely structural. Don't change what the
definition does &mdash; just make it readable.
{{< /prompt >}}

## Cleanup &amp; chores

### Layer hygiene

{{< prompt >}}
Tidy this document's layers. Title case every layer name, delete any
layers with no geometry, and group related layers under sensible
parents.
{{< /prompt >}}

### Standardise materials

{{< prompt >}}
Look at every object's material. Where multiple objects share the same
colour, consolidate them onto a single named material.
{{< /prompt >}}

### Find the duplicates

{{< prompt >}}
Find any objects that are exact duplicates (same geometry, same
position). Move all duplicates to a layer called `Duplicates` so I
can review and delete them.
{{< /prompt >}}

## Teaching &amp; learning

### Walk me through a workflow

{{< prompt >}}
I want to model a turbine blade. Walk me through the steps in Rhino
&mdash; what curves to draw first, how to sweep them, and what to
watch out for. Don't make geometry yet; just explain.
{{< /prompt >}}

### Explain what just happened

{{< prompt >}}
I just ran a boolean and got an unexpected result. Look at what's in
the document and tell me what probably went wrong.
{{< /prompt >}}

---

Got a good prompt? [Send us a PR](https://github.com/mcneel/RhinoMCP/edit/main/docs/content/docs/recipes.md)
and we'll add it here.
