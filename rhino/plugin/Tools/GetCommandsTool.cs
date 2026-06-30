using Rhino.Commands;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GetCommandsTool
{
    private const int MaxResults = 200;

    [McpServerTool("get_commands", "List Rhino Commands", true, false)]
    [Description("Discover Rhino command names available to run_command. Returns English names from all registered plugins (including those not yet loaded; invoking such a command may trigger plugin load). Test commands are excluded. Use filter to narrow the list before calling run_command.")]
    public static string GetCommands(
        RhinoDoc doc,
        [Description("Substring filter (case-insensitive). Strongly recommended — unfiltered results can exceed 1000 commands.")] string? filter = null)
    {
        var trimmed = filter?.TrimStart('_', '-');
        // If the user literally searched for "_" or "-" (or "__"/"--"/etc.),
        // TrimStart produces an empty string. Fall back to the original filter
        // so we don't silently return every command in that case.
        var needle = string.IsNullOrEmpty(trimmed) ? filter : trimmed;
        string[] all = Command.GetCommandNames(true, false)
            .Where(n => string.IsNullOrEmpty(needle)
                     || n.Contains(needle, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (all.Length == 0)
            return string.IsNullOrEmpty(filter)
                ? "No commands found."
                : $"No commands found matching '{filter}'.";

        if (all.Length <= MaxResults)
            return $"# {all.Length} commands\n" + string.Join("\n", all);

        string head = string.Join("\n", all.Take(MaxResults));
        return $"# {all.Length} commands (showing first {MaxResults}; refine filter to narrow)\n{head}";
    }
}
