using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Rhino.AI.Models;

namespace Rhino.AI;

/// <summary>
/// A harness loop
/// </summary>
/// <param name="harness">The harness</param>
public sealed class Loop(IHarness harness)
{

    private IHarness Harness { get; } = harness;

    public async Task<IEnumerable<ITurn>> StartAsync(Agent agent, IEnumerable<ITurn> start, CancellationToken token)
    {
        List<ITurn> conversation = new(start);
        List<ITurn> results = [];

        do
        {
            // Endpoints validate the transcript as a whole, so results have to be sent behind the calls they answer, never on their own.
            conversation.AddRange(results);
            results.Clear();

            foreach (ITurn turn in await agent.Model.SendAsync(Harness, conversation, token).ConfigureAwait(false))
            {
                if (turn is TurnEnd) continue;
                conversation.Add(turn);

                if (turn is ToolTurn tool)
                    results.Add(new ToolResultTurn(tool.Id, tool.Name, await UseAsync(tool, token).ConfigureAwait(false)));
            }
        }
        while (results.Count > 0);

        return conversation;
    }

    private async Task<ToolReturn> UseAsync(ToolTurn tool, CancellationToken token)
    {
        IMcp? mcp = Harness.Mcps.Values.FirstOrDefault(m => m.Tools.ContainsKey(tool.Name));
        if (mcp is null)
            return ToolReturn.Failure($"No tool named {tool.Name} is registered.", "Call one of the declared tools instead.");

        return await Harness.UseToolAsync(mcp.Name, tool.Name, tool.Args.ToList(), token).ConfigureAwait(false);
    }

}
