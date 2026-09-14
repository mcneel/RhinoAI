using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public abstract class GenericHarness : IHarness
{

    private Loop Loop { get;}

    public string DefaultPrompt { get; set; } = "You are a Rhino Expert.";

    public List<IMcp> Mcps { get; } = [];

    protected List<JsonNode> Contents { get; } = [];

    public PermissionSet Permissions { get; set; } = new PermissionSet();

    public GenericHarness()
    {
        Loop = new(this);
        Mcps.Add(new DefaultToolsMcp());
    }

    internal async Task<IEnumerable<ITurn>> LoopAsync(ITurn turn, CancellationToken token)
    => await Loop.StartAsync(turn, token);

    internal abstract Task<IEnumerable<ITurn>> SendAsync(IEnumerable<ITurn> turn, CancellationToken token);

    public async Task<ToolReturn> UseToolAsync(string name, List<IToolArg> args, CancellationToken token)
    {
        foreach (IMcp mcp in Mcps)
        {
            if (!mcp.Tools.TryGetValue(name, out ITool? tool ) || tool is null) continue;
            
            Permissability permissability = Permissions.HasPermission(tool, args);
            if (permissability == Permissability.Deny) return ToolReturn.Refused();
            if (permissability == Permissability.Ask) return ToolReturn.AskPermisson();

            return await mcp.RunToolAsync(name, args, token).ConfigureAwait(false);
        }

        return ToolReturn.Failure($"No MCP provides a tool called '{name}'.", "Call one of the tools offered in this session.");
    }

}
