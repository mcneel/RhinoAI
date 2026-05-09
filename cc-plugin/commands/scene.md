---
description: Summarize the contents of the current Rhino document.
---

Inspect the active Rhino document and give the user a concise summary:

1. Call `mcp__rhino__list_objects` to see what's in the document.
2. Optionally call `mcp__rhino__get_selection` if the user asks about the current selection.

Report back with: object counts by type, layers in use, and anything notable (empty doc, very large object count, mixed units, etc.). Keep it short — bullets are fine.
