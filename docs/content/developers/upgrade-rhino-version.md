---
title: Upgrade to a newer Rhino
linkTitle: Upgrade Rhino version
weight: 2
---

Moving a plugin from Rhino 7 → 8, or 8 → 9, is mostly a long tail of small
API touch-ups spread across a lot of files. That's exactly the kind of work
to hand to Claude Code with Rhino MCP &mdash; this page is about how to do
that, not about the changes themselves.

## What you need

- **Claude Code** with the [Rhino MCP plugin](../docs/cc-plugin) installed.
- **The target Rhino version** open with Rhino MCP running. The assistant
  needs to be talking to the Rhino you're porting *to*, so it can load and
  test the built plugin there.
- Your plugin's source checked out locally, with Claude Code started in
  that repo.

## The loop

With Rhino MCP loaded, the assistant can:

- Bump the RhinoCommon (and Grasshopper) NuGet versions and rebuild.
- For each compile or runtime error, look up the current API and propose a
  fix.
- Install the freshly built `.rhp` into the target Rhino, run the plugin's
  commands, and read what happened &mdash; so a fix that compiles but
  breaks at runtime gets caught in the same pass.
- If your plugin ships Grasshopper components, place them on the canvas
  with the GH1 or GH2 tools and verify they still solve.

## A prompt to start with

{{< prompt >}}
This repo is a Rhino 7 plugin. I want it working on Rhino 8. Bump the
RhinoCommon and Grasshopper package references, fix any build errors,
then install it into the Rhino I have open and run each of its commands
and Grasshopper components to confirm they still behave the same. Show
me the diff before each file change.
{{< /prompt >}}

For Rhino 8 → 9, the same prompt works &mdash; just swap the target version.

If your plugin ships Grasshopper components and you're targeting Rhino 9,
you'll probably also want GH2 equivalents &mdash; see
[Upgrade GH1 to GH2](upgrade-gh1-to-gh2) for that workflow.

## What to review

- **Package version bumps.** Confirm the RhinoCommon / Grasshopper /
  Grasshopper2 versions the assistant picked match what you actually want
  to target &mdash; not just the latest pre-release it could find.
- **API substitutions.** Same caveat as for the .NET Core upgrade: a
  same-signature replacement isn't always a same-behaviour replacement.
- **Removed features.** If an API was removed rather than renamed, the
  assistant may stub the call out to make the build pass. Search the diff
  for `TODO`, `NotImplementedException`, and commented-out blocks before
  you merge.

## When the assistant gets stuck

If it can't find a replacement API or starts guessing, point it at the
[Rhino developer docs](https://developer.rhino3d.com/) or a specific
RhinoCommon class, and ask it to retry just the failing file. The MCP
keeps it honest about what actually runs &mdash; your job is to keep it
honest about what it's allowed to invent.
