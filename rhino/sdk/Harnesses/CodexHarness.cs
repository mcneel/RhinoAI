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

    public Guid? SessionId { get; set; }

    public async Task<ToolReturn> UseToolAsync(string mcpName, string toolName, List<IToolArg> args, CancellationToken token)
        => await GenericHarness.UseToolAsync(this, mcpName, toolName, args, token);

    public Func<PermissionRequest, CancellationToken, Task>? AskUser { get; set; }

    public async IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation] CancellationToken token)
    {
        if (agent.Model is not CodexDesktopModel claudeModel) yield break;

        using Process process = StartCodex(agent);

        using CancellationTokenRegistration _ = token.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        Task writing = WriteLoopAsync(process, start);

        await foreach (ITurn turn in StreamLoopAsync(process, token))
        {
            yield return turn;
        }

        await writing;
        await process.WaitForExitAsync(token);
    }

    public async Task<IEnumerable<ITurn>> LoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        if (agent.Model is not CodexDesktopModel claudeModel) return [];

        List<ITurn> turns = [];
        await foreach (ITurn turn in StreamLoopAsync(agent, start, token))
        {
            turns.Add(turn);
        }

        return turns;
    }

    private Process StartCodex(Agent agent)
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

        process.StartInfo.ArgumentList.Add("exec");

        if (SessionId is not null)
            process.StartInfo.ArgumentList.Add("resume");

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

        if (SessionId is Guid sessionId)
            process.StartInfo.ArgumentList.Add(sessionId.ToString());

        process.StartInfo.ArgumentList.Add("-");

        process.ErrorDataReceived += ReadErrors;
        process.OutputDataReceived += ReadOutput;

        if (!process.Start()) { }
        process.BeginErrorReadLine();

        return process;
    }

    private async Task WriteLoopAsync(Process process, IEnumerable<ITurn> turn)
    {
        string prompt = JsonSerializer.Serialize(turn);
        await process.StandardInput.WriteLineAsync(prompt).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private async IAsyncEnumerable<ITurn> StreamLoopAsync(Process process, [EnumeratorCancellation] CancellationToken token)
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
                        SessionId = thing["thread_id"]?.GetValue<Guid>();
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

    private void ReadErrors(object sender, DataReceivedEventArgs e)
    {
        Debug.WriteLine(e.Data);
    }

    private void ReadOutput(object sender, DataReceivedEventArgs e)
    {
        ;
    }

    // TODO : Pin the MCP protocol version via env var once Codex has one that works (CODEX_MCP_PROTOCOL_VERSION had no effect, still sends 2025-06-18)
    private IEnumerable<string> GetMcpArgs()
    {
        string disabledTools = TomlArray(Permissions.ProhibitedTools().Select(permission => permission.ToolName));

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
            yield return $"mcp_servers.{mcp.Name}={{{transport}, disabled_tools={disabledTools}}}";
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
