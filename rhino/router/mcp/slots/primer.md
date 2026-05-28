---
description: How slots work — one slot is one running Rhino. Covers spawn_slot, auto-spawn, adopted slots, version pinning (Rhino 8 / 9 / WIP), close_slot error codes, and how list_slots prunes crashed Rhinos. Read before juggling multiple Rhinos.
---

# Slots — primer

A **slot** is one running Rhino instance managed by the router. Each has a
short animal-name ID (`octopus`, `badger`, …). Almost every MCP tool takes
an optional `slot=...` argument that picks which Rhino it runs against.

## Where slots come from

- **`spawn_slot`** — the router launches a fresh Rhino, returns its slot ID.
  Pass `version="8"`, `"9"`, or `"WIP"` to pin a specific Rhino;
  omit to use the router's configured default.
- **Auto-spawn** — if you call a tool without a `slot` arg and no slot is
  running (or none matches the tool's version pin), the router spawns one
  for you. The response includes `auto_spawned_slot` so you know which one
  was created.
- **Adopted slots** — a Rhino the user started themselves can advertise
  itself to the router and get adopted into the slot list. You can drive
  it like any spawned slot.

## Version pinning

Tools that only work on a specific Rhino pin themselves to a version. The
big one today: `g2_*` (Grasshopper 2) tools pin to Rhino WIP. Calling a
`g2_*` tool against an explicit Rhino 8 slot returns `wrong_rhino_version`;
calling it without a slot auto-spawns a WIP rather than reusing your Rhino
8 slot.

If you want both versions running, `spawn_slot` each explicitly and pass
the right `slot` on every call.

## Closing slots

`close_slot` kills the Rhino — nothing is saved. Two error codes are worth
recognising:

- `slot_not_found` — no slot with that ID is running. Call `list_slots` to
  see what's actually alive.
- `cannot_close_adopted` — the slot is a user-started Rhino. The router
  won't kill a Rhino it didn't launch. Ask the user to close the window
  themselves.

## list_slots

Two side effects worth knowing about:

1. It first scans for any new user-started Rhinos that have advertised
   themselves and adopts them.
2. It probes every known slot and prunes any whose Rhino has crashed.

So `list_slots` is the way to recover from "I had a slot but the Rhino
crashed" — it'll drop the dead entry, and you can re-`spawn_slot`.

## Spawn errors

`spawn_slot` can fail with:

- `rhino_not_installed` — the requested version isn't installed. Pass a
  different `version`, or install the missing Rhino.
- `startup_timeout` — Rhino launched but didn't finish booting in time.
  Usually a license / EULA / update dialog is blocking — check the
  window. Or the rh-mcp plugin isn't installed in that Rhino.
- `existing_rhino_unreachable` — we tried to add a listener to a previously
  known Rhino and its control endpoint stopped answering. The slot has
  already been pruned; just call `spawn_slot` again.
- `unsupported_platform` — the OS can't run the requested Rhino at all.
- `cancelled` — the spawn was aborted before Rhino finished starting.

## Default behaviour

If you don't care about versions and just want "a Rhino", omit `slot`
entirely on every tool call. The router will reuse the most-recent
compatible slot, or auto-spawn one. Most workflows never need to call
`spawn_slot` directly.
