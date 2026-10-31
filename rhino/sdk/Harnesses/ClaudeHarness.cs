using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Rhino.AI.Models;

namespace Rhino.AI;

internal sealed class ClaudeHarness(PlugIns.PlugInToken token) : Harness(token)
{

    [Obsolete("This will only be called by MCPs registered in this codebase, not by external MCPs and the harness has no permission requests at present" +
              "--permission-prompt-tool would resolve this")]
#pragma warning disable CS0809 // Obsolete member overrides non-obsolete member
    public override Func<IPermissionRequest, CancellationToken, Task>? AskUser { get; set; }
#pragma warning restore CS0809 // Obsolete member overrides non-obsolete member

    public override async IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation] CancellationToken token)
    {
        if (agent.Model is not ClaudeDesktopModel claudeModel) yield break;
        if (!await EnsureLoggedIn(token, claudeModel))
        {
            yield return new MessageTurn("Not Logged In", RoleType.System, DateTime.UtcNow, TimeSpan.Zero, 0);
            yield break;
        }

        string prompt = UserLine(start.OfType<MessageTurn>().Last(m => m.Role == RoleType.User));

        using AIProcess process = StartClaude(agent, start, token);

        using CancellationTokenRegistration _ = token.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        Task writing = WriteLoopAsync(process, prompt);

        bool ended = false;
        await foreach (ITurn turn in StreamLoopAsync(process, token))
        {
            while (process.TryPop(out ITurn processTurn))
            {
                ended |= processTurn is TurnEnd;
                yield return processTurn;
            }

            ended |= turn is TurnEnd;
            yield return turn;
        }

        Exception? ex = null;
        try
        {
            await writing;
        }
        catch (IOException) { }
        catch (Exception writeEx)
        {
            ex = writeEx;
        }

        if (ex is not null)
        {
            yield return new MessageTurn(ex.Message, RoleType.System);
        }

        await process.WaitForExitAsync(token);

        while (process.TryPop(out ITurn processTurn))
        {
            ended |= processTurn is TurnEnd;
            yield return processTurn;
        }

        if (process.ExitCode != 0)
        {
            yield return new TurnEnd(StopReason.Error, $"{process.ExitCode}", DateTime.UtcNow, TimeSpan.Zero, 0);
            ended = true;
        }

        if (!ended)
            yield return new TurnEnd(StopReason.Error);
    }

    private bool LoggedIn { get; set; } = false;
    private async Task<bool> EnsureLoggedIn(CancellationToken token, ClaudeDesktopModel claudeModel)
    {
        if (LoggedIn) return true;
        try
        {
            using Process claude = GetClaudeProcess(token, claudeModel);
            claude.StartInfo.AddArgument("auth");
            claude.StartInfo.AddArgument("status");

            using CancellationTokenRegistration _ = token.Register(() =>
            {
                try { claude.Kill(entireProcessTree: true); } catch (Exception) { }
            });

            if (!claude.Start()) return LoggedIn;

            List<string> lines = [];
            while (true)
            {
                string? line = await claude.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
                if (line is null || line.Length == 0) break;
                lines.Add(line);
            }

            await claude.WaitForExitAsync(token);

            string data = string.Join("\n", lines);
            JsonNode? node = JsonObject.Parse(data);
            if (node?["loggedIn"]?.GetValue<bool>() == true)
            {
                LoggedIn = true;
                return LoggedIn;
            }
            LoggedIn = await LogIn(token, claudeModel);
            return LoggedIn;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> LogIn(CancellationToken token, ClaudeDesktopModel model)
    {
        using Process claude = GetClaudeProcess(token, model);
        claude.StartInfo.AddArgument("auth");
        claude.StartInfo.AddArgument("login");
        claude.StartInfo.AddArgument("--claudeai");

        using CancellationTokenRegistration _ = token.Register(() =>
        {
            try { claude.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        if (!claude.Start()) return false;

        List<string> lines = [];
        while (true)
        {
            string? line = await claude.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null || line.Length == 0) break;
            lines.Add(line);
        }

        // TODO : What is user closes the browser?

        await claude.WaitForExitAsync(token);

        if (claude.ExitCode == 0) return true;

        // TODO : Handle Failure
        string data = string.Join("\n", lines);
        // JsonNode? node = JsonObject.Parse(data);

        return false;
    }

    public override async Task<IEnumerable<ITurn>> LoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        if (agent.Model is not ClaudeDesktopModel claudeModel) return [];

        List<ITurn> turns = [];
        await foreach (ITurn turn in StreamLoopAsync(agent, start, token))
        {
            turns.Add(turn);
        }

        return turns;
    }

    private Process GetClaudeProcess(CancellationToken token, IModel model)
    {
        ClaudeDesktopModel claudeModel = (ClaudeDesktopModel)model;
        string exePath = claudeModel.ExePath;

        Process process = new()
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
#if !NET48
        process.StartInfo.StandardInputEncoding = utf8;
#endif
        process.StartInfo.StandardOutputEncoding = utf8;
        process.StartInfo.StandardErrorEncoding = utf8;

        return process;
    }

    private AIProcess StartClaude(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        // TODO : Check agent.Model
        Process process = GetClaudeProcess(token, agent.Model!);

        process.StartInfo.Environment["MCP_TIMEOUT"] = ((long)McpTimeouts.Startup.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

        process.StartInfo.AddArgument("-p");

        if (agent.Config.SessionId != Guid.Empty)
        {
            process.StartInfo.AddArgument("--resume");
        }
        else
        {
            // TODO : If Call fails, this must be un-set
            agent.Config.SessionId = Guid.NewGuid();
            process.StartInfo.AddArgument("--session-id");
        }

        process.StartInfo.AddArgument(agent.Config.SessionId.ToString()!);

        // --allowedTools, --allowed-tools <tools...> Comma or space-separated list of tool names to allow (e.g. "Bash(git *) Edit")
        string allowedTools = string.Empty;

        // ALLOW ALL TOOLS DO PERMISSIONS OURSELVES
        foreach (Mcp mcp in Mcps.Values)
        {
            // TODO : Check Permissions.ProhibitedTools()
            allowedTools += $"mcp__{CoerceMcpName(mcp.Name)}__*";
            allowedTools += " ";
        }
        process.StartInfo.AddArgument("--allowedTools");
        process.StartInfo.AddArgument(allowedTools);


        // --disallowedTools, --disallowed-tools <tools...> Comma or space-separated list of tool names to deny (e.g. "Bash(git *) Edit")
        string disallowedTools = string.Empty;
        foreach (Permission permission in Instances.Preferences.Permissions.ProhibitedTools())
        {
            disallowedTools += $"mcp__{CoerceMcpName(permission.McpName)}__{permission.ToolName}";
            // permission.ArgumentPermissions // TODO : Use these for smarter permissions
            disallowedTools += " ";
        }
        process.StartInfo.AddArgument("--disallowedTools");
        process.StartInfo.AddArgument(disallowedTools);

        // Model
        //   --model <model>                       Model for the current session. Provide an alias for the latest model (e.g. 'fable', 'opus', or 'sonnet') or a model's full name (e.g. 'claude-fable-5').
        process.StartInfo.AddArgument("--model");
        process.StartInfo.AddArgument(agent.Model!.Name);

        // --fallback-model <model>              Enable automatic fallback to specified model(s) when the default model is overloaded or not available.
        process.StartInfo.AddArgument("--fallback-model");
        process.StartInfo.AddArgument("opus");

        // --effort <level>                      Effort level for the current session (low, medium, high, xhigh, max)
        if (!string.IsNullOrEmpty(agent.Config.ModelEffort))
        {
            process.StartInfo.AddArgument("--effort");
            process.StartInfo.AddArgument(agent.Config.ModelEffort);
        }

        // --strict-mcp-config                   Only use MCP servers from --mcp-config, ignoring all other MCP configurations
        process.StartInfo.AddArgument("--strict-mcp-config");

        // --mcp-config <configs...>             Load MCP servers from JSON files or strings (space-separated)
        process.StartInfo.AddArgument("--mcp-config");
        process.StartInfo.AddArgument(GetMcpJsons(agent));

        // --effort <level>  Effort level for the current session (low, medium, high, xhigh, max)
        process.StartInfo.AddArgument("--effort");
        process.StartInfo.AddArgument("high");

        // --prompt-suggestions [value]          Enable prompt suggestions
        process.StartInfo.AddArgument("--prompt-suggestions");
        process.StartInfo.AddArgument("false");

        process.StartInfo.AddArgument("--input-format");
        process.StartInfo.AddArgument("stream-json");

        // Output as JSON
        process.StartInfo.AddArgument("--output-format");
        process.StartInfo.AddArgument("stream-json");

        process.StartInfo.AddArgument("--verbose");

        if (!agent.Config.UseLocalSettings)
        {
            process.StartInfo.AddArgument("--tools");
            process.StartInfo.AddArgument("");
            process.StartInfo.AddArgument("--setting-sources");
            process.StartInfo.AddArgument("");
        }

        string systemPrompt = string.Join("\n\n", start.OfType<SystemTurn>().Select(s => s.Prompt));
        if (systemPrompt.Length > 0)
        {
            process.StartInfo.AddArgument("--append-system-prompt");
            process.StartInfo.AddArgument(systemPrompt);
        }

        process.StartInfo.AddArgument("--disable-slash-commands");

        AIProcess turnProcess = new(process);

        if (!process.Start()) { }
        process.BeginErrorReadLine();

        return turnProcess;
    }

    private static string CoerceMcpName(string mcpName)
        => mcpName.Replace(' ', '_');

    private static string UserLine(MessageTurn message) => new JsonObject
    {
        ["type"] = "user",
        ["message"] = new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(message.Content.Select(ToBlock).ToArray()),
        },
    }.ToJsonString();

    private static JsonNode ToBlock(IMessageContent content) => content switch
    {
        TextContent text => new JsonObject { ["type"] = "text", ["text"] = text.Text },
        ImageContent image => new JsonObject
        {
            ["type"] = "image",
            ["source"] = new JsonObject
            {
                ["type"] = "base64",
                ["media_type"] = image.MediaType,
                ["data"] = Convert.ToBase64String(image.Bytes),
            },
        },
        _ => throw new NotSupportedException($"The Claude harness cannot send {content.GetType().Name}."),
    };

    private async Task WriteLoopAsync(AIProcess process, string prompt)
    {
        await process.StandardInput.WriteLineAsync(prompt).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private async IAsyncEnumerable<ITurn> StreamLoopAsync(AIProcess process, [EnumeratorCancellation] CancellationToken token)
    {
        string? line;
        try
        {
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0)
                    continue;

                List<ITurn> newturns = [];
                try
                {
                    JsonNode? thing = JsonObject.Parse(line);
                    newturns = thing?["type"]?.GetValue<string>() switch
                    {
                        "user" => ParseUser(thing),
                        "system" => ParseSystem(thing),
                        "result" => ParseResult(thing),
                        "assistant" => ParseAssisant(thing),
                        "rate_limit_event" => ParseRateLimit(thing),

                        _ => []
                    };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }


                foreach (ITurn turn in newturns)
                {
                    yield return turn;
                }
            }
        }
        finally
        {
            DisposeLeases();
        }
    }

    private List<ITurn> ParseRateLimit(JsonNode thing)
    {
        return [];
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
            ToolIdToTool.TryRemove(id, out string? toolName);

            List<string> texts = [];
            foreach (JsonNode? block in contentContent!.AsArray())
            {
                if (block?["type"]?.GetValue<string>() != "text") continue;
                if (block["text"]?.GetValue<string>() is string text) texts.Add(text);
            }

            string message = string.Join("\n", texts);
            ToolReturn toolReturn = content["is_error"]?.GetValue<bool>() == true
                ? new ToolReturn(message, ToolResult.Failure, null)
                : ToolReturn.Success(message);

            return new ToolResultTurn(id, toolName ?? "err", toolReturn, timestamp);
        }
        else if (kind == JsonValueKind.String)
        {
            ToolIdToTool.TryRemove(id, out string? toolName);
            string? message = contentContent?.GetValue<string>() ?? "no message found";

            ToolReturn toolReturn = content["is_error"]?.GetValue<bool>() == true
                ? new ToolReturn(message, ToolResult.Failure, null)
                : ToolReturn.Success(message);

            return new ToolResultTurn(id, toolName ?? "err", toolReturn, timestamp);
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
            if (subType is not null && subType.StartsWith("hook_", StringComparison.OrdinalIgnoreCase)) return [];

            // Ignore Init for now - I don't know what to do with it?
            if (subType is not null && subType.Equals("init", StringComparison.OrdinalIgnoreCase))
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

    private List<ITurn> ParseAssisant(JsonNode node)
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

            // NOTE : Can't ask for permission here, it's already happened

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

    private System.Collections.Concurrent.ConcurrentDictionary<string, string> ToolIdToTool { get; } = new(StringComparer.OrdinalIgnoreCase);

    private ToolTurn? GetToolTurn(JsonNode content, DateTime? timestamp, int? tokenCount)
    {
        string? id = content["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return null;

        string? name = content["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name)) return null;

        List<IToolArg> args = GetArgs(content["input"]);
        ToolTurn turn = new(id, name, args, timestamp: timestamp, tokenCount: tokenCount);
        ToolIdToTool[id] = name;
        return turn;
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

    // TODO : Pin the MCP protocol era via env var (MCP_PROTOCOL_NEGOTIATION=legacy, undocumented) instead of relying on the discover probe falling back
    private string GetMcpJsons(Agent agent)
    {
        JsonObject array = new();

        // NOTE : Do the MCP configs need to be done? Can turns not just be run?
        string mcpJson = string.Empty;
        foreach (Mcp mcp in Mcps.Values)
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
                Mcps.MemoryMcpManager.McpLease lease = AI.Mcps.MemoryMcpManager.RegisterMemoryMcp(agent, this, memoryMcp);
                Leases.Add(lease);

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

        foreach (KeyValuePair<string, JsonNode?> server in array)
        {
            if (server.Value is JsonObject config)
                config["timeout"] = (long)McpTimeouts.ToolCall.TotalMilliseconds;
        }

        JsonObject servers = new()
        {
            ["mcpServers"] = array
        };

        // TODO : Options
        string json = servers.ToJsonString();
        return json;
    }

    private List<Mcps.MemoryMcpManager.McpLease> Leases { get; } = [];
    private void DisposeLeases()
    {
        foreach (Mcps.MemoryMcpManager.McpLease lease in Leases)
        {
            lease.Dispose();
        }
        Leases.Clear();
    }

}
