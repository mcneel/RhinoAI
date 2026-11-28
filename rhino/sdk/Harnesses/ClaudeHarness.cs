using System;
using System.IO;
using System.Linq;
using System.Text;
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

    public override async IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation] CancellationToken token)
    {
        if (agent.Model is not ClaudeDesktopModel claudeModel) yield break;
        if (await EnsureLoggedIn(token, claudeModel) is string loginFailureMessage)
        {
            yield return new MessageTurn(loginFailureMessage, RoleType.System) { Success = false };
            yield break;
        }

        string prompt = UserLine(start.OfType<MessageTurn>().Last(m => m.Role == RoleType.User));

        List<ITurn> turns = new(start);

        Guid sessionId = agent.Config.SessionId == Guid.Empty ? Guid.NewGuid() : agent.Config.SessionId;
        using AIProcess process = CreateClaude(agent, sessionId, turns, token);
        if (TryStart(process) is ITurn failTurn)
        {
            yield return failTurn;
            yield break;
        }

        using CancellationTokenRegistration _ = token.Register(process.Kill);

        ClaudeControlChannel channel = new(this, agent, process.StandardInput, token);
        Task writing = channel.SendAsync(prompt);

        bool ended = false;
        await foreach (ITurn turn in StreamLoopAsync(process, agent, sessionId, channel, token))
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

        // No exit code as the reason: the stderr lines already yielded say why, and "1" does not.
        if (!ended)
            yield return new TurnEnd(StopReason.Error);
    }

    private bool LoggedIn { get; set; } = false;
    private async Task<string?> EnsureLoggedIn(CancellationToken token, ClaudeDesktopModel claudeModel)
    {
        if (LoggedIn) return null;
        try
        {
            using Process claude = GetClaudeProcess(token, claudeModel);
            claude.StartInfo.AddArgument("auth");
            claude.StartInfo.AddArgument("status");

            using CancellationTokenRegistration _ = token.Register(() =>
            {
                try { claude.Kill(entireProcessTree: true); } catch (Exception) { }
            });

            if (!claude.Start())
            {
                return "Could not start Claude to Log In";
            }

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
                return null;
            }
            return await LogIn(claudeModel, token);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private async Task<string?> LogIn(ClaudeDesktopModel model, CancellationToken token)
    {
        using Process claude = GetClaudeProcess(token, model);
        claude.StartInfo.AddArgument("auth");
        claude.StartInfo.AddArgument("login");
        claude.StartInfo.AddArgument("--claudeai");

        using CancellationTokenRegistration _ = token.Register(() =>
        {
            try { claude.Kill(entireProcessTree: true); } catch (Exception) { }
        });

        if (!claude.Start()) return "Could not start Claude to Log In";

        List<string> lines = [];
        while (true)
        {
            string? line = await claude.StandardOutput.ReadLineAsync(token).ConfigureAwait(false);
            if (line is null || line.Length == 0) break;
            lines.Add(line);
        }

        // TODO : What is user closes the browser?

        await claude.WaitForExitAsync(token);

        if (claude.ExitCode == 0) return null;

        string data = string.Join("\n", lines);
        // JsonNode? node = JsonObject.Parse(data);

        return data;
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
        string exePath = claudeModel.ExePath ?? throw new FileNotFoundException("Claude CLI not found.");

        Process process = new()
        {
            EnableRaisingEvents = true,
            StartInfo = new()
            {
                UseShellExecute = false,
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

    private static ITurn? TryStart(AIProcess process)
    {
        try
        {
            process.Start();
            return null;
        }
        catch (Exception ex)
        {
            return new MessageTurn(ex.Message, RoleType.System);
        }
    }

    private AIProcess CreateClaude(Agent agent, Guid sessionId, IEnumerable<ITurn> start, CancellationToken token)
    {
        // TODO : Check agent.Model
        Process process = GetClaudeProcess(token, agent.Model!);

        process.StartInfo.Environment["MCP_TIMEOUT"] = ((long)McpTimeouts.Startup.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);

        process.StartInfo.AddArgument("-p");

        process.StartInfo.AddArgument(agent.Config.SessionId == Guid.Empty ? "--session-id" : "--resume");
        process.StartInfo.AddArgument(sessionId.ToString());

        process.StartInfo.AddArgument("--permission-prompt-tool");
        process.StartInfo.AddArgument("stdio");

        process.StartInfo.AddArgument("--allowedTools");
        process.StartInfo.AddArgument(string.Join(" ", AllowedTools(agent)));

        process.StartInfo.AddArgument("--disallowedTools");
        process.StartInfo.AddArgument(string.Join(" ", DisallowedTools(agent)));

        // Model
        //   --model <model>                       Model for the current session. Provide an alias for the latest model (e.g. 'fable', 'opus', or 'sonnet') or a model's full name (e.g. 'claude-fable-5').
        if (!ModelDefinition.IsDefault(agent.Model?.Name) && !string.IsNullOrEmpty(agent.Model?.Name))
        {
            process.StartInfo.AddArgument("--model");
            process.StartInfo.AddArgument(agent.Model.Name);
        }

        // --fallback-model <model>              Enable automatic fallback to specified model(s) when the default model is overloaded or not available.
        // process.StartInfo.AddArgument("--fallback-model");
        // process.StartInfo.AddArgument("opus");

        // --effort <level> Effort level for the current session (low, medium, high, xhigh, max)
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

        process.StartInfo.AddArgument("--append-system-prompt");
        process.StartInfo.AddArgument(CliSystemPrompt(start));

        process.StartInfo.AddArgument("--disable-slash-commands");

        return new(process);
    }

    // A leased in-memory MCP already runs Rhino's permission chain per call, so letting the CLI ask too would prompt twice.
    private IEnumerable<string> AllowedTools(Agent agent)
    {
        foreach (Mcp mcp in Mcps.Values)
        {
            if (!mcp.Enabled) continue;

            if (mcp is MemoryMcp)
            {
                yield return $"{WirePrefix(mcp.Name)}*";
                continue;
            }

            foreach (string tool in PermissionSet.ToolsAllowedForAnyArguments(agent.Definition, mcp.Name))
                yield return WirePrefix(mcp.Name) + tool;
        }
    }

    private IEnumerable<string> DisallowedTools(Agent agent)
    {
        foreach (Mcp mcp in Mcps.Values)
        {
            if (!mcp.Enabled) continue;

            IEnumerable<string> denied = AIInstances.Preferences.Permissions.ToolsDeniedForAnyArguments(mcp.Name)
                .Concat(agent.Definition?.Permissions.ToolsDeniedForAnyArguments(mcp.Name) ?? []);
            foreach (string tool in denied.Distinct(StringComparer.OrdinalIgnoreCase))
                yield return WirePrefix(mcp.Name) + tool;
        }
    }

    internal static string WirePrefix(string mcpName)
        => $"mcp__{mcpName.Replace(' ', '_')}__";

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

    private async IAsyncEnumerable<ITurn> StreamLoopAsync(AIProcess process, Agent agent, Guid launchedSessionId, ClaudeControlChannel channel, [EnumeratorCancellation] CancellationToken token)
    {
        ClaudeStreamParser parser = new(this, launchedSessionId, reported => agent.Config.SessionId = reported, channel.Answer);
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                foreach (ITurn turn in parser.Parse(line))
                {
                    if (turn is TurnEnd)
                        await channel.CloseAsync().ConfigureAwait(false);

                    yield return turn;
                }
            }
        }
        finally
        {
            DisposeLeases();
        }
    }

    // TODO : Pin the MCP protocol era via env var (MCP_PROTOCOL_NEGOTIATION=legacy, undocumented) instead of relying on the discover probe falling back
    internal string GetMcpJsons(Agent agent)
    {
        JsonObject array = new();

        // NOTE : Do the MCP configs need to be done? Can turns not just be run?
        string mcpJson = string.Empty;
        foreach (Mcp mcp in Mcps.Values)
        {
            if (!mcp.Enabled) continue;

            // TODO : MCP's
            if (mcp is StdioMcp stdioMcp)
            {
                array[mcp.Name] = new JsonObject()
                {
                    ["type"] = "stdio",
                    ["command"] = stdioMcp.Command,
                    ["args"] = new JsonArray([.. stdioMcp.Args.Select(arg => JsonValue.Create(arg))]),
                    ["env"] = new JsonObject(ToNode(stdioMcp.Env))
                };
            }
            else if (mcp is HttpMcp httpMcp)
            {
                array[mcp.Name] = new JsonObject()
                {
                    ["type"] = "http",
                    ["url"] = httpMcp.Url.AbsoluteUri,
                    ["headers"] = new JsonObject(ToNode(httpMcp.Headers))
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

    private static IEnumerable<KeyValuePair<string, JsonNode?>> ToNode(IReadOnlyDictionary<string, string> dict)
    {
        foreach (KeyValuePair<string, string> kvp in dict)
        {
            if (JsonValue.Create(kvp.Value) is not JsonNode node) continue;
            yield return new KeyValuePair<string, JsonNode?>(kvp.Key, node);
        }
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
