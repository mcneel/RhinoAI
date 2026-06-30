using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Acp;
using ContentBlock = Acp.ContentBlock; // disambiguate from RhMcp.Server.ContentBlock

namespace RhMcp;

// The Claude Code stream-json strategy: turns one `claude` stdout line into ACP session/update
// events and frames one user turn for stdin. Owns no process/threads/turn gating (the runner does);
// stays RhinoApp- and AISettings-free so it Compile Include's into a test project. Model, extra args
// and the system prompt come from the ctor-injected Definition; the MCP server set is resolved by
// the runner and handed in per spawn.
internal sealed class ClaudeStreamJsonParser : IStreamJsonParser
{
    private AgentDefinition Definition { get; }

    public ClaudeStreamJsonParser(AgentDefinition definition)
    {
        Definition = definition;
    }

    public string DisplayName => Definition.Name;

    public string NotFoundMessage => "Claude CLI not found. Install Claude Code (claude.ai/install).";

    public void ConfigureArguments(ProcessStartInfo psi, string mcpUrl, string agentSessionId, IReadOnlyList<string> mcpServers, bool resume)
    {
        // Same {"mcpServers":{...}} shape Claude Code expects; rhino points at this doc's HTTP
        // listener (not the router) so the agent always operates on the exact doc. Extra servers the
        // runner resolved are merged in, but never the reserved 'rhino' key.
        JsonObject servers = new()
        {
            ["rhino"] = new JsonObject { ["type"] = "http", ["url"] = mcpUrl },
        };
        foreach (string entry in mcpServers)
            if (JsonNode.Parse(entry) is JsonObject obj)
                foreach (KeyValuePair<string, JsonNode?> kvp in obj)
                    if (kvp.Key != "rhino" && kvp.Value is JsonNode node)
                        servers[kvp.Key] = node.DeepClone();

        string mcpConfig = new JsonObject { ["mcpServers"] = servers }.ToJsonString(McpSerializer.Options);

        // Raise Claude Code's per-tool-call timeout to one hour so a genuinely slow tool (a heavy
        // geometry op, a long script) isn't aborted at Claude's 60s default. MCP_TOOL_TIMEOUT is read
        // as milliseconds and clamped [1000, int.MaxValue]; MCP_TIMEOUT covers MCP server
        // startup/handshake. (ask_user no longer needs this: it returns immediately and the answer
        // arrives as the next prompt, so it never holds a tool call open.)
        psi.Environment["MCP_TOOL_TIMEOUT"] = "3600000";
        psi.Environment["MCP_TIMEOUT"] = "3600000";

        psi.ArgumentList.Add("-p");
        psi.ArgumentList.Add("--input-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--output-format");
        psi.ArgumentList.Add("stream-json");
        psi.ArgumentList.Add("--verbose");
        psi.ArgumentList.Add("--mcp-config");
        psi.ArgumentList.Add(mcpConfig);
        psi.ArgumentList.Add("--strict-mcp-config");
        psi.ArgumentList.Add("--allowedTools");
        psi.ArgumentList.Add("mcp__rhino*");
        psi.ArgumentList.Add("--append-system-prompt");
        psi.ArgumentList.Add(AgentPrompts.Compose(Definition.SystemPrompt));
        psi.ArgumentList.Add("--disable-slash-commands");
        psi.ArgumentList.Add(resume ? "--resume" : "--session-id");
        psi.ArgumentList.Add(agentSessionId);

        if (Definition.Model.Length > 0)
        {
            psi.ArgumentList.Add("--model");
            psi.ArgumentList.Add(Definition.Model);
        }
        foreach (string arg in Definition.ExtraArgs)
            psi.ArgumentList.Add(arg);
    }

    // ACP content blocks -> Claude's stream-json user content (text + base64 image). Underscored
    // property names survive the camelCase policy, so media_type stays media_type.
    public string FormatTurn(IReadOnlyList<ContentBlock> prompt)
    {
        List<object> content = [];
        foreach (ContentBlock block in prompt)
        {
            switch (block)
            {
                case TextContentBlock text:
                    content.Add(new { type = "text", text = text.Text });
                    break;
                case ImageContentBlock image:
                    content.Add(new
                    {
                        type = "image",
                        source = new { type = "base64", media_type = image.MimeType, data = image.Data },
                    });
                    break;
            }
        }

        return JsonSerializer.Serialize(new
        {
            type = "user",
            message = new { role = "user", content },
        }, McpSerializer.Options);
    }

