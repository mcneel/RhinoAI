using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Collections.Generic;

using Rhino.AI.Models;

namespace Rhino.AI;

internal sealed class ClaudeHarness : IHarness
{

    public PermissionSet Permissions { get; } = new();

    private Dictionary<string, IMcp> PrivateMcps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, IMcp> Mcps => PrivateMcps;

    private Dictionary<string, ISkill> PrivateSkills { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ISkill> Skills => PrivateSkills;

    public HarnessConfig Config { get; } = new();

    public bool AddMcp(IMcp mcp) => PrivateMcps.TryAdd(mcp.Name, mcp);

    public bool AddSkill(ISkill skill) => PrivateSkills.TryAdd(skill.Name, skill);

    public Guid? SessionId { get; set; }

    // TODO : Not used? Hmm ..
    public async Task<ToolReturn> UseToolAsync(string mcpName, string toolName, List<IToolArg> args, CancellationToken token)
        => ToolReturn.Refused();

    public async Task<IEnumerable<ITurn>> LoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        if (agent.Model is not ClaudeDesktopModel claudeModel) return [];

        string exePath = ClaudeDesktopModel.ExePath;

        using Process process = new()
        {
            EnableRaisingEvents = true,
            StartInfo = new()
            {
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,

                FileName = exePath,
                WorkingDirectory = Config.CurrentWorkingDirectory,
                CreateNoWindow = true,
                // ArgumentList
            }
        };

        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);
        process.StartInfo.StandardInputEncoding = utf8;
        process.StartInfo.StandardOutputEncoding = utf8;
        process.StartInfo.StandardErrorEncoding = utf8;

        process.StartInfo.ArgumentList.Add("-p");

        if (SessionId is not null)
        {
            process.StartInfo.ArgumentList.Add("--resume");
        }
        else
        {
            // TODO : If Call fails, this must be un-set
            SessionId ??= Guid.NewGuid();
            process.StartInfo.ArgumentList.Add("--session-id");
        }

        process.StartInfo.ArgumentList.Add(SessionId!.ToString());

        // --allowedTools, --allowed-tools <tools...> Comma or space-separated list of tool names to allow (e.g. "Bash(git *) Edit")
        string allowedTools = string.Empty;
        
        // ALLOW ALL TOOLS DO PERMISSIONS OURSELVES
        foreach (IMcp mcp in Mcps.Values)
        {
            // TODO : Check Permissions.ProhibitedTools()
            allowedTools += $"mcp__{CoerceMcpName(mcp.Name)}__*";
            allowedTools += " ";
        }
        process.StartInfo.ArgumentList.Add("--allowedTools");
        process.StartInfo.ArgumentList.Add(allowedTools);


        // --disallowedTools, --disallowed-tools <tools...> Comma or space-separated list of tool names to deny (e.g. "Bash(git *) Edit")
        string disallowedTools = string.Empty;
        foreach (Permission permission in Permissions.ProhibitedTools())
        {
            disallowedTools += $"mcp__{CoerceMcpName(permission.McpName)}__{permission.ToolName}";
            // permission.ArgumentPermissions // TODO : Use these for smarter permissions
            disallowedTools += " ";
        }
        process.StartInfo.ArgumentList.Add("--disallowedTools");
        process.StartInfo.ArgumentList.Add(disallowedTools);

        // Model
        //   --model <model>                       Model for the current session. Provide an alias for the latest model (e.g. 'fable', 'opus', or 'sonnet') or a model's full name (e.g. 'claude-fable-5').
        process.StartInfo.ArgumentList.Add("--model");
        process.StartInfo.ArgumentList.Add(agent.Model.Name);

        if (UserPermissions.IsPermitted("anthropic", "opus"))
        {
            // --fallback-model <model>              Enable automatic fallback to specified model(s) when the default model is overloaded or not available.
            process.StartInfo.ArgumentList.Add("--fallback-model");
            process.StartInfo.ArgumentList.Add("opus");
        }

        // --strict-mcp-config                   Only use MCP servers from --mcp-config, ignoring all other MCP configurations
        process.StartInfo.ArgumentList.Add("--strict-mcp-config");

        // --mcp-config <configs...>             Load MCP servers from JSON files or strings (space-separated)
        process.StartInfo.ArgumentList.Add("--mcp-config");
        process.StartInfo.ArgumentList.Add(GetMcpJsons());

        // --effort <level>  Effort level for the current session (low, medium, high, xhigh, max)
        process.StartInfo.ArgumentList.Add("--effort");
        process.StartInfo.ArgumentList.Add("high");

        // --prompt-suggestions [value]          Enable prompt suggestions
        process.StartInfo.ArgumentList.Add("--prompt-suggestions");
        process.StartInfo.ArgumentList.Add("false");

        // Output as JSON
        process.StartInfo.ArgumentList.Add("--output-format");
        process.StartInfo.ArgumentList.Add("stream-json");

        process.StartInfo.ArgumentList.Add("--verbose");

        if (!agent.Config.UseLocalSettings)
        {
            process.StartInfo.ArgumentList.Add("--tools");
            process.StartInfo.ArgumentList.Add("");
            process.StartInfo.ArgumentList.Add("--setting-sources");
            process.StartInfo.ArgumentList.Add("");
        }

