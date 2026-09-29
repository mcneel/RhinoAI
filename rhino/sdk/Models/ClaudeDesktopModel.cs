using System;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Text.Json;
using System.Text;
using System.IO;
using System;

namespace Rhino.AI.Models;

/// <summary>A Claude Desktop Model</summary>
internal sealed class ClaudeDesktopModel(string name) : DesktopModel(name, "Anthropic")
{

    private List<ITurn> Turns { get; } = [];

    public override bool Available => File.Exists(ClaudeExePath);

    // TODO : Check Definitions.json
    private static string ClaudeExePath
        => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude");

    protected async override Task<IEnumerable<ITurn>> SendPrivateAsync(IHarness harness, IEnumerable<ITurn> turn, CancellationToken token)
    {
        Turns.Clear();
        Process process = new()
        {
            EnableRaisingEvents = true,
            StartInfo = new()
            {
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,

                FileName = ClaudeExePath,
                WorkingDirectory = "/Users/sykes/Desktop",
                CreateNoWindow = true,
                // ArgumentList
            }
        };

        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);
        process.StartInfo.StandardInputEncoding = utf8;
        process.StartInfo.StandardOutputEncoding = utf8;
        process.StartInfo.StandardErrorEncoding = utf8;

        process.StartInfo.ArgumentList.Add("-p");

        string mcpName = "rhino";

        // --allowedTools, --allowed-tools <tools...> Comma or space-separated list of tool names to allow (e.g. "Bash(git *) Edit")
        string allowedTools = string.Empty;
        foreach (Permission permission in harness.Permissions.AllowedTools())
        {
            allowedTools += $"mcp__{mcpName}__{permission.ToolName}";
            // permission.ArgumentPermissions // TODO : Use these for smarter permissions
            allowedTools += " ";
        }
        process.StartInfo.ArgumentList.Add("--allowedTools");
        process.StartInfo.ArgumentList.Add(allowedTools);


        // --disallowedTools, --disallowed-tools <tools...> Comma or space-separated list of tool names to deny (e.g. "Bash(git *) Edit")
        string disallowedTools = string.Empty;
        foreach (Permission permission in harness.Permissions.ProhibitedTools())
        {
            disallowedTools += $"mcp__{mcpName}__{permission.ToolName}";
            // permission.ArgumentPermissions // TODO : Use these for smarter permissions
            disallowedTools += " ";
        }
        process.StartInfo.ArgumentList.Add("--disallowedTools");
        process.StartInfo.ArgumentList.Add(disallowedTools);

        // Model
        //   --model <model>                       Model for the current session. Provide an alias for the latest model (e.g. 'fable', 'opus', or 'sonnet') or a model's full name (e.g. 'claude-fable-5').
        process.StartInfo.ArgumentList.Add("--model");
        process.StartInfo.ArgumentList.Add(name);

        if (UserPermissions.IsPermitted("anthropic", "opus"))
        {
            // --fallback-model <model>              Enable automatic fallback to specified model(s) when the default model is overloaded or not available.
            process.StartInfo.ArgumentList.Add("--fallback-model");
            process.StartInfo.ArgumentList.Add("opus");
        }

        // --no-session-persistence              Disable session persistence - sessions will not be saved to disk and cannot be resumed (only works with --print)
        process.StartInfo.ArgumentList.Add("--no-session-persistence");

        // --strict-mcp-config                   Only use MCP servers from --mcp-config, ignoring all other MCP configurations
        process.StartInfo.ArgumentList.Add("--strict-mcp-config");

        // --mcp-config <configs...>             Load MCP servers from JSON files or strings (space-separated)
        process.StartInfo.ArgumentList.Add("--mcp-config");
        process.StartInfo.ArgumentList.Add(GetMcpJsons(harness));

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

        // process.StartInfo.ArgumentList.Add("--append-system-prompt");
        // string prompt = JsonSerializer.Serialize(turn);
        // process.StartInfo.ArgumentList.Add(prompt);

        process.StartInfo.ArgumentList.Add("--disable-slash-commands");

        process.ErrorDataReceived += ReadErrors;
        process.OutputDataReceived += ReadOutput;

        // process.StandardInput

        if (!process.Start()) { }
        process.BeginErrorReadLine();

        _ = Task.Run(() => ReadLoopAsync(process, token), token);
        _ = Task.Run(() => WriteLoopAsync(process, turn), token);

