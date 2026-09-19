using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

namespace Rhino.AI.Models;

internal sealed class GeminiSerializationConverter : ITurnConverter
{

    public string Vendor => "Google";

    public JsonObject ToRequest(RequestSettings settings, IHarness harness, IEnumerable<ITurn> turns)
    {
        StringBuilder system = new();
        JsonArray contents = [];
        JsonArray? parts = null;
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

            if (ToPart(turn) is not JsonObject part)
                continue;

            if (parts is null || turn.Role != role)
            {
                parts = [];
                contents.Add(new JsonObject { ["role"] = RoleName(turn.Role), ["parts"] = parts });
                role = turn.Role;
            }

            parts.Add(part);
        }

        JsonObject body = new()
        {
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject
            {
                ["maxOutputTokens"] = settings.MaxOutputTokens,
                ["thinkingConfig"] = new JsonObject { ["includeThoughts"] = true },
            },
        };

        if (system.Length > 0)
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = system.ToString() }) };

        JsonArray declarations = Declarations(harness);
        if (declarations.Count > 0)
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations });

        return body;
    }

    public IReadOnlyList<ITurn> FromResponse(IHarness harness, JsonNode response)
    {
        if (response is not JsonObject payload)
            throw new JsonException("Gemini returned something other than a response object.");

        if ((payload["candidates"] as JsonArray)?[0] is not JsonObject candidate)
            throw new JsonException("Gemini returned no candidates.");

        List<ITurn> turns = [];
        foreach (JsonNode? node in candidate["content"]?["parts"] as JsonArray ?? [])
        {
            if (node is JsonObject part && ToTurn(harness, part) is ITurn turn)
                turns.Add(turn);
        }

        turns.Add(new TurnEnd(Stop((string?)candidate["finishReason"]), (int?)payload["usageMetadata"]?["candidatesTokenCount"]));

        return turns;
    }

    private JsonObject? ToPart(ITurn turn) => turn switch
    {
        MessageTurn message => new JsonObject { ["text"] = message.Message },
        ThinkingTurn thinking => ToThought(thinking),
        ToolTurn tool => ToCall(tool),
        ToolResultTurn result => new JsonObject
        {
            ["functionResponse"] = new JsonObject
            {
                ["id"] = result.Id,
                ["name"] = result.ToolName,
                ["response"] = new JsonObject { ["result"] = result.Data },
            },
        },
        _ => null,
    };

    private JsonObject ToThought(ThinkingTurn thinking)
    {
        JsonObject part = new() { ["text"] = thinking.Thinking, ["thought"] = true };
        Sign(part, thinking.Signature);
        return part;
    }

    private JsonObject ToCall(ToolTurn tool)
    {
        JsonObject part = new()
        {
            ["functionCall"] = new JsonObject
            {
                ["id"] = tool.Id,
                ["name"] = tool.Name,
                ["args"] = ToolArgs.ToJson(tool.Args),
            },
        };

        // Gemini 3 rejects a replayed call whose thought signature is missing, so it rides along on the part.
        Sign(part, tool.Signature);
        return part;
    }

    private void Sign(JsonObject part, ThoughtSignature? signature)
    {
        if (signature is not null && signature.Vendor == Vendor)
            part["thoughtSignature"] = signature.Value;
    }

    private ITurn? ToTurn(IHarness harness, JsonObject part)
    {
        ThoughtSignature? signature = (string?)part["thoughtSignature"] is string value
            ? new ThoughtSignature(Vendor, null, value)
            : null;

        if (part["functionCall"] is JsonObject call)
        {
            string name = (string?)call["name"] ?? string.Empty;
            return new ToolTurn((string?)call["id"] ?? NewId(), name, ToolArgs.FromJson(harness, name, call["args"]), signature);
        }

        if ((string?)part["text"] is not string text)
            return null;

        return (bool?)part["thought"] == true
            ? new ThinkingTurn(text, signature)
            : new MessageTurn(text, RoleType.Assistant);
    }

    private static string NewId() => $"call_{Guid.NewGuid():N}";

    private static JsonArray Declarations(IHarness harness)
    {
        JsonArray declarations = [];
        foreach (ITool tool in ToolSchema.Tools(harness))
        {
            declarations.Add(new JsonObject
            {
                ["name"] = tool.Name,
                ["description"] = tool.Description,
                ["parameters"] = ToolSchema.Parameters(tool, SchemaType),
            });
        }

        return declarations;
    }

    private static string RoleName(RoleType role) => role switch
    {
        RoleType.User => "user",
        RoleType.Assistant => "model",
        
        _ => throw new InvalidOperationException($"A {role} turn does not belong in the Gemini contents."),
    };

    private static string SchemaType(ToolArgType type) => type switch
    {
        ToolArgType.String or ToolArgType.FilePath or ToolArgType.URL => "STRING",
        ToolArgType.Number => "NUMBER",
        ToolArgType.Integer => "INTEGER",
        ToolArgType.Boolean => "BOOLEAN",
        ToolArgType.Array => "ARRAY",
        ToolArgType.Object => "OBJECT",
        
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Tool argument has no declared type."),
    };

    private static StopReason Stop(string? reason) => reason switch
    {
        "STOP" => StopReason.EndTurn,
        "MAX_TOKENS" => StopReason.MaxTokens,
        "SAFETY" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" => StopReason.Refusal,
        _ => StopReason.Other,
    };

}
