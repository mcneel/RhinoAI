using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Acp;
using ContentBlock = Acp.ContentBlock;

namespace Rhino.AI;

internal sealed class AntigravityStreamJsonParser : IStreamJsonParser
{
    private AgentDefinition Definition { get; }
    private string SystemPrelude { get; }
    private bool IsResumed { get; set; }

    public AntigravityStreamJsonParser(AgentDefinition definition, string? systemPrelude = null)
    {
        Definition = definition;
        SystemPrelude = string.IsNullOrWhiteSpace(systemPrelude)
            ? GeminiConnection.BuildSystemPrompt(definition)
            : systemPrelude!;
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
        IsResumed = resume && !string.IsNullOrEmpty(agentSessionId);
        GeminiConnection.EnsureMcpConfigured(mcpUrl, mcpServers, SystemPrelude);
        psi.WorkingDirectory = GeminiConnection.PrepareWorkspace(SystemPrelude);

        psi.Environment["GEMINI_SYSTEM_PROMPT"] = SystemPrelude;
        psi.Environment["ENABLE_TOOL_SEARCH"] = "false";
        psi.Environment["MCP_TOOL_TIMEOUT"] = "3600000";
        psi.Environment["MCP_TIMEOUT"] = "3600000";

        psi.AddArgument("--input-format");
        psi.AddArgument("stream-json");

        psi.AddArgument("--output-format");
        psi.AddArgument("stream-json");

        psi.AddArgument("--dangerously-skip-permissions");
        psi.AddArgument("--disable-slash-commands");

        string model = AISettings.EffectiveModel(Definition);
        if (!string.IsNullOrEmpty(model))
        {
            // agy draws through its built-in image-generator subagent and rejects an image model on
            // --model. The picker no longer offers image models, but one saved from an older build may
            // still be in settings, so it falls back to a planner model instead of failing to launch.
            string cliModel = model.EndsWith("-image", StringComparison.OrdinalIgnoreCase)
                || model.Contains("nano-banana", StringComparison.OrdinalIgnoreCase)
                ? "gemini-3.8-flash-high"
                : model;
            psi.AddArgument("--model");
            psi.AddArgument(cliModel);
        }

        if (IsResumed)
        {
            psi.AddArgument("--conversation");
            psi.AddArgument(agentSessionId);
        }
    }

    public string FormatTurn(IReadOnlyList<ContentBlock> prompt) =>
        GeminiConnection.BuildTurnPayload(Definition, SystemPrelude, prompt, IsResumed);

    public ParsedLine Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return ParsedLine.None;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(line);
            JsonElement root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return ParsedLine.None;

            string eventType = root.TryGetProperty("event", out JsonElement ev)
                ? ev.GetString() ?? string.Empty
                : root.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;

            // Route any chunk carrying explicit functionCall blocks directly to the tool dispatcher
            // before falling through to text/message handlers so raw JSON never echoes to the panel.
            if (eventType is "functionCall" or "function_call" or "tool_call" or "mcp_tool_call" or "item.started")
                return EmitToolCall(root);

            if (eventType is "functionResponse" or "function_response" or "tool_result" or "item.completed")
                return EmitToolResult(root);

            if (TryExtractFunctionCallUpdates(root, out List<SessionUpdate> directToolCalls))
                return ParsedLine.Emit([.. directToolCalls]);

            return eventType switch
            {
                "init" or "thread.started" => EmitSession(root),
                "step_update" => EmitStepUpdate(root),
                "text_delta" or "assistant" or "message" => EmitTextDelta(root),
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

        if (TryExtractFunctionCallUpdates(step, out List<SessionUpdate> stepToolCalls))
            return ParsedLine.Emit([.. stepToolCalls]);

        string stepType = step.TryGetProperty("step_type", out JsonElement st) ? st.GetString() ?? string.Empty : string.Empty;
        string state = step.TryGetProperty("state", out JsonElement s) ? s.GetString() ?? string.Empty : string.Empty;

        if (stepType is "tool" or "tool_call" or "mcp_tool" or "subagent" or "functionCall" or "function_call")
        {
            string toolName = ReadToolName(step);
            string toolCallId = ReadToolCallId(step);

            if (state is "DONE" or "COMPLETED")
            {
                return ParsedLine.Emit(new ToolCallUpdateSessionUpdate
                {
                    ToolCallId = toolCallId,
                    Status = ToolCallStatus.Completed,
                    RawOutput = ReadToolOutput(step)
                });
            }

            if (state is "ERROR" or "FAILED")
            {
                return ParsedLine.Emit(new ToolCallUpdateSessionUpdate
                {
                    ToolCallId = toolCallId,
                    Status = ToolCallStatus.Failed,
                    RawOutput = ReadToolOutput(step)
                });
            }

            return ParsedLine.Emit(new ToolCallSessionUpdate
            {
                ToolCallId = toolCallId,
                Title = toolName,
                RawInput = ReadToolInput(step)
            });
        }

        // Suppress internal thinking chunks from echoing to the user-facing conversation panel.
        if (stepType == "thinking")
            return ParsedLine.None;

        if (stepType is "agent_response" or "message")
        {
            if (step.TryGetProperty("text_delta", out JsonElement delta) && delta.GetString() is { Length: > 0 } text)
                return EmitSanitizedText(text);

            if (step.TryGetProperty("content", out JsonElement content))
            {
                if (content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } fullText)
                    return EmitSanitizedText(fullText);
                if (content.ValueKind == JsonValueKind.Array && TryExtractFunctionCallUpdates(content, out List<SessionUpdate> arrayCalls))
                    return ParsedLine.Emit([.. arrayCalls]);
            }
        }

