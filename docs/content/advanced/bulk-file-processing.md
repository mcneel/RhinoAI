---
title: Bulk file processing
linkTitle: Bulk file processing
weight: 5
---

You've got a folder of `.3dm` files and a job to do on each of them
&mdash; purge unused layers, re-export every file as STEP, replace a
block definition across the set, fix unit settings, strip annotations,
add a title block, re-save under a new naming scheme. The hand version
is a click-fest. The traditional scripted version is a fragile pile of
`RunScript` hacks that breaks the moment one file has a dialog you
didn't expect.

The MCP gives you a cleaner path: each file opens in its own **slot**,
the assistant drives it through whatever sequence you describe, and the
slot closes when it's done. Your main Rhino window keeps whatever you
were actually working on.

## What you need

- An MCP-connected assistant ([Claude Code](../docs/cc-plugin),
  Claude Desktop, or similar).
- **Rhino** running with Rhino MCP.
- A folder of `.3dm` files you want to process, and write-access to
  wherever the output should land.
- A clear description of the per-file operation. "Make it tidy" is not
  enough; "purge unused block definitions, set tolerance to 0.001, save
  as `<name>-clean.3dm`" is.

## A prompt to start with

{{< prompt >}}
For every `.3dm` in `incoming/`, open it in a slot, purge unused
layers and block definitions, set the absolute tolerance to 0.001,
export the contents of layer `Deliverable` as a STEP file into
`out/<name>.step`, then close the slot. Don't touch my current
document. Do it four files at a time and stop on the first error so
I can look at it.
{{< /prompt >}}

Variations worth trying:

- **Format conversion** &mdash; "re-save every file as Rhino 7 format
  into `legacy/`, preserving the folder structure."
- **Mass edit** &mdash; "in every file, find the block named `TitleBlock`
  and replace its definition with the one from `template.3dm`."
- **Audit and report** &mdash; "open each file, collect layer counts,
  object counts, units, and tolerance, write the results to
  `audit.csv`. Don't modify anything."
- **Rename and reorganise** &mdash; "for each file, read the project code
  from layer `Meta`, and re-save it as `<code>/<original-name>.3dm`."

## What you should see

The assistant calls `spawn_slot` per file (or per worker, if it's
batching), `open_doc` into that slot, then a sequence of `run_command`
/ `run_python` / `run_csharp` calls to do the actual work, then
`save_doc` (or an export command) and `close_slot`. Your main Rhino
window doesn't flicker; the slot documents are headless siblings.

For anything non-trivial, expect the assistant to run the first file
end-to-end and show you the result before fanning out. If it doesn't
offer, ask for it &mdash; verifying one good output is much cheaper
than auditing fifty bad ones.

## What to review

- **The first output.** Before the batch runs, demand one completed
  file you can open and inspect. Layer names, unit settings, and
  tolerances are easy to get subtly wrong in a way that only shows up
  downstream.
- **Skipped files.** If a file errors, did the assistant skip it and
  carry on, or stop? Be explicit about which behaviour you want. For
  destructive batches, "stop on first error" is usually safer.
- **The save path.** Make the assistant echo the full output folder
  and a couple of example output filenames before it starts. Files
  landing in the wrong place is the most common way a batch wastes an
  afternoon.
- **Originals.** If the operation overwrites in place, confirm there's
  a backup. The assistant won't make one unless you ask.

## When the assistant gets stuck

Common failure modes:

- **`ActiveDoc` confusion.** Scripts that use `sc.doc` or `Rhino.RhinoDoc.ActiveDoc`
  inside a slot may not be talking to the slot you think. Tell the
  assistant to use the injected `__rhino_doc__` handle inside
  `run_python` / `run_csharp` calls. See the
  [doc-handle note](../docs/recipes).
- **Modal dialogs.** Some commands pop UI on certain files (missing
  fonts, missing linked blocks, unit mismatch on import). In a slot,
  these can hang the run. If it's stalling on one file, ask for the
  command to be replaced with a script-call equivalent that doesn't
  prompt, or for the file to be skipped with a logged reason.
- **Slot leak.** If the batch errors halfway through, slots can stay
  open. Ask for a `list_slots` and have the assistant close anything
  stray before you start the next run.
- **Memory pressure.** Big files plus many parallel slots will exhaust
  RAM faster than you expect. Cap the parallelism explicitly &mdash;
  "at most four slots at a time" &mdash; rather than letting it fan
  out across the whole folder.
- **`RunScript` reflex.** If the assistant reaches for `RunScript`
  with a long dash-prefixed command string, push back: it's the
  fragile path. The Rhino API has direct equivalents for most things
  &mdash; ask for those instead. On Mac in particular, `_-Close` and
  `_-New` have quirks worth avoiding.

## Related

- [Headless render farm](../headless-render-farm) &mdash; same
  slot-per-file shape, but the per-file job is "render", not "modify
  and save".
