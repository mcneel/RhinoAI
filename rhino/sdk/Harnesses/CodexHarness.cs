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

internal sealed class CodexHarness : IHarness
{

    public PermissionSet Permissions { get; } = new();

    private Dictionary<string, IMcp> PrivateMcps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, IMcp> Mcps => PrivateMcps;

    private Dictionary<string, ISkill> PrivateSkills { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ISkill> Skills => PrivateSkills;

    public HarnessConfig Config { get; } = new();

    public bool AddMcp(IMcp mcp) => PrivateMcps.TryAdd(mcp.Name, mcp);

    public bool AddSkill(ISkill skill) => PrivateSkills.TryAdd(skill.Name, skill);

    public async Task<ToolReturn> UseToolAsync(string mcpName, string toolName, List<IToolArg> args, CancellationToken token)
        => await GenericHarness.UseToolAsync(this, mcpName, toolName, args, token);

    public Func<PermissionRequest, CancellationToken, Task>? AskUser { get; set; }

    public async IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation] CancellationToken token)
    {
        if (agent.Model is not CodexDesktopModel) yield break;
        if (!await EnsureLoggedIn(token))
        {
            yield return new MessageTurn("Not Logged In", RoleType.System, DateTime.UtcNow, TimeSpan.Zero, 0);
            yield break;
        }


        using AIProcess process = StartCodex(agent, start, token);

        using CancellationTokenRegistration _ = token.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        Task writing = WriteLoopAsync(process, start);

        bool ended = false;
        await foreach (ITurn turn in StreamLoopAsync(process, agent, token))
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

    public async Task<IEnumerable<ITurn>> LoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        if (agent.Model is not CodexDesktopModel) return [];

        List<ITurn> turns = [];
        await foreach (ITurn turn in StreamLoopAsync(agent, start, token))
        {
            turns.Add(turn);
        }

        return turns;
    }

    private Process GetCodexProcess(CancellationToken token)
    {
        string exePath = CodexDesktopModel.ExePath;

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
        process.StartInfo.StandardInputEncoding = utf8;
        process.StartInfo.StandardOutputEncoding = utf8;
        process.StartInfo.StandardErrorEncoding = utf8;

        return process;
    }

    private AIProcess StartCodex(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        Process process = GetCodexProcess(token);

        process.StartInfo.ArgumentList.Add("exec");

        if (agent.Config.SessionId != Guid.Empty)
        {
            process.StartInfo.ArgumentList.Add("resume");
        }

        // Not a git repo

        process.StartInfo.ArgumentList.Add("--skip-git-repo-check");

        // Model
        //   --model <model>                       Model for the current session. Provide an alias for the latest model (e.g. 'fable', 'opus', or 'sonnet') or a model's full name (e.g. 'claude-fable-5').
        process.StartInfo.ArgumentList.Add("--model");
        process.StartInfo.ArgumentList.Add(agent.Model.Name);

        foreach (string mcpArg in GetMcpArgs())
        {
            process.StartInfo.ArgumentList.Add(mcpArg);
        }

        process.StartInfo.ArgumentList.Add("-c");
        process.StartInfo.ArgumentList.Add("model_reasoning_effort=\"high\"");

        string systemPrompt = string.Join("\n\n", start.OfType<SystemTurn>().Select(s => s.Prompt));
        if (systemPrompt.Length > 0)
        {
            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add($"developer_instructions={JsonSerializer.Serialize(systemPrompt)}");
        }

        // Output as JSON
        process.StartInfo.ArgumentList.Add("--json");

        if (!agent.Config.UseLocalSettings)
        {
            process.StartInfo.ArgumentList.Add("--ignore-user-config");
            process.StartInfo.ArgumentList.Add("--ignore-rules");

            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("sandbox_mode=\"read-only\"");

            process.StartInfo.ArgumentList.Add("-c");
            process.StartInfo.ArgumentList.Add("web_search=\"disabled\"");

            // code_mode_host stays enabled: this model routes every tool call, MCP included, through code mode.
            string[] codexTools = ["shell_tool", "unified_exec", "multi_agent", "goals",
                                            "image_generation", "view_image", "sleep_tool", "apps", "tool_suggest",
                                            "remote_plugin", "browser_use", "computer_use"];

            foreach (string builtInTool in codexTools)
            {
                process.StartInfo.ArgumentList.Add("--disable");
                process.StartInfo.ArgumentList.Add(builtInTool);
            }
        }

        if (agent.Config.SessionId != Guid.Empty)
            process.StartInfo.ArgumentList.Add(agent.Config.SessionId.ToString());

        process.StartInfo.ArgumentList.Add("-");

        AIProcess turnProcess = new(process);

        if (!process.Start()) { }
        process.BeginErrorReadLine();

        return turnProcess;
    }

    private async Task WriteLoopAsync(AIProcess process, IEnumerable<ITurn> turn)
    {
        MessageTurn latest = turn.OfType<MessageTurn>().Last(m => m.Role == RoleType.User);
        await process.StandardInput.WriteLineAsync(latest.Message).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private async IAsyncEnumerable<ITurn> StreamLoopAsync(AIProcess process, Agent agent, [EnumeratorCancellation] CancellationToken token)
    {
        List<ITurn> turnsOut = [];
        List<JsonNode> stringies = [];
        try
        {
            string? line = null;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (string.IsNullOrEmpty(line)) continue;
                List<ITurn> newTurns = [];
                try
                {
                    JsonNode? thing = JsonObject.Parse(line) ?? throw new Exception($"Could not parse {line}");

                    string? turnId = thing["type"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(turnId)) continue;

                    string[] turnIds = turnId.Split('.');
                    if (turnIds.Length is not (1 or 2))
                    {
                        throw new Exception($"{turnId} was unexpected!");
                    }

                    string turnType = turnIds[0].ToLowerInvariant();
                    string turnStatus = turnIds.Length == 2 ? turnIds[1].ToLowerInvariant() : string.Empty;

                    if (string.Equals(turnId, "thread.started", StringComparison.OrdinalIgnoreCase))
                    {
                        agent.Config.SessionId = thing["thread_id"]?.GetValue<Guid>() ?? agent.Config.SessionId;
                        continue;
                    }

                    newTurns = turnType switch
                    {
                        "thread" => turnStatus switch
                        {
                            "started" => ParseStarted(thing),
                            _ => [],
                        },

                        "turn" => turnStatus switch
                        {
                            "started" => [new TurnStart()],
                            "completed" => ParseTurnCompleted(thing),
                            "failed" => ParseTurnFailed(thing),
                            _ => [],
                        },

                        "item" => turnStatus switch
                        {
                            "started" => ParseStarted(thing),
                            "completed" => ParseItemCompleted(thing),
                            // TODO : Parse item.updated (progress on a running item, e.g. todo_list)
                            "updated" => [],
                            _ => []
                        },

                        "error" => ParseError(thing),

                        _ => []
                    };

                    turnsOut.AddRange(newTurns);
                    if (newTurns.Count == 0)
                    {
                        stringies.Add(thing);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }

                foreach (ITurn turn in newTurns)
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


    private bool LoggedIn { get; set; } = false;
    private async Task<bool> EnsureLoggedIn(CancellationToken token)
    {
        if (LoggedIn) return true;
        try
        {
            using Process codex = GetCodexProcess(token);
            codex.StartInfo.ArgumentList.Add("login");
            codex.StartInfo.ArgumentList.Add("status");

            using CancellationTokenRegistration _ = token.Register(() =>
            {
                try { codex.Kill(entireProcessTree: true); } catch (Exception) { }
            });

            if (!codex.Start()) return LoggedIn;

            await codex.WaitForExitAsync(token);

            if (codex.ExitCode == 0)
            {
                LoggedIn = true;
                return LoggedIn;
            }

            LoggedIn = await LogIn(token);
            return LoggedIn;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> LogIn(CancellationToken token)
    {
        using Process codex = GetCodexProcess(token);
        codex.StartInfo.ArgumentList.Add("login");

        using CancellationTokenRegistration _ = token.Register(() =>
        {
            try { codex.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        if (!codex.Start()) return false;

        List<string> lines = [];
        while (true)
        {
            string? line = await codex.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null || line.Length == 0) break;
            lines.Add(line);
        }

        // TODO : What is user closes the browser?

        await codex.WaitForExitAsync(token);

        if (codex.ExitCode == 0) return true;

        // TODO : Handle Failure
        string data = string.Join("\n", lines);
        // JsonNode? node = JsonObject.Parse(data);

        return false;
    }

    private static List<ITurn> ParseTurnFailed(JsonNode thing)
        => [new TurnEnd(StopReason.Error, ErrorMessage(thing["error"]) ?? "Codex turn failed.")];

    private static List<ITurn> ParseError(JsonNode thing)
        => [new TurnEnd(StopReason.Error, ErrorMessage(thing) ?? "Codex reported an error.")];

    private static string? ErrorMessage(JsonNode? node) => node switch
    {
        JsonValue value when value.TryGetValue(out string? text) => Unwrap(text),
        JsonObject obj when obj["error"] is JsonNode inner => ErrorMessage(inner) ?? obj.ToJsonString(),
        JsonObject obj => ErrorMessage(obj["message"]) ?? obj.ToJsonString(),
        _ => null,
    };

    // Codex forwards the API's error body as a JSON string inside "message", so the readable text sits one level down.
    private static string Unwrap(string text)
    {
        if (!text.TrimStart().StartsWith('{')) return text;

        try
        {
            return JsonNode.Parse(text) is JsonObject obj && ErrorMessage(obj) is string inner ? inner : text;
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private List<ITurn> ParseStarted(JsonNode thing)
    {
        JsonNode? item = thing["item"];
        if (item?["type"]?.GetValue<string>() != "mcp_tool_call") return [];
        if (!TryGetToolCallName(item, out string id, out string name)) return [];

        List<IToolArg> args = GetArgs(item["arguments"]);

        return [new ToolTurn(id, name, args)];
    }

    private List<IToolArg> GetArgs(JsonNode? node)
    {
        if (node is not JsonObject jArgs) return [];
        List<IToolArg> args = [];

        foreach (KeyValuePair<string, JsonNode?> jArg in jArgs)
        {
            if (jArg.Value is null) continue;

            IToolArg? toolArg = ParseArg(jArg!);
            if (toolArg is null) continue;

            args.Add(toolArg);
        }

        return args;
    }

    private IToolArg? ParseArg(KeyValuePair<string, JsonNode> jArg)
    {
        JsonValueKind kind = jArg.Value.GetValueKind();
        string propName = jArg.Key;

        return kind switch
        {
            JsonValueKind.String => new ToolString(propName, jArg.Value.GetValue<string>()),
            JsonValueKind.Number => new ToolNumber(propName, jArg.Value.GetValue<double>()),
            JsonValueKind.True => new ToolBoolean(propName, true),
            JsonValueKind.False => new ToolBoolean(propName, false),

            // Null, Undefined, Object, Array (for now)

            _ => null
        };
    }

    private static List<ITurn> ParseItemCompleted(JsonNode thing)
    {
        JsonNode? item = thing["item"];
        if (item is null) return [];

        JsonNode? type = item["type"];
        if (type is null) return [];

        return type?.GetValue<string>()?.ToLowerInvariant() switch
        {
            "agent_message" => [new MessageTurn(item["text"]?.GetValue<string>() ?? item.ToJsonString(), RoleType.Assistant)],

            "mcp_tool_call" => GetToolResultTurn(item),
            "reasoning" => ParseReasoning(item),

            _ => []
        };
    }

    private static List<ITurn> ParseReasoning(JsonNode item)
    {
        List<string> parts = [];
        if (item["text"]?.GetValue<string>() is string text) parts.Add(text);

        if (item["summary"] is JsonArray summary)
        {
            foreach (JsonNode? entry in summary)
            {
                string? part = entry switch
                {
                    JsonValue value when value.TryGetValue(out string? raw) => raw,
                    JsonObject obj => obj["text"]?.GetValue<string>(),
                    _ => null,
                };
                if (!string.IsNullOrEmpty(part)) parts.Add(part);
            }
        }

        return parts.Count == 0 ? [] : [new ThinkingTurn(string.Join("\n", parts))];
    }

    private static List<ITurn> GetToolResultTurn(JsonNode item)
    {
        if (!TryGetToolCallName(item, out string id, out string name)) return [];

        List<string> texts = [];
        if (item["result"]?["content"] is JsonArray content)
        {
            foreach (JsonNode? block in content)
            {
                if (block?["type"]?.GetValue<string>() != "text") continue;
                if (block["text"]?.GetValue<string>() is string text) texts.Add(text);
            }
        }
        string message = string.Join("\n", texts);

        string? error = item["error"] switch
        {
            JsonValue value when value.TryGetValue(out string? text) => text,
            JsonObject obj => obj["message"]?.GetValue<string>() ?? obj.ToJsonString(),
            _ => null,
        };
        bool failed = error is not null || item["status"]?.GetValue<string>() == "failed";

        ToolReturn toolReturn = failed
            ? new ToolReturn(error ?? message, ToolResult.Failure, null)
            : ToolReturn.Success(message);

        return [new ToolResultTurn(id, name, toolReturn)];
    }

    private static bool TryGetToolCallName(JsonNode item, out string id, out string name)
    {
        id = string.Empty;
        name = string.Empty;

        string? callId = item["id"]?.GetValue<string>();
        string? server = item["server"]?.GetValue<string>();
        string? tool = item["tool"]?.GetValue<string>();

        if (string.IsNullOrEmpty(callId)) return false;
        if (string.IsNullOrEmpty(server) || string.IsNullOrEmpty(tool)) return false;

        id = callId;
        name = ToolSchema.WireName(server, tool);

        return true;
    }

    private static List<ITurn> ParseTurnCompleted(JsonNode thing)
    {
        int? tokenCount = thing?["usage"]?["input_tokens"]?.GetValue<int>();
        // new ToolResultTurn()
        return [new TurnEnd(StopReason.EndTurn, tokenCount: tokenCount)];
    }

    // TODO : Pin the MCP protocol version via env var once Codex has one that works (CODEX_MCP_PROTOCOL_VERSION had no effect, still sends 2025-06-18)
    private IEnumerable<string> GetMcpArgs()
    {
        string disabledTools = TomlArray(Permissions.ProhibitedTools().Select(permission => permission.ToolName));
        string timeouts = $"startup_timeout_sec={(long)McpTimeouts.Startup.TotalSeconds}, tool_timeout_sec={(long)McpTimeouts.ToolCall.TotalSeconds}";

        foreach (IMcp mcp in Mcps.Values)
        {
            string? transport = mcp switch
            {
                StdioMcp stdioMcp => $"command='{stdioMcp.ProcessPath.LocalPath}'",
                HttpMcp httpMcp => $"url='{httpMcp.Url.AbsoluteUri}'",
                MemoryMcp memMcp => HandleMemoryMcp(memMcp),
                _ => null,
            };
            if (transport is null) continue;

            yield return "-c";
            yield return $"mcp_servers.{mcp.Name}={{{transport}, {timeouts}, disabled_tools={disabledTools}}}";
        }
    }

    private List<Mcps.MemoryMcpManager.McpLease> Leases { get; } = [];

    private string HandleMemoryMcp(MemoryMcp mcp)
    {
        Mcps.MemoryMcpManager.McpLease lease = AI.Mcps.MemoryMcpManager.RegisterMemoryMcp(this, mcp);
        Leases.Add(lease);
        return $"url='{lease.Uri.AbsoluteUri}'";
    }

    private void DisposeLeases()
    {
        foreach (Mcps.MemoryMcpManager.McpLease lease in Leases)
        {
            lease.Dispose();
        }
        Leases.Clear();
    }

    private static string TomlArray(IEnumerable<string> values)
        => $"[{string.Join(", ", values.Select(value => $"'{value}'"))}]";

}
