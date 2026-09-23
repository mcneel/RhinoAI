using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Rhino.AI.Models;
using System;

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
    
    // TODO: Make set and re
    public string DefaultPrompt { get; }

    // TODO : Enum ??
    // public string Effort { get; set; }

    public Agent(IModel model, IHarness harness, string defaultPrompt = "")
    {
        Model = model;
        Harness = harness;
        DefaultPrompt = defaultPrompt;
    }

    internal Agent Fork() => WithNewModel(Model);

    /// <summary>
    /// Create a new Agent with a new model
    /// </summary>
    /// <param name="model"></param>
    /// <returns>A freshly made agent with all of the state copied safely</returns>
    internal Agent WithNewModel(IModel model)
    {
        Agent agent = new (model, Harness, DefaultPrompt);
        agent.PrivateTurns.AddRange(PrivateTurns.Select(t => t.Copy()));
        return agent;
    }

    /// <summary>
    /// Create a new Agent that has permission.
    /// </summary>
    /// <param name="model"></param>
    /// <returns>A freshly made agent with all of the state copied safely</returns>
    internal Agent WithPermission()
    {
        // Finds the first Agent or Vendor that has permission
        // TODO : Check existing models
        IModel model = default!;
        Agent agent = new (model, Harness, DefaultPrompt);
        agent.PrivateTurns.AddRange(PrivateTurns.Select(t => t.Copy()));
        return agent;
    }

    public async Task<IEnumerable<ITurn>> SendAsync(string message, CancellationToken token)
    {
        if (!UserPermissions.IsPermitted(Model.Vendor, Model.Name))
            throw new PermissionException("Model or Vendor does not have permission.");

        List<ITurn> startTurns = new (Turns);

        // The loop hands back the whole transcript, so system turns added on every send would stack up.
        if (startTurns.Count == 0)
        {
            if (!string.IsNullOrEmpty(DefaultPrompt))
                startTurns.Add(new SystemTurn(DefaultPrompt));

            if (SkillsPrompt(Harness.Skills) is string skillsPrompt)
                startTurns.Add(new SystemTurn(skillsPrompt));
        }

        startTurns.Add(new MessageTurn(message));

        IEnumerable<ITurn> turns = await Harness.LoopAsync(this, startTurns, token).ConfigureAwait(false);
        PrivateTurns.Clear();
        PrivateTurns.AddRange(turns);

        return turns;
    }

    private string? SkillsPrompt(IReadOnlyDictionary<string, ISkill> skills)
    {
        if (skills.Count == 0) return null;
        
        // If read_skill is not available, return null.
        if (!Harness.Mcps.Values.Any(m => m.Tools.ContainsKey("read_skill"))) return null;

        StringBuilder prompt = new("The following skills are available. Call read_skill with a skill's name to load its full instructions before using it.\n");
        foreach (ISkill skill in skills.Values)
            prompt.Append($"\n- {skill.Name}: {skill.Description}");

        return prompt.ToString();
    }

    public static Agent GetClaudeDesktopAgent(string prompt = "")
        => new Agent(new ClaudeDesktopModel("opus"), new ClaudeHarness(), prompt);

    // public static Agent GetCodexDesktopAgent()
    //     => new Agent(new CodexDesktopModel("gpt-6"), new CodexHarness());

    public static Agent GetClaudeAgent(string model, string prompt)
        => new Agent(ClaudeModel.Default(model), new GenericHarness(), prompt);

    public static Agent GetChatGptAgent(string model, string prompt)
        => new Agent(ChatGptModel.Default(model), new GenericHarness(), prompt);

    public static Agent GetGeminiAgent(string model, string prompt)
        => new Agent(GeminiModel.Default(model, "Google"), new GenericHarness(), prompt);

    public static Agent GetDeepSeekAgent(string model, string prompt)
        => new Agent(DeepSeekModel.Default(model), new GenericHarness(), prompt);

    public static Agent GetLMStudioAgent(string model, string prompt)
        => new Agent(LMStudioModel.Default(model), new GenericHarness(), prompt);

}
