---
title: Headless render farm
linkTitle: Headless render farm
weight: 2
---

If you have a model you need to capture from many angles &mdash; a study
model with twenty options, a presentation board, a turntable, a daylight
sweep &mdash; the MCP can spin up headless Rhino documents in parallel,
point cameras at them, dump viewport images, and tear them down again
without you ever clicking "New Document".

This is what the **slot** tools are for. Each slot is its own Rhino
document; the assistant can open, drive, and close them programmatically
while your main session keeps its window focused on whatever you were
actually doing.

## What you need

- An MCP-connected assistant ([Claude Code](../docs/cc-plugin),
  Claude Desktop, or similar).
- **Rhino** running with Rhino MCP.
- A `.3dm` (or several) you want to capture, or a recipe the assistant
  can build from scratch.
- A folder you're happy for the assistant to write images into.

## A prompt to start with

{{< prompt >}}
Open `studies/option-*.3dm`, each in its own slot. For every file,
set the camera to an isometric view framed on layer `Massing`, render
the active viewport at 1920&times;1080, save it as
`renders/<filename>.png`, and close the slot. Don't touch my current
document.
{{< /prompt >}}

Variations worth trying:

- **Turntable** &mdash; "spin the camera around the model in 15&deg;
  steps, save each frame numbered."
- **Sun study** &mdash; "step the sun from 08:00 to 18:00 in 30-minute
  intervals, capture from the same camera each time."
- **Option matrix** &mdash; "for every combination of these three
  GH sliders, solve, capture, label the filename with the slider
  values."

## What you should see

The assistant calls `spawn_slot` for each file or variation, `open_doc`
into that slot, `set_camera` (or `run_command` for named views),
`get_viewport_image` to capture, then `close_slot` when it's done. Your
main Rhino window stays on whatever you had open &mdash; the slot
documents are headless siblings, not takeovers.

If it's working from a Grasshopper definition, expect to see `g2_*`
solve calls between the camera move and the capture so the geometry
updates before the shutter clicks.

## What to review

- **Camera framing.** Headless slots don't have a viewport you can
  eyeball, so the assistant is guessing at zoom and target. Ask it to
  do one capture first and show you the image before it loops over
  fifty.
- **Render settings.** `get_viewport_image` captures the display mode,
  not a Rendered/Raytraced pass by default. If you need ray-traced
  output, say so explicitly and check the first frame matches what you
  expected.
- **Output paths.** Make the assistant echo the full output folder
  before it starts &mdash; cheaper than discovering forty PNGs landed in
  the wrong place.

## When the assistant gets stuck

Common failure modes:

- **Slot leak.** If a run errors halfway through, slots may stay open.
  Ask for a `list_slots` and have it close anything stray.
- **ActiveDoc confusion.** Remind it that each slot has its own document
  handle; `sc.doc` inside a `run_python` call may not be the slot you
  think. The
  [doc-handle note](../docs/recipes) covers this.
- **Memory pressure.** Spinning up many slots at once can chew RAM.
  Cap the parallelism &mdash; "do at most four at a time" &mdash; rather
  than letting it fan out.