        token.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
        });

        await process.WaitForExitAsync();

        // Other possible args

        // --system-prompt <prompt>              System prompt to use for the session

        // attach <id>                           Open a background session in this terminal. <id> is the short id that `claude --bg` prints and `claude agents` lists

        // auth                                  Manage authentication
        // setup-token                           Set up a long-lived authentication token (requires Claude subscription)

        // process.Exited 

        return Turns;
    }

    private async Task WriteLoopAsync(Process process, IEnumerable<ITurn> turn)
    {
        string prompt = JsonSerializer.Serialize(turn);
        await process.StandardInput.WriteLineAsync(prompt).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
        process.StandardInput.Close();
    }

    private async Task ReadLoopAsync(Process process, CancellationToken token)
    {
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

                    Turns.AddRange(newturns);
                }
                catch
                {

                }
            }
        }
        catch { }
    }

    private static List<ITurn> ParseUser(JsonNode node)
    {
        JsonNode? message = node["message"];
        JsonNode? content = message?["content"];
        if (content is not JsonArray contents) return [];

        List<ITurn> turns = new(contents.Count);
        foreach (JsonNode? cont in contents)
        {
            ITurn? turn = cont?["type"]?.ToString() switch
            {
                "tool_result" => GetToolResultTurn(cont),

                _ => null,
            };

            if (turn is null) continue;

            turns.Add(turn);
        }

        return turns;
    }

    private static ITurn? GetToolResultTurn(JsonNode content)
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

            ToolResultTurn toolResult = new(id, toolName, ToolReturn.Success(""));
            return toolResult;
        }
        else if (kind == JsonValueKind.String)
        {
            string? contentResult = contentContent!.GetValue<string>();
            if (string.IsNullOrEmpty(contentResult)) return null;
            // TODO : What type is this? - Is it a message?
            return new MessageTurn(contentResult, RoleType.User);
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
            if (subType is not null && subType.Equals("init")) return [];
        }

        return [];
    }

    private static List<ITurn> ParseResult(JsonNode node)
    {
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

            if (string.Equals(stopReason, "end_turn")) return [new TurnEnd(reason)];
        }

        return [];
    }

    private static List<ITurn> ParseAssisant(JsonNode node)
    {
        JsonNode? message = node["message"];
        JsonNode? content = message?["content"];
        if (content is not JsonArray contents) return [];

        // TODO : Input tokens or output tokens?
        int? tokenCount = message?["usage"]?["input_tokens"]?.GetValue<int>() ?? null;

        List<ITurn> turns = new(contents.Count);
        foreach (JsonNode? cont in contents)
        {
            ITurn? turn = cont?["type"]?.ToString() switch
            {
                "tool_use" => GetToolTurn(cont, tokenCount),
                "thinking" => GetThinking(cont, tokenCount),
                "text" => GetText(cont, tokenCount),

                _ => null,
            };

            if (turn is null) continue;

            turns.Add(turn);
        }

        return turns;
    }

    private static MessageTurn? GetText(JsonNode content, int? tokenCount)
    {
        string? text = content["text"]?.GetValue<string>();
        if (string.IsNullOrEmpty(text)) return null;
        return new MessageTurn(text, RoleType.Assistant, tokenCount: tokenCount);
    }

    private static ThinkingTurn? GetThinking(JsonNode content, int? tokenCount)
    {
        string? thinking = content["thinking"]?.GetValue<string>();
        if (string.IsNullOrEmpty(thinking)) return null;
        return new ThinkingTurn(thinking);
    }

    private static ToolTurn? GetToolTurn(JsonNode content, int? tokenCount)
    {
        string? id = content["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(id)) return null;

        string? name = content["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(name)) return null;

        List<IToolArg> args = GetArgs(content["input"]);
        return new ToolTurn(id, name, args, tokenCount: tokenCount);
    }

    private static List<IToolArg> GetArgs(JsonNode? node)
    {
        List<IToolArg> args = [];
        if (node is not JsonObject obj) return args;

        for (int i = 0; i < obj.Count; i++)
        {
            JsonNode? kvp = obj[i];
            if (kvp is null) continue;

            string propName = kvp.GetPropertyName();
            JsonValueKind kind = kvp.GetValueKind();

            IToolArg? arg = kind switch
            {
                JsonValueKind.String => new ToolString(propName, kvp.GetValue<string>()),
                JsonValueKind.Number => new ToolNumber(propName, kvp.GetValue<double>()),
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

    // TODO : This
    // {"type":"rate_limit_event","rate_limit_info":{"status":"allowed","resetsAt":1790665800,"rateLimitType":"five_hour","overageStatus":"rejected","overageDisabledReason":"org_level_disabled","isUsingOverage":false,"unifiedWindows":{"five_hour":{"utilization":0.01,"resetsAt":1790665800},"seven_day":{"utilization":0.04,"resetsAt":1791007200}}},"uuid":"159c04ba-5afd-423c-911e-ba28c6517b93","session_id":"636253d7-0e0b-407c-8330-1a461bd94a13"}

    private void ReadErrors(object sender, DataReceivedEventArgs e)
    {
        Debug.WriteLine(e.Data);
        ;
    }

    private void ReadOutput(object sender, DataReceivedEventArgs e)
    {
        ;
    }

    private static string GetMcpJsons(IHarness harness)
    {
        JsonObject array = new();

        // NOTE : Do the MCP configs need to be done? Can turns not just be run?
        string mcpJson = string.Empty;
        foreach (IMcp mcp in harness.Mcps.Values)
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
