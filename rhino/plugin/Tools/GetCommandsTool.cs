using Rhino.Commands;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GetCommandsTool
{
    private const int MaxResults = 200;

    [McpServerTool(Name = "get_commands")]
    [Description("Discover Rhino command names available to run_command. Returns English names from all registered plugins (including those not yet loaded; invoking such a command may trigger plugin load). Test commands are excluded. Use filter to narrow the list before calling run_command.")]
    public static string GetCommands(
        [Description("Substring filter (case-insensitive). Strongly recommended — unfiltered results can exceed 1000 commands.")] string? filter = null)
    {
        string[] all = Command.GetCommandNames(true, false)
            .Where(n => string.IsNullOrEmpty(filter)
                     || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
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
