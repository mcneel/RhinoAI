using System;
using System.Linq;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Collections.Generic;

using Rhino.AI.Models;

namespace Rhino.AI;

internal sealed class CodexStreamParser(Harness harness, IReadOnlyDictionary<string, Mcp> servers, Action<CodexRateLimits> onRateLimits)
{

    private const int WarnPercent = 80;

    private int? TokenCount { get; set; }
    private bool HasWarned { get; set; }

    public ITurn? Parse(string method, JsonObject parameters)
    {
        switch (method)
        {
            case "turn/started":
                return new TurnStart();

            case "item/started":
                return parameters["item"] is JsonObject started ? ToolCallTurn(started) : null;

            case "item/completed":
                return parameters["item"] is JsonObject completed ? CompletedTurn(completed) : null;

            case "thread/tokenUsage/updated":
                JsonNode? last = parameters["tokenUsage"]?["last"];
                TokenCount = (int)((Long(last?["inputTokens"]) ?? 0) + (Long(last?["outputTokens"]) ?? 0));
                return null;

            case "account/rateLimits/updated":
                return RateLimitWarning(parameters["rateLimits"]);

            case "turn/completed":
                return TurnEndOf(parameters["turn"]);

            default:
                return null;
        }
    }

    private string McpNameOf(string? server)
        => server is not null && servers.TryGetValue(server, out Mcp? mcp) ? mcp.Name : server ?? string.Empty;

    private ToolTurn? ToolCallTurn(JsonObject item)
    {
        if (item["type"]?.GetValue<string>() != "mcpToolCall") return null;
        if (item["id"]?.GetValue<string>() is not string id) return null;
        if (item["tool"]?.GetValue<string>() is not string tool) return null;

        string name = ToolSchema.WireName(McpNameOf(item["server"]?.GetValue<string>()), tool);
        return new ToolTurn(id, name, ToolArgs.FromJson(harness, name, item["arguments"]));
    }

    private ITurn? CompletedTurn(JsonObject item) => item["type"]?.GetValue<string>() switch
    {
        "agentMessage" => item["text"]?.GetValue<string>() is string text ? new MessageTurn(text, RoleType.Assistant) : null,
        "mcpToolCall" => ToolResultTurnOf(item),
        "reasoning" => ThinkingTurnOf(item),
        _ => null,
    };

    private ToolResultTurn? ToolResultTurnOf(JsonObject item)
    {
        if (item["id"]?.GetValue<string>() is not string id) return null;
        if (item["tool"]?.GetValue<string>() is not string tool) return null;

        string name = ToolSchema.WireName(McpNameOf(item["server"]?.GetValue<string>()), tool);

        List<string> texts = [];
        if (item["result"]?["content"] is JsonArray content)
        {
            foreach (JsonNode? block in content)
            {
                if (block?["type"]?.GetValue<string>() == "text" && block["text"]?.GetValue<string>() is string text)
                    texts.Add(text);
            }
        }

        string message = string.Join("\n", texts);
        string? error = item["error"]?["message"]?.GetValue<string>();
        bool failed = error is not null || item["status"]?.GetValue<string>() == "failed";

        ToolReturn toolReturn = failed
            ? new ToolReturn(error ?? message, ToolResult.Failure, null)
            : ToolReturn.Success(message);

        return new ToolResultTurn(id, name, toolReturn);
    }

    private static ThinkingTurn? ThinkingTurnOf(JsonObject item)
    {
        List<string> parts = [];
        foreach (string key in new[] { "summary", "content" })
        {
            if (item[key] is not JsonArray entries) continue;
            foreach (JsonNode? entry in entries)
            {
                string? part = entry switch
                {
                    JsonValue value when value.TryGetValue(out string? raw) => raw,
                    JsonObject obj => obj["text"]?.GetValue<string>(),
                    _ => null,
                };
                if (!string.IsNullOrEmpty(part)) parts.Add(part!);
            }
        }

        return parts.Count == 0 ? null : new ThinkingTurn(string.Join("\n", parts));
    }

    private TurnEnd TurnEndOf(JsonNode? turn) => turn?["status"]?.GetValue<string>() switch
    {
        "completed" => new TurnEnd(StopReason.EndTurn, tokenCount: TokenCount),
        "interrupted" => new TurnEnd(StopReason.Cancelled, tokenCount: TokenCount),
        _ => new TurnEnd(StopReason.Error, turn?["error"]?["message"]?.GetValue<string>() ?? "Codex turn failed.", tokenCount: TokenCount),
    };

    private MessageTurn? RateLimitWarning(JsonNode? node)
    {
        if (node is null) return null;

        CodexRateLimits limits = new(
            Window(node["primary"]),
            Window(node["secondary"]),
            node["planType"]?.GetValue<string>(),
            node["rateLimitReachedType"]?.GetValue<string>());

        onRateLimits(limits);

        if (HasWarned || Warning(limits) is not string warning) return null;
        HasWarned = true;
        return new MessageTurn(warning, RoleType.System);
    }

    private static CodexRateLimitWindow? Window(JsonNode? node)
    {
        if (node is null) return null;

        long? resetsAt = Long(node["resetsAt"]);
        return new CodexRateLimitWindow(
            (int)(Long(node["usedPercent"]) ?? 0),
            Long(node["windowDurationMins"]),
            resetsAt is long seconds ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null);
    }

    private static string? Warning(CodexRateLimits limits)
    {
        CodexRateLimitWindow? fullest = new[] { limits.Primary, limits.Secondary }
            .Where(window => window is not null)
            .OrderByDescending(window => window!.UsedPercent)
            .FirstOrDefault();

        if (limits.LimitReached is string reached)
            return $"Codex usage limit reached ({reached}).{Resets(fullest)}";

        if (fullest is null || fullest.UsedPercent < WarnPercent) return null;

        return $"Codex usage: {fullest.UsedPercent}% of your {WindowLabel(fullest.WindowMinutes)} limit used.{Resets(fullest)}";
    }

    private static string Resets(CodexRateLimitWindow? window)
        => window?.ResetsAt is DateTimeOffset resetsAt
            ? $" Resets {resetsAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)}."
            : string.Empty;

    private static string WindowLabel(long? minutes) => minutes switch
    {
        300 => "5-hour",
        10080 => "weekly",
        43200 => "30-day",
        long other when other % 1440 == 0 => $"{other / 1440}-day",
        long other => $"{other / 60}-hour",
        null => "usage",
    };

    private static long? Long(JsonNode? node)
        => node is JsonValue value && value.TryGetValue(out double number) ? (long)number : null;

}
