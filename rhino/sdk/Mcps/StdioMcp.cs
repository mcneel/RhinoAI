using System;
using System.IO;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Rhino.AI;

/// <summary>
/// A STandard Input Output MCP that drives an executable
/// </summary>
public class StdioMcp(string name, string command, IEnumerable<string> args, IDictionary<string, string> env) : Mcp(name)
{

    private const int RememberedErrorLines = 20;
    private const int GracefulExitMilliseconds = 2000;

    public StdioMcp(string name, string command) : this(name, command, [], new Dictionary<string, string>())
    {
    }

    public string Command { get; } = command;

    public IReadOnlyList<string> Args { get; } = new List<string>(args);

    public IReadOnlyDictionary<string, string> Env { get; } = new Dictionary<string, string>(env);

    private Process? Process { get; set; }
    private bool Disposed { get; set; }
    private int LastId { get; set; }

    private StreamReader? Output => Process?.StandardOutput;
    private StreamReader? Error => Process?.StandardError;
    private StreamWriter? Input => Process?.StandardInput;

    private Queue<string> RecentErrors { get; } = new(RememberedErrorLines);
    private Task ErrorDrain { get; set; } = Task.CompletedTask;

    private bool Initialized { get; set; } = false;
    public override async Task<bool> InitAsync(CancellationToken token)
    {
        if (Initialized) return true;
        Process = new()
        {
            StartInfo = new(Command)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (string arg in Args)
            Process.StartInfo.AddArgument(arg);
        foreach (KeyValuePair<string, string> variable in Env)
            Process.StartInfo.Environment[variable.Key] = variable.Value;

        if (!Process.Start()) return false;

        ErrorDrain = DrainErrorAsync();

        foreach (McpClient.ListedTool tool in await McpClient.ConnectAsync(RequestAsync, NotifyAsync, token).ConfigureAwait(false))
            PrivateTools[tool.Name] = new StdioTool(this, tool.Name, tool.Description, tool.ReadOnly, tool.Destructive, tool.Args);

        Initialized = true;
        return true;
    }

    public async Task<ToolReturn> RunToolAsync(string toolName, IReadOnlyList<IToolArg> args, CancellationToken token)
    {
        if (!Initialized) await InitAsync(token).ConfigureAwait(false);

        if (!PrivateTools.TryGetValue(toolName, out ITool? tool))
            return ToolReturn.UnknownTool(Name, toolName);

        return await McpClient.CallToolAsync(RequestAsync, tool, args, token).ConfigureAwait(false);
    }

    private async Task<JsonNode?> RequestAsync(string method, JsonObject? parameters, CancellationToken token)
    {
        JsonObject message = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = ++LastId,
            ["method"] = method,
        };
        if (parameters is not null)
            message["params"] = parameters;

        string wanted = LastId.ToString(CultureInfo.InvariantCulture);
        await WriteAsync(message, token).ConfigureAwait(false);

        if (Output is not StreamReader output)
            throw new InvalidOperationException($"MCP '{Name}' is not running.");

        while (await output.ReadLineAsync(token).ConfigureAwait(false) is string line)
        {
            JsonNode? response = JsonNode.Parse(line);
            if (response?["id"]?.ToJsonString() != wanted)
                continue;

            if (response["error"] is JsonNode error)
                throw new InvalidOperationException($"MCP '{Name}' {method} failed: {error.ToJsonString()}{Stderr()}");

            return response["result"];
        }

        throw new InvalidOperationException($"MCP '{Name}' closed stdout during {method}.{Stderr()}");
    }

    private Task NotifyAsync(string method, CancellationToken token)
        => WriteAsync(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method }, token);

    private async Task WriteAsync(JsonObject message, CancellationToken token)
    {
        if (Input is not StreamWriter input)
            throw new InvalidOperationException($"MCP '{Name}' is not running.");

        await input.WriteLineAsync(message.ToJsonString().AsMemory(), token).ConfigureAwait(false);
        await input.FlushAsync(token).ConfigureAwait(false);
    }

    public override bool RegisterTool(ITool tool) => false;

    private async Task DrainErrorAsync()
    {
        if (Error is not StreamReader error)
            return;

        while (await error.ReadLineAsync().ConfigureAwait(false) is string line)
        {
            lock (RecentErrors)
            {
                RecentErrors.Enqueue(line);
                if (RecentErrors.Count > RememberedErrorLines)
                    RecentErrors.Dequeue();
            }
        }
    }

    private string Stderr()
    {
        lock (RecentErrors)
            return RecentErrors.Count == 0 ? "" : $" stderr: {string.Join(" | ", RecentErrors)}";
    }

    ~StdioMcp()
    {
        // AI : Claude = May be a better way
        Dispose();
    }

    public override void Dispose()
    {
        if (Disposed)
            return;

        Disposed = true;

        if (Process is not Process process)
            return;

        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                if (!process.WaitForExit(GracefulExitMilliseconds))
                    process.Kill(true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or AggregateException or Win32Exception)
        {
        }

        process.Dispose();
        Process = null;
        PrivateTools.Clear();
    }

    private sealed record StdioTool(StdioMcp Host, string Name, string Description, bool ReadOnly, bool Destructive, ToolParameter[] Args) : ITool
    {
        public async Task<ToolReturn> UseAsync(IReadOnlyList<IToolArg> args, CancellationToken token)
            => await Host.RunToolAsync(Name, args, token);
    }

}
