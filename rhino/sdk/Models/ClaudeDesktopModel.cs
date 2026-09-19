using System;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using System.Text.Json;
using System.Text;

namespace Rhino.AI.Models;

internal sealed class ClaudeDesktopModel(string name) : DesktopModel(name, "Anthropic")
{

    public async override Task<IEnumerable<ITurn>> SendAsync(IHarness harness, IEnumerable<ITurn> turn, CancellationToken token)
    {
        Process process = new()
        {
            EnableRaisingEvents = true,
            StartInfo = new()
            {
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,

                FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude"),
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
        // --fallback-model <model>              Enable automatic fallback to specified model(s) when the default model is overloaded or not available.
        process.StartInfo.ArgumentList.Add("--fallback-model");
        process.StartInfo.ArgumentList.Add("opus");

        // --no-session-persistence              Disable session persistence - sessions will not be saved to disk and cannot be resumed (only works with --print)
        process.StartInfo.ArgumentList.Add("--no-session-persistence");

        // --strict-mcp-config                   Only use MCP servers from --mcp-config, ignoring all other MCP configurations
        process.StartInfo.ArgumentList.Add("--strict-mcp-config");
        process.StartInfo.ArgumentList.Add("true");

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

        // process.StartInfo.ArgumentList.Add("--append-system-prompt");
        // string prompt = JsonSerializer.Serialize(turn);
        // process.StartInfo.ArgumentList.Add(prompt);

        process.StartInfo.ArgumentList.Add("--disable-slash-commands");

        process.ErrorDataReceived += ReadErrors;
        process.OutputDataReceived += ReadOutput;

        // process.StandardInput

        if (!process.Start()) { }
        // process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        _ = Task.Run(() => ReadLoopAsync(process), token);
        _ = Task.Run(() => WriteLoopAsync(process, turn), token);

        await process.WaitForExitAsync();

        // Other possible args

        // --system-prompt <prompt>              System prompt to use for the session

        // attach <id>                           Open a background session in this terminal. <id> is the short id that `claude --bg` prints and `claude agents` lists

        // auth                                  Manage authentication
        // setup-token                           Set up a long-lived authentication token (requires Claude subscription)

        // process.Exited 

        return [];
    }

    private async Task WriteLoopAsync(Process process, IEnumerable<ITurn> turn)
    {
        string prompt = JsonSerializer.Serialize(turn);
        await process.StandardInput.WriteLineAsync(prompt).ConfigureAwait(false);
        await process.StandardInput.FlushAsync().ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(Process process)
    {
        try
        {
            string? line;
            while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
            {
                if (line.Length == 0)
                    continue;
                try
                {
                    ;

                }
                catch (Exception ex)
                {
                    ;
                }
            }
        }
        catch { }
    }

    private void ReadErrors(object sender, DataReceivedEventArgs e)
    {
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
                    ["command"] = stdioMcp.ProcessPath.AbsolutePath
                    // ["args"] = ""
                    // ["env"] = ""
                };
            }
            else if (mcp is HttpMcp httpMcp)
            {
                array[mcp.Name] = new JsonObject()
                {
                    ["type"] = "http",
                    ["url"] = httpMcp.Url.AbsolutePath
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
