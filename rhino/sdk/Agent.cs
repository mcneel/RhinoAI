using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Rhino.AI.Models;

namespace Rhino.AI;

/// <summary>
/// The Agent is the controller of all the AI parts.
/// </summary>
public sealed class Agent
{

    public List<ITurn> PrivateTurns { get; } = [];
    public IReadOnlyList<ITurn> Turns => PrivateTurns;

    /// <summary>
    /// The Model in use by the agent
    /// </summary>
    public IModel Model { get; }
    
    public IHarness Harness { get; }
    
    public string DefaultPrompt { get; set; } = "";

    // TODO : Enum ??
    // public string Effort { get; set; }

    public Agent(IModel model, IHarness harness)
    {
        Model = model;
        Harness = harness;
    }

    internal Agent Fork() => WithNewModel(Model);

    /// <summary>
    /// Create a new Agent with a new model
    /// </summary>
    /// <param name="model"></param>
    /// <returns>A freshly made agent with all of the state copied safely</returns>
    internal Agent WithNewModel(IModel model)
    {
        Agent agent = new (model, Harness);
        agent.PrivateTurns.AddRange(PrivateTurns.Select(t => t.Copy()));
        return agent;
    }

    public async Task<IEnumerable<ITurn>> SendAsync(string message, CancellationToken token)
    {
        List<ITurn> startTurns = new (Turns);
        if (!string.IsNullOrEmpty(DefaultPrompt))
            startTurns.Add(new SystemTurn(DefaultPrompt));

        startTurns.Add(new MessageTurn(message));

        IEnumerable<ITurn> turns = await Harness.LoopAsync(Model, startTurns, token).ConfigureAwait(false);
        PrivateTurns.Clear();
        PrivateTurns.AddRange(turns);

        return turns;
    }

    public static Agent GetClaudeDesktopAgent()
        => new Agent(new ClaudeDesktopModel("opus"), new ClaudeHarness());

    public static Agent GetClaudeAgent(string model)
        => new Agent(new ClaudeModel(model), new GenericHarness());

    public static Agent GetChatGptAgent(string model)
        => new Agent(new ChatGptModel(model), new GenericHarness());

    public static Agent GetGeminiAgent(string model)
        => new Agent(new GeminiModel(model, "Google"), new GenericHarness());

    // public static Agent GetCodexDesktopAgent()
    //     => new Agent(new CodexDesktopModel("gpt-6"), new CodexHarness());

}
