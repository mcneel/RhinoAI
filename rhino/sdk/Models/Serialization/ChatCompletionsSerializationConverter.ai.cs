using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal sealed class ChatCompletionsSerializationConverter(string vendor) : ITurnConverter
{

    public string Vendor { get; } = vendor;

    public JsonObject ToRequest(RequestSettings settings, IHarness harness, IEnumerable<ITurn> turns)
    {
        StringBuilder system = new();
        StringBuilder text = new();
        JsonArray messages = [];
        JsonArray calls = [];

        void CloseAssistantMessage()
        {
            if (text.Length == 0 && calls.Count == 0)
                return;

            JsonObject message = new() { ["role"] = "assistant", ["content"] = text.ToString() };
            if (calls.Count > 0)
                message["tool_calls"] = calls;

            messages.Add(message);
            text.Clear();
            calls = [];
        }

        foreach (ITurn turn in turns)
        {
            switch (turn)
            {
                case SystemTurn prompt:
                    Append(system, prompt.Prompt);
                    break;

                case MessageTurn { Role: RoleType.Assistant } reply:
                    Append(text, reply.Message);
                    break;

                case ToolTurn tool:
                    calls.Add(ToCall(tool));
                    break;

                case MessageTurn message:
                    CloseAssistantMessage();
                    messages.Add(new JsonObject { ["role"] = "user", ["content"] = message.Message });
                    break;

                case ToolResultTurn result:
                    CloseAssistantMessage();
                    messages.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = result.Id, ["content"] = result.Data });
                    break;

                // DeepSeek answers a replayed reasoning_content with a 400, so a thought never goes back up the wire.
                case ThinkingTurn:
                    break;
            }
        }

        CloseAssistantMessage();

        if (system.Length > 0)
            messages.Insert(0, new JsonObject { ["role"] = "system", ["content"] = system.ToString() });

        JsonObject body = new()
        {
            ["model"] = settings.Model,
            ["max_tokens"] = settings.MaxOutputTokens,
            ["messages"] = messages,
        };

        JsonArray declarations = Declarations(harness);
        if (declarations.Count > 0)
            body["tools"] = declarations;

        return body;
    }

    public IReadOnlyList<ITurn> FromResponse(IHarness harness, JsonNode response)
    {
        if (response is not JsonObject payload)
            throw new JsonException($"{Vendor} returned something other than a completion object.");

        if ((payload["choices"] as JsonArray)?[0] is not JsonObject choice)
            throw new JsonException($"{Vendor} returned no choices.");

        if (choice["message"] is not JsonObject message)
            throw new JsonException($"{Vendor} returned a choice with no message.");

        List<ITurn> turns = [];

        if (Text(message["reasoning_content"]) is string thinking)
            turns.Add(new ThinkingTurn(thinking));

        if (Text(message["content"]) is string content)
            turns.Add(new MessageTurn(content, RoleType.Assistant));

        foreach (JsonNode? node in message["tool_calls"] as JsonArray ?? [])
        {
            if (node is JsonObject call)
                turns.Add(ToolCall(harness, call));
        }

        turns.Add(new TurnEnd(Stop((string?)choice["finish_reason"]), (int?)payload["usage"]?["completion_tokens"]));

        return turns;
    }

    private static void Append(StringBuilder builder, string value)
    {
        if (builder.Length > 0)
            builder.Append("\n\n");

        builder.Append(value);
    }

    private static string? Text(JsonNode? node) => (string?)node is string value && value.Length > 0 ? value : null;

    private static JsonObject ToCall(ToolTurn tool) => new()
    {
        ["id"] = tool.Id,
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = tool.Name,
            ["arguments"] = ToolArgs.ToJson(tool.Args).ToJsonString(),
        },
    };

    private ToolTurn ToolCall(IHarness harness, JsonObject call)
    {
        JsonObject? function = call["function"] as JsonObject;

        string name = (string?)function?["name"] ?? string.Empty;
        JsonNode? arguments = ToolArgs.Parse(Vendor, name, (string?)function?["arguments"]);

        return new ToolTurn((string?)call["id"] ?? string.Empty, name, ToolArgs.FromJson(harness, name, arguments));
    }

    private static JsonArray Declarations(IHarness harness)
    {
        JsonArray declarations = [];
        foreach (ITool tool in ToolSchema.Tools(harness))
        {
            declarations.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = ToolSchema.Parameters(tool, SchemaType),
                },
            });
        }

        return declarations;
    }

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

    private static StopReason Stop(string? reason) => reason switch
    {
        "stop" => StopReason.EndTurn,
        "tool_calls" => StopReason.ToolUse,
        "length" => StopReason.MaxTokens,
        "content_filter" => StopReason.Refusal,
        "insufficient_system_resource" => StopReason.Error,

        _ => StopReason.Other,
    };

}