    public ParsedLine Parse(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("type", out JsonElement typeEl))
            return ParsedLine.None;

        // Unknown event types fall through to ParsedLine.None on purpose: stream-json carries event
        // kinds we do not translate (system, etc.), and a new one must never fault the turn. This is
        // a deliberate fail-soft, not an unhandled-case discard.
        return typeEl.GetString() switch
        {
            "assistant" => EmitAssistant(root),
            "user" => EmitToolResults(root),
            "result" => ParsedLine.Complete(StopReason.EndTurn, ReadUsage(root)),
            _ => ParsedLine.None,
        };
    }

    // The `result` event carries the turn's accounting: a `usage` object with input/output token
    // counts (cache_* fields ignored for the headline number) and a top-level `total_cost_usd`.
    // Every field is best-effort: a result without usage degrades to TokenUsage.Empty rather than
    // faulting the turn.
    private static TokenUsage ReadUsage(JsonElement root)
    {
        int input = 0, output = 0;
        if (root.TryGetProperty("usage", out JsonElement usage) && usage.ValueKind == JsonValueKind.Object)
        {
            input = ReadInt(usage, "input_tokens");
            output = ReadInt(usage, "output_tokens");
        }
        decimal? cost = root.TryGetProperty("total_cost_usd", out JsonElement c) && c.ValueKind == JsonValueKind.Number && c.TryGetDecimal(out decimal d)
            ? d
            : null;
        return new TokenUsage(input, output, cost);
    }

    private static int ReadInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int v) ? v : 0;

    private static ParsedLine EmitAssistant(JsonElement root)
    {
        if (!TryGetContent(root, out JsonElement content))
            return ParsedLine.None;

        List<SessionUpdate> updates = [];
        foreach (JsonElement block in content.EnumerateArray())
        {
            switch (Str(block, "type"))
            {
                case "text" when block.TryGetProperty("text", out JsonElement text):
                    updates.Add(new AgentMessageChunkSessionUpdate { Content = new TextContentBlock { Text = text.GetString() ?? string.Empty } });
                    break;
                // No id means the later tool_result can never correlate back to this chip, so skip the
                // tool-call rather than emit a phantom keyed on "" (the absence sentinel, not a real id).
                case "tool_use" when TryStr(block, "id", out string toolCallId):
                    updates.Add(new ToolCallSessionUpdate
                    {
                        ToolCallId = toolCallId,
                        Title = Str(block, "name"),
                        // Clone so the element outlives the `using JsonDocument` above.
                        RawInput = block.TryGetProperty("input", out JsonElement input) ? input.Clone() : null,
                    });
                    break;
            }
        }
        return updates.Count > 0 ? ParsedLine.Emit([.. updates]) : ParsedLine.None;
    }

    private static ParsedLine EmitToolResults(JsonElement root)
    {
        if (!TryGetContent(root, out JsonElement content))
            return ParsedLine.None;

        List<SessionUpdate> updates = [];
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (Str(block, "type") != "tool_result")
                continue;
            // Without tool_use_id there is no chip to fold this output into (SetToolResult drops an
            // empty id anyway), so skip rather than emit an update keyed on the "" absence sentinel.
            if (!TryStr(block, "tool_use_id", out string toolCallId))
                continue;
            updates.Add(new ToolCallUpdateSessionUpdate
            {
                ToolCallId = toolCallId,
                Status = ToolCallStatus.Completed,
                // Clone so the element outlives the `using JsonDocument` above.
                RawOutput = block.TryGetProperty("content", out JsonElement output) ? output.Clone() : null,
            });
        }
        return updates.Count > 0 ? ParsedLine.Emit([.. updates]) : ParsedLine.None;
    }

    private static bool TryGetContent(JsonElement root, out JsonElement content)
    {
        content = default;
        return root.TryGetProperty("message", out JsonElement message)
            && message.TryGetProperty("content", out content)
            && content.ValueKind == JsonValueKind.Array;
    }

    // JSON-boundary string read. Str collapses the TryGetProperty+GetString+?? spray to one place for
    // display-only fields (a missing/non-string value is "" and that is fine downstream). TryStr is for
    // fields where absence is meaningful (tool ids): false means "not present", never a silent "".
    private static string Str(JsonElement obj, string name) => TryStr(obj, name, out string value) ? value : string.Empty;

    private static bool TryStr(JsonElement obj, string name, out string value)
    {
        if (obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.String && el.GetString() is { Length: > 0 } s)
        {
            value = s;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
