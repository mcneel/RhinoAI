using System;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal static class ToolSchema
{

    public static IEnumerable<ITool> Tools(IHarness harness)
    {
        foreach (IMcp mcp in harness.Mcps.Values)
        {
            foreach (ITool tool in mcp.Tools.Values)
                yield return tool;
        }
    }

    public static JsonObject Parameters(ITool tool, Func<ToolArgType, string> typeName)
    {
        JsonObject properties = [];
        JsonArray required = [];

        foreach (ToolArg arg in tool.Args)
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
