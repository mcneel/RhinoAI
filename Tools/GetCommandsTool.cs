using System;
using System.Linq;
using System.Text.Json.Nodes;
using Rhino.Commands;

namespace RhMcp.Tools;

public sealed class GetCommandsTool : IMcpTool
{
    public string Name => "get_commands";
    public string Description => "List all Rhino commands currently registered in this session, including WIP and plugin commands. Useful when documentation is unavailable.";
    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            filter = new { type = "string", description = "Optional substring filter (case-insensitive)" }
        }
    };

    public object Execute(JsonObject? args)
    {
        var filter = args?["filter"]?.GetValue<string>() ?? "";

        string[] names = Command.GetCommandNames(true, false)
            .Where(n => string.IsNullOrEmpty(filter)
                     || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .Order()
            .ToArray();

        var text = names.Length > 0
            ? string.Join("\n", names)
            : "No commands found matching filter.";

        return new { content = new[] { new { type = "text", text } } };
    }
}
