using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal sealed class ChatGptSerializationConverter : ITurnConverter
{

    public string Vendor => "OpenAI";

    public JsonObject ToRequest(RequestSettings settings, IHarness harness, IEnumerable<ITurn> turns)
    {
        StringBuilder instructions = new();
        JsonArray input = [];

        foreach (ITurn turn in turns)
        {
            if (turn is SystemTurn prompt)
            {
                if (instructions.Length > 0)
                    instructions.Append("\n\n");

                instructions.Append(prompt.Prompt);
                continue;
            }

            if (ToItem(turn) is JsonObject item)
                input.Add(item);
        }

        JsonObject body = new()
        {
            ["model"] = settings.Model,
            ["max_output_tokens"] = settings.MaxOutputTokens,
            ["input"] = input,
            ["reasoning"] = new JsonObject { ["summary"] = "auto" },
            // The transcript lives here, not on OpenAI's servers, so reasoning has to come back encrypted to be replayable.
            ["store"] = false,
            ["include"] = new JsonArray("reasoning.encrypted_content"),
        };

        if (instructions.Length > 0)
            body["instructions"] = instructions.ToString();

        JsonArray declarations = Declarations(harness);
        if (declarations.Count > 0)
            body["tools"] = declarations;

        return body;
    }

    public IReadOnlyList<ITurn> FromResponse(IHarness harness, JsonNode response)
    {
        if (response is not JsonObject payload)
            throw new JsonException("OpenAI returned something other than a response object.");

        List<ITurn> turns = [];
        bool calling = false;
        foreach (JsonNode? node in payload["output"] as JsonArray ?? [])
        {
            if (node is not JsonObject item || ToTurn(harness, item) is not ITurn turn)
                continue;

            calling |= turn is ToolTurn;
            turns.Add(turn);
        }

        StopReason reason = Stop((string?)payload["status"], (string?)payload["incomplete_details"]?["reason"], calling);
        turns.Add(new TurnEnd(reason, (int?)payload["usage"]?["output_tokens"]));

        return turns;
    }

    private JsonObject? ToItem(ITurn turn) => turn switch
    {
        MessageTurn message => new JsonObject
        {
            ["type"] = "message",
            ["role"] = RoleName(message.Role),
            ["content"] = message.Message,
        },
        ThinkingTurn thinking when Replayable(thinking.Signature) is ThoughtSignature signature => new JsonObject
        {
            ["type"] = "reasoning",
            ["id"] = signature.Id,
            ["summary"] = new JsonArray(),
            ["encrypted_content"] = signature.Value,
        },
        ToolTurn tool => new JsonObject
        {
            ["type"] = "function_call",
            ["call_id"] = tool.Id,
            ["name"] = tool.Name,
            ["arguments"] = ToolArgs.ToJson(tool.Args).ToJsonString(),
        },
        ToolResultTurn result => new JsonObject
        {
            ["type"] = "function_call_output",
            ["call_id"] = result.Id,
            ["output"] = result.Data,
        },

        _ => null,
    };

    private ThoughtSignature? Replayable(ThoughtSignature? signature)
        => signature is not null && signature.Vendor == Vendor && signature.Id is not null ? signature : null;

    private ITurn? ToTurn(IHarness harness, JsonObject item) => (string?)item["type"] switch
    {
        "reasoning" => new ThinkingTurn(Join(item["summary"]), new ThoughtSignature(Vendor, (string?)item["id"], (string?)item["encrypted_content"] ?? string.Empty)),
        "message" => new MessageTurn(Join(item["content"]), RoleType.Assistant),
        "function_call" => ToolCall(harness, item),
        _ => null,
    };

    private static ToolTurn ToolCall(IHarness harness, JsonObject item)
    {
        string name = (string?)item["name"] ?? string.Empty;
        JsonNode? arguments = (string?)item["arguments"] is string json && json.Length > 0 ? JsonNode.Parse(json) : null;

        return new ToolTurn((string?)item["call_id"] ?? string.Empty, name, ToolArgs.FromJson(harness, name, arguments));
    }

    private static string Join(JsonNode? parts)
    {
        StringBuilder text = new();
        foreach (JsonNode? node in parts as JsonArray ?? [])
        {
            if (node is not JsonObject part || (string?)part["text"] is not string value)
                continue;

            if (text.Length > 0)
                text.Append('\n');

            text.Append(value);
        }

        return text.ToString();
    }

    private static JsonArray Declarations(IHarness harness)
    {
        JsonArray declarations = [];
        foreach (ITool tool in ToolSchema.Tools(harness))
        {
            declarations.Add(new JsonObject
            {
                ["type"] = "function",
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = ToolSchema.Parameters(tool, SchemaType),
                ["strict"] = false,
            });
        }

        return declarations;
    }

    private static string RoleName(RoleType role) => role switch
    {
        RoleType.User => "user",
        RoleType.Assistant => "assistant",
        
        _ => throw new InvalidOperationException("A system turn belongs in the OpenAI instructions, not the input list."),
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

    private static StopReason Stop(string? status, string? incomplete, bool calling) => status switch
    {
        "completed" when calling => StopReason.ToolUse,
        "completed" => StopReason.EndTurn,
        "incomplete" when incomplete == "max_output_tokens" => StopReason.MaxTokens,
        "incomplete" when incomplete == "content_filter" => StopReason.Refusal,
        "failed" => StopReason.Error,
        
        _ => StopReason.Other,
    };

}
