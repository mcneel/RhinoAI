using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

public sealed class Agent
{

    public List<ITurn> PrivateTurns { get; } = [];
    public IReadOnlyList<ITurn> Turns => PrivateTurns;

    public Model Model { get; }
    
    private IHarness Harness { get; }

    public Agent(Model model, IHarness harness)
    {
        Model = model;
        Harness = harness;
    }

    internal Agent Fork() => WithNewModel(Model);

    internal Agent WithNewModel(Model model)
    {
        Agent agent = new (model, this.Harness);
        agent.PrivateTurns.AddRange(this.PrivateTurns.Select(t => t.Copy()));
        return agent;
    }

    public async Task<IEnumerable<ITurn>> SendAsync(string message, CancellationToken token)
    {
        List<ITurn> startTurns = [];
        startTurns.AddRange(Turns);
        startTurns.Add(new MessageTurn(message));

        IEnumerable<ITurn> turns = await Harness.LoopAsync(startTurns, token);
        PrivateTurns.Clear();
        PrivateTurns.AddRange(turns);

        return turns;
    }

}
