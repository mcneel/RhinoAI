using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Acp;
using ContentBlock = Acp.ContentBlock; // disambiguate from Rhino.AI.Server.ContentBlock

namespace Rhino.AI;

// Verified against codex-cli 0.153.4. Static settings live in CODEX_HOME's config.toml, not in flags, because `codex exec resume` takes neither --approve-for-me nor --profile: a flag-based setup works on turn one and silently stops on turn two.
internal sealed class CodexStreamJsonParser : IStreamJsonParser
{
    private AgentDefinition Definition { get; }
    private string CodexHome { get; }

    public CodexStreamJsonParser(AgentDefinition definition, string codexHome)
    {
        Definition = definition;
        CodexHome = codexHome;
    }

    public string DisplayName => Definition.Name;

    public string NotFoundMessage => "Codex CLI not found. Install Codex (npm i -g @openai/codex).";

    public bool IsOneTurnPerProcess => true;

    public IReadOnlyList<string> AuthStatusArguments => ["login", "status"];

    public IReadOnlyList<string> LoginArguments => ["login"];

    // `codex login status` answers in prose ("Logged in using ChatGPT"), so the negative is tested
    // first: "not logged in" contains "logged in".
    public CliLogin.State ReadAuthState(string output, int exitCode)
    {
        if (output.Contains("not logged in", StringComparison.OrdinalIgnoreCase))
            return CliLogin.State.SignedOut;
        if (exitCode == 0 && output.Contains("logged in", StringComparison.OrdinalIgnoreCase))
            return CliLogin.State.SignedIn;
        return CliLogin.State.Unknown;
    }

    public void ConfigureArguments(ProcessStartInfo psi, string mcpUrl, string agentSessionId, IReadOnlyList<string> mcpServers, bool resume)
    {
        psi.Environment["CODEX_HOME"] = CodexHome;

        psi.AddArgument("exec");
        if (resume)
        {
            psi.AddArgument("resume");
            psi.AddArgument(agentSessionId);
        }

        psi.AddArgument("--json");
        psi.AddArgument("--skip-git-repo-check");

        psi.AddArgument("-c");
        psi.AddArgument($"mcp_servers.rhino.url={EncodeString(mcpUrl)}");

        foreach (string entry in mcpServers)
        {
            if (JsonNode.Parse(entry) is not JsonObject servers)
                continue;
            foreach (KeyValuePair<string, JsonNode?> server in servers)
            {
                if (server.Key == "rhino" || server.Value is not JsonObject config)
                    continue;
                foreach (KeyValuePair<string, JsonNode?> field in config)
                    if (field.Value is JsonValue value)
                    {
                        psi.AddArgument("-c");
                        psi.AddArgument($"mcp_servers.{server.Key}.{field.Key}={EncodeValue(value)}");
                    }
                // Without this every call to a user-added server dies on "approval policy is never", the same pre-approval the shipped config gives rhino.
                psi.AddArgument("-c");
                psi.AddArgument($"mcp_servers.{server.Key}.default_tools_approval_mode=\"approve\"");
            }
        }

        psi.AddArgument("-c");
        psi.AddArgument($"developer_instructions={EncodeString(AgentPrompts.Compose(AISettings.EffectivePrompt(Definition)))}");

        if (AISettings.EffectiveModel(Definition) is { Length: > 0 } model)
        {
            psi.AddArgument("-m");
            psi.AddArgument(model);
        }
        // foreach (string arg in Definition.ExtraArgs)
        //     psi.AddArgument(arg);

        psi.AddArgument("-"); // the positional PROMPT, so it has to stay last
    }

    private static string EncodeValue(JsonValue value) =>
        value.TryGetValue(out string? text) ? EncodeString(text) : value.ToJsonString();

    private static string EncodeString(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    public string FormatTurn(IReadOnlyList<ContentBlock> prompt)
    {
        StringBuilder builder = new();
        foreach (ContentBlock block in prompt)
        {
            string piece = block switch
            {
                TextContentBlock text => text.Text,
                ImageContentBlock => "[image omitted: this agent has no inline-image support]",
                _ => string.Empty,
            };
            if (piece.Length == 0)
                continue;
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(piece);
        }
        return builder.ToString();
    }

    public ParsedLine Parse(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("type", out JsonElement typeEl))
            return ParsedLine.None;

        // Unknown types fail soft: turn.started, item.updated, reasoning and command/web items all land here, and a new one must never fault the turn.
        return typeEl.GetString() switch
        {
            "thread.started" => EmitSession(root),
            "item.started" => EmitItemStarted(root),
            "item.completed" => EmitItemCompleted(root),
            "turn.completed" => ParsedLine.Complete(StopReason.EndTurn, ReadUsage(root)),
            "turn.failed" => EmitTurnFailed(root),
            _ => ParsedLine.None,
        };
    }

