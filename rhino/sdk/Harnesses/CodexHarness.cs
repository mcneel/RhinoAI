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

    // TODO : Not used? Hmm ..
    public async Task<ToolReturn> UseToolAsync(string mcpName, string toolName, List<IToolArg> args, CancellationToken token)
        => ToolReturn.Refused();

    public async Task<IEnumerable<ITurn>> LoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        if (agent.Model is not CodexDesktopModel claudeModel) return [];

        string exePath = CodexDesktopModel.ExePath;

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

        process.StartInfo.ArgumentList.Add("exec");

        if (SessionId is not null)
        {
            process.StartInfo.ArgumentList.Add("resume");
            process.StartInfo.ArgumentList.Add(SessionId!.ToString());
        }

        // Not a git repo

        process.StartInfo.ArgumentList.Add("--skip-git-repo-check");

        // TODO : Use RhinoMcp
        string mcpName = "rhino";

        // Model
        //   --model <model>                       Model for the current session. Provide an alias for the latest model (e.g. 'fable', 'opus', or 'sonnet') or a model's full name (e.g. 'claude-fable-5').
        process.StartInfo.ArgumentList.Add("--model");
        process.StartInfo.ArgumentList.Add(agent.Model.Name);

        foreach(string mcpArg in GetMcpArgs())
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

        process.ErrorDataReceived += ReadErrors;
        process.OutputDataReceived += ReadOutput;

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
        List<JsonNode> stringies = [];
        try
        {
            string? line = null;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (string.IsNullOrEmpty(line)) continue;
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

                    List<ITurn> newturns = turnType switch
                    {
                        "thread" => turnStatus switch
                        {
                            // "started" => ParseStarted()
                            _ => [],
                        },

                        "turn" => turnStatus switch
                        {
                            "started" => [new TurnStart()],
                            "completed" => ParseTurnCompleted(thing),
                            // TODO : Parse turn.failed into a TurnEnd(StopReason.Error) carrying error.message
                            "failed" => [],
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

                        // TODO : Parse the top-level error event (a stream failure, carries message)
                        "error" => [],

                        _ => []
                    };

                    turnsOut.AddRange(newturns);
                    if (newturns.Count == 0)
                    {
                        stringies.Add(thing);
                    }
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

    private List<ITurn> ParseStarted(JsonNode thing)
    {
        JsonNode? item = thing["item"];
        if (item is null) return [];
        string id = item["id"]?.GetValue<string>() ?? "err";
        string? mcpName = item["server"]?.GetValue<string>();
        string? toolName = item["tool"]?.GetValue<string>();

        List<IToolArg> args = GetArgs(item["arguments"]);
        
        return [new ToolTurn(id, $"mcp__{mcpName}__{toolName}", args)];
    }

    private List<IToolArg> GetArgs(JsonNode? node)
    {
        if (node is not JsonObject jArgs) return [];
        List<IToolArg> args = [];

        foreach(KeyValuePair<string,JsonNode?> jArg in jArgs)
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
            
            // TODO : Message
            "mcp_tool_call" => [GetToolResultTurn(item)],

            _ => []
        };
    }

    private static ToolResultTurn GetToolResultTurn(JsonNode item)
    {
        string id = item["id"]?.GetValue<string>() ?? "id";
        string toolName = item["tool"]?.GetValue<string>() ?? "tool_name";

        string? error = item["error"]?.GetValue<string>();

        ToolReturn toolReturn = error switch
        {
            string err => ToolReturn.Success(err),
            _ => ToolReturn.Success("Successfull") // TODO : More?
        };

        return new (id, toolName, toolReturn);
    }

    private static List<ITurn> ParseTurnCompleted(JsonNode thing)
    {
        int? tokenCount =thing?["usage"]?["input_tokens"]?.GetValue<int>();
        // new ToolResultTurn()
        return [new TurnEnd(StopReason.EndTurn, tokenCount: tokenCount)];
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

    private string HandleMemoryMcp(MemoryMcp mcp)
    {
        Rhino.AI.Mcps.MemoryMcpManager.McpLease leaase = Rhino.AI.Mcps.MemoryMcpManager.RegisterMemoryMcp(mcp);
        return $"url='{leaase.Uri.AbsoluteUri}'";
    }

    // Codex splits -c keys on every dot, even inside TOML quotes.
    private static string ServerKey(string mcpName) => mcpName.Replace('.', '_').Replace(' ', '_');

    private static string TomlArray(IEnumerable<string> values)
        => $"[{string.Join(", ", values.Select(value => $"'{value}'"))}]";

}
