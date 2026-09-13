using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Rhino.AI;

// Rhino.AI.Agent agent = new (Agent.Claude, "<default-prompt>");
// IEnumerable<Rhino.AI.Turn> turn = await agent.SendAsync("Please create me a component that does X", cancellationToken);
// if (turn.Success) ...

public sealed class Agent
{

    public bool Ready { get; }

    public bool Available { get; }

    private int CurrentModelIndex { get; set; } = 0;
    public Model CurrentModel => PrivateModels[CurrentModelIndex];

    public List<Model> PrivateModels { get; } = [];
    public IReadOnlyList<Model> Models => PrivateModels;

    public string DefaultPrompt { get; set; } = "";

    public IHarness Harness { get; }

    public Agent(IHarness harness)
    {
        Harness = harness;
    }

    public async Task<IEnumerable<ITurn>> SendAsync(string message, CancellationToken token)
        => await Harness.LoopAsync(new MessageTurn(message), token);
}

public sealed class Loop(IHarness harness)
{

    private IHarness Harness { get; } = harness;

    public async Task<IEnumerable<ITurn>> StartAsync(ITurn start, CancellationToken token)
    {
        List<ITurn> nextTurn = [start];
        while (nextTurn.Count > 0)
        {
            IEnumerable<ITurn> turns = await Harness.SendAsync(nextTurn, token);
            nextTurn.Clear();
            foreach(ITurn turn in turns)
            {
                if (turn is ToolTurn tool)
                {
                    ToolResult result = Harness.UseTool(tool.Name, tool.Args);
                    nextTurn.Add(new MessageTurn(result.Json));
                }
                else
                {
                    nextTurn.Add(turn);
                }
            }
        }

        return nextTurn;
    }

}

public sealed class RhinoHarness : IHarness
{

    private Loop Loop { get; }

    public RhinoHarness()
    {
        Loop = new(this);
    }

    public async Task<IEnumerable<ITurn>> LoopAsync(ITurn turn, CancellationToken token)
    => await Loop.StartAsync(turn, token);

    public async Task<IEnumerable<ITurn>> SendAsync(IEnumerable<ITurn> turn, CancellationToken token)
    {
        // --> RIGHT HERE CLAUDE <--
    }

    public ToolResult UseTool(string name, List<KeyValuePair<string, string>> args)
    {
        // Check MCPs

        return new ToolResult("Tool not found! OH NO");
    }

}

// public sealed class DesktopHarness : IHarness
