---
description: Gotchas for `run_python` and `run_csharp` — the injected doc handle, output capture, headless docs, and Rhino-API traps that bite when scripting through MCP. Read before writing any non-trivial script.
---

# Scripting through MCP — primer

`run_python` and `run_csharp` execute scripts inside the running Rhino, routed
through Rhino's `_ScriptEditor _Run` command. They aren't a REPL — each call
is a fresh script run, written to a temp file, executed, and the captured
command-window output returned as JSON.

## The document handle

The script editor injects a variable called `__rhino_doc__` for you.

- **Python:** `__rhino_doc__` is the active `RhinoDoc` for this slot.
- **C#:** `__rhino_doc__` is typed as `RhinoDoc`.

Use it directly. Do **not** use:

- `scriptcontext.doc` / `sc.doc` (Python) — points at the script editor's
  internal doc, not this slot's doc.
- `rhinoscriptsyntax.*` (Python) — most calls reach for `sc.doc` internally
  and write to the wrong document.
- `RhinoDoc.ActiveDoc` (C#) — not trustworthy after creating/opening a new
  doc; can lag behind reality.

If you must use `rhinoscriptsyntax`, point it at the right doc explicitly,
e.g. `rs.AddPoint([0,0,0])` is fine only because the underlying call doesn't
target the doc; anything that mutates geometry should use the `Rhino.*` API
against `__rhino_doc__`.

## Return shape

Every call returns JSON:

```json
{ "stdout": "...captured output...", "error": null }
```

On failure, `error` is non-null and contains the traceback (Python) or
compile / exception text (C#). The split is heuristic — output is partitioned
at the first line matching `Traceback`, `Compile Error`, `error CS…`,
`Exception:`, or `Unhandled exception`. Anything before that goes in
`stdout`; anything after, `error`.

`print(...)` (Python) and `Console.WriteLine(...)` / `RhinoApp.WriteLine(...)`
(C#) all land in `stdout`.

## Creating new documents

When you need a fresh empty doc:

- Use `_New` (not `_-New`). The dash form opens a *headless* doc that the
  user never sees and that won't render in viewports.
- Don't rely on `RhinoDoc.ActiveDoc` immediately after — it can lag. To find
  the new doc, diff `RhinoDoc.OpenDocuments()` against the before-set, or
  take the one with the highest `RuntimeSerialNumber`.

## Closing documents (Mac)

`_-Close` on Mac only works on docs that have a saved path. For unsaved
docs, save to a temp file first, then close. `doc.Dispose()` is a no-op on
non-headless docs — it doesn't unload the doc from the running Rhino.

## What the slot sees

A script runs against this slot's Rhino. If you have multiple slots open,
each `run_python` / `run_csharp` call targets one — pass `slot=...` to pick.
The injected `__rhino_doc__` is whichever doc Rhino considers active in that
slot at the moment the script runs.

## Timing

Scripts run synchronously to the MCP call. There's no streaming — `stdout`
arrives in one chunk when the script finishes. For long-running work,
prefer breaking the job into smaller scripts and returning intermediate
state, rather than running a 30-second monolith and hoping nothing times
out.
