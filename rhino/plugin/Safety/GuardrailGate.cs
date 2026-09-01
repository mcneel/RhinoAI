using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using RhMcp.Server;

namespace RhMcp;

// Applies the Safety-tab guardrails to calls on the filtered (in-panel) endpoint. Returns null to
// allow, or a block reason the dispatcher hands back to the agent as a tool error so it can explain
// itself instead of silently failing.
internal static class GuardrailGate
{
    private const string FileFenceLabel = "Block file access outside the document's folder";

    public static Task<string?> CheckAsync(
        ToolHandler tool, IDictionary<string, JsonElement>? args, IServiceProvider services, CancellationToken ct)
    {
        if (AISettings.BlockDestructiveTools && tool.Destructive)
            return Task.FromResult<string?>(Blocked(
                "Block destructive tools", $"the '{tool.Name}' tool is categorised as destructive."));

        if (AISettings.BlockFileAccessOutsideDoc
            && tool.Name is "open_doc" or "save_doc"
            && PathFenceViolation(Argument(args, "path"), services) is string fenced)
            return Task.FromResult<string?>(Blocked(FileFenceLabel, fenced));

        return tool.Name switch
        {
            "run_python" or "run_csharp" => CheckScriptAsync(tool, Argument(args, "script"), ct),
            "run_command" => CheckCommandAsync(tool, Argument(args, "command"), ct),
            _ => Task.FromResult<string?>(null),
        };
    }

    private static async Task<string?> CheckScriptAsync(ToolHandler tool, string? script, CancellationToken ct)
    {
        if (script is null)
            return null;

        foreach ((bool enabled, ScriptRail rail, string label) in ScriptRails())
        {
            if (!enabled)
                continue;
            IReadOnlyList<string> matches = ScriptGuard.ScanScript(rail, script);
            if (matches.Count > 0)
                return Blocked(label, $"the script matches: {string.Join(", ", matches)}."
                    + (rail == ScriptRail.FileAccess
                        ? " Script file access cannot be path-checked, so any file API use is blocked while this guardrail is on."
                        : string.Empty));
        }

        return await AskUserIfRequiredAsync(tool, script, ct).ConfigureAwait(false);
    }

    private static async Task<string?> CheckCommandAsync(ToolHandler tool, string? command, CancellationToken ct)
    {
        if (command is null)
            return null;

        if (AISettings.BlockFileAccessOutsideDoc
            && ScriptGuard.ScanCommand(ScriptRail.FileAccess, command) is { Count: > 0 } accessed)
            return Blocked(FileFenceLabel, $"the command string matches: {string.Join(", ", accessed)}.");

        if (AISettings.BlockScriptFileWrites
            && ScriptGuard.ScanCommand(ScriptRail.FileWrite, command) is { Count: > 0 } written)
            return Blocked("Block file writes and deletes in scripts",
                $"the command string matches: {string.Join(", ", written)}.");

        if (AnyScriptRailEnabled()
            && ScriptGuard.ScanCommandForScriptEscapes(command) is { Count: > 0 } escapes)
            return Blocked("script guardrails",
                $"the command string matches: {string.Join(", ", escapes)}. Script-running commands are blocked "
                + "while script guardrails are on; use run_python or run_csharp so the script can be checked.");

        return await AskUserIfRequiredAsync(tool, command, ct).ConfigureAwait(false);
    }

    private static async Task<string?> AskUserIfRequiredAsync(ToolHandler tool, string body, CancellationToken ct)
    {
        if (!AISettings.AskBeforeScripts)
            return null;
        if (await ScriptApproval.RequestAsync(tool.Title ?? tool.Name, body, ct).ConfigureAwait(false))
            return null;
        return "The user reviewed this script and declined to run it. Ask them how they would like to proceed.";
    }

    private static (bool Enabled, ScriptRail Rail, string Label)[] ScriptRails() =>
    [
        (AISettings.BlockFileAccessOutsideDoc, ScriptRail.FileAccess, FileFenceLabel),
        (AISettings.BlockScriptFileWrites, ScriptRail.FileWrite, "Block file writes and deletes in scripts"),
        (AISettings.BlockScriptNetwork, ScriptRail.Network, "Block network access in scripts"),
        (AISettings.BlockScriptProcessLaunch, ScriptRail.ProcessLaunch, "Block launching programs in scripts"),
        (AISettings.BlockScriptEnvironmentReads, ScriptRail.EnvironmentRead, "Block reading environment variables in scripts"),
    ];

    private static bool AnyScriptRailEnabled() =>
        ScriptRails().Any(r => r.Enabled);

    private static string? PathFenceViolation(string? path, IServiceProvider services)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        if (services.GetService(typeof(RhinoDoc)) is not RhinoDoc doc)
            return null;

        string docDir = Path.GetDirectoryName(doc.Path) ?? string.Empty;
        if (docDir.Length == 0)
            return "the document has not been saved, so there is no document folder to allow. "
                + "Ask the user to save the document or to disable this guardrail.";

        try
        {
            string root = Path.GetFullPath(docDir);
            if (!root.EndsWith(Path.DirectorySeparatorChar))
                root += Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return $"'{path}' is outside the document's folder '{docDir}'.";
            return null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return $"'{path}' could not be resolved to a real location.";
        }
    }

    private static string? Argument(IDictionary<string, JsonElement>? args, string name) =>
        args is not null && args.TryGetValue(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Blocked(string guardrail, string detail) =>
        $"Blocked by the user's safety guardrail \"{guardrail}\": {detail} "
        + "Do not attempt to work around this. Tell the user what was blocked; they can adjust "
        + "guardrails in Rhino Options > AI > Safety.";
}
