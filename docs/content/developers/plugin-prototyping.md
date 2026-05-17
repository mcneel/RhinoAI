---
title: Plugin prototyping
linkTitle: Plugin prototyping
weight: 4
---

If you've ever wanted to try a plugin idea but couldn't be bothered to
scaffold the project, write the manifest, wire up a command, build,
load, debug, repeat &mdash; this is what Rhino MCP plus an AI assistant
removes. Describe what you want the plugin to do, and the assistant can
scaffold it, build it, load it into a running Rhino, exercise its
commands, and iterate on what it sees.

This is closer to "rapid prototyping a plugin idea" than "ship a
production plugin." The output is a starting point you can keep
developing &mdash; or throw away once you've decided whether the idea
was worth pursuing.

## What you need

- **Claude Code** with the [Rhino MCP plugin](../docs/cc-plugin)
  installed.
- **Rhino** open with Rhino MCP running, on the Rhino version you want
  to target.
- An empty folder where the assistant can scaffold the plugin, with
  Claude Code started there.
- The Rhino plugin templates installed (`dotnet new install
  Rhino.Templates`), or willingness to let the assistant install
  them.

## A prompt to start with

{{< prompt >}}
Scaffold a Rhino 8 plugin in this folder called `EdgeAudit`. It
should add one command, `EdgeAuditReport`, that scans the active doc
for Breps with naked edges, prints a per-object count to the command
line, and selects the worst offender. Use the standard plugin
template, target `net7.0`, and once it builds load it into Rhino and
run the command on whatever's open so I can see it work.
{{< /prompt >}}

Adjust scope to taste &mdash; one command, three commands, a Grasshopper
component, a display conduit. The pattern is the same.

## What you should see

The assistant runs `dotnet new rhinoplugin` (or equivalent), edits the
generated command file to match your spec, builds the `.rhp`, loads it
into the running Rhino via `run_command` (`_PluginManager` /
`_-LoadPlugIn`), and then invokes the new command. It reads the command
line output back through the MCP, so it can tell whether the command
actually did what you asked &mdash; not just whether it compiled.

If the first build fails or the command misbehaves, expect an iterate
loop: edit, build, reload, re-run, observe. The whole point of doing
this through Rhino MCP is that the assistant closes that loop itself.

## What to review

Even for a throwaway prototype, glance at:

- **The plugin GUID and manifest.** If you might keep the plugin, make
  sure the GUID is the assistant's, not a placeholder from the
  template, and that the plugin name / version are what you want.
- **Where it loads from.** The assistant will usually load the built
  `.rhp` from `bin/Debug/...`. That's fine for prototyping; for
  anything you want to keep, decide whether to ship via Yak or copy to
  the plugins folder.
- **API choices.** Brand-new RhinoCommon code from an assistant is
  often *almost right* &mdash; the right namespace, a plausible method
  name, slightly wrong arguments. Read the diff with the
  [RhinoCommon docs](https://developer.rhino3d.com/api/rhinocommon/)
  open in the other window.
- **Cleanup.** Prototyping leaves loaded plugins in your Rhino session.
  If you abandon the experiment, unload it before forgetting.

## When the assistant gets stuck

- **Template missing.** If `dotnet new rhinoplugin` isn't available,
  the assistant should install `Rhino.Templates` rather than
  hand-rolling a `.csproj` &mdash; the templates set up a lot of
  boring things correctly.
- **Plugin won't load.** Usually a target-framework mismatch (plugin
  built for `net8.0`, Rhino running `net7.0`) or a missing reference.
  Ask the assistant to print the exact load error from Rhino's command
  line before guessing.
- **"Works on the next reload."** Rhino caches loaded plugin
  assemblies; for tight iteration loops, use the plugin developer's
  reload flow (or just unload, rebuild, reload). The assistant should
  do this for you; if it skips the reload step, builds will land but
  behaviour won't change.

## Related

- [Upgrade a plugin to .NET Core](../upgrade-to-netcore) &mdash; once
  the prototype proves the idea, you'll want it on a modern target.
- [Upgrade to a newer Rhino](../upgrade-rhino-version) &mdash; same
  idea, different axis.
