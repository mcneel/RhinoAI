using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Rhino.AI.Models;

namespace Rhino.AI;

/// <summary>
/// The Harness is the exo-skeleton of the AI Agent.
/// It is where every tool, skill and permission is registered.
/// </summary>
public interface IHarness
{

    /// <summary>Permissions for the MCPs and Tools</summary>
    public PermissionSet Permissions { get; }

#region Extensions

    /// <summary>All of the registered MCPs</summary>
    public IReadOnlyDictionary<string, IMcp> Mcps { get; }
    
    /// <summary>All of the registered Skills</summary>
    public IReadOnlyDictionary<string, ISkill> Skills { get; }

    public bool AddMcp(IMcp mcp);
    
    // public bool AddSkill(ISkill skill);

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

#endregion

#region Tools

    // TODO : Specify an Mcp or name of an mcp?

    /// <summary>
    /// Uses a tool and returns the result.
    /// </summary>
    /// <param name="name">The tool name</param>
    /// <param name="args">The args for the tool</param>
    /// <param name="token">Cancellation Token</param>
    /// <returns></returns>
    public Task<ToolReturn> UseToolAsync(string mcpName, string toolName, List<IToolArg> args, CancellationToken token);

#endregion

}