        // process.StartInfo.ArgumentList.Add("--append-system-prompt");
        // string prompt = JsonSerializer.Serialize(turn);
        // process.StartInfo.ArgumentList.Add(prompt);

        process.StartInfo.ArgumentList.Add("--disable-slash-commands");

        process.ErrorDataReceived += ReadErrors;
        process.OutputDataReceived += ReadOutput;

        // process.StandardInput

        if (!process.Start()) { }
        process.BeginErrorReadLine();

        Task<IEnumerable<ITurn>> reading = ReadLoopAsync(process, token);
        Task writing = WriteLoopAsync(process, start);

        using CancellationTokenRegistration _ = token.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        await process.WaitForExitAsync(token);
        await Task.WhenAll(reading, writing);

        // Other possible args

        // --system-prompt <prompt>              System prompt to use for the session

        // attach <id>                           Open a background session in this terminal. <id> is the short id that `claude --bg` prints and `claude agents` lists

        // auth                                  Manage authentication
        // setup-token                           Set up a long-lived authentication token (requires Claude subscription)

        // process.Exited 

        return await reading;
    }

    private static string CoerceMcpName(string mcpName)
        => mcpName.Replace(' ', '_');

    private async Task WriteLoopAsync(Process process, IEnumerable<ITurn> turn)
    {
        string prompt = JsonSerializer.Serialize(turn);
        await process.StandardInput.WriteLineAsync(prompt).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private async Task<IEnumerable<ITurn>> ReadLoopAsync(Process process, CancellationToken token)
    {
        List<ITurn> turnsOut = [];
        try
        {
            string? line = null;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0)
                    continue;
                try
                {
                    JsonNode? thing = JsonObject.Parse(line);
                    List<ITurn> newturns = thing?["type"]?.GetValue<string>() switch
                    {
                        "user" => ParseUser(thing),
                        "system" => ParseSystem(thing),
                        "result" => ParseResult(thing),
                        "assistant" => ParseAssisant(thing),

                        _ => []
                    };

                    turnsOut.AddRange(newturns);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        return turnsOut;
    }

    private static DateTime? GetTimestamp(JsonNode node)
    {
        string? timestamp = node["timestamp"]?.GetValue<string>();
        if (!DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsed)) return null;
        return parsed;
    }

    private List<ITurn> ParseUser(JsonNode node)
    {
        JsonNode? message = node["message"];
        JsonNode? content = message?["content"];
        if (content is not JsonArray contents) return [];

        DateTime? timestamp = GetTimestamp(node);

        List<ITurn> turns = new(contents.Count);
        foreach (JsonNode? cont in contents)
        {
            ITurn? turn = cont?["type"]?.ToString() switch
            {
                "tool_result" => GetToolResultTurn(cont, timestamp),

                _ => null,
            };

            if (turn is null) continue;

            turns.Add(turn);
        }

        return turns;
    }

    private ITurn? GetToolResultTurn(JsonNode content, DateTime? timestamp)
    {
        string? id = content["tool_use_id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return null;

        JsonNode? contentContent = content["content"];

        JsonValueKind? kind = contentContent?.GetValueKind();
        if (kind == JsonValueKind.Array)
        {
            // TODO : A little flimsy?
            string? toolName = content["content"]?[0]?["tool_name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(toolName)) return null;

            ToolResultTurn toolResult = new(id, toolName, ToolReturn.Success(""), timestamp);
            return toolResult;
        }
        else if (kind == JsonValueKind.String)
        {
            string? contentResult = contentContent!.GetValue<string>();
            if (string.IsNullOrEmpty(contentResult)) return null;
            // TODO : What type is this? - Is it a message?
            return new MessageTurn(contentResult, RoleType.User, timestamp);
        }

        return null;
    }

    private static List<ITurn> ParseSystem(JsonNode node)
    {
        JsonNode? subTypeNode = node["subtype"];
        if (subTypeNode is not null)
        {
            string? subType = subTypeNode.GetValue<string>();

            // Ignore Hooks for now
            if (subType is not null && subType.StartsWith("hook_")) return [];

            // Ignore Init for now - I don't know what to do with it?
            if (subType is not null && subType.Equals("init"))
            {
                return [];
            }
        }

        return [];
    }

    private static List<ITurn> ParseResult(JsonNode node)
    {

        string? error = null;
        if (node["is_error"]?.GetValue<bool>() == true)
        {
            error = node["result"]?.GetValue<string>();
        }

        if (node["stop_reason"] is JsonNode stopProp)
        {
            string stopReason = stopProp.GetValue<string>();
            StopReason reason = stopReason.ToLowerInvariant() switch
            {
                "end_turn" => StopReason.EndTurn,
                "tool_use" => StopReason.ToolUse,
                "max_tokens" => StopReason.MaxTokens,
                "refusal" => StopReason.Refusal,
                "error" => StopReason.Error,

                _ => StopReason.Other
            };

            TimeSpan? duration = node["duration_ms"]?.GetValue<double>() is double ms ? TimeSpan.FromMilliseconds(ms) : null;

            return [new TurnEnd(reason, error, duration: duration)];
        }

        StopReason endReason = error is null ? StopReason.EndTurn : StopReason.Error;
        return [new TurnEnd(endReason, error)];
    }

    private static List<ITurn> ParseAssisant(JsonNode node)
    {
        JsonNode? message = node["message"];
        JsonNode? content = message?["content"];
        if (content is not JsonArray contents) return [];

        // TODO : Input tokens or output tokens?
        int? tokenCount = message?["usage"]?["input_tokens"]?.GetValue<int>() ?? null;

        DateTime? timestamp = GetTimestamp(node);

        List<ITurn> turns = new(contents.Count);
        foreach (JsonNode? cont in contents)
        {
            ITurn? turn = cont?["type"]?.ToString() switch
            {
                "tool_use" => GetToolTurn(cont, timestamp, tokenCount),
                "thinking" => GetThinking(cont, timestamp, tokenCount),
                "text" => GetText(cont, timestamp, tokenCount),

                _ => null,
            };

            if (turn is null) continue;

            turns.Add(turn);
        }

        return turns;
    }

    private static MessageTurn? GetText(JsonNode content, DateTime? timestamp, int? tokenCount)
    {
        string? text = content["text"]?.GetValue<string>();
        if (string.IsNullOrEmpty(text)) return null;
        return new MessageTurn(text, RoleType.Assistant, timestamp, tokenCount: tokenCount);
    }

    private static ThinkingTurn? GetThinking(JsonNode content, DateTime? timestamp, int? tokenCount)
    {
        string? thinking = content["thinking"]?.GetValue<string>();
        if (string.IsNullOrEmpty(thinking)) return null;
        return new ThinkingTurn(thinking, timestamp: timestamp, tokenCount: tokenCount);
    }

    private static ToolTurn? GetToolTurn(JsonNode content, DateTime? timestamp, int? tokenCount)
    {
        string? id = content["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return null;

        string? name = content["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name)) return null;

        List<IToolArg> args = GetArgs(content["input"]);
        return new ToolTurn(id, name, args, timestamp: timestamp, tokenCount: tokenCount);
    }

    private static List<IToolArg> GetArgs(JsonNode? node)
    {
        List<IToolArg> args = [];
        if (node is not JsonObject obj) return args;

        foreach (KeyValuePair<string, JsonNode?> kvp in obj)
        {
            string propName = kvp.Key;
            IToolArg? arg = kvp.Value?.GetValueKind() switch
            {
                JsonValueKind.String => new ToolString(propName, kvp.Value.GetValue<string>()),
                JsonValueKind.Number => new ToolNumber(propName, kvp.Value.GetValue<double>()),
                JsonValueKind.True => new ToolBoolean(propName, true),
                JsonValueKind.False => new ToolBoolean(propName, false),

                // Null, Undefined, Object, Array (for now)

                _ => null
            };

            if (arg is null) continue;
            args.Add(arg);
        }

        return args;
    }

    private void ReadErrors(object sender, DataReceivedEventArgs e)
    {
        // TODO : Handle failed resume
        Debug.WriteLine(e.Data);
        ;
    }

    private void ReadOutput(object sender, DataReceivedEventArgs e)
    {
        ;
    }

    // TODO : Pin the MCP protocol era via env var (MCP_PROTOCOL_NEGOTIATION=legacy, undocumented) instead of relying on the discover probe falling back
    private string GetMcpJsons()
    {
        JsonObject array = new();

        // NOTE : Do the MCP configs need to be done? Can turns not just be run?
        string mcpJson = string.Empty;
        foreach (IMcp mcp in Mcps.Values)
        {
            // TODO : MCP's
            if (mcp is StdioMcp stdioMcp)
            {
                array[mcp.Name] = new JsonObject()
                {
                    ["type"] = "stdio",
                    ["command"] = stdioMcp.ProcessPath.LocalPath
                    // ["args"] = ""
                    // ["env"] = ""
                };
            }
            else if (mcp is HttpMcp httpMcp)
            {
                array[mcp.Name] = new JsonObject()
                {
                    ["type"] = "http",
                    ["url"] = httpMcp.Url.AbsoluteUri
                    // "headers": {
                    //     "Authorization": "Bearer ${MCP_TOKEN}",
                    //     "X-Tenant": "mcneel"
                    // }
                };
            }
            else if (mcp is MemoryMcp memoryMcp)
            {
                Mcps.MemoryMcpManager.McpLease lease = AI.Mcps.MemoryMcpManager.RegisterMemoryMcp(memoryMcp);

                array[mcp.Name] = new JsonObject()
                {
                    ["type"] = "http",
                    ["url"] = lease.Uri.AbsoluteUri
                    // "headers": {
                    //     "Authorization": "Bearer ${MCP_TOKEN}",
                    //     "X-Tenant": "mcneel"
                    // }
                };
            }
        }

        JsonObject servers = new()
        {
            ["mcpServers"] = array
        };

        // TODO : Options
        string json = servers.ToJsonString();
        return json;
    }

}
