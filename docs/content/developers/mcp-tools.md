---
title: MCP Tool Reference
weight: 2
---

A high-level catalogue of every tool the plugin exposes. The router surfaces these to your AI agent automatically.

## Document

| Tool | What it does |
| --- | --- |
| `open_doc` | Open a `.3dm` file in a new or existing instance. |
| `close_doc` | Close the active document. |
| `save_doc` | Save the active document, optionally to a new path. |

## Inspection

| Tool | What it does |
| --- | --- |
| `list_objects` | List objects in the document, optionally filtered by layer or type. |
| `get_selection` | Return the currently selected objects. |
| `set_selection` | Replace the current selection. |
| `get_commands` | List Rhino commands available to scripting. |
| `get_viewport_image` | Capture the active viewport as a PNG &mdash; the agent's eyes. |

## Camera & view

| Tool | What it does |
| --- | --- |
| `set_camera` | Position the camera (eye, target, up). |
| `zoom_to_layer` | Frame all geometry on a given layer. |
| `zoom_to_object` | Frame a specific object by ID. |

## Layers & materials

| Tool | What it does |
| --- | --- |
| `set_layer_material` | Assign a material to a layer. |

## Scripting & execution

| Tool | What it does |
| --- | --- |
| `run_command` | Execute a Rhino command line as if typed. |
| `run_python` | Run a Python snippet inside Rhino. |
| `run_csharp` | Run a C# snippet inside Rhino. |
| `probe_intersection` | Test ray/curve/surface intersections without modifying the document. |

> **Script doc handle:** in `run_python` and `run_csharp` snippets, use the injected `__rhino_doc__` variable rather than `sc.doc` or `rs.*`, which may write to the wrong slot's document.

## Grasshopper 1 (Rhino 8)

| Tool | What it does |
| --- | --- |
| `gh1_search_components` | Find components by name or nickname. |
| `gh1_describe_component` | Get inputs/outputs/parameters of a component. |
| `gh1_place_component` | Place a component on the canvas. |
| `gh1_place_slider` | Place a number slider. |
| `gh1_connect` | Wire one output to one input. |
| `gh1_connect_many` | Wire many connections in a single call. |
| `gh1_get_canvas_graph` | Read the current canvas as a graph. |
| `gh1_apply_graph` | Replace the canvas with a graph definition. |
| `gh1_clear_canvas` | Wipe the canvas. |
| `gh1_solve` | Force a solver run and return results. |

## Grasshopper 2 (Rhino 9 WIP)

GH2 mirrors the GH1 tool surface (`gh2_*`) plus:

| Tool | What it does |
| --- | --- |
| `gh2_start` | Boot the GH2 environment if it isn't already running. |

See [rhino/plugin/Tools/](https://github.com/mcneel/RhinoMCP/tree/main/rhino/plugin/Tools) for the source of each tool.
