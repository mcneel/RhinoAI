---
title: Upgrade a plugin to .NET Core
linkTitle: Upgrade to .NET Core
weight: 1
---

If you have a Rhino plugin still targeting `net48`, you'll want to move it
to `net7.0` (Rhino 8) or `net8.0` (Rhino 8 recent / Rhino 9). This page
showcases how you can use the RhinoMCP to facilitate this upgrade.

## What you need

- **Claude Code** with the [Rhino MCP plugin](../docs/cc-plugin) installed.
- **Rhino** open with Rhino MCP running, on the version you're targeting.
- Your plugin's source checked out locally, with Claude Code started in
  that repo.

## The loop

With Rhino MCP loaded, the assistant can:

- Read your `.csproj` and source files.
- Edit project and code files and re-build.
- Load the freshly built `.rhp` into Rhino, run its commands, and read back
  what happened &mdash; so it can tell whether a change actually worked, not
  just whether it compiled.

That last point is what makes this worth doing through Rhino MCP rather than
a plain LLM session: the assistant closes its own feedback loop instead of
asking you to copy errors back and forth.

## A prompt to start with

{{< prompt >}}
This repo is a Rhino plugin targeting `net48`. Convert it to multi-target
both `net48` and `net8.0` so it builds for Rhino 7 and Rhino 8. Work one
error at a time, build after each change, and once it builds cleanly load
it into Rhino and run each of its commands to confirm nothing regressed.
Show me the diff before each file change.
{{< /prompt >}}

Adjust the target framework and "show me the diff" cadence to taste.

## What to review

Even with the assistant driving, you're still the one merging the result.
Things worth looking at before you accept the work:

- **The `.csproj` diff.** Multi-targeting introduces conditional package
  references and conditional `Compile` items; make sure the conditions
  match how you want the two TFMs to differ.
- **Any swapped APIs.** When a `net48`-era RhinoCommon call doesn't exist
  on the newer target, the assistant will pick a replacement. Spot-check
  the substitutions &mdash; same behaviour, not just same signature.
- **Plugin manifest / yak files.** If you ship through yak, the manifest
  and target folders may need updating too.

## When the assistant gets stuck

If it loops on the same error or starts inventing APIs, stop it and either
narrow the scope (one file, one error) or paste the actual RhinoCommon
docs / NuGet version it should be working against. The MCP gives it eyes
inside Rhino, but it can't read your minds about which target framework
version you actually want.
