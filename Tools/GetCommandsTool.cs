using System;
using System.ComponentModel;
using System.Linq;

using ModelContextProtocol.Server;

using Rhino.Commands;

namespace RhMcp.Tools;

[McpServerToolType]
public static class GetCommandsTool
{
    [McpServerTool(Name = "get_commands")]
    [Description("List all Rhino commands currently registered in this session, including WIP and plugin commands. Useful when documentation is unavailable.")]
    public static string GetCommands(
        [Description("Optional substring filter (case-insensitive)")] string? filter = null)
    {
        string[] names = Command.GetCommandNames(true, false)
            .Where(n => string.IsNullOrEmpty(filter)
                     || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Order()
            .ToArray();

        return names.Length > 0
            ? string.Join("\n", names)
            : "No commands found matching filter.";
    }
}