        return ParsedLine.None;
    }

    private static ParsedLine EmitTextDelta(JsonElement root)
    {
        if (TryExtractFunctionCallUpdates(root, out List<SessionUpdate> toolCalls))
            return ParsedLine.Emit([.. toolCalls]);

        if (root.TryGetProperty("text_delta", out JsonElement delta) && delta.GetString() is { Length: > 0 } text)
            return EmitSanitizedText(text);

        if (root.TryGetProperty("message", out JsonElement msg) && msg.ValueKind == JsonValueKind.Object)
        {
            if (TryExtractFunctionCallUpdates(msg, out List<SessionUpdate> msgToolCalls))
                return ParsedLine.Emit([.. msgToolCalls]);

            if (msg.TryGetProperty("content", out JsonElement content))
            {
                if (content.ValueKind == JsonValueKind.String && content.GetString() is { Length: > 0 } msgText)
                    return EmitSanitizedText(msgText);
                if (content.ValueKind == JsonValueKind.Array && TryExtractFunctionCallUpdates(content, out List<SessionUpdate> contentCalls))
                    return ParsedLine.Emit([.. contentCalls]);
            }
        }

        return ParsedLine.None;
    }

    private static ParsedLine EmitToolCall(JsonElement root)
    {
        if (TryExtractFunctionCallUpdates(root, out List<SessionUpdate> updates))
            return ParsedLine.Emit([.. updates]);

        JsonElement target = root.TryGetProperty("item", out JsonElement item) && item.ValueKind == JsonValueKind.Object
            ? item
            : root;

        return ParsedLine.Emit(new ToolCallSessionUpdate
        {
            ToolCallId = ReadToolCallId(target),
            Title = ReadToolName(target),
            RawInput = ReadToolInput(target)
        });
    }

    private static ParsedLine EmitToolResult(JsonElement root)
    {
        JsonElement target = root.TryGetProperty("functionResponse", out JsonElement fr) && fr.ValueKind == JsonValueKind.Object
            ? fr
            : root.TryGetProperty("function_response", out JsonElement fr2) && fr2.ValueKind == JsonValueKind.Object
                ? fr2
                : root.TryGetProperty("item", out JsonElement item) && item.ValueKind == JsonValueKind.Object
                    ? item
                    : root;

        string id = ReadToolCallId(target, allowGeneratedFallback: false);
        if (string.IsNullOrEmpty(id))
            id = ReadToolCallId(root, allowGeneratedFallback: false);
        if (string.IsNullOrEmpty(id))
            return ParsedLine.None;

        bool failed = (target.TryGetProperty("is_error", out JsonElement errEl) && errEl.ValueKind == JsonValueKind.True)
            || (target.TryGetProperty("status", out JsonElement stEl) && stEl.ValueKind == JsonValueKind.String && stEl.GetString() is "failed" or "error" or "ERROR");

        return ParsedLine.Emit(new ToolCallUpdateSessionUpdate
        {
            ToolCallId = id,
            Status = failed ? ToolCallStatus.Failed : ToolCallStatus.Completed,
            RawOutput = ReadToolOutput(target) ?? ReadToolOutput(root)
        });
    }

    private static ParsedLine EmitResult(JsonElement root)
    {
        string status = root.TryGetProperty("status", out JsonElement s) ? s.GetString() ?? string.Empty : string.Empty;

        if (status is "SUCCESS" or "" && !root.TryGetProperty("error", out _))
        {
            TokenUsage usage = TokenUsage.Empty;
            List<SessionUpdate> updates = [];

            if (root.TryGetProperty("result", out JsonElement res) && res.ValueKind == JsonValueKind.Object)
            {
                if (res.TryGetProperty("usage", out JsonElement u) && u.ValueKind == JsonValueKind.Object)
                {
                    int inTokens = u.TryGetProperty("input_tokens", out JsonElement inEl) && inEl.TryGetInt32(out int i) ? i : 0;
                    int outTokens = u.TryGetProperty("output_tokens", out JsonElement outEl) && outEl.TryGetInt32(out int o) ? o : 0;
                    usage = new TokenUsage(inTokens, outTokens, null);
                }

                if (TryExtractFunctionCallUpdates(res, out List<SessionUpdate> resToolCalls))
                {
                    updates.AddRange(resToolCalls);
                }
                else
                {
                    string summaryText = string.Empty;
                    if (res.TryGetProperty("summary", out JsonElement sumEl) && sumEl.GetString() is { Length: > 0 } sum)
                        summaryText = sum;
                    else if (res.TryGetProperty("text", out JsonElement txtEl) && txtEl.GetString() is { Length: > 0 } txt)
                        summaryText = txt;
                    else if (res.TryGetProperty("content", out JsonElement cntEl) && cntEl.ValueKind == JsonValueKind.String && cntEl.GetString() is { Length: > 0 } cnt)
                        summaryText = cnt;

                    if (!string.IsNullOrEmpty(summaryText))
                    {
                        ParsedLine sanitized = EmitSanitizedText(summaryText);
                        if (sanitized.Updates is { Count: > 0 })
                            updates.AddRange(sanitized.Updates);
                    }
                }
            }
            else if (root.TryGetProperty("usage", out JsonElement directUsage) && directUsage.ValueKind == JsonValueKind.Object)
            {
                int inTokens = directUsage.TryGetProperty("input_tokens", out JsonElement inEl) && inEl.TryGetInt32(out int i) ? i : 0;
                int outTokens = directUsage.TryGetProperty("output_tokens", out JsonElement outEl) && outEl.TryGetInt32(out int o) ? o : 0;
                usage = new TokenUsage(inTokens, outTokens, null);
            }

            return new ParsedLine(updates, true, StopReason.EndTurn, usage, null);
        }

        string error = root.TryGetProperty("error", out JsonElement err)
            ? (err.ValueKind == JsonValueKind.String ? err.GetString() ?? "Unknown failure" : err.GetRawText())
            : "Unknown failure";
        return ParsedLine.Failed(StopReason.Refusal, new AgentMessageChunkSessionUpdate
        {
            Content = new TextContentBlock { Text = $"Antigravity: {error}" }
        });
    }

    private static ParsedLine EmitFailure(JsonElement root)
    {
        string message = root.TryGetProperty("message", out JsonElement msg) && msg.ValueKind == JsonValueKind.String
            ? msg.GetString() ?? "Operation failed"
            : "Operation failed";
        return ParsedLine.Failed(StopReason.Refusal, new AgentMessageChunkSessionUpdate
        {
            Content = new TextContentBlock { Text = $"Antigravity error: {message}" }
        });
    }

    private static ParsedLine EmitFallbackMessage(JsonElement root)
    {
        if (TryExtractFunctionCallUpdates(root, out List<SessionUpdate> updates))
            return ParsedLine.Emit([.. updates]);

        if (root.TryGetProperty("response", out JsonElement resp) && resp.ValueKind == JsonValueKind.String && resp.GetString() is { Length: > 0 } text)
            return EmitSanitizedText(text);

        return ParsedLine.None;
    }

    // Ensures text chunks containing raw JSON functionCall payloads are demuxed into
    // ToolCallSessionUpdates rather than echoed as raw JSON to the conversation panel.
    private static ParsedLine EmitSanitizedText(string text)
    {
        string trimmed = text.Trim();
        if (trimmed.Length > 1 && (trimmed[0] == '{' || trimmed[0] == '['))
        {
            try
            {
                using JsonDocument embedded = JsonDocument.Parse(trimmed);
                if (TryExtractFunctionCallUpdates(embedded.RootElement, out List<SessionUpdate> toolUpdates))
                    return ParsedLine.Emit([.. toolUpdates]);

                if (embedded.RootElement.ValueKind == JsonValueKind.Object &&
                    (embedded.RootElement.TryGetProperty("functionCall", out _) ||
                     embedded.RootElement.TryGetProperty("function_call", out _) ||
                     embedded.RootElement.TryGetProperty("tool_call", out _)))
                {
                    return EmitToolCall(embedded.RootElement);
                }
            }
            catch (JsonException)
            {
                // Not valid standalone JSON; check if it's a raw unparseable functionCall fragment
                if (trimmed.Contains("\"functionCall\"", StringComparison.Ordinal) ||
                    trimmed.Contains("\"function_call\"", StringComparison.Ordinal))
                {
                    return ParsedLine.None;
                }
            }
        }

        // Drop intermediate subagent wait notes or meta-filler emitted while background generators run.
        if (trimmed.StartsWith("Waiting for subagent", StringComparison.OrdinalIgnoreCase) ||
            trimmed.IndexOf("(Turn ended silently)", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            string cleaned = System.Text.RegularExpressions.Regex.Replace(
                trimmed,
                @"(Waiting for subagent completion\.{0,3}|\*?\(Turn ended silently\)\*?)",
                string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
            if (cleaned.Length == 0)
                return ParsedLine.None;
            text = cleaned;
        }

        return ParsedLine.Emit(new AgentMessageChunkSessionUpdate
        {
            Content = new TextContentBlock { Text = text }
        });
    }

    private static bool TryExtractFunctionCallUpdates(JsonElement element, out List<SessionUpdate> updates)
    {
        updates = [];

        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                if (TryExtractFunctionCallUpdates(item, out List<SessionUpdate> nested))
                    updates.AddRange(nested);
            }
            return updates.Count > 0;
        }

        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (element.TryGetProperty("functionCall", out JsonElement fc) && fc.ValueKind == JsonValueKind.Object)
        {
            updates.Add(BuildFunctionCallUpdate(fc, element));
            return true;
        }

        if (element.TryGetProperty("function_call", out JsonElement fcSnake) && fcSnake.ValueKind == JsonValueKind.Object)
        {
            updates.Add(BuildFunctionCallUpdate(fcSnake, element));
            return true;
        }

        if (element.TryGetProperty("functionCalls", out JsonElement fcArray) && fcArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in fcArray.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    JsonElement inner = item.TryGetProperty("functionCall", out JsonElement nestedFc) && nestedFc.ValueKind == JsonValueKind.Object
                        ? nestedFc
                        : item;
                    updates.Add(BuildFunctionCallUpdate(inner, item));
                }
            }
            if (updates.Count > 0)
                return true;
        }

        if (element.TryGetProperty("tool_calls", out JsonElement tcArray) && tcArray.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in tcArray.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    JsonElement inner = item.TryGetProperty("function", out JsonElement fn) && fn.ValueKind == JsonValueKind.Object
                        ? fn
                        : item;
                    updates.Add(BuildFunctionCallUpdate(inner, item));
                }
            }
            if (updates.Count > 0)
                return true;
        }

        if (element.TryGetProperty("parts", out JsonElement parts) && parts.ValueKind == JsonValueKind.Array &&
            TryExtractFunctionCallUpdates(parts, out List<SessionUpdate> partUpdates))
        {
            updates.AddRange(partUpdates);
            return true;
        }

        if (element.TryGetProperty("content", out JsonElement content))
        {
            if (content.ValueKind is JsonValueKind.Object or JsonValueKind.Array &&
                TryExtractFunctionCallUpdates(content, out List<SessionUpdate> contentUpdates))
            {
                updates.AddRange(contentUpdates);
                return true;
            }
        }

        if (element.TryGetProperty("candidates", out JsonElement candidates) && candidates.ValueKind == JsonValueKind.Array &&
            TryExtractFunctionCallUpdates(candidates, out List<SessionUpdate> candidateUpdates))
        {
            updates.AddRange(candidateUpdates);
            return true;
        }

        return false;
    }

    private static ToolCallSessionUpdate BuildFunctionCallUpdate(JsonElement fc, JsonElement parent)
    {
        string id = ReadToolCallId(fc, allowGeneratedFallback: false);
        if (string.IsNullOrEmpty(id))
            id = ReadToolCallId(parent, allowGeneratedFallback: true);

        string name = ReadToolName(fc);
        if (name == "Tool")
            name = ReadToolName(parent);

        JsonElement? rawInput = ReadToolInput(fc) ?? ReadToolInput(parent);

        return new ToolCallSessionUpdate
        {
            ToolCallId = id,
            Title = name,
            RawInput = rawInput
        };
    }

    private static JsonElement UnwrapToolInfo(JsonElement obj) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty("tool_info", out JsonElement info) && info.ValueKind == JsonValueKind.Object
            ? info
            : obj;

    private static string ReadToolName(JsonElement obj)
    {
        JsonElement target = UnwrapToolInfo(obj);
        string rawName = ReadDirectName(target);
        if (rawName.Length == 0 && !target.Equals(obj))
            rawName = ReadDirectName(obj);

        if (TryReadRawParameters(target, out JsonElement parms) || (!target.Equals(obj) && TryReadRawParameters(obj, out parms)))
        {
            if (TryGetStringProperty(parms, "ToolName", out string mcpTool) ||
                TryGetStringProperty(parms, "tool_name", out mcpTool) ||
                (rawName == "call_mcp_tool" && TryGetStringProperty(parms, "name", out mcpTool)))
            {
                return mcpTool;
            }
        }

        return rawName.Length > 0 ? rawName : "Tool";
    }

    private static string ReadDirectName(JsonElement obj)
    {
        if (obj.ValueKind != JsonValueKind.Object)
            return string.Empty;

        foreach (string key in new[] { "name", "tool_name", "tool", "function" })
        {
            if (obj.TryGetProperty(key, out JsonElement el) && el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } s)
                return s;
        }
        return string.Empty;
    }

    private static string ReadToolCallId(JsonElement obj, bool allowGeneratedFallback = true)
    {
        foreach (string key in new[] { "tool_call_id", "id", "call_id", "tool_use_id" })
        {
            if (obj.TryGetProperty(key, out JsonElement el) && el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } s)
                return s;
        }
        if (obj.TryGetProperty("step_index", out JsonElement idx))
        {
            return idx.ValueKind switch
            {
                JsonValueKind.Number => $"tool_{idx.GetRawText()}",
                JsonValueKind.String when idx.GetString() is { Length: > 0 } s => $"tool_{s}",
                _ => allowGeneratedFallback ? Guid.NewGuid().ToString("N") : string.Empty
            };
        }
        return allowGeneratedFallback ? Guid.NewGuid().ToString("N") : string.Empty;
    }

    private static JsonElement? ReadToolInput(JsonElement obj)
    {
        JsonElement target = UnwrapToolInfo(obj);
        if (!TryReadRawParameters(target, out JsonElement parms) && (target.Equals(obj) || !TryReadRawParameters(obj, out parms)))
            return null;

        if (parms.ValueKind == JsonValueKind.Object &&
            (parms.TryGetProperty("Arguments", out JsonElement mcpArgs) || parms.TryGetProperty("arguments", out mcpArgs)) &&
            (parms.TryGetProperty("ToolName", out _) || parms.TryGetProperty("tool_name", out _) || parms.TryGetProperty("ServerName", out _)))
        {
            return CloneOrParseJson(mcpArgs);
        }

        return parms.Clone();
    }

    private static bool TryReadRawParameters(JsonElement obj, out JsonElement parms)
    {
        parms = default;
        if (obj.ValueKind != JsonValueKind.Object)
            return false;

        foreach (string key in new[] { "args", "arguments", "input", "parameters", "subagent_info" })
        {
            if (obj.TryGetProperty(key, out JsonElement el) && el.ValueKind != JsonValueKind.Null && el.ValueKind != JsonValueKind.Undefined)
            {
                parms = CloneOrParseJson(el);
                return true;
            }
        }
        return false;
    }

    private static JsonElement CloneOrParseJson(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } rawJson)
        {
            string trimmed = rawJson.Trim();
            if (trimmed.Length > 1 && (trimmed[0] == '{' || trimmed[0] == '['))
            {
                try
                {
                    using JsonDocument parsed = JsonDocument.Parse(trimmed);
                    return parsed.RootElement.Clone();
                }
                catch (JsonException)
                {
                }
            }
        }
        return el.Clone();
    }

    private static bool TryGetStringProperty(JsonElement obj, string propName, out string value)
    {
        if (obj.ValueKind == JsonValueKind.Object &&
            obj.TryGetProperty(propName, out JsonElement el) &&
            el.ValueKind == JsonValueKind.String &&
            el.GetString() is { Length: > 0 } s)
        {
            value = s;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static JsonElement? ReadToolOutput(JsonElement obj)
    {
        JsonElement target = UnwrapToolInfo(obj);
        if (TryReadDirectOutput(target, out JsonElement output))
            return output;
        if (!target.Equals(obj) && TryReadDirectOutput(obj, out output))
            return output;
        return null;
    }

    private static bool TryReadDirectOutput(JsonElement obj, out JsonElement output)
    {
        output = default;
        if (obj.ValueKind != JsonValueKind.Object)
            return false;

        foreach (string key in new[] { "output", "result", "response", "content", "error", "subagent_info" })
        {
            if (obj.TryGetProperty(key, out JsonElement el) && el.ValueKind != JsonValueKind.Null && el.ValueKind != JsonValueKind.Undefined)
            {
                output = CloneOrParseJson(el);
                return true;
            }
        }
        return false;
    }
}