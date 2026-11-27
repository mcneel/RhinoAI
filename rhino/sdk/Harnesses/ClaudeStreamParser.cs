using System;
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Collections.Generic;

using Rhino.AI.Models;

namespace Rhino.AI;

internal sealed class ClaudeStreamParser(Harness harness, Guid launchedSessionId, Action<Guid> onSessionStarted)
{

    private Dictionary<string, string> ToolNames { get; } = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<ITurn> Parse(string line)
    {
        if (line.Length == 0) return [];

        try
        {
            JsonNode? node = JsonNode.Parse(line);
            return node?["type"]?.GetValue<string>() switch
            {
                "user" => ParseUser(node),
                "system" => ParseSystem(node),
                "result" => ParseResult(node),
                "assistant" => ParseAssistant(node),

                _ => [],
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            Debug.WriteLine(ex);
            return [];
        }
    }

    private static DateTime? GetTimestamp(JsonNode node)
    {
        string? timestamp = node["timestamp"]?.GetValue<string>();
        if (!DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsed)) return null;
        return parsed;
    }

    private List<ITurn> ParseUser(JsonNode node)
    {
        if (node["message"]?["content"] is not JsonArray contents) return [];

        DateTime? timestamp = GetTimestamp(node);

        List<ITurn> turns = new(contents.Count);
        foreach (JsonNode? content in contents)
        {
            if (content?["type"]?.ToString() is "tool_result" && GetToolResultTurn(content, timestamp) is ToolResultTurn turn)
                turns.Add(turn);
        }

        return turns;
    }

    private ToolResultTurn? GetToolResultTurn(JsonNode content, DateTime? timestamp)
    {
        string? id = content["tool_use_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return null;

        string? message = content["content"] switch
        {
            JsonArray blocks => TextBlocks(blocks),
            JsonValue value when value.GetValueKind() == JsonValueKind.String => value.GetValue<string>(),

            _ => null,
        };
        if (message is null) return null;

        ToolNames.TryGetValue(id, out string? toolName);
        ToolNames.Remove(id);

        ToolReturn toolReturn = content["is_error"]?.GetValue<bool>() == true
            ? new ToolReturn(message, ToolResult.Failure, null)
            : ToolReturn.Success(message);

        return new ToolResultTurn(id, toolName ?? "err", toolReturn, timestamp);
    }

    private static string TextBlocks(JsonArray blocks)
    {
        List<string> texts = [];
        foreach (JsonNode? block in blocks)
        {
            if (block?["type"]?.GetValue<string>() != "text") continue;
            if (block["text"]?.GetValue<string>() is string text) texts.Add(text);
        }

        return string.Join("\n", texts);
    }

    private List<ITurn> ParseSystem(JsonNode node)
    {
        if (string.Equals(node["subtype"]?.GetValue<string>(), "init", StringComparison.OrdinalIgnoreCase))
            onSessionStarted(Guid.TryParse(node["session_id"]?.GetValue<string>(), out Guid reported) ? reported : launchedSessionId);

        return [];
    }

    private static List<ITurn> ParseResult(JsonNode node)
    {
        string? error = node["is_error"]?.GetValue<bool>() == true ? node["result"]?.GetValue<string>() : null;
        TimeSpan? duration = node["duration_ms"]?.GetValue<double>() is double ms ? TimeSpan.FromMilliseconds(ms) : null;
        int? tokenCount = GetTokenCount(node);

        List<string> errors = [];
        if (node["errors"] is JsonArray jsonArr)
        {
            foreach (JsonNode? jItem in jsonArr)
            {
                if (jItem?.GetValue<string>() is not string err) continue;
                errors.Add(err);
            }
        }

        // stop_reason belongs to the model's last reply, so a run the CLI cut short (turn limit, budget) still reads "tool_use".
        string? subtype = node["subtype"]?.GetValue<string>();
        bool isAborted = subtype is not null && subtype != "success";

        StopReason reason = isAborted ? StopReason.Error : node["stop_reason"]?.GetValue<string>().ToLowerInvariant() switch
        {
            "end_turn" => StopReason.EndTurn,
            "tool_use" => StopReason.ToolUse,
            "max_tokens" => StopReason.MaxTokens,
            "refusal" => StopReason.Refusal,
            "error" => StopReason.Error,
            null when error is null && errors.Count == 0 => StopReason.EndTurn,
            null => StopReason.Error,

            _ => StopReason.Other
        };

        string? message = !string.IsNullOrEmpty(error) ? error
            : errors.Count > 0 ? string.Join("\n", errors)
            : isAborted ? $"Claude stopped early ({subtype})."
            : null;

        return [new TurnEnd(reason, message, duration: duration, tokenCount: tokenCount)];
    }

    private static int? GetTokenCount(JsonNode node)
    {
        if (node["usage"] is not JsonNode usage) return null;

        return (usage["input_tokens"]?.GetValue<int>() ?? 0)
            + (usage["cache_creation_input_tokens"]?.GetValue<int>() ?? 0)
            + (usage["cache_read_input_tokens"]?.GetValue<int>() ?? 0)
            + (usage["output_tokens"]?.GetValue<int>() ?? 0);
    }

    private List<ITurn> ParseAssistant(JsonNode node)
    {
        if (node["message"]?["content"] is not JsonArray contents) return [];

        DateTime? timestamp = GetTimestamp(node);

        List<ITurn> turns = new(contents.Count);
        foreach (JsonNode? content in contents)
        {
            ITurn? turn = content?["type"]?.ToString() switch
            {
                "tool_use" => GetToolTurn(content, timestamp),
                "thinking" => GetThinking(content, timestamp),
                "text" => GetText(content, timestamp),

                _ => null,
            };

            if (turn is not null) turns.Add(turn);
        }

        return turns;
    }

    private static MessageTurn? GetText(JsonNode content, DateTime? timestamp)
    {
        string? text = content["text"]?.GetValue<string>();
        if (string.IsNullOrEmpty(text)) return null;
        return new MessageTurn(text,RoleType.Assistant, timestamp);
    }

    private static ThinkingTurn? GetThinking(JsonNode content, DateTime? timestamp)
    {
        string? thinking = content["thinking"]?.GetValue<string>();
        if (string.IsNullOrEmpty(thinking)) return null;
        return new ThinkingTurn(thinking,timestamp: timestamp);
    }

    private ToolTurn? GetToolTurn(JsonNode content, DateTime? timestamp)
    {
        string? id = content["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return null;

        string? name = content["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name)) return null;

        ToolNames[id] = name;
        return new ToolTurn(id, name, ToolArgs.FromJson(harness, name, content["input"]), timestamp: timestamp);
    }

}
