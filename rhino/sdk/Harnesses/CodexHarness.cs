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

            string[] codexTools = ["shell_tool", "unified_exec", "code_mode_host", "multi_agent", "goals",
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
        List<string> stringies = [];
        try
        {
            string? line = null;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (string.IsNullOrEmpty(line)) continue;

                stringies.Add(line);
                try
                {
                    JsonNode? thing = JsonObject.Parse(line) ?? throw new Exception($"Could not parse {line}");

                    string? turnId = thing["type"]?.GetValue<string>();
                    if (string.IsNullOrEmpty(turnId)) continue;

                    string[] turnIds = turnId.Split('.');
                    if (turnIds.Length != 2)
                    {
                        throw new Exception($"{turnId} was unexpected!");
                    }

                    string turnType = turnIds[0].ToLowerInvariant();
                    string turnStatus = turnIds[1].ToLowerInvariant();

                    if (string.Equals(turnId, "thread.started", StringComparison.OrdinalIgnoreCase))
                    {
                        SessionId = thing["thread_id"]?.GetValue<Guid>();
                        continue;
                    }
                    else if (string.Equals(turnId, "item.completed", StringComparison.OrdinalIgnoreCase))
                    {
                        // Code Mode is unavailable because code-mode host is disabled. Code mode will fail closed; enable `features.code_mode_host` and install `codex-code-mode-host`."
                        // Ignore this message
                        string? message = thing["item"]?["message"]?.GetValue<string>();
                        if (message is not null && message.Contains("Code Mode is unavailable", StringComparison.OrdinalIgnoreCase)) continue;
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
                            _ => [],
                        },

                        "item" => turnStatus switch
                        {
                            "completed" => ParseItemCompleted(thing),
                            _ => []
                        },

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

    private static List<ITurn> ParseItemCompleted(JsonNode thing)
    {
        JsonNode? item = thing["item"];
        if (item is null) return [];

        JsonNode? type = item["type"];
        if (type is null) return [];

        if (string.Equals(type?.GetValue<string>(), "agent_message", StringComparison.OrdinalIgnoreCase))
        {
            string message = item["text"]?.GetValue<string>() ?? item.ToJsonString();
            return [new MessageTurn(message, RoleType.Assistant)];
        }
        
        return [];
    }

    private static List<ITurn> ParseTurnCompleted(JsonNode thing)
    {
        int? tokenCount =thing?["usage"]?["input_tokens"]?.GetValue<int>();
        return [new TurnEnd(StopReason.EndTurn, tokenCount: tokenCount)];
    }

    private static DateTime? GetTimestamp(JsonNode node)
    {
        string? timestamp = node["timestamp"]?.GetValue<string>();
        if (!DateTime.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime parsed)) return null;
        return parsed;
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

    private IEnumerable<string> GetMcpArgs()
    {
        string disabledTools = TomlArray(Permissions.ProhibitedTools().Select(permission => permission.ToolName));

        foreach (IMcp mcp in Mcps.Values)
        {
            string? transport = mcp switch
            {
                StdioMcp stdioMcp => $"command='{stdioMcp.ProcessPath.LocalPath}'",
                HttpMcp httpMcp => $"url='{httpMcp.Url.AbsoluteUri}'",
                _ => null,
            };
            if (transport is null) continue;

            yield return "-c";
            yield return $"mcp_servers.{ServerKey(mcp.Name)}={{{transport}, disabled_tools={disabledTools}}}";
        }
    }

    // Codex splits -c keys on every dot, even inside TOML quotes.
    private static string ServerKey(string mcpName) => mcpName.Replace('.', '_');

    private static string TomlArray(IEnumerable<string> values)
        => $"[{string.Join(", ", values.Select(value => $"'{value}'"))}]";

}
