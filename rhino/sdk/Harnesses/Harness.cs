using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using FuzzySharp;
using FuzzySharp.PreProcess;

namespace Rhino.AI;

/// <summary>
/// The Harness is the exo-skeleton of the AI Agent.
/// It is where every tool, skill and permission is registered, and it can run a full Agent loop.
/// </summary>
public class Harness
{

    protected Loop Loop { get; }

    private Dictionary<string, Mcp> PrivateMcps { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All of the registered MCPs</summary>
    public IReadOnlyDictionary<string, Mcp> Mcps => PrivateMcps;

    private Dictionary<string, ISkill> PrivateSkills { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All of the registered Skills</summary>
    public IReadOnlyDictionary<string, ISkill> Skills => PrivateSkills;

    /// <summary>Configuration for the Harness</summary>
    public HarnessConfig Config { get; } = new();

    // NOTE : Token in harness is a bit iffy. BUT. In theory this is only used by one plugin and not shared...
    public Harness(PlugIns.PlugInToken token)
    {
        Loop = new(this);
        AddMcp(new DefaultToolsMcp(this, token));
    }

    /// <summary>
    /// Runs the Harness loop to completion.
    /// </summary>
    /// <param name="agent">An AI Agent to give access to the harness</param>
    /// <param name="start">Information to start the loop with</param>
    /// <param name="token">Cancellation Token</param>
    /// <returns>Every turn produced by the completed loop</returns>
    public virtual async Task<IEnumerable<ITurn>> LoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        List<ITurn> turns = [];
        await foreach (ITurn turn in StreamLoopAsync(agent, start, token).ConfigureAwait(false))
        {
            turns.Add(turn);
        }

        return turns;
    }

    /// <summary>
    /// Runs the Harness loop, streaming each turn as it is produced.
    /// </summary>
    /// <param name="agent">An AI Agent to give access to the harness</param>
    /// <param name="start">Information to start the loop with</param>
    /// <param name="token">Cancellation Token</param>
    /// <returns>Each turn of the loop as it arrives</returns>
    public virtual async IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation] CancellationToken token)
    {
        List<ITurn> turns = start is List<ITurn> s ? s : new(start);
        if (IsFirstTurn(turns))
        {
            InsertRhinoHarnessPrompt(ref turns);
        }

        await foreach (ITurn turn in Loop.StreamAsync(agent, start, token))
            yield return turn;
    }

    protected static bool InsertRhinoHarnessPrompt(ref List<ITurn> turns)
    {
        if (GetPrompt("RhinoHarnessPrompt.md") is string prompt)
        {
            turns.Add(new MessageTurn(prompt, RoleType.System));
            return true;
        }

        return false;
    }

    protected static string CliSystemPrompt(IEnumerable<ITurn> start)
        => string.Join("\n\n", start.OfType<SystemTurn>().Select(s => s.Prompt).Prepend(GetPrompt("RhinoHarnessPrompt.md")));

    protected static string GetPrompt(string fileName)
    {
        string resourceName = $"Rhino.AI.Prompts.{fileName}";
        using System.IO.Stream stream = typeof(RhinoHarness).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded prompt '{resourceName}' is missing.");
        using System.IO.StreamReader reader = new(stream);
        return reader.ReadToEnd();
    }

    protected static bool IsFirstTurn(IEnumerable<ITurn> start)
        => !start.Any(t => t is TurnEnd);

    /// <summary>
    /// Uses a tool and returns the result.
    /// </summary>
    /// <param name="agent">The Agent calling the tool, whose definition decides permissions</param>
    /// <param name="mcpName">The MCP name</param>
    /// <param name="toolName">The tool name</param>
    /// <param name="args">The args for the tool</param>
    /// <param name="token">Cancellation Token</param>
    /// <returns>The tool's result, or a failure if the MCP or tool is unavailable, refused, or timed out</returns>
    public async Task<ToolReturn> UseToolAsync(Agent agent, string mcpName, string toolName, List<IToolArg> args, CancellationToken token)
    {
        if (!Mcps.TryGetValue(mcpName, out Mcp? mcp) || mcp is null) return ToolReturn.Failure($"Mcp named {mcpName} is not available", "");
        if (!mcp.Enabled) return ToolReturn.Failure($"Mcp named {mcpName} is not enabled.", "Use a different mcp.");

        if (!mcp.Tools.TryGetValue(toolName, out ITool? tool) || tool is null)
        {
            IEnumerable<ITool> likelyTools = mcp.Tools.Values
                .OrderByDescending(t => Fuzz.WeightedRatio(toolName, t.Name, PreprocessMode.Full))
                .Take(3);
            IEnumerable<string> likelyToolNames = likelyTools.Select(t => t.Name);
            string likelyToolString = string.Join(" or ", likelyToolNames);
            return ToolReturn.Failure($"Tool named {toolName} is not available in {mcpName}", $"Did you mean {likelyToolString}?");
        }

        if (!await mcp.InitAsync(token)) return ToolReturn.Failure("Could not initialize MCP", "Unknown");

        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(McpTimeouts.ToolCall);
        try
        {
            return await mcp.RunToolAsync(agent, this, toolName, args, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return ToolReturn.Failure($"{toolName} timed out after {McpTimeouts.ToolCall}.", "Try a smaller or simpler operation.");
        }
    }

    /// <summary>
    /// Adds an <see cref="Mcp"/> to the <see cref="Harness"/>
    /// </summary>
    /// <param name="mcp">An mcp</param>
    /// <returns><see langword="true"/> on success</returns>
    public bool AddMcp(Mcp mcp) => PrivateMcps.TryAdd(mcp.Name, mcp);

    public void ReplaceMcp(Mcp mcp)
    {
        PrivateMcps.Remove(mcp.Name);
        PrivateMcps.Add(mcp.Name, mcp);
    }

    /// <summary>
    /// Adds an <see cref="ISkill"/> to the <see cref="Harness"/>
    /// </summary>
    /// <param name="skill">A skill</param>
    /// <returns><see langword="true"/> on success</returns>
    public bool AddSkill(ISkill skill) => PrivateSkills.TryAdd(skill.Name, skill);

    internal void RaiseBeforeAskUserPermission(IPermissionRequest e) => BeforeAskUserPermission?.Invoke(this, e);
    public event EventHandler<IPermissionRequest>? BeforeAskUserPermission;

    /// <summary>
    /// Awaitable Permissions request. If null, permissions are assumed true.
    /// </summary>
    public virtual Func<IPermissionRequest, CancellationToken, Task>? AskUser { get; set; }

}

