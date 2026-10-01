using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
        List<ITurn> turns = [];
        await foreach (ITurn turn in StreamAsync(agent, start, token).ConfigureAwait(false))
        {
            turns.Add(turn);
        }

        return turns;
    }

    public async IAsyncEnumerable<ITurn> StreamAsync(Agent agent, IEnumerable<ITurn> start, [EnumeratorCancellation]CancellationToken token)
    {
        List<ITurn> conversation = new(start);
        List<ITurn> results = [];

        do
        {
            // Endpoints validate the transcript as a whole, so results have to be sent behind the calls they answer, never on their own.
            conversation.AddRange(results);
            results.Clear();

            await foreach (ITurn turn in agent.Model.StreamAsync(Harness, conversation, token))
            {
                if (turn is TurnEnd) continue;
                conversation.Add(turn);
                yield return turn;

                if (turn is ToolTurn tool)
                {
                    ToolResultTurn result = new (tool.Id, tool.Name, await UseAsync(tool, token).ConfigureAwait(false));
                    results.Add(result);
                    yield return result;
                }
            }

        }
        while (results.Count > 0);
    }

    private async Task<ToolReturn> UseAsync(ToolTurn tool, CancellationToken token)
    {
        if (!ToolSchema.TryParseWireName(tool.Name, out string mcpName, out string toolName))
            return ToolReturn.Failure($"Could not parse {tool.Name}.", "N/A");

        return await Harness.UseToolAsync(mcpName, toolName, tool.Args.ToList(), token).ConfigureAwait(false);
    }

}
