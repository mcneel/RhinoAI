using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal sealed class ClaudeSerializationConverter(string vendor = "Anthropic") : ITurnConverter
{

    public string Vendor { get; } = vendor;

    public JsonObject ToRequest(RequestSettings settings, IHarness harness, IEnumerable<ITurn> turns)
    {
        StringBuilder system = new();
        JsonArray messages = [];
        JsonArray? blocks = null;
        RoleType role = RoleType.System;

        foreach (ITurn turn in turns)
        {
            if (turn is SystemTurn prompt)
            {
                if (system.Length > 0)
                    system.Append("\n\n");

                system.Append(prompt.Prompt);
                continue;
            }

            if (ToBlock(turn) is not JsonObject block)
                continue;

            // Claude takes one message per run of same-role turns, not one message per turn.
            if (blocks is null || turn.Role != role)
            {
                blocks = [];
                messages.Add(new JsonObject { ["role"] = RoleName(turn.Role), ["content"] = blocks });
                role = turn.Role;
            }

            blocks.Add(block);
        }

        JsonObject body = new()
        {
            ["model"] = settings.Model,
            ["max_tokens"] = settings.MaxOutputTokens,
            ["thinking"] = new JsonObject { ["type"] = "adaptive", ["display"] = "summarized" },
            ["messages"] = messages,
        };

        if (system.Length > 0)
            body["system"] = system.ToString();

        JsonArray declarations = Declarations(harness);
        if (declarations.Count > 0)
            body["tools"] = declarations;

        return body;
    }

    public IReadOnlyList<ITurn> FromResponse(IHarness harness, JsonNode response)
    {
        if (response is not JsonObject message)
            throw new JsonException("Claude returned something other than a message object.");

        List<ITurn> turns = [];
        foreach (JsonNode? node in message["content"] as JsonArray ?? [])
        {
            if (node is JsonObject block && ToTurn(harness, block) is ITurn turn)
                turns.Add(turn);
        }

        turns.Add(new TurnEnd(Stop((string?)message["stop_reason"]), (int?)message["usage"]?["output_tokens"]));

        return turns;
    }

    private JsonObject? ToBlock(ITurn turn) => turn switch
    {
        MessageTurn message => new JsonObject { ["type"] = "text", ["text"] = message.Message },
        ThinkingTurn thinking when Replayable(thinking.Signature) is ThoughtSignature signature => ToThinking(thinking, signature),
        ToolTurn tool => new JsonObject
        {
            ["type"] = "tool_use",
            ["id"] = tool.Id,
            ["name"] = tool.Name,
            ["input"] = ToolArgs.ToJson(tool.Args),
        },
        ToolResultTurn result => new JsonObject
        {
            ["type"] = "tool_result",
            ["tool_use_id"] = result.Id,
            ["content"] = result.Data,
            ["is_error"] = !result.Success,
        },
        _ => null,
    };

    private ThoughtSignature? Replayable(ThoughtSignature? signature)
        => signature is not null && signature.Vendor == Vendor ? signature : null;

    private static JsonObject ToThinking(ThinkingTurn thinking, ThoughtSignature signature) => signature.IsRedacted
        ? new JsonObject { ["type"] = "redacted_thinking", ["data"] = signature.Value }
        : new JsonObject { ["type"] = "thinking", ["thinking"] = thinking.Thinking, ["signature"] = signature.Value };

    private ITurn? ToTurn(IHarness harness, JsonObject block) => (string?)block["type"] switch
    {
        "text" => new MessageTurn((string?)block["text"] ?? string.Empty, RoleType.Assistant),
        "thinking" => new ThinkingTurn((string?)block["thinking"] ?? string.Empty, new ThoughtSignature(Vendor, null, (string?)block["signature"] ?? string.Empty)),
        "redacted_thinking" => new ThinkingTurn(string.Empty, new ThoughtSignature(Vendor, null, (string?)block["data"] ?? string.Empty, IsRedacted: true)),
        "tool_use" => ToolCall(harness, block),
        _ => null,
    };

    private static ToolTurn ToolCall(IHarness harness, JsonObject block)
    {
        string name = (string?)block["name"] ?? string.Empty;
        return new ToolTurn((string?)block["id"] ?? string.Empty, name, ToolArgs.FromJson(harness, name, block["input"]));
    }

    private static JsonArray Declarations(IHarness harness)
    {
        JsonArray declarations = [];
        foreach (ITool tool in ToolSchema.Tools(harness))
        {
            declarations.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["input_schema"] = ToolSchema.Parameters(tool, SchemaType),
            });
        }

        return declarations;
    }

    private static string RoleName(RoleType role) => role switch
    {
        RoleType.User => "user",
        RoleType.Assistant => "assistant",

        _ => throw new InvalidOperationException($"A {role} turn does not belong in the Claude message list."),
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

    private static StopReason Stop(string? reason) => reason switch
    {
        "end_turn" or "stop_sequence" => StopReason.EndTurn,
        "tool_use" => StopReason.ToolUse,
        "max_tokens" => StopReason.MaxTokens,
        "refusal" => StopReason.Refusal,

        _ => StopReason.Other,
    };

}
