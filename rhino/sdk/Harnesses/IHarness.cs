using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI;

/// <summary>
/// The Harness is the exo-skeleton of the AI Agent.
/// It is where every tool, skill and permission is registered.
/// </summary>
public interface IHarness
{

    /// <summary>Permissions for the MCPs and Tools</summary>
    public PermissionSet Permissions { get; }

    public HarnessConfig Config { get; }

#region Extensions

    /// <summary>All of the registered MCPs</summary>
    public IReadOnlyDictionary<string, IMcp> Mcps { get; }
    
    /// <summary>All of the registered Skills</summary>
    public IReadOnlyDictionary<string, ISkill> Skills { get; }

    /// <summary>
    /// Adds an <see cref="IMcp"/> to the <see cref="IHarness"/>
    /// </summary>
    /// <param name="mcp">An mcp</param>
    /// <returns><see cref="true"> on success</returns>
    public bool AddMcp(IMcp mcp);
    
    /// <summary>
    /// Adds an <see cref="ISkill"/> to the <see cref="IHarness"/>
    /// </summary>
    /// <param name="skill">An mcp</param>
    /// <returns><see cref="true"> on success</returns>
    public bool AddSkill(ISkill skill);

#endregion

#region Send/Recieve

    /// <summary>
    /// Begins the IHarness <see cref="Loop"/>.
    /// </summary>
    /// <param name="agent">An AI Agent to give access to the harness</param>
    /// <param name="start">Information to start the loop with</param>
    /// <param name="token">Cancellation Token</param>
    /// <returns>Any returned tasks from the completed loop</returns>
    public Task<IEnumerable<ITurn>> LoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token);

    /// <summary>
    /// Begins the IHarness <see cref="Loop"/>.
    /// </summary>
    /// <param name="agent">An AI Agent to give access to the harness</param>
    /// <param name="start">Information to start the loop with</param>
    /// <param name="token">Cancellation Token</param>
    /// <returns>Any returned tasks from the completed loop</returns>
    public IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token);

#endregion

#region Tools

    /// <summary>
    /// Uses a tool and returns the result.
    /// </summary>
    /// <param name="name">The tool name</param>
    /// <param name="args">The args for the tool</param>
    /// <param name="token">Cancellation Token</param>
    /// <returns></returns>
    public Task<ToolReturn> UseToolAsync(string mcpName, string toolName, List<IToolArg> args, CancellationToken token);

#endregion

#region Permissions

    /// <summary>
    /// Awaitable Permissions request. If null, permissions are assumed true.
    /// </summary>
    public Func<PermissionRequest, CancellationToken, Task>? AskUser { get; }

#endregion

}

/// <summary>
/// Harness Configuration
/// </summary>
public sealed class HarnessConfig
{
    
    public string CurrentWorkingDirectory { get; set; } = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

}

/// <summary>
/// A request for permission
/// </summary>
public sealed class PermissionRequest : EventArgs
{

    public string McpName { get; }
    public string ToolName { get; }
    
    public bool HasPermission { get; set; } = false;

    public IReadOnlyList<IToolArg> Args { get; }

    public PermissionRequest(IMcp mcp, ITool tool, List<IToolArg> args)
    {
        McpName = mcp.Name;
        ToolName = tool.Name;
        Args = args;

    }

}
