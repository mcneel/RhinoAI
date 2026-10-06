using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Nodes;

using Rhino.AI.Models;

namespace Rhino.AI.Mcps;

internal static class McpTools
{

    public static JsonArray List(McpSession session)
    {
        HashSet<string> denied = new(session.Harness.Permissions.ProhibitedTools()
            .Where(permission => permission.McpName == session.Mcp.Name)
            .Select(permission => permission.ToolName), StringComparer.OrdinalIgnoreCase);

        JsonArray tools = [];
        foreach (ITool tool in session.Mcp.Tools.Values)
        {
            if (denied.Contains(tool.Name) || denied.Contains("*")) continue;

            JsonNode described = Describe(tool);
            tools.Add(described);
        }

        return tools;
    }

    public static JsonObject Describe(ITool tool) => new()
    {
        ["name"] = tool.Name,
        ["description"] = tool.Description,
        ["inputSchema"] = ToolSchema.Parameters(tool, SchemaType),
        ["annotations"] = new JsonObject()
        {
            ["readOnlyHint"] = tool.ReadOnly,
            ["destructiveHint"] = tool.Destructive,
        },
    };

    public static JsonObject ToCallResult(ToolReturn toolReturn)
    {
        string text = toolReturn.Guidance is string guidance
            ? $"{toolReturn.Message}\n\n{guidance}"
            : toolReturn.Message;

        JsonNode block = new JsonObject()
        {
            ["type"] = "text",
            ["text"] = text,
        };

        return new JsonObject()
        {
            ["content"] = new JsonArray(block),
            ["isError"] = IsError(toolReturn.Result),
        };
    }

    private static bool IsError(ToolResult result) => result switch
    {
        ToolResult.Success or ToolResult.Mixed => false,
        ToolResult.Failure => true,

        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "Unknown tool result."),
    };

    private static string SchemaType(ToolArgType type) => type switch
    {
        ToolArgType.String or ToolArgType.FilePath or ToolArgType.URL => "string",
        ToolArgType.Number => "number",
        ToolArgType.Integer => "integer",
        ToolArgType.Boolean => "boolean",
        ToolArgType.Array => "array",
        ToolArgType.Object => "object",

        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tool argument has no declared type."),
    };

}
