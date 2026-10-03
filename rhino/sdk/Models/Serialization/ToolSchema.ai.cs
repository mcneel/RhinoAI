using System;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal static class ToolSchema
{

    public static IEnumerable<(IMcp, ITool)> Tools(IHarness harness)
    {
        foreach (IMcp mcp in harness.Mcps.Values)
        {
            foreach (ITool tool in mcp.Tools.Values)
                yield return (mcp, tool);
        }
    }

    private const string WireSeparator = "__";

    public static string WireName(string mcpName, string toolName) => $"{mcpName}{WireSeparator}{toolName}";

    public static bool TryParseWireName(string wireName, out string mcpName, out string toolName)
    {
        int split = wireName.IndexOf(WireSeparator, StringComparison.Ordinal);
        if (split <= 0 || split + WireSeparator.Length >= wireName.Length)
        {
            mcpName = string.Empty;
            toolName = string.Empty;
            return false;
        }

        mcpName = wireName[..split];
        toolName = wireName[(split + WireSeparator.Length)..];
        return true;
    }

    public static JsonObject Parameters(ITool tool, Func<ToolArgType, string> typeName)
    {
        JsonObject properties = [];
        JsonArray required = [];

        foreach (ToolParameter arg in tool.Args)
        {
            properties[arg.Name] = new JsonObject
            {
                ["type"] = typeName(arg.Type),
                ["description"] = arg.Description,
            };

            // Add(string) binds to the generic overload, which builds a node that cannot serialize without a TypeInfoResolver.
            if (arg.Required)
                required.Add(JsonValue.Create(arg.Name));
        }

        return new JsonObject
        {
            ["type"] = typeName(ToolArgType.Object),
            ["properties"] = properties,
            ["required"] = required,
        };
    }

}
