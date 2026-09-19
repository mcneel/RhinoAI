using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Rhino.AI.Models;

namespace Rhino.AI;

public sealed class Loop(IHarness harness)
{

    private IHarness Harness { get; } = harness;

    public async Task<IEnumerable<ITurn>> StartAsync(IModel model, IEnumerable<ITurn> start, CancellationToken token)
    {
        List<ITurn> conversation = [];
        List<ITurn> nextTurn = new (start);
        
        while (nextTurn.Count > 0)
        {
            IEnumerable<ITurn> turns = await model.SendAsync(Harness, nextTurn, token);
            nextTurn.Clear();
            foreach (ITurn turn in turns)
            {
                if (turn is TurnEnd) continue;
                conversation.Add(turn);

                if (turn is ToolTurn tool)
                {
                    IMcp? mcp = Harness.Mcps.Values.FirstOrDefault(m => m.Tools.Any(t => string.Equals(t.Key, tool.Name)));
                    if (mcp is null) continue; // TODO : Handle better

                    ToolReturn result = await Harness.UseToolAsync(mcp.Name, tool.Name, tool.Args.ToList(), token).ConfigureAwait(false);
                    nextTurn.Add(new ToolResultTurn(tool.Id, tool.Name, result));
                }
            }
        }

        return conversation;
    }

}
