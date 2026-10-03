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

    public HarnessConfig Config { get; } = new();

    public GenericHarness(PlugIns.PlugInToken token)
    {
        Loop = new(this);
        AddMcp(new DefaultToolsMcp(this, token));
    }

    public async Task<IEnumerable<ITurn>> LoopAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        List<ITurn> turns = [];
        await foreach (ITurn turn in StreamLoopAsync(agent, start, token).ConfigureAwait(false))
        {
            turns.Add(turn);
        }

        return turns;
    }

    public async IAsyncEnumerable<ITurn> StreamLoopAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation] CancellationToken token)
    {
        await foreach (ITurn turn in Loop.StreamAsync(agent, start, token))
            yield return turn;
    }

    public async Task<ToolReturn> UseToolAsync(string mcpName, string toolName, List<IToolArg> args, CancellationToken token)
        => await UseToolAsync(this, mcpName, toolName, args, token);

    public static async Task<ToolReturn> UseToolAsync(IHarness harness, string mcpName, string toolName, List<IToolArg> args, CancellationToken token)
    {
        if (!harness.Mcps.TryGetValue(mcpName, out IMcp? mcp) || mcp is null) return ToolReturn.Failure($"Mcp named {mcpName} is not available", "");

        if (!mcp.Tools.TryGetValue(toolName, out ITool? tool) || tool is null)
        {
            IEnumerable<ITool> likelyTools = mcp.Tools.Values
                .OrderByDescending(t => Fuzz.WeightedRatio(toolName, t.Name, PreprocessMode.Full))
                .Take(3);
            IEnumerable<string> likelyToolNames = likelyTools.Select(t => t.Name);
            string likelyToolString = string.Join(" or ", likelyToolNames);
            return ToolReturn.Failure($"Tool named {toolName} is not available in {mcpName}", $"Did you mean {likelyToolString}?");
        }

        Permissability permissability = harness.Permissions.HasPermission(mcpName, toolName, args);
        if (permissability == Permissability.Deny) return ToolReturn.Refused();
        if (permissability == Permissability.Ask && harness.AskUser is not null)
        {
            PermissionRequest request = new(mcp, tool, args);
            await harness.AskUser.Invoke(request, token);
            if (!request.HasPermission) return ToolReturn.Refused();
        }

        return await mcp.RunToolAsync(toolName, args, token).ConfigureAwait(false);
    }

    public bool AddMcp(IMcp mcp) => PrivateMcps.TryAdd(mcp.Name, mcp);

    public bool AddSkill(ISkill skill) => PrivateSkills.TryAdd(skill.Name, skill);

    public Func<PermissionRequest, CancellationToken, Task>? AskUser { get; set; }

}
