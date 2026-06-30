using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Acp;
using ContentBlock = Acp.ContentBlock; // disambiguate from RhMcp.Server.ContentBlock

namespace RhMcp;

// The Codex CLI (`codex exec`) stream-json strategy: turns one Codex stdout line into ACP
// session/update events and frames one user turn for stdin. Owns no process/threads/turn gating (the
// runner does); stays RhinoApp- and AISettings-free so it Compile Include's into a test project. Model,
// extra args and the system prompt come from the ctor-injected Definition; the MCP server set is
// resolved by the runner and handed in per spawn.
//
// CONTRACT NOTE (deferred live check): the Codex CLI is not present in this environment, so the
// launch/parse shape below is correct-by-construction and headlessly tested, NOT validated against the
// shipped binary. Each formerly-doubtful flag/field is now a single documented default, sourced inline.
// If the installed `codex` differs, only ConfigureArguments here changes; the runner and ACP seam are
// untouched. The defaults to confirm on a real machine: `--experimental-json`,
// `-c mcp_servers.rhino.{url,type,tool_timeout_sec}`, `experimental_instructions`,
// `--resume`/`--session-id`.
internal sealed class CodexStreamJsonParser : IStreamJsonParser
{
    private AgentDefinition Definition { get; }

    public CodexStreamJsonParser(AgentDefinition definition)
    {
        Definition = definition;
    }

    public string DisplayName => Definition.Name;

    public string NotFoundMessage => "Codex CLI not found. Install Codex (npm i -g @openai/codex).";

