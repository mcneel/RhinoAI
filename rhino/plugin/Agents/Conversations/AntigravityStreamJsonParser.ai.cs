using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Acp;
using ContentBlock = Acp.ContentBlock;

namespace Rhino.AI;

internal sealed class AntigravityStreamJsonParser : IStreamJsonParser
{
    private AgentDefinition Definition { get; }

    public AntigravityStreamJsonParser(AgentDefinition definition)
    {
        Definition = definition;
    }

    public string DisplayName => Definition.Name;

    public string NotFoundMessage => "Antigravity CLI not found. Install it from https://antigravity.google/download.";

    public bool IsOneTurnPerProcess => true;

    public IReadOnlyList<string> AuthStatusArguments => ["--version"];

    public IReadOnlyList<string> LoginArguments => ["install"];

    public CliLogin.State ReadAuthState(string output, int exitCode) =>
        exitCode == 0 ? CliLogin.State.SignedIn : CliLogin.State.Unknown;

    public void ConfigureArguments(ProcessStartInfo psi, string mcpUrl, string agentSessionId, IReadOnlyList<string> mcpServers, bool resume, IReadOnlyList<ContentBlock> prompt)
    {
        psi.AddArgument("--input-format");
        psi.AddArgument("stream-json");

        psi.AddArgument("--output-format");
        psi.AddArgument("stream-json");

        psi.AddArgument("--dangerously-skip-permissions");

        string model = AISettings.EffectiveModel(Definition);
        if (!string.IsNullOrEmpty(model))
        {
            psi.AddArgument("--model");
            psi.AddArgument(model);
        }

        if (resume && !string.IsNullOrEmpty(agentSessionId))
        {
            psi.AddArgument("--conversation");
            psi.AddArgument(agentSessionId);
        }
    }

    public string FormatTurn(IReadOnlyList<ContentBlock> prompt)
    {
        StringBuilder builder = new();
        foreach (ContentBlock block in prompt)
        {
            if (block is TextContentBlock text && text.Text.Length > 0)
            {
                if (builder.Length > 0) builder.Append('\n');
                builder.Append(text.Text);
            }
        }

        var payload = new JsonObject
        {
            ["event"] = "user",
            ["message"] = new JsonObject
            {
                ["content"] = builder.ToString()
            }
        };

        return payload.ToJsonString();
    }

    public ParsedLine Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return ParsedLine.None;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;

            string eventType = root.TryGetProperty("event", out JsonElement ev)
                ? ev.GetString() ?? string.Empty
                : root.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;

