using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal sealed class ChatCompletionsSerializationConverter(string vendor) : ITurnConverter
{

    public string Vendor { get; } = vendor;

    public JsonObject ToRequest(RequestSettings settings, Harness harness, IEnumerable<ITurn> turns)
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

        JsonObject body = new();
        if (!ModelDefinition.IsDefault(settings.Model))
            body["model"] = settings.Model;

        body["max_tokens"] = settings.MaxOutputTokens;
        body["messages"] = messages;

        JsonArray declarations = Declarations(harness);
        if (declarations.Count > 0)
            body["tools"] = declarations;

        return body;
    }

    public IReadOnlyList<ITurn> FromResponse(Harness harness, JsonNode response)
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

        if (payload["usage"] is JsonObject usage)
            turns.Add(new UsageTurn((int?)usage["prompt_tokens"] ?? 0, (int?)usage["completion_tokens"] ?? 0));

        turns.Add(new TurnEnd(Stop((string?)choice["finish_reason"])));

        return turns;
    }

    public JsonNode Merge(IEnumerable<JsonNode> events)
    {
        StringBuilder reasoning = new();
        StringBuilder content = new();
        SortedDictionary<int, StreamedCall> calls = [];
        string? finish = null;
        JsonNode? usage = null;

        foreach (JsonNode chunk in events)
        {
            if (chunk["error"] is JsonNode error)
                throw new HttpRequestException($"{Vendor} failed mid-reply: {error.ToJsonString()}");

            if (chunk["usage"] is JsonObject reported)
                usage = reported;

            if (chunk["choices"] is not JsonArray { Count: > 0 } choices || choices[0] is not JsonObject choice)
                continue;

            finish = (string?)choice["finish_reason"] ?? finish;

            if (choice["delta"] is not JsonObject delta)
                continue;

            reasoning.Append((string?)delta["reasoning_content"]);
            content.Append((string?)delta["content"]);

            foreach (JsonNode? node in delta["tool_calls"] as JsonArray ?? [])
            {
                if (node is not JsonObject fragment)
                    continue;

                int index = (int?)fragment["index"] ?? calls.Count;
                if (!calls.TryGetValue(index, out StreamedCall? call))
                    calls[index] = call = new StreamedCall();

                call.Id ??= (string?)fragment["id"];
                call.Name ??= (string?)fragment["function"]?["name"];
                call.Arguments.Append((string?)fragment["function"]?["arguments"]);
            }
        }

        if (finish is null)
            throw new HttpRequestException($"{Vendor} ended the stream before the reply finished.");

        JsonArray toolCalls = [];
        foreach (StreamedCall call in calls.Values)
        {
            toolCalls.Add(new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments.ToString() },
            });
        }

        JsonObject message = new()
        {
            ["role"] = "assistant",
            ["reasoning_content"] = reasoning.ToString(),
            ["content"] = content.ToString(),
            ["tool_calls"] = toolCalls,
        };

        return new JsonObject
        {
            ["choices"] = new JsonArray(new JsonObject { ["message"] = message, ["finish_reason"] = finish }),
            ["usage"] = usage?.DeepClone(),
        };
    }

    private sealed class StreamedCall
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public StringBuilder Arguments { get; } = new();
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

    private ToolTurn ToolCall(Harness harness, JsonObject call)
    {
        JsonObject? function = call["function"] as JsonObject;

        string name = (string?)function?["name"] ?? string.Empty;
        JsonNode? arguments = ToolArgs.Parse(Vendor, name, (string?)function?["arguments"]);

        return new ToolTurn((string?)call["id"] ?? string.Empty, name, ToolArgs.FromJson(harness, name, arguments));
    }

    private static JsonArray Declarations(Harness harness)
    {
        JsonArray declarations = [];
        foreach ((Mcp mcp, ITool tool) in ToolSchema.Tools(harness))
        {
            declarations.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = ToolSchema.WireName(mcp.Name, tool.Name),
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
