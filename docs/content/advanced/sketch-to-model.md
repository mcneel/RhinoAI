---
title: Sketch to model
linkTitle: Sketch to model
weight: 4
---

If you've got a hand sketch &mdash; a napkin drawing, a whiteboard
photo, a marked-up plan &mdash; you can drop the image into the chat
and ask the assistant to propose a parametric build. It reads the
sketch, suggests an interpretation, and constructs a first-pass model
or Grasshopper definition you can iterate on.

This is a conversation, not a one-shot. The first model will be wrong
in interesting ways; the value is in how quickly you can correct
it.

## What you need

- An MCP-connected assistant that can accept images
  ([Claude Code](../docs/cc-plugin), Claude Desktop, or similar).
- **Rhino** running with Rhino MCP, ideally on Rhino 9 if you want the
  result as GH2.
- A reasonably legible image of the sketch. Phone photo is fine;
  shadows and skew are tolerable, illegible handwriting is not.

## A prompt to start with

{{< prompt >}}
Here's a sketch of a shelving unit I want to build. Read the
proportions and structure off the drawing, tell me what you think the
key parameters are, and propose a parametric model. Don't build
anything yet &mdash; show me your interpretation first so I can
correct it.
{{< /prompt >}}

That second sentence matters. Without it the assistant will dive
straight to geometry and you'll spend longer correcting the result than
you would have spent on a slower start.

## What you should see

1. **An interpretation.** The assistant lists what it thinks the sketch
   shows: overall dimensions, the parts, which proportions are
   load-bearing, which annotations are dimensions vs. labels.
2. **A parameter proposal.** A short list of sliders it thinks you'll
   want.
3. **Once you confirm:** a first build, either as Rhino geometry or a
   GH2 graph, depending on what you asked for.
4. **A comparison.** Often a screenshot via `get_viewport_image` framed
   to roughly match the sketch's angle, so you can eyeball whether the
   massing matches.

## What to review

- **Dimensions.** Sketches rarely have every measurement. The assistant
  will infer the missing ones from proportion, which is almost always
  *almost right*. Spot-check by asking for a few overall dims and
  comparing to what you meant.
- **Topology vs. style.** A sketch of "a chair" might be interpreted as
  *that specific chair* or as *a chair-shaped recipe*. Be explicit
  about which one you want.
- **Hidden assumptions.** Material thicknesses, joinery, tolerances,
  fixings &mdash; none of these are in the sketch. The assistant will
  invent reasonable defaults; check them before you cut anything.

## When the assistant gets stuck

- **Illegible sketch.** If the response is vague ("a shelf-like object
  with some compartments"), the image isn't carrying enough
  information. Annotate the sketch with dimensions and labels, or
  describe the bits the drawing doesn't show.
- **Wrong axis.** Plan vs. elevation vs. perspective confusion is
  common. State it: "this is the front elevation."
- **Too literal.** If the model copies sketch artefacts (a wobbly line
  becomes a wobbly edge), tell the assistant to treat the sketch as
  intent, not as a digitised input.

## Related

- [Make this parametric](../make-this-parametric) &mdash; once the
  first-pass model exists, this workflow takes it the rest of the
  way.