            return eventType switch
            {
                "init" or "thread.started" => EmitSession(root),
                "step_update" => EmitStepUpdate(root),
                "text_delta" => EmitTextDelta(root),
                "tool_call" or "item.started" => EmitToolCall(root),
                "tool_result" or "item.completed" => EmitToolResult(root),
                "result" or "turn.completed" => EmitResult(root),
                "error" or "turn.failed" => EmitFailure(root),
                _ => EmitFallbackMessage(root)
            };
        }
        catch (JsonException)
        {
            return ParsedLine.None;
        }
    }

    private static ParsedLine EmitSession(JsonElement root)
    {
        if (root.TryGetProperty("conversation_id", out JsonElement id) && id.GetString() is { Length: > 0 } convId)
            return ParsedLine.Session(convId);
        if (root.TryGetProperty("thread_id", out JsonElement tid) && tid.GetString() is { Length: > 0 } threadId)
            return ParsedLine.Session(threadId);
        return ParsedLine.None;
    }

    private static ParsedLine EmitStepUpdate(JsonElement root)
    {
        if (!root.TryGetProperty("step_update", out JsonElement step) || step.ValueKind != JsonValueKind.Object)
            return ParsedLine.None;

        string stepType = step.TryGetProperty("step_type", out JsonElement st) ? st.GetString() ?? string.Empty : string.Empty;
        string state = step.TryGetProperty("state", out JsonElement s) ? s.GetString() ?? string.Empty : string.Empty;

        if (stepType is "agent_response" or "message")
        {
            if (step.TryGetProperty("text_delta", out JsonElement delta) && delta.GetString() is { Length: > 0 } text)
            {
                return ParsedLine.Emit(new AgentMessageChunkSessionUpdate
                {
                    Content = new TextContentBlock { Text = text }
                });
            }

            if (step.TryGetProperty("content", out JsonElement content) && content.GetString() is { Length: > 0 } fullText)
            {
                return ParsedLine.Emit(new AgentMessageChunkSessionUpdate
                {
                    Content = new TextContentBlock { Text = fullText }
                });
            }
        }

        if (stepType == "thinking")
        {
            if (step.TryGetProperty("text_delta", out JsonElement thinkDelta) && thinkDelta.GetString() is { Length: > 0 } thinkText)
            {
                return ParsedLine.Emit(new AgentMessageChunkSessionUpdate
                {
                    Content = new TextContentBlock { Text = thinkText }
                });
            }
        }

        if (stepType == "tool")
        {
            string toolName = step.TryGetProperty("tool_name", out JsonElement tn) ? tn.GetString() ?? "Tool" : "Tool";
            string toolCallId = step.TryGetProperty("step_index", out JsonElement idx) ? $"tool_{idx}" : Guid.NewGuid().ToString("N");

            if (state == "ACTIVE")
            {
                return ParsedLine.Emit(new ToolCallSessionUpdate
                {
                    ToolCallId = toolCallId,
                    Title = toolName,
                    RawInput = step.TryGetProperty("tool_info", out JsonElement info) ? info.Clone() : null
                });
            }

            if (state == "DONE")
            {
                return ParsedLine.Emit(new ToolCallUpdateSessionUpdate
                {
                    ToolCallId = toolCallId,
                    Status = ToolCallStatus.Completed,
                    RawOutput = step.TryGetProperty("tool_info", out JsonElement info) ? info.Clone() : null
                });
            }
        }

        return ParsedLine.None;
    }

    private static ParsedLine EmitTextDelta(JsonElement root)
    {
        if (root.TryGetProperty("text_delta", out JsonElement delta) && delta.GetString() is { Length: > 0 } text)
        {
            return ParsedLine.Emit(new AgentMessageChunkSessionUpdate
            {
                Content = new TextContentBlock { Text = text }
            });
        }

        if (root.TryGetProperty("message", out JsonElement msg) && msg.TryGetProperty("content", out JsonElement content) && content.GetString() is { Length: > 0 } msgText)
        {
            return ParsedLine.Emit(new AgentMessageChunkSessionUpdate
            {
                Content = new TextContentBlock { Text = msgText }
            });
        }

        return ParsedLine.None;
    }

    private static ParsedLine EmitToolCall(JsonElement root)
    {
        string id = root.TryGetProperty("tool_call_id", out JsonElement tid) ? tid.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
        string title = root.TryGetProperty("tool", out JsonElement tool) ? tool.GetString() ?? "Tool" : "Tool";

        return ParsedLine.Emit(new ToolCallSessionUpdate
        {
            ToolCallId = id,
            Title = title,
            RawInput = root.TryGetProperty("arguments", out JsonElement args) ? args.Clone() : null
        });
    }

    private static ParsedLine EmitToolResult(JsonElement root)
    {
        string id = root.TryGetProperty("tool_call_id", out JsonElement tid) ? tid.GetString() ?? string.Empty : string.Empty;
        if (string.IsNullOrEmpty(id)) return ParsedLine.None;

        return ParsedLine.Emit(new ToolCallUpdateSessionUpdate
        {
            ToolCallId = id,
            Status = ToolCallStatus.Completed,
            RawOutput = root.TryGetProperty("result", out JsonElement res) ? res.Clone() : null
        });
    }

    private static ParsedLine EmitResult(JsonElement root)
    {
        string status = root.TryGetProperty("status", out JsonElement s) ? s.GetString() ?? string.Empty : string.Empty;

        if (status is "SUCCESS" or "" && !root.TryGetProperty("error", out _))
        {
            TokenUsage usage = TokenUsage.Empty;

            if (root.TryGetProperty("result", out JsonElement res) && res.ValueKind == JsonValueKind.Object)
            {
                if (res.TryGetProperty("usage", out JsonElement u) && u.ValueKind == JsonValueKind.Object)
                {
                    int inTokens = u.TryGetProperty("input_tokens", out JsonElement inEl) ? inEl.GetInt32() : 0;
                    int outTokens = u.TryGetProperty("output_tokens", out JsonElement outEl) ? outEl.GetInt32() : 0;
                    usage = new TokenUsage(inTokens, outTokens, null);
                }

                string summaryText = string.Empty;
                if (res.TryGetProperty("summary", out JsonElement sumEl) && sumEl.GetString() is { Length: > 0 } sum)
                    summaryText = sum;
                else if (res.TryGetProperty("text", out JsonElement txtEl) && txtEl.GetString() is { Length: > 0 } txt)
                    summaryText = txt;
                else if (res.TryGetProperty("content", out JsonElement cntEl) && cntEl.GetString() is { Length: > 0 } cnt)
                    summaryText = cnt;

                if (!string.IsNullOrEmpty(summaryText))
                {
                    return ParsedLine.Emit(new AgentMessageChunkSessionUpdate
                    {
                        Content = new TextContentBlock { Text = summaryText }
                    });
                }
            }
            else if (root.TryGetProperty("usage", out JsonElement directUsage))
            {
                int inTokens = directUsage.TryGetProperty("input_tokens", out JsonElement inEl) ? inEl.GetInt32() : 0;
                int outTokens = directUsage.TryGetProperty("output_tokens", out JsonElement outEl) ? outEl.GetInt32() : 0;
                usage = new TokenUsage(inTokens, outTokens, null);
            }

            return ParsedLine.Complete(StopReason.EndTurn, usage);
        }

        string error = root.TryGetProperty("error", out JsonElement err) ? err.GetString() ?? "Unknown failure" : "Unknown failure";
        return ParsedLine.Failed(StopReason.Refusal, new AgentMessageChunkSessionUpdate
        {
            Content = new TextContentBlock { Text = $"Antigravity: {error}" }
        });
    }

    private static ParsedLine EmitFailure(JsonElement root)
    {
        string message = root.TryGetProperty("message", out JsonElement msg) ? msg.GetString() ?? "Operation failed" : "Operation failed";
        return ParsedLine.Failed(StopReason.Refusal, new AgentMessageChunkSessionUpdate
        {
            Content = new TextContentBlock { Text = $"Antigravity error: {message}" }
        });
    }

    private static ParsedLine EmitFallbackMessage(JsonElement root)
    {
        if (root.TryGetProperty("response", out JsonElement resp) && resp.GetString() is { Length: > 0 } text)
        {
            return ParsedLine.Emit(new AgentMessageChunkSessionUpdate
            {
                Content = new TextContentBlock { Text = text }
            });
        }
        return ParsedLine.None;
    }
}