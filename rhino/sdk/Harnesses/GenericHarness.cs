using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using FuzzySharp;
using FuzzySharp.PreProcess;

namespace Rhino.AI;

/// <summary>
/// A super simple generic harness that can run a full Agent loop.
/// </summary>
public class GenericHarness : IHarness
{

    private Loop Loop { get; }

    private Dictionary<string, IMcp> PrivateMcps { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, IMcp> Mcps => PrivateMcps;

    private Dictionary<string, ISkill> PrivateSkills { get; } = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ISkill> Skills => PrivateSkills;

    public PermissionSet Permissions { get; } = new PermissionSet();

    public GenericHarness()
    {
        Loop = new(this);
        AddMcp(new DefaultToolsMcp(this));
    }

    public async Task<IEnumerable<ITurn>> LoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    => await Loop.StartAsync(agent, start, token);

    public async Task<ToolReturn> UseToolAsync(string mcpName, string toolName, List<IToolArg> args, CancellationToken token)
    {
        if (!Mcps.TryGetValue(mcpName, out IMcp? mcp) || mcp is null) return ToolReturn.Failure($"Mcp named {mcpName} is not available", "");
        
        if (!mcp.Tools.TryGetValue(toolName, out ITool? tool) || tool is null)
        {
            IEnumerable<ITool> likelyTools = mcp.Tools.Values
                .OrderByDescending(t => Fuzz.WeightedRatio(toolName, t.Name, PreprocessMode.Full))
                .Take(3);
            IEnumerable<string> likelyToolNames = likelyTools.Select(t => t.Name);
            string likelyToolString = string.Join(" or ", likelyToolNames);
            return ToolReturn.Failure($"Tool named {toolName} is not available in {mcpName}", $"Did you mean {likelyToolString}?");
        }

        Permissability permissability = Permissions.HasPermission(tool, args);
        if (permissability == Permissability.Deny) return ToolReturn.Refused();
        if (permissability == Permissability.Ask)
        {
            if (!await RequestPermissionFromUser(tool, args, token)) return ToolReturn.Refused();
        }

        return await mcp.RunToolAsync(toolName, args, token).ConfigureAwait(false);
    }

    public virtual async Task<bool> RequestPermissionFromUser(ITool tool, List<IToolArg> args, CancellationToken token)
    {
        Permissability permissability = Permissions.HasPermission(tool, args);
        if (permissability == Permissability.Always) return true;
        if (permissability == Permissability.Deny) return false;
        
        PermissionRequested request = new();
        PermissionRequested?.Invoke(this, request);
        return request.HasPermission;
    }

    public bool AddMcp(IMcp mcp) => PrivateMcps.TryAdd(mcp.Name, mcp);

    public bool AddSkill(ISkill skill) => PrivateSkills.TryAdd(skill.Name, skill);

    public event EventHandler<PermissionRequested>? PermissionRequested;

}

/// <summary>
/// A request for permission
/// </summary>
public class PermissionRequested : EventArgs
{

    public bool HasPermission { get; set; } = true;

}
