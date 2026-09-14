using System;
using System.Threading;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Rhino.AI;

public abstract class GenericHarness : IHarness
{

    private Loop Loop { get;}

    public string DefaultPrompt { get; set; } = "You are a Rhino Expert.";

    protected Dictionary<string, IMcp> PrivateMcps { get; } = new (StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, IMcp> Mcps => PrivateMcps;

    protected Dictionary<string, ISkill> PrivateSkills { get; } = new (StringComparer.OrdinalIgnoreCase);
    public IReadOnlyDictionary<string, ISkill> Skills => PrivateSkills;

    protected List<JsonNode> Contents { get; } = [];

    public PermissionSet Permissions { get; set; } = new PermissionSet();

    public GenericHarness()
    {
        Loop = new(this);
        PrivateMcps.Add(nameof(DefaultToolsMcp), new DefaultToolsMcp());
    }

    public async Task<IEnumerable<ITurn>> LoopAsync(ITurn turn, CancellationToken token)
    => await Loop.StartAsync(turn, token);

    public abstract Task<IEnumerable<ITurn>> SendAsync(IEnumerable<ITurn> turn, CancellationToken token);

    public async Task<ToolReturn> UseToolAsync(string name, List<IToolArg> args, CancellationToken token)
    {
        foreach (IMcp mcp in Mcps.Values)
        {
            if (!mcp.Tools.TryGetValue(name, out ITool? tool ) || tool is null) continue;
            
            Permissability permissability = Permissions.HasPermission(tool, args);
            if (permissability == Permissability.Deny) return ToolReturn.Refused();
            if (permissability == Permissability.Ask)
            {
                if (!await RequestPermissionFromUser(name, args, token)) return ToolReturn.Refused();
            }

            return await mcp.RunToolAsync(name, args, token).ConfigureAwait(false);
        }

        return ToolReturn.Failure($"No MCP provides a tool called '{name}'.", "Call one of the tools offered in this session.");
    }

    public abstract Task<bool> RequestPermissionFromUser(string name, List<IToolArg> args, CancellationToken token);

}
