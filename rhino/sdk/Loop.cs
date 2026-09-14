using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public sealed class Loop(IHarness harness)
{

    private IHarness Harness { get; } = harness;

    public async Task<IEnumerable<ITurn>> StartAsync(ITurn start, CancellationToken token)
    {
        List<ITurn> conversation = [];
        List<ITurn> nextTurn = [start];
        while (nextTurn.Count > 0)
        {
            IEnumerable<ITurn> turns = await Harness.SendAsync(nextTurn, token);
            nextTurn.Clear();
            foreach (ITurn turn in turns)
            {
                conversation.Add(turn);

                if (turn is ToolTurn tool)
                {
                    ToolReturn result = await Harness.UseToolAsync(tool.Name, tool.Args, token).ConfigureAwait(false);
                    nextTurn.Add(new ToolResultTurn(tool.Name, result));
                }
            }
        }

        return conversation;
    }

}

// public sealed class DesktopHarness : IHarness