    public void ConfigureArguments(ProcessStartInfo psi, string mcpUrl, string agentSessionId, IReadOnlyList<string> mcpServers, bool resume)
    {
        psi.ArgumentList.Add("exec"); // non-interactive run; prompt arrives on stdin
        // Codex emits its JSON event stream under --experimental-json (the event envelope is {msg:{type}}).
        psi.ArgumentList.Add("--experimental-json");
        psi.ArgumentList.Add("-"); // read the prompt from stdin

        // Register Rhino as an MCP server via -c config override; Codex's [mcp_servers] table takes a
        // streamable-http server keyed by name, with .url and .type ("http"). rhino points at this doc's
        // HTTP listener (not the router) so the agent always operates on the exact doc.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"mcp_servers.rhino.url=\"{mcpUrl}\"");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("mcp_servers.rhino.type=\"http\"");

        // Raise the rhino server's per-tool-call timeout to one hour so a genuinely slow tool (a heavy
        // geometry op, a long script) isn't aborted at a short default. Codex's [mcp_servers.<name>]
        // table takes tool_timeout_sec (SECONDS, unlike Claude's MCP_TOOL_TIMEOUT milliseconds), set
        // here via -c on the rhino server. (ask_user no longer needs this: it returns immediately and
        // the answer arrives as the next prompt, so it never holds a tool call open.) GAP: Codex
        // exposes no MCP-tool-timeout ENV var to mirror, and the CLI is not present in this
        // environment, so the key name/units are correct-by-construction (see the CONTRACT NOTE
        // above), not validated against the shipped binary.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("mcp_servers.rhino.tool_timeout_sec=3600");

        // Extra servers the runner resolved (a JSON-object string of name -> server-config), translated
        // to -c overrides beside rhino. rhino is never overwritten: those tools are the agent's hands on
        // this exact doc. Each server's fields become -c mcp_servers.<name>.<key>=<value>.
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
                        psi.ArgumentList.Add("-c");
                        psi.ArgumentList.Add($"mcp_servers.{server.Key}.{field.Key}={EncodeValue(value)}");
                    }
            }
        }

        // The composed system prompt (shared ask_user steer + this agent's SystemPrompt) goes through
        // Codex's experimental_instructions -c key. Always non-empty because the steer is always present.
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"experimental_instructions={EncodeString(AgentPrompts.Compose(Definition.SystemPrompt))}");

        // Resume the same conversation across respawns (--resume) rather than re-opening fresh
        // (--session-id), keyed off the runner's sticky resume flag.
        psi.ArgumentList.Add(resume ? "--resume" : "--session-id");
        psi.ArgumentList.Add(agentSessionId);

        // Built-in defaults set Model="" / ExtraArgs=[], so these append nothing; custom entries layer
        // their model/args on top via the model -c key.
        if (Definition.Model.Length > 0)
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"model=\"{Definition.Model}\"");
        }
        foreach (string arg in Definition.ExtraArgs)
            psi.ArgumentList.Add(arg);
    }

    // A Codex -c value: strings get TOML-style double-quoting; numbers/bools pass through bare.
    private static string EncodeValue(JsonValue value) =>
        value.TryGetValue(out string? text) ? EncodeString(text) : value.ToJsonString();

    private static string EncodeString(string text) =>
        "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    // Codex `exec -` consumes a plain-text prompt from stdin (no JSON envelope). The agent has no
    // filesystem access, so text files are inlined fenced; images degrade to a short note since this path
    // has no inline-image support.
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

    // Codex frames each event as a top-level object carrying a `msg` payload whose `type` names the
    // event (session_configured, agent_message, mcp_tool_call, task_complete, etc.).
    public ParsedLine Parse(string line)
    {
        using JsonDocument doc = JsonDocument.Parse(line);
        JsonElement root = doc.RootElement;

        // Events nest under `msg`; fall back to root for shapes that carry `type` directly.
        JsonElement msg = root.TryGetProperty("msg", out JsonElement m) ? m : root;
        if (!msg.TryGetProperty("type", out JsonElement typeEl))
            return ParsedLine.None;

        // Unknown event types fall through to ParsedLine.None on purpose: Codex carries event kinds we
        // do not translate, and a new one must never fault the turn. Deliberate fail-soft, not a discard.
        // session_configured is intentionally None: session-start is the runner's NoteSessionStarted
        // concern, not the parser's.
        return typeEl.GetString() switch
        {
            "agent_message" => EmitAssistant(msg),
            "mcp_tool_call" => EmitToolCall(msg),
            "task_complete" => ParsedLine.Complete(StopReason.EndTurn, ReadUsage(msg)), // the terminal event ends the turn
            _ => ParsedLine.None,
        };
    }

    // CONTRACT NOTE (deferred live check): Codex's token accounting field on task_complete is not
    // validated against the shipped binary. The default assumed here is a `usage` object with
    // input_tokens/output_tokens; cost is not reported by Codex, so it stays null (tokens only).
    // Best-effort: a task_complete without usage degrades to TokenUsage.Empty, never faulting the turn.
    private static TokenUsage ReadUsage(JsonElement msg)
    {
        if (!msg.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
            return TokenUsage.Empty;
        return new TokenUsage(ReadInt(usage, "input_tokens"), ReadInt(usage, "output_tokens"), null);
    }

    private static int ReadInt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out JsonElement el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int v) ? v : 0;

    // Assistant text rides directly on the event under `message`.
    private static ParsedLine EmitAssistant(JsonElement msg) =>
        TryStr(msg, "message", out string text)
            ? ParsedLine.Emit(new AgentMessageChunkSessionUpdate { Content = new TextContentBlock { Text = text } })
            : ParsedLine.None;

    // A tool call carries the invoked tool name under `tool`; it doubles as the correlation id, so an
    // absent name skips the update rather than emitting a chip keyed on "".
    private static ParsedLine EmitToolCall(JsonElement msg) =>
        TryStr(msg, "tool", out string name)
            ? ParsedLine.Emit(new ToolCallSessionUpdate { ToolCallId = name, Title = name })
            : ParsedLine.None;

    // JSON-boundary string read: false means the field is absent (or not a non-empty string), never a
    // silent "" sentinel. Codex's id-bearing fields are absence-sensitive, so they all route through here.
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
