using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RhMcp;

// Discovery result for one definition; kept separate so AgentDefinition stays free of
// computed/probed state.
internal sealed record ResolvedAgent(AgentDefinition Definition, bool Available);

// Seeds built-in agent definitions, overlays custom entries from settings, probes each
// definition's search paths for availability, and resolves the active agent (first
// Enabled && Available, Claude-default-first). Re-runnable on load and on settings change.
internal static class AgentRegistry
{
    private static IReadOnlyList<ResolvedAgent> ChainBacking { get; set; } = [];

    public static IReadOnlyList<ResolvedAgent> Chain => ChainBacking;

    public static void Refresh() =>
        ChainBacking = AISettings.GetAgents()
            .Select(static def => new ResolvedAgent(def, ProbeAvailable(def)))
            .ToArray();

    // The built-in definitions in chain order (Claude first, Codex second), reproducing
    // today's hardcoded install-dir candidates and empty model/args so out-of-the-box launch
    // behavior is unchanged. AISettings.GetAgents re-seeds from here on every read.
    public static IReadOnlyList<AgentDefinition> Builtins() =>
    [
        Builtin("claude", AgentAdapter.Claude, "claude"),
        Builtin("codex", AgentAdapter.Codex, "codex"),
        Builtin("gemini", AgentAdapter.Gemini, "gemini"),
    ];

    private static AgentDefinition Builtin(string name, AgentAdapter adapter, string command) =>
        new(name, adapter, command, DefaultSearchPaths(command), string.Empty, [], string.Empty, true, true);

    // The full chain: built-ins (always present, in their seed order) overlaid with custom entries.
    // A custom entry that aliases a built-in name overrides it in place (keeping the built-in's
    // IsBuiltin), never duplicated; a custom entry with a new name is appended after the built-ins.
    // Pure so AISettings.GetAgents and the headless test shim share one source of truth for the
    // invariant rather than each reimplementing it.
    public static IReadOnlyList<AgentDefinition> Overlay(IReadOnlyList<AgentDefinition> builtins, IReadOnlyList<AgentDefinition> custom)
    {
        List<AgentDefinition> chain = builtins.ToList();
        foreach (AgentDefinition entry in custom)
        {
            int existing = chain.FindIndex(a => a.Name == entry.Name);
            if (existing >= 0)
                chain[existing] = entry with { IsBuiltin = chain[existing].IsBuiltin };
            else
                chain.Add(entry);
        }
        return chain;
    }

    // Where a normally-installed CLI lands so it is found with zero config: every entry on PATH,
    // then the standard per-user/system install dirs, the npm global bin, and Claude Code's own
    // local dir. On Windows the CLIs install as claude.cmd / claude.exe / gemini.cmd, so each
    // dir+command is expanded by the executable extensions (the bare name never exists). De-duped,
    // first-found-anywhere; probing and TryResolveCommand both just File.Exists each candidate, so
    // adding to this list is the only seam needed to widen detection.
    public static IReadOnlyList<string> DefaultSearchPaths(string command)
    {
        List<string> candidates = new();
        foreach (string dir in CandidateDirs())
            foreach (string ext in ExecutableExtensions())
                candidates.Add(Path.Combine(dir, command + ext));
        return candidates.Distinct().ToArray();
    }

    // On non-Windows a CLI is the bare name (one variant, no extension). On Windows the launcher is
    // a PATHEXT-resolved wrapper, so we probe each known extension; "" is included so a fully
    // qualified command already carrying its extension still resolves.
    private static IEnumerable<string> ExecutableExtensions()
    {
        if (!OperatingSystem.IsWindows())
            return [string.Empty];

        string pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD";
        string[] fromEnv = pathext.Split(';', StringSplitOptions.RemoveEmptyEntries);
        return new[] { string.Empty }.Concat(fromEnv).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> CandidateDirs()
    {
        foreach (string dir in PathDirs())
            yield return dir;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            yield return Path.Combine(appData, "npm");
            yield return Path.Combine(localAppData, "Microsoft", "WindowsApps");
        }
        else
        {
            yield return Path.Combine(home, ".local", "bin");
            yield return "/usr/local/bin";
            yield return "/opt/homebrew/bin";
        }
        yield return Path.Combine(home, ".claude", "local");

        if (NpmGlobalBin(home) is { } npmBin)
            yield return npmBin;
    }

    private static IEnumerable<string> PathDirs()
    {
        string path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (string dir in path.Split(Path.PathSeparator))
            if (dir.Length > 0)
                yield return dir;
    }

    // npm's global prefix: the binaries live directly under the prefix on Windows, under
    // <prefix>/bin elsewhere. NPM_CONFIG_PREFIX wins when set; else the conventional ~/.npm-global.
    // Both branches return only an existing dir (string? is genuine absence, not a candidate). We
    // don't shell out to `npm prefix -g`: probing is on a UI-thread load path and must stay
    // non-blocking.
    private static string? NpmGlobalBin(string home)
    {
        string prefix = Environment.GetEnvironmentVariable("NPM_CONFIG_PREFIX") ?? string.Empty;
        string candidate = prefix.Length > 0
            ? (OperatingSystem.IsWindows() ? prefix : Path.Combine(prefix, "bin"))
            : Path.Combine(home, ".npm-global", "bin");
        return Directory.Exists(candidate) ? candidate : null;
    }

    private static bool ProbeAvailable(AgentDefinition def) => def.SearchPaths.Any(File.Exists);

    // First Enabled && Available in chain order, then the configured default, then the
    // first enabled+available built-in. Falls back to nothing only when discovery is empty.
    public static bool TryResolveActive(out AgentDefinition def)
    {
        string preferred = AISettings.DefaultAgentName;
        ResolvedAgent[] usable = ChainBacking.Where(static r => r.Definition.Enabled && r.Available).ToArray();

        ResolvedAgent? match = usable.FirstOrDefault(r => r.Definition.Name == preferred)
            ?? usable.FirstOrDefault();
        if (match is not null)
        {
            def = match.Definition;
            return true;
        }

        def = default!;
        return false;
    }

    public static bool TryGet(string name, out AgentDefinition def)
    {
        ResolvedAgent? match = ChainBacking.FirstOrDefault(r => r.Definition.Name == name);
        if (match is not null)
        {
            def = match.Definition;
            return true;
        }

        def = default!;
        return false;
    }
}
