using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Collections.Generic;

using Rhino.AI.Models;

namespace Rhino.AI;

// Under --permission-prompt-tool stdio the CLI blocks on stdin for each answer, so stdin stays open until the turn ends.
internal sealed class ClaudeControlChannel(Harness harness, Agent agent, StreamWriter input, CancellationToken token)
{

    private SemaphoreSlim WriteGate { get; } = new(1, 1);

    private bool IsClosed { get; set; }

    public async Task SendAsync(string line)
    {
        await WriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsClosed) return;
            await input.WriteLineAsync(line).ConfigureAwait(false);
            await input.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            WriteGate.Release();
        }
    }

    public async Task CloseAsync()
    {
        await WriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsClosed) return;
            IsClosed = true;
            input.Close();
        }
        catch (IOException) { }
        finally
        {
            WriteGate.Release();
        }
    }

    public void Answer(ClaudePermissionAsk ask) => _ = AnswerAsync(ask);

    private async Task AnswerAsync(ClaudePermissionAsk ask)
    {
        JsonObject reply = new()
        {
            ["type"] = "control_response",
            ["response"] = new JsonObject
            {
                ["subtype"] = "success",
                ["request_id"] = ask.RequestId,
                ["response"] = await DecideAsync(ask).ConfigureAwait(false),
            },
        };

        try
        {
            await SendAsync(reply.ToJsonString()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
    }

    private async Task<JsonObject> DecideAsync(ClaudePermissionAsk ask)
    {
        try
        {
            if (await IsAllowedAsync(ask).ConfigureAwait(false))
                return new JsonObject { ["behavior"] = "allow", ["updatedInput"] = ask.Input?.DeepClone() ?? new JsonObject() };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Deny(ex.Message);
        }

        ToolReturn refused = ToolReturn.Refused(ask.ToolName ?? "this tool");
        return Deny($"{refused.Message}\n\n{refused.Guidance}");
    }

    private static JsonObject Deny(string message) => new() { ["behavior"] = "deny", ["message"] = message };

    private async Task<bool> IsAllowedAsync(ClaudePermissionAsk ask)
    {
        if (ask.ServerName is null || ask.ToolName is null) return false;
        if (!harness.Mcps.TryGetValue(ask.ServerName, out Mcp? mcp) || mcp is null) return false;

        string prefix = ClaudeHarness.WirePrefix(mcp.Name);
        if (!ask.ToolName.StartsWith(prefix, StringComparison.Ordinal)) return false;
        string toolName = ask.ToolName[prefix.Length..];

        ITool? tool = mcp.Tools.GetValueOrDefault(toolName);
        IReadOnlyList<IToolArg> args = ToolArgs.FromJson(tool, ask.Input);
        Permissability permission = PermissionSet.ResolvePermissionChain(agent.Definition, mcp.Name, toolName, [.. args]);

        if (permission == Permissability.Deny) return false;
        if (permission != Permissability.Ask || harness.AskUser is null) return true;

        string[] declaredOrSupplied = tool is null ? [.. args.Select(arg => arg.Name)] : Array.ConvertAll(tool.Args, arg => arg.Name);
        PermissionRequest request = new(agent, mcp.Name, toolName, declaredOrSupplied, args);
        harness.RaiseBeforeAskUserPermission(request);
        await harness.AskUser.Invoke(request, token).ConfigureAwait(false);
        return request.HasPermission;
    }

}

internal sealed record ClaudePermissionAsk(string RequestId, string? ServerName, string? ToolName, JsonNode? Input);
