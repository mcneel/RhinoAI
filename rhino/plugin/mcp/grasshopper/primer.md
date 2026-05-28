---
description: Headstart reference for working with Grasshopper through this MCP server — which Rhino-side dash command opens each version, file extensions, MCP tool naming, and the GH1↔GH2 API differences that trip up agents. Read before driving GH1 or GH2 if you haven't recently.
---

# Grasshopper primer — GH1 vs GH2

GH1 ships with Rhino 8+. GH2 is the next-gen rewrite and only ships with
Rhino 9 / WIP. Both can be open in the same Rhino at the same time. They are
separate plug-ins with separate canvases, separate component libraries, and
separate MCP tools.

## Dash commands (for `run_python` / `run_csharp` / `run_command`)

```
GH1:   _Grasshopper     (toggle window)
GH2:   _G2              (toggle window) — NOT `_GH2`.
```

Both have dash-prefixed scripted forms (`_-Grasshopper`, `_-G2`) but their
sub-options differ and aren't fully symmetric — prefer the MCP tools
(`g1_start`, `g2_start`) over driving the commands directly.

## File extensions

```
GH1:   .gh, .ghx
GH2:   .ghz
```

## MCP tool naming

All Grasshopper MCP tools are prefixed:

```
g1_*   → operates on the GH1 canvas
g2_*   → operates on the GH2 canvas
```

Most tool pairs are symmetric (`g1_place_component` ↔ `g2_place_component`,
`g1_connect` ↔ `g2_connect`, etc.) but a few differ — see "Asymmetries"
below.

## Pinning behaviour

Tools prefixed `g2_*` pin to Rhino WIP. If you call one without a `slot`
argument, the router will auto-spawn Rhino WIP rather than Rhino 8. Calling
a `g2_*` tool against an explicit Rhino 8 slot returns `wrong_rhino_version`.

## Asymmetries to remember

Solve tool names differ:

```
GH1:   g1_solve_graph     (also has a zoom_views option)
GH2:   g2_solve_canvas
```

Slider parameters differ:

```
GH1:   { Min, Value, Max, Type ∈ "float"|"int"|"even"|"odd" }
GH2:   { Min, Value, Max, Decimals ∈ 0..12 }
```

Component search match fields differ:

```
GH1:   Name + NickName + Description
GH2:   Name + Info
```

File-open path: there is no MCP tool to load a `.gh` / `.ghz` directly. For
GH1, `_-Grasshopper _Document _Open "<path>"` works from `run_python`. The
GH2 equivalent has caused instability in testing — prefer building the graph
with `g2_apply_graph` rather than opening a `.ghz`, until a tool ships for
it.

## Recommended startup sequence

1. (Optional) `spawn_slot` if you want to pick the Rhino version explicitly.
   Otherwise omit `slot` and let the router auto-spawn.
2. `g1_start` or `g2_start` to open the canvas.
3. `g1_get_canvas_graph` / `g2_get_canvas_graph` before editing, so you
   don't duplicate what's already there.
4. `g1_search_components` / `g2_search_components` +
   `g1_describe_component` / `g2_describe_component` to confirm inputs /
   outputs before wiring.
5. Build with `g{1,2}_apply_graph` (atomic, one round-trip) for larger
   definitions; use the individual place/connect tools for incremental
   edits.
6. Finish with the appropriate solve tool so the user sees output.