/// <summary>
/// Harness Configuration
/// </summary>
public sealed class HarnessConfig
{

    /// <summary>The working directory of the Harness</summary>
    public string CurrentWorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

}

/// <summary>
/// A request for permission
/// </summary>
public interface IPermissionRequest
{

    /// <summary>The name of the MCP</summary>
    public string McpName { get; }

    /// <summary>The name of the tool</summary>
    public string ToolName { get; }

    /// <summary>Current permission status of the request</summary>
    public bool HasPermission { get; }

    /// <summary>The args of the permission</summary>
    public IReadOnlyList<IToolArg> Args { get; }

}

/// <summary>
/// A request for permission
/// </summary>
internal sealed class PermissionRequest(Agent agent, string mcpName, string toolName, IReadOnlyList<string> declaredArgs, IReadOnlyList<IToolArg> args) : EventArgs, IPermissionRequest
{

    public PermissionRequest(Agent agent, Mcp mcp, ITool tool, IReadOnlyList<IToolArg> args)
        : this(agent, mcp.Name, tool.Name, Array.ConvertAll(tool.Args, arg => arg.Name), args) { }

    /// <summary>The agent whose tool call is waiting on the answer</summary>
    public Agent Agent { get; } = agent;

    /// <summary>The name of the MCP</summary>
    public string McpName { get; } = mcpName;

    /// <summary>The name of the tool</summary>
    public string ToolName { get; } = toolName;

    /// <summary>Every argument the tool declares, supplied in this call or not</summary>
    public IReadOnlyList<string> DeclaredArgs { get; } = declaredArgs;

    /// <summary>Current permission status of the request</summary>
    public bool HasPermission { get; set; } = false;

    /// <summary>The args of the permission</summary>
    public IReadOnlyList<IToolArg> Args { get; } = args;

}