    private static ParsedLine EmitSession(JsonElement root) =>
        TryStr(root, "thread_id", out string threadId) ? ParsedLine.Session(threadId) : ParsedLine.None;

    private static ParsedLine EmitItemStarted(JsonElement root)
    {
        if (!TryItem(root, out JsonElement item) || Str(item, "type") != "mcp_tool_call")
            return ParsedLine.None;
        if (!TryStr(item, "id", out string toolCallId))
            return ParsedLine.None;

        return ParsedLine.Emit(new ToolCallSessionUpdate
        {
            ToolCallId = toolCallId,
            Title = Str(item, "tool"),
            RawInput = item.TryGetProperty("arguments", out JsonElement args) ? args.Clone() : null,
        });
    }

    private ParsedLine EmitItemCompleted(JsonElement root)
    {
        if (!TryItem(root, out JsonElement item))
            return ParsedLine.None;

        return Str(item, "type") switch
        {
            "agent_message" => EmitAssistant(item),
            "mcp_tool_call" => EmitToolResult(item),
            "error" => EmitNote(Str(item, "message")),
            _ => ParsedLine.None,
        };
    }

    private ParsedLine EmitTurnFailed(JsonElement root) =>
        ReadFailureMessage(root) is { Length: > 0 } message
            ? ParsedLine.Failed(StopReason.Refusal, Note($"{DisplayName} failed: {message}"))
            : ParsedLine.Complete(StopReason.Refusal);

    private ParsedLine EmitNote(string message) =>
        message.Length > 0 ? ParsedLine.Emit(Note($"{DisplayName}: {message}")) : ParsedLine.None;

    private static AgentMessageChunkSessionUpdate Note(string text) =>
        new() { Content = new TextContentBlock { Text = text } };

    private static string ReadFailureMessage(JsonElement root)
    {
        string raw = root.TryGetProperty("error", out JsonElement error) && error.ValueKind == JsonValueKind.Object
            ? Str(error, "message")
            : Str(root, "message");
        return Unwrap(raw);
    }

    // Codex nests the upstream API error as an escaped JSON string, which is unreadable in a transcript.
    private static string Unwrap(string text)
    {
        if (text.Length == 0 || text[0] != '{')
            return text;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("error", out JsonElement inner)
                && inner.ValueKind == JsonValueKind.Object
                && inner.TryGetProperty("message", out JsonElement message)
                && message.ValueKind == JsonValueKind.String)
                return message.GetString() ?? text;
        }
        catch (JsonException)
        {
        }

        return text;
    }

    private static ParsedLine EmitAssistant(JsonElement item) =>
        TryStr(item, "text", out string text)
            ? ParsedLine.Emit(new AgentMessageChunkSessionUpdate { Content = new TextContentBlock { Text = text } })
            : ParsedLine.None;

    private static ParsedLine EmitToolResult(JsonElement item)
    {
        if (!TryStr(item, "id", out string toolCallId))
            return ParsedLine.None;

        // A failure carries `error` where a success carries `result`.
        bool failed = Str(item, "status") != "completed";
        JsonElement? output = item.TryGetProperty(failed ? "error" : "result", out JsonElement payload) && payload.ValueKind != JsonValueKind.Null
            ? payload.Clone()
            : null;

        return ParsedLine.Emit(new ToolCallUpdateSessionUpdate
        {
            ToolCallId = toolCallId,
            Status = failed ? ToolCallStatus.Failed : ToolCallStatus.Completed,
            RawOutput = output,
        });
    }

    private static TokenUsage ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
            return TokenUsage.Empty;
        return new TokenUsage(ReadInt(usage, "input_tokens"), ReadInt(usage, "output_tokens"), null);
    }

    private static int ReadInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int v) ? v : 0;

    private static bool TryItem(JsonElement root, out JsonElement item) =>
        root.TryGetProperty("item", out item) && item.ValueKind == JsonValueKind.Object;

    private static string Str(JsonElement obj, string name) =>
        TryStr(obj, name, out string value) ? value : string.Empty;

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
